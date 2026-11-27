"""Verify the party/lobby and authoritative game transport with two opt-in graphical Windows players."""
import argparse
import hashlib
import ipaddress
import json
import os
import secrets
import socket
import sqlite3
import subprocess
import tempfile
import time
from contextlib import ExitStack, closing
from pathlib import Path

from verify import binary_for, running, wait_http


def players(phase, state, output, game, ids, passwords, rematch=False, graphics_api=None, facing=False, manual=False):
    processes = {}
    with ExitStack() as stack:
        for i, role in enumerate(("captain", "guest")):
            env = dict(state, ANIMECS_PROBE_ROLE=role, ANIMECS_PROBE_PHASE=phase,
                       ANIMECS_PROBE_ID=ids[i], ANIMECS_PROBE_PEER=ids[1-i], ANIMECS_PROBE_PASSWORD=passwords[i])
            processes[role] = stack.enter_context(running(
                game, env, output / f"{role}-{phase}-stdout.log", "-party-probe", str(output),
                *(["-force-"+graphics_api] if graphics_api else []),
                "-screen-fullscreen", "0", "-screen-width", "1024", "-screen-height", "576",
                "-logFile", str(output / f"{role}-{phase}-player.log"), *(["-party-rematch"] if rematch else []),
                *(["-party-facing"] if facing else []), *(["-party-manual"] if manual else [])))
        previous = {}
        deadline = float("inf") if manual else time.monotonic() + (80 * 60 if rematch else 420)
        while time.monotonic() < deadline:
            complete = True
            for role, process in processes.items():
                stage = output / f"{role}-{phase}-stage.txt"
                if stage.exists():
                    value = stage.read_text(encoding="utf-8-sig")
                    if previous.get(role) != value:
                        print(role, phase, value, flush=True)
                        previous[role] = value
                err = output / f"{role}-{phase}-error.txt"
                if err.exists():
                    raise RuntimeError(err.read_text(encoding="utf-8-sig"))
                code = process.poll()
                if code is None:
                    complete = False
                elif code != 0:
                    raise RuntimeError(f"{role} exited with {code}; see {role}-{phase}-player.log")
            if complete:
                return ({role:process.pid for role,process in processes.items()},
                        {role:process.returncode for role,process in processes.items()})
            time.sleep(.2)
        raise TimeoutError(f"Graphical players timed out in {phase}: {previous}")


