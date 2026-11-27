"""Exercise two independent clients and persistent state using isolated services on loopback."""
import argparse
import base64
import concurrent.futures
import hashlib
import hmac
import json
import os
from pathlib import Path
import secrets
import sqlite3
import socket
import struct
import tempfile
import time
from contextlib import closing, ExitStack

from verify import binary_for, free_port, request, running, wait_http


def play(output, work, env, tickets, verify_entries):
    magic = 0x53434103
    address = ("127.0.0.1", int(env["ANIMECS_BATTLE_PORT"]))
    with ExitStack() as stack:
        server = stack.enter_context(running(binary_for(output, "Battle"), env, work / "battle.log",
                                             "-batchmode", "-nographics", "-logFile", "-"))
        clients = [stack.enter_context(socket.socket(socket.AF_INET, socket.SOCK_DGRAM)) for _ in tickets]
        states = [None, None]
        for client in clients:
            client.settimeout(.12)
        until = time.monotonic() + 45
        sequence = 0
        while time.monotonic() < until and server.poll() is None:
            sequence += 1
            for seat, client in enumerate(clients):
                state = states[seat]
                if state is None:
                    packet = struct.pack("<IBQ", magic, 1, 0) + tickets[seat].encode("ascii")
                else:
                    token, round_id, tick = struct.unpack_from("<Qii", state, 5)
                    packet = bytearray(52)
                    struct.pack_into("<IBQiiiIhhhhhh", packet, 0, magic, 3, token, round_id, tick, sequence,
                                     0, 900 + 1100 * seat, 0, 32767, 0, 0, 0)
                    struct.pack_into("<iH", packet, 46, tick, 32768)
                client.sendto(packet, address)
                try:
                    state = client.recv(2048)
                except (socket.timeout, ConnectionResetError):
                    continue
                if len(state) == 98 and state[4] == 2:
                    assert state[21] == seat
                    states[seat] = state
            if all(s and struct.unpack_from("<i", s, 52)[0] > 0 and
                   abs(struct.unpack_from("<h", s, 36)[0] - (900 + 1100*i)) <= 2 for i, s in enumerate(states)):
                assert states[0][5:13] != states[1][5:13]
                break
        else:
            raise RuntimeError("Two authenticated seats did not receive authoritative input acknowledgements")
        # Real clients negotiate the roster extension after their first private snapshot.
        expected = 2166136261
        for char in "wear.catband":
            expected = ((expected ^ ord(char)) * 16777619) & 0xffffffff
        for client, state in zip(clients, states):
            token = struct.unpack_from("<Q", state, 5)[0]
            until = time.monotonic() + 3
            while time.monotonic() < until:
                client.sendto(struct.pack("<IBQ", magic, 6, token), address)
                try:
                    roster = client.recv(2048)
                except socket.timeout:
                    continue
                if len(roster) >= 21+10*62 and struct.unpack_from("<IBQ", roster) == (magic,5,token):
                    assert struct.unpack_from("<I", roster, 21+42)[0] == expected, "Missing authoritative headwear"
                    break
            else:
                raise RuntimeError("Did not receive negotiated party roster")
        until=time.monotonic()+12
        while not verify_entries() and time.monotonic()<until:
            time.sleep(.1)
        assert verify_entries(), "Authenticated seat entry was not reported before the match ended"
        print("Battle: two signed seats acknowledged input, replicated equipment and earned entry while playing")


