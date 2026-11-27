"""Local Debian smoke test for published service directories. Uses isolated state and loopback ports."""
import argparse
import concurrent.futures
import contextlib
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import ssl
import struct
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request


def free_port(kind):
    with socket.socket(socket.AF_INET, kind) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def binary_for(output, role):
    name = "AnimeCS." + role + (".exe" if os.name == "nt" else ".x86_64" if role == "Battle" else "")
    return output / role / name


@contextlib.contextmanager
def running(binary, env, log, *args):
    with log.open("ab") as output:
        proc = subprocess.Popen([str(binary), *args], stdout=output, stderr=output, env=env, cwd=log.parent)
        try:
            yield proc
        finally:
            proc.terminate()
            try:
                proc.wait(10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()


def request(url, method="GET", body=None, headers=None, *, tls=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=4, context=tls) as reply:
            return reply.status, reply.read(), reply.headers
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read(), exc.headers


def wait_http(url, proc, tls=None):
    until = time.monotonic() + 20
    while time.monotonic() < until and proc.poll() is None:
        try:
            if request(url + "/health", tls=tls)[0] == 200:
                return
        except OSError:
            pass
        time.sleep(.1)
    raise RuntimeError("Service did not start")


def data_test(output, work):
    binary = binary_for(output, "Data")
    url = "http://127.0.0.1:" + str(free_port(socket.SOCK_STREAM))
    token = secrets.token_hex(24)
    env = dict(os.environ, ANIMECS_DATA_TOKEN=token, ANIMECS_DATA_DIR=str(work / "db"), ANIMECS_DATA_URL=url)
    endpoint = url + "/v1/players/smoke-test"
    headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json"}
    profile = {"inventory": ["outfit.test"], "equipment": {"outfit": "outfit.test"}, "victories": 2, "avatarId": "avatar.test"}
    with running(binary, env, work / "data.log") as proc:
        wait_http(url, proc)
        assert request(endpoint)[0] == 401
        assert request(endpoint, "PUT", profile, headers)[0] == 428
        assert request(endpoint, "PUT", profile, {**headers, "If-None-Match": "*"})[0] == 201
        assert request(endpoint, "PUT", profile, {**headers, "If-None-Match": "*"})[0] == 412
        profile["victories"] = 3
        assert request(endpoint, "PUT", profile, {**headers, "If-Match": '"1"'})[0] == 204
        assert request(endpoint, "PUT", profile, {**headers, "If-Match": '"1"'})[0] == 412
        start = threading.Barrier(2)
        def update(victories):
            candidate = {**profile, "victories": victories}
            start.wait(timeout=5)
            status = request(endpoint, "PUT", candidate, {**headers, "If-Match": '"2"'})[0]
            return status, candidate
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = dict(pool.map(update, (5, 7)))
        assert set(results) == {204, 412}
        profile = results[204]
    with running(binary, env, work / "data.log") as proc:
        wait_http(url, proc)
        status, body, response = request(endpoint, headers=headers)
        assert status == 200 and json.loads(body)["profile"] == profile and response["ETag"] == '"3"'
    print("Data: authenticated writes, concurrent update conflict and restart persistence passed", flush=True)


def update_test(output, work):
    binary = binary_for(output, "HotUpdate")
    url = "http://127.0.0.1:" + str(free_port(socket.SOCK_STREAM))
    env = dict(os.environ, ANIMECS_UPDATE_DIR=str(work / "updates"), ANIMECS_UPDATE_URL=url)
    source = work / "source"
    source.mkdir()
    payload = b'{"setting":1}'
    (source / "rules.json").write_bytes(payload)
    with running(binary, env, work / "update.log") as proc:
        wait_http(url, proc)
        assert request(url + "/v1/manifest")[0] == 204
        publish = [str(binary), "publish", "v1", str(source)]
        first = subprocess.run(publish, check=True, env=env, capture_output=True, text=True).stdout.strip()
        repeated = subprocess.run(publish, check=True, env=env, capture_output=True, text=True).stdout.strip()
        manifest = request(url + "/v1/manifest")[1]
        assert first == repeated == "updateManifestSha256=" + hashlib.sha256(manifest).hexdigest()
        row = json.loads(manifest)["files"][0]
        assert row == {"path": "rules.json", "sha256": hashlib.sha256(payload).hexdigest(), "size": len(payload)}
        blob = url + "/v1/blobs/" + row["sha256"]
        assert request(blob)[1] == payload
        assert request(blob, headers={"Range": "bytes=0-3"})[:2] == (206, payload[:4])
        (source / "rules.json").write_bytes(b'{"setting":2}')
        assert subprocess.run(publish, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode != 0
        second = subprocess.run([str(binary), "publish", "v2", str(source)], check=True, env=env,
                                capture_output=True, text=True).stdout.strip()
        manifest = request(url + "/v1/manifest")[1]
        assert second != first and second == "updateManifestSha256=" + hashlib.sha256(manifest).hexdigest()
        assert json.loads(manifest)["version"] == "v2" and request(blob)[1] == payload
        (source / "rules.json").write_bytes(payload)
        assert subprocess.run(publish, check=True, env=env, capture_output=True, text=True).stdout.strip() == first
        restored = json.loads(request(url + "/v1/manifest")[1])
        assert restored["version"] == "v1" and restored["files"] == [row]
    print("HotUpdate: live manifest switch, rollback, digest, range and immutable releases passed", flush=True)


def tls_test(output, work, roles):
    openssl = shutil.which("openssl")
    if not openssl and os.name == "nt":
        git_openssl = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/usr/bin/openssl.exe"
        if git_openssl.is_file():
            openssl = str(git_openssl)
    if not openssl:
        raise RuntimeError("--tls requires the OpenSSL command-line utility")
    cert, key, pfx = (work / name for name in ("qa-cert.pem", "qa-key.pem", "qa.pfx"))
    secret = secrets.token_hex(24)
    env = dict(os.environ, ANIMECS_QA_PFX_PASSWORD=secret)
    subprocess.run([openssl, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                    "-subj", "/CN=localhost", "-addext", "subjectAltName=IP:127.0.0.1",
                    "-addext", "basicConstraints=critical,CA:TRUE",
                    "-addext", "keyUsage=critical,digitalSignature,keyEncipherment,keyCertSign",
                    "-keyout", str(key), "-out", str(cert)], check=True, capture_output=True)
    subprocess.run([openssl, "pkcs12", "-export", "-inkey", str(key), "-in", str(cert),
                    "-out", str(pfx), "-passout", "env:ANIMECS_QA_PFX_PASSWORD"], env=env, check=True, capture_output=True)
    trust = ssl.create_default_context(cafile=str(cert))
    for role in roles:
        if role not in ("Data", "HotUpdate"):
            continue
        prefix = "ANIMECS_DATA" if role == "Data" else "ANIMECS_UPDATE"
        url = "https://127.0.0.1:" + str(free_port(socket.SOCK_STREAM))
        settings = dict(os.environ, Kestrel__Certificates__Default__Path=str(pfx),
                        Kestrel__Certificates__Default__Password=secret)
        settings[prefix + "_URL"] = url
        settings[prefix + "_DIR"] = str(work / ("tls-" + role))
        with running(binary_for(output, role), settings, work / ("tls-" + role + ".log")) as proc:
            wait_http(url, proc, tls=trust)
            try:
                request(url + "/health")
            except urllib.error.URLError as error:
                if not isinstance(error.reason, ssl.SSLCertVerificationError):
                    raise
            else:
                raise AssertionError("The default client trusted a temporary, uninstalled certificate")
            if role == "Data":
                status, _, _ = request(url + "/v2/register", "POST",
                                       {"id": "tls-check", "password": secrets.token_hex(16)},
                                       {"Content-Type": "application/json"}, tls=trust)
                assert status == 200
            else:
                assert request(url + "/v1/manifest", tls=trust)[0] == 204
        print(role + ": direct TLS and process-local certificate trust passed", flush=True)


def battle_test(output, work):
    binary = binary_for(output, "Battle")
    port = free_port(socket.SOCK_DGRAM)
    env = dict(os.environ, HOME=str(work), ANIMECS_BATTLE_PORT=str(port), ANIMECS_BATTLE_ADDRESS="127.0.0.1")
    magic = 0x53434103
    def input_packet(state, sequence, yaw):
        token, round_no, tick = struct.unpack_from("<Qii", state, 5)
        packet = bytearray(52)
        struct.pack_into("<IBQiiiIhhhhhh", packet, 0, magic, 3, token, round_no, tick, sequence,
                         0, yaw, 0, 32767, 0, 0, 0)
        struct.pack_into("<iH", packet, 46, tick, 32768)
        return packet

    with running(binary, env, work / "battle.log", "-batchmode", "-nographics", "-logFile", "-") as proc, contextlib.ExitStack() as stack:
        clients = []
        for _ in range(2):
            sock = stack.enter_context(socket.socket(socket.AF_INET, socket.SOCK_DGRAM))
            sock.settimeout(.2)
            clients.append(sock)
        address = ("127.0.0.1", port)
        states = [None, None]
        seq = 0
        pending_tick = -1
        until = time.monotonic() + 45
        while time.monotonic() < until and proc.poll() is None:
            for index, sock in enumerate(clients):
                state = states[index]
                if state is None:
                    sock.sendto(struct.pack("<IBQ", magic, 1, 0), address)
                else:
                    seq += 1
                    sock.sendto(input_packet(state, seq, 1200 + index * 300), address)
                try:
                    packet, _ = sock.recvfrom(512)
                except (socket.timeout, ConnectionResetError):
                    continue
                if len(packet) == 98 and struct.unpack_from("<IB", packet) == (magic, 2):
                    states[index] = packet
            if not all(states):
                continue
            assert states[0][21] != states[1][21]
            assert states[0][5:13] != states[1][5:13]
            if all(struct.unpack_from("<i", s, 52)[0] > 0 and abs(struct.unpack_from("<h", s, 36)[0] - yaw) < 3
                   for s, yaw in zip(states, (1200, 1500))):
                tick = struct.unpack_from("<i", states[0], 17)[0]
                if pending_tick < 0:
                    pending_tick = tick
                if tick >= pending_tick + 4:
                    break
        else:
            raise RuntimeError("Battle did not acknowledge both seats")

        # Leave a sequence gap, deliver the newer input twice, then deliver the older input late.
        sequence = seq + 5
        packet = input_packet(states[0], sequence, 2100)
        for outgoing in (packet, packet, input_packet(states[0], sequence - 1, -1700)):
            clients[0].sendto(outgoing, address)
        until = time.monotonic() + 3
        while time.monotonic() < until:
            state = clients[0].recv(512)
            if struct.unpack_from("<i", state, 52)[0] != sequence:
                continue
            assert abs(struct.unpack_from("<h", state, 36)[0] - 2100) < 3
            if struct.unpack_from("<i", state, 17)[0] >= tick + 6:
                break
        else:
            raise RuntimeError("Battle failed to reconcile reordered inputs")

        seats = {state[21] for state in states}
        for _ in range(8):
            sock = stack.enter_context(socket.socket(socket.AF_INET, socket.SOCK_DGRAM))
            sock.settimeout(2)
            sock.sendto(struct.pack("<IBQ", magic, 1, 0), address)
            state = sock.recv(512)
            assert len(state) == 98 and struct.unpack_from("<IB", state) == (magic, 2)
            seats.add(state[21])
        assert seats == set(range(10))
        print("Battle: ten independent seats, private snapshots, tick advance and reordered input ACK passed", flush=True)

    fast_env = dict(env, ANIMECS_BATTLE_SNAPSHOT_HZ="50")
    with running(binary, fast_env, work / "battle-fast.log", "-batchmode", "-nographics", "-logFile", "-") as proc:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.settimeout(.25)
            previous = None
            consecutive = 0
            until = time.monotonic() + 45
            while time.monotonic() < until and proc.poll() is None:
                if previous is None:
                    sock.sendto(struct.pack("<IBQ", magic, 1, 0), address)
                try:
                    state = sock.recv(512)
                except (socket.timeout, ConnectionResetError):
                    continue
                assert len(state) == 98 and struct.unpack_from("<IB", state) == (magic, 2)
                tick = struct.unpack_from("<i", state, 17)[0]
                consecutive = consecutive + 1 if previous is not None and tick == previous + 1 else 0
                previous = tick
                if consecutive >= 4:
                    print("Battle: 50 Hz mode sent snapshots on consecutive simulation ticks", flush=True)
                    break
            else:
                raise RuntimeError("Battle failed to publish at the configured full tick rate")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("roles", nargs="*", metavar="ROLE", help="Data, HotUpdate or Battle; defaults to all three")
    parser.add_argument("--tls", action="store_true", help="also verify direct HTTPS for the selected web services")
    args = parser.parse_args()
    tests = {"Data": data_test, "HotUpdate": update_test, "Battle": battle_test}
    for role in args.roles:
        if role not in tests:
            parser.error("Unknown service: " + role)
    with tempfile.TemporaryDirectory(prefix="animecs-verify-") as directory:
        work = Path(directory)
        try:
            for role in args.roles or tests:
                tests[role](args.output.resolve(), work)
            if args.tls:
                tls_test(args.output.resolve(), work, args.roles or tests)
        except Exception:
            for log in work.glob("*.log"):
                print(log.read_text(errors="replace")[-2800:])
            raise