def check(output):
    reports = [json.loads((output/f"{role}-report.json").read_text(encoding="utf-8-sig")) for role in ("captain","guest")]
    def angular_distance(a,b):
        return abs((a-b+180)%360-180)
    for index, report in enumerate(reports):
        assert all(report.get(k) for k in ("registered", "loggedIn", "friends", "invited", "joined", "loadingCaptured"))
        assert report["seat"] == index and report["teammateSeat"] == 1-index
        assert report["first"]["acceptedInput"] > 0
        assert all(report["first"][k] < report["second"][k] for k in ("acceptedInput", "tick", "rosterTick"))
        for when in ("first","second"):
            assert angular_distance(report[when]["yaw"],reports[1-index][when]["teammateYaw"]) < .2
            assert angular_distance(report[when]["yaw"],report[when]["teammateYaw"]) > 10
        for suffix in ("loading","match"):
            capture = output/f"{report['role']}-{suffix}.png"
            assert capture.is_file() and capture.stat().st_size > 16000
    assert reports[0]["room"] and reports[0]["room"] == reports[1]["room"]
    assert reports[0]["map"] == reports[1]["map"]
    return reports


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, default=Path("Builds/QAClient/AnimeCS.exe"))
    parser.add_argument("--services", type=Path, default=Path("Builds/Windows/Services"))
    parser.add_argument("--evidence", type=Path, default=Path("Builds/RenderChecks/GraphicalParty"))
    parser.add_argument("--bind-host",default="127.0.0.1",help="This machine's loopback or private LAN IPv4 address")
    parser.add_argument("--finish-match",action="store_true",help="Observe the final menu for seven seconds, then verify leader rematch and teammate follow")
    parser.add_argument("--verify-facing",action="store_true",help="Turn both clients toward one another once and verify idle camera and replicated orientation")
    parser.add_argument("--manual-play",action="store_true",help="Join two clients and hand their windows to the user until both are closed")
    parser.add_argument("--graphics-api",choices=("d3d11","d3d12"),help="Pin the Windows graphics API for renderer comparisons")
    args=parser.parse_args()
    if args.manual_play and (args.finish_match or args.verify_facing):
        parser.error("Use --manual-play on its own for keyboard and mouse control.")
    try:
        address=ipaddress.IPv4Address(args.bind_host)
        local_ranges=map(ipaddress.IPv4Network,("10.0.0.0/8","172.16.0.0/12","192.168.0.0/16"))
        if not (address.is_loopback or any(address in block for block in local_ranges)):
            parser.error("Choose a local loopback or private LAN IPv4 address.")
        host=str(address)
        def free_port(kind):
            with socket.socket(socket.AF_INET,kind) as reservation:
                reservation.bind((host,0))
                return reservation.getsockname()[1]
        free_port(socket.SOCK_STREAM)
    except (ValueError,OSError) as error:
        parser.error(f"Local interface unavailable: {error}")
    os.environ["NO_PROXY"]=",".join(filter(None,(os.environ.get("NO_PROXY"),host,"127.0.0.1")))
    game=args.player.resolve()
    services=args.services.resolve()
    for path in [game]+[binary_for(services,role) for role in ("Data","HotUpdate","Battle")]:
        if not path.is_file():
            parser.error(f"Build missing: {path}")
    probe=game.with_name(game.stem+"_Data")/"Managed"/"AnimeCS.RenderProbe.dll"
    if probe.parent.is_dir() and (not probe.is_file() or b"PartyClientProbe" not in probe.read_bytes()):
        parser.error("This player does not contain the graphical party probe. Build with ANIMECS_RENDER_PROBE.")
    output=args.evidence.absolute()/(time.strftime("%Y%m%d-%H%M%S")+"-"+secrets.token_hex(2))
    output.mkdir(parents=True)
    print(f"Evidence: {output}", flush=True)
    urls=[]
    for _ in range(2):
        port=free_port(socket.SOCK_STREAM)
        while port in urls:
            port=free_port(socket.SOCK_STREAM)
        urls.append(port)
    data,update=[f"http://{host}:{p}" for p in urls]
    ids=["SkyQA"+secrets.token_hex(4) for _ in range(2)]
    passwords=[secrets.token_urlsafe(20) for _ in ids]
    with tempfile.TemporaryDirectory(prefix="animecs-graphical-party-") as workspace, ExitStack() as resources:
        work=Path(workspace)
        shared=dict(os.environ, ANIMECS_DATA_ENDPOINT=data, ANIMECS_UPDATE_ENDPOINT=update,
                    ANIMECS_DATA_URL=data, ANIMECS_UPDATE_URL=update, ANIMECS_DATA_DIR=str(work/"db"),
                    ANIMECS_UPDATE_DIR=str(work/"updates"), ANIMECS_BATTLE_STATE_DIR=str(work/"results"),
                    ANIMECS_BATTLE_DATA_ENDPOINT=data, ANIMECS_BATTLE_SECRET=secrets.token_hex(32),
                    ANIMECS_BATTLE_PORT=str(free_port(socket.SOCK_DGRAM)), ANIMECS_BATTLE_ADDRESS=host,
                    ANIMECS_BATTLE_PUBLIC_HOST=host, ANIMECS_BATTLE_MAP="map.dust2")
        for role,url in (("Data",data),("HotUpdate",update)):
            service=resources.enter_context(running(binary_for(services,role),shared,output/f"{role}.log"))
            wait_http(url,service)
        battle=resources.enter_context(running(binary_for(services,"Battle"),shared,output/"battle-stdout.log",
                                               "-batchmode","-nographics","-logFile",str(output/"battle.log")))
        client_env=dict(os.environ, ANIMECS_DATA_ENDPOINT=data, ANIMECS_UPDATE_ENDPOINT=update)
        client_env.pop("ANIMECS_BATTLE_SECRET",None)
        client_env.pop("ANIMECS_DATA_TOKEN",None)
        registration_pids,registration_exits=players("register",client_env,output,game,ids,passwords,graphics_api=args.graphics_api)
        if battle.poll() is not None:
            raise RuntimeError("Battle server exited before party login")
        battle_pids,battle_exits=players("login",client_env,output,game,ids,passwords,args.finish_match,args.graphics_api,args.verify_facing,args.manual_play)
        assert len(set(battle_pids.values()))==2
        if args.manual_play:
            print("Both user-controlled windows closed.",flush=True)
            return
        reports=check(output)
        if args.verify_facing:
            for report in reports:
                f=report["facing"]
                assert f["heldSeconds"]>=3 and f["samples"]>=5 and f["lastTick"]>f["firstTick"]
                assert f["lastAcknowledged"]>f["firstAcknowledged"]
                assert max(f[k] for k in ("maxCameraError","maxAuthorityError","maxPeerError","maxActorError"))<=2
                assert (output/(report["role"]+"-facing.png")).stat().st_size>16000
        settlement=None
        if args.finish_match:
            first,second=reports
            assert first["nextRoom"] and first["nextRoom"]==second["nextRoom"] and first["nextRoom"]!=first["room"]
            assert (first["finalOurScore"],first["finalTheirScore"],first["finalTick"]) == (
                second["finalOurScore"],second["finalTheirScore"],second["finalTick"])
            final_round=first["finalOurScore"]+first["finalTheirScore"]
            assert max(first["finalOurScore"],first["finalTheirScore"])==13
            for i,report in enumerate(reports):
                assert report["stableFinal"] and report["finalMenuSeconds"]>=7
                assert report["roundsPlayed"]==final_round and report["pairedPurchasesConfirmed"]
                assert report["creditsAfterPurchase"]<report["creditsBeforePurchase"]
                assert report["startAgainVisible"]==(i==0)
                assert report["nextInputAcknowledged"] and report["nextSeat"]==i
                assert report["trafficSeconds"]>=6 and min(report["downKiBPerSecond"],report["upKiBPerSecond"])>0
                for suffix in ("finished","finished-later","rematch"):
                    assert (output/f"{report['role']}-{suffix}.png").stat().st_size>16000
            with closing(sqlite3.connect(work/"db"/"players.db")) as db:
                settled=db.execute("SELECT settled FROM battle_history WHERE room=?",(first["room"],)).fetchone()
                next_room=db.execute("SELECT 1 FROM battle_history WHERE room=?",(first["nextRoom"],)).fetchone()
                wins=[db.execute("SELECT json_extract(state,'$.victories') FROM accounts WHERE id=?",(name,)).fetchone()[0] for name in ids]
                participation=[]
                for seat in range(len(ids)):
                    recorded=[row[0] for row in db.execute(
                        "SELECT ordinal FROM battle_progress WHERE room=? AND seat=? ORDER BY ordinal",(first["room"],seat))]
                    assert len(recorded)>1 and recorded[0]==0 and recorded[-1]==final_round
                    assert recorded[1:]==list(range(recorded[1],final_round+1))
                    participation.append({"seat":seat,"entered":True,"firstCompletedRound":recorded[1],"completedRounds":len(recorded)-1})
            assert settled==(1,) and next_room==(1,)
            assert wins==[int(first["finalOurScore"]>first["finalTheirScore"])]*2
            settlement={"settled":1,"winsBySeat":wins,"rematchAllocated":True,"participation":participation}
    assemblies={}
    for label,library in (("runtime",probe.with_name("AnimeCS.Runtime.dll")),("probe",probe)):
        if library.is_file(): assemblies[label]=hashlib.sha256(library.read_bytes()).hexdigest()
    summary={"result":"passed","managedSha256":assemblies,"player":str(game),"playerSha256":hashlib.sha256(game.read_bytes()).hexdigest(),
             "accountStore":"isolated temporary SQLite", "network":"two graphical clients over "+("loopback" if address.is_loopback else "same-host private LAN IPv4")+" UDP",
             "bindAddress":host,"requestedGraphicsApi":args.graphics_api or "default",
             "facingChecked":args.verify_facing,
             "registrationProcesses":registration_pids,"battleClientProcesses":battle_pids,
             "exitCodes":{"registration":registration_exits,"battle":battle_exits},"reports":reports}
    if settlement is not None:
        summary["settlement"]=settlement
    (output/"verified.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    print(f"Verified: {output/'verified.json'}", flush=True)


if __name__=="__main__":
    main()