def verify_reservation(api,event,database,secret):
    password=secrets.token_hex(16)
    leader=api("register",id="租期_A",password=password)["token"]
    guest=api("register",id="租期_B",password=password)["token"]
    api("friend",leader,id="租期_B")
    api("invite",leader,id="租期_B")
    api("accept",guest,id="租期_A")
    room=api("start",leader)["state"]["battleId"]
    now=int(time.time())
    first=now-20
    window=60*60
    previous_expiry=now+30

    def expiry(room_id):
        with closing(sqlite3.connect(database)) as db:
            row=db.execute("SELECT expires FROM battle WHERE room=?",(room_id,)).fetchone()
            return row[0] if row else None

    def deadline(room_id,value):
        with closing(sqlite3.connect(database)) as db, db:
            assert db.execute("UPDATE battle SET expires=? WHERE room=?",(value,room_id)).rowcount==1

    def ticket(state):
        payload,signature=(base64.b64decode(s,validate=True) for s in state["battleTicket"].split("."))
        assert hmac.compare_digest(signature,hmac.new(secret.encode(),payload,hashlib.sha256).digest())
        fields=payload.decode("utf8").split("|")
        assert fields[0]==state["battleId"] and fields[3]==state["id"]
        return int(fields[1]),int(fields[2]),int(fields[4])

    # Move only this isolated fixture's initial reservation close to its real one-hour boundary.
    with closing(sqlite3.connect(database)) as db, db:
        db.execute("UPDATE battle_history SET created=? WHERE room=?",(previous_expiry-window,room))
    deadline(room,previous_expiry)
    t0,seat,generation=ticket(api("start",leader)["state"])
    assert (t0,seat)==(previous_expiry,0) and ticket(api("state",guest)["state"])[0]==t0
    assert event(room,0,1,first)==204
    extended=expiry(room)
    assert extended==first+window, f"Fresh admission did not renew the original lease from its occurrence (delta {extended-first}s, expected {window}s)"
    assert ticket(api("state",leader)["state"])==(extended,0,generation)
    assert ticket(api("state",guest)["state"])==(extended,1,generation)
    assert event(room,0,1,first+1)==204 and expiry(room)==extended
    assert event(room,0,2,first+2)==204 and expiry(room)==first+2+window
    assert event(room,1,3,first+4)==204 and expiry(room)==first+4+window
    assert event(room,1,3,first+8)==204 and expiry(room)==first+4+window
    assert event(room,2,1,first+6)==204 and expiry(room)==first+6+window
    assert event(room,2,2,first+11)==204 and expiry(room)==first+6+window
    yesterday=min(first-window,now//86400*86400-1)
    assert event(room,3,3,yesterday)==204 and expiry(room)==first+6+window
    assert event(room,4,1,first+10)==204 and expiry(room)==first+10+window
    assert ticket(api("start",leader)["state"])==(first+10+window,0,generation)
    received=int(time.time())
    assert event(room,5,3,received+240)==204
    assert received+window<=expiry(room)<=int(time.time())+window
    print("Lease: first authenticated admission per seat and first round renew from event time; repeated start, duplicate or out-of-order delivery, and cross-day backlog keep the signed deadline")

    deadline(room,now-2)
    assert event(room,6,3,now-4)==204 and expiry(room)==now-2
    assert not api("state",guest)["state"].get("battleTicket")
    replacement=api("start",leader)["state"]
    current=replacement["battleId"]
    assert current!=room and ticket(replacement)[2]>generation
    deadline(current,previous_expiry)
    assert event(room,7,3,now)==204 and expiry(current)==previous_expiry
    assert api("start",leader)["state"]["battleId"]==current
    assert ticket(api("state",guest)["state"])[0]==previous_expiry
    api("end",leader,battleId=current)
    assert event(current,0,1,now)==204 and expiry(current) is None
    assert not api("state",leader)["state"].get("battleTicket")
    print("Lease: expired and explicitly released rooms stay closed, late old-room activity leaves the successor's ticket and reservation intact")


def verify(output, work, battle, reservation_only=False):
    url = "http://127.0.0.1:" + str(free_port(socket.SOCK_STREAM))
    env = dict(os.environ, ANIMECS_DATA_DIR=str(work / "db"), ANIMECS_DATA_URL=url,
               ANIMECS_BATTLE_SECRET=secrets.token_hex(32), ANIMECS_BATTLE_PORT=str(free_port(socket.SOCK_DGRAM)),
               ANIMECS_BATTLE_DATA_ENDPOINT=url, ANIMECS_BATTLE_STATE_DIR=str(work / "results"),
               ANIMECS_BATTLE_ADDRESS="127.0.0.1", ANIMECS_BATTLE_PUBLIC_HOST="127.0.0.1")
    password = secrets.token_hex(16)
    auth={"Authorization":"Bearer "+env["ANIMECS_BATTLE_SECRET"]}

    def event(room,ordinal,mask,occurred,authorization=auth):
        route=f"{url}/internal/battles/{room}/progress/{ordinal}/{mask}/{occurred}"
        return request(route,"POST",None,authorization)[0]

    def progress(state,kind):
        return next(t["progress"] for t in state["tasks"] if t["eventKey"]==kind)

    def api(action, token=None, expected=200, **body):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        get = action == "state"
        status, raw, _ = request(url+"/v2/"+action, "GET" if get else "POST", None if get else body, headers)
        assert status == expected, (action, status, expected, raw[:200] if status != 200 else b"")
        return json.loads(raw)

    def start():
        return running(binary_for(output, "Data"), env, work / "data.log")

    with start() as data:
        wait_http(url, data)
        verify_reservation(api,event,work/"db"/"players.db",env["ANIMECS_BATTLE_SECRET"])
        if reservation_only:
            return
        assert request(url+"/v2/state","POST","{}",{"Content-Type":"text/plain"})[0]==415
        api("state", expected=401)
        first = api("register", id="青空_A", password=password)
        second = api("register", id="青空_B", password=password)
        api("register", expected=409, id="青空_Ａ", password=password)
        api("login", expected=401, id="青空_A", password=password+"X")
        a = api("login", id="青空_a", password=password)["token"]
        b = second["token"]
        assert first["state"]["id"] == "青空_A"
        api("friend", a, id="青空_B")
        assert api("state", b)["state"]["friends"] == ["青空_A"]
        api("invite", a, id="青空_B")
        assert api("state", b)["state"]["invites"] == ["青空_A"]
        assert api("accept", b, id="青空_A")["state"]["party"] == ["青空_A", "青空_B"]
        c=api("register",id="青空_C",password=password)["token"]
        api("friend",a,id="青空_C")
        api("invite",a,id="青空_C")
        api("accept",c,id="青空_A")
        api("chat", b, text="已收到邀请")
        assert api("state", a)["state"]["messages"][-1]["text"] == "已收到邀请"
        initial = api("state", a)["state"]
        offer = next(o for o in initial["offers"] if o["itemId"] not in initial["inventory"] and o["itemId"].startswith("skin."))
        bought = api("buy", a, id=offer["id"])["state"]
        assert bought["credits"] == initial["credits"] - offer["price"] and offer["itemId"] in bought["inventory"]
        api("buy", a, expected=409, id=offer["id"])
        headwear = next(o for o in initial["offers"] if o["itemId"] == "wear.catband")
        api("buy", a, id=headwear["id"])
        drawn = api("draw", a)["state"]
        assert drawn["lastDrawId"] not in initial["inventory"] and drawn["lastDrawId"] in drawn["inventory"]
        display = dict(signature="星风", avatarId=initial["avatarId"], showCharacterId=drawn["lastDrawId"], showSkinId=offer["itemId"])
        api("profile", a, **display)
        equipment = json.dumps({"current":{"outfitId":"outfit.nora", "worn":{"headwear":"wear.catband"}, "skins":[]}, "presets":[]})
        api("loadout", a, loadoutJson=equipment)
        api("loadout", a, expected=400, loadoutJson='{"outfitId":"outfit.unknown"}')
        task = next(t for t in drawn["tasks"] if t["eventKey"] == "round.complete")
        api("progress", a, expected=403, eventId="client-round", kind="round.complete", amount=1)
        def claim(_):
            h = {"Authorization":"Bearer "+a,"Content-Type":"application/json"}
            return request(url+"/v2/claim", "POST", {"id":task["id"]}, h)[0]
        api("start", b, expected=400)
        match_a = api("start", a)["state"]
        match_b = api("state", b)["state"]
        assert match_a["battleId"] == match_b["battleId"] and match_a["battleTicket"] != match_b["battleTicket"]
        assert not match_a["battleFinished"] and match_a["contentVersion"] and match_a["mapId"]
        previous=match_a["battleId"]
        api("restart",b,expected=400,battleId=previous)
        api("restart",a,expected=425,battleId=previous)
        assert match_b["members"][0]["showCharacterId"] == drawn["lastDrawId"]
        lineup=url+"/internal/battles/"+match_a["battleId"]+"/lineup"
        assert request(lineup,headers={"Authorization":"Bearer "+a})[0] == 401
        status, raw, _=request(lineup,headers={"Authorization":"Bearer "+env["ANIMECS_BATTLE_SECRET"]})
        assert status==200
        roster=json.loads(raw)["members"]
        assert [p["id"] for p in roster]==["青空_A","青空_B","青空_C"] and roster[0]["loadoutJson"]==equipment
        assert offer["itemId"] in roster[0]["owned"]
        api("loadout",a,loadoutJson='{"current":{},"presets":[]}')
        frozen=json.loads(request(lineup,headers={"Authorization":"Bearer "+env["ANIMECS_BATTLE_SECRET"]})[1])
        assert frozen["members"][0]["loadoutJson"]==equipment
        api("loadout",a,loadoutJson=equipment)
        if battle:
            play(output,work,env,[match_a["battleTicket"],match_b["battleTicket"]],
                 lambda:all(progress(api("state",token)["state"],"match.enter")==1 for token in (a,b)))
        now=int(time.time())
        yesterday=now//86400*86400-1
        assert event(previous,0,1,now,{"Authorization":"Bearer "+a})==401
        assert event(previous,0,8,now)==400
        for mask in (1,2,3):
            assert event(previous,0,mask,now)==204
        for ordinal in (1,2):
            assert event(previous,ordinal,1,now)==204
        assert event(previous,3,2,yesterday)==204
        assert event(previous,3,2,now)==204
        sa,sb,sc=(api("state",token)["state"] for token in (a,b,c))
        assert [progress(s,"match.enter") for s in (sa,sb,sc)]==[1,1,0]
        assert [progress(s,"round.complete") for s in (sa,sb,sc)]==[task["goal"],0,0]
        assert not sa["battleFinished"]
        # A restarted battle worker only saw A reconnect. Data still has B's original admission.
        settle = url+"/internal/battles/"+previous+"/settle/0/18/1"
        assert request(settle,"POST",None,{"Authorization":"Bearer "+a})[0] == 401
        for _ in range(2):
            assert request(settle,"POST",None,auth)[0] == 204
        assert request(url+"/internal/battles/"+previous+"/settle/0/18","POST",None,auth)[0]==204
        final=api("state",b)["state"]
        assert final["victories"]==1 and final["battleFinished"] and final["battleId"]==previous and not final.get("battleTicket")
        assert progress(final,"round.complete")==0 and api("state",c)["state"]["victories"]==0
        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            assert sorted(pool.map(claim,range(2)))==[200,409]
            successors=list(pool.map(lambda _: api("restart",a,battleId=previous)["state"]["battleId"],range(2)))
        assert successors[0]==successors[1] and successors[0]!=previous
        assert api("state",b)["state"]["battleId"]==successors[0]
        api("end",a,battleId=previous)
        assert api("state",a)["state"]["battleId"]==successors[0]
        legacy=url+"/internal/battles/"+successors[0]+"/settle/1"
        assert request(legacy,"POST",None,auth)[0]==204
        assert request(legacy+"/4","POST",None,auth)[0]==204
        assert api("state",a)["state"]["battleFinished"]
        assert progress(api("state",c)["state"],"match.enter")==0
        api("end",a,battleId=successors[0])
        api("leave", b)
        api("leave", c)
        assert api("state", a)["state"]["party"] == ["青空_A"]
    with start() as data:
        wait_http(url, data)
        p = api("login", id="青空_A", password=password)["state"]
        assert p["signature"]==display["signature"] and p["loadoutJson"]==equipment and p["friends"]==["青空_B","青空_C"] and p["victories"]==1
        assert p["xp"] == drawn["xp"] + task["experience"] and p["credits"] == drawn["credits"] + task["credits"]
        assert event(previous,3,2,now)==204
        assert progress(api("state",b)["state"],"round.complete")==0
        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            assert list(pool.map(lambda _:event(previous,4,2,now),range(2)))==[204,204]
        assert progress(api("state",b)["state"],"round.complete")==1
        historical=time.strftime("%Y-%m-%d",time.gmtime(yesterday))
        with sqlite3.connect(work/"db"/"players.db") as db:
            assert db.execute("SELECT progress FROM daily WHERE owner=? AND day=? AND task=?",
                              ("青空_B",historical,task["id"])).fetchone()==(1,)
            assert db.execute("SELECT COUNT(*),MIN(occurred) FROM battle_progress WHERE room=? AND ordinal=3 AND seat=1",
                              (previous,)).fetchone()==(1,yesterday)
    env["ANIMECS_LOCAL_PRACTICE_PROGRESS"]="1"
    with start() as data:
        wait_http(url,data)
        local=api("login",id="青空_B",password=password)
        assert local["state"]["localPracticeProgress"]
        for _ in range(2):
            s=api("progress",local["token"],eventId="local-practice.entry",kind="practice.enter",amount=1)["state"]
            assert next(t for t in s["tasks"] if t["eventKey"]=="practice.enter")["progress"]==1
    print("Data: frozen seats, authenticated live entry and round tasks, absent member, occurrence UTC day, duplicates across service restart, late delivery, legacy settlement, concurrent claims and rematch passed")


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("output",type=Path)
    parser.add_argument("--battle",action="store_true",help="Also verify signed join tickets against the dedicated Unity server")
    parser.add_argument("--reservation-only",action="store_true",help="Run only the authoritative activity and one-hour room reservation checks")
    opts=parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="animecs-party-") as temp:
        verify(opts.output.resolve(),Path(temp),opts.battle,opts.reservation_only)
