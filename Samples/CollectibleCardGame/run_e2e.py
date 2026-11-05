#!/usr/bin/env python3
"""Run a Blazor WebAssembly sample's browser tests against a temporary game server and web client on free ports.

This file is copied into each Blazor sample in the Metaplay SDK repository, next to the sample's e2e.toml. Keep the
copies identical: change one, copy it to the others.

Every listener gets a free port, and the ports are passed to the tests in E2E_* environment variables, so runs from
several checkouts, and CI, don't collide with each other or with a stack started by hand on the default ports.
A run builds the sample's `build` solution, starts the game server and the web client with a fresh database, waits
for both, runs each test phase, and stops everything.

Uses only the Python standard library (3.11 or later, for tomllib) and the .NET SDK. For a phase with `playwright`
set, it installs the browser the tests' Playwright version needs.

Usage, from the sample directory:
  python3 run_e2e.py [options] [test args]   Bring a stack up, run the test phases on it, tear it down. Arguments
                                     this script does not know are appended to each phase's command. A --filter is
                                     combined with the phase's own, so it narrows each phase.
  --sample DIR                       The sample directory (default: the directory of this script).
  --output-dir DIR                   Keep logs and test output in DIR (default: $E2E_OUTPUT_DIR). Without
                                     it they go to a temporary directory that is deleted when the tests pass.
  --serve                            Same as a plain run, but leave the stack running and print its URL.
  --stop                             Stop the stack that --serve left running for this sample directory.
  --no-test                          Bring the stack up without running the tests (implies --serve).
  --trimmed                          Publish the web client (Release, IL-trimmed) and serve that instead of the dev
                                     server, so the tests run against the client players get. Slower to start.
  --plain-server                     Start the server without the `testArgs` from e2e.toml, e.g. with the shipped
                                     match timings, for playing by hand.
  --phase NAME                       Run only the phases whose name contains NAME (case-insensitive). Repeatable.
  --serve-static WWWROOT PORT [--bind ADDR]
                                     Serve a published client the way a deployed server does (the harness runs this
                                     itself for --trimmed): the .wasm MIME type, the .br and .gz files the publish
                                     wrote, and index.html for client-side routes.

  e.g. python3 run_e2e.py --filter "FullyQualifiedName~ShellPageTests"

e2e.toml, at the sample root:

  prebuild = [["dotnet", "run", "--project", "tools/X", "--", "arg"]]   # optional: commands run before the build
  build = "tools/e2e-stack.slnx"                # built once, before anything starts

  [server]
  project = "Backend/Server"                    # started with `dotnet run` from this directory
  args = ["--TestRoutes:Enabled=true"]          # always passed
  testArgs = ["--Matchmaking:FillWait=00:00:01.500"]   # passed unless --plain-server
  testArgsAllowed = ["Matchmaking:FillWait"]    # optional: the only options testArgs may set
  env = { METAPLAY_OPTIONS = "Config/Options.base.yaml" }   # optional

  [client]
  project = "WebClient/WebClient.csproj"
  framework = "net10.0-browser"

  [[phases]]                                    # run in order
  name = "browser tests"
  command = ["dotnet", "test", "WebClient.Tests/WebClient.Tests.csproj", "--no-build"]
  nunitWorkers = true                           # append an NUnit worker count sized to the host
  server = true                                 # whether the shared server is up during the phase (default)
  playwright = "WebClient.Tests/bin/Debug/net10.0"   # optional: install the Chromium this build of Playwright needs

The game server always gets its listen ports, --Environment:EnableSystemHttpServer=true (for the /isReady probe) and a
fresh --Database:SqliteDirectory. The web client is up for every phase. A phase whose tests start their own game
server (to restart it, or to run it with other options) sets `server = false`: the harness stops the shared server
first, and the tests start theirs with E2E_SERVER_PORT_ARGS, so it binds the same ports and the web client reaches it.

Environment variables for the tests. They are not prefixed METAPLAY_: the game server reads each METAPLAY_* variable as
a runtime option and exits on an unknown one, and a test that starts its own server passes its environment on.
  E2E_CLIENT_URL         Origin of the web client
  E2E_WS_PORT            The game server's WebSocket port; the client reads it from the page URL (?wsPort=)
  E2E_CDN_PORT           The game server's CDN emulator port; the client reads it from the page URL (?cdnPort=)
  E2E_SERVER_PUBLIC_URL  The server's public web API
  E2E_SERVER_ADMIN_URL   The server's Admin API (and LiveOps Dashboard)
  E2E_SERVER_SYSTEM_URL  The server's system HTTP server, whose /isReady reports readiness
  E2E_SERVER_PORT_ARGS   The server's listener arguments, one per line, for a test that starts its own server
  E2E_SERVER_ARGS        The extra arguments the server was started with, one per line
  E2E_CLIENT_WWWROOT     The published wwwroot being served, set only with --trimmed
  E2E_OUTPUT_DIR         The run's output directory. A phase may write summary.txt there, and the harness prints it.

Environment variables for the harness:
  E2E_TEST_WORKERS       NUnit worker count for phases with nunitWorkers. If unset, half the host's cores divided
                         by the number of active runs (runs holding an admission slot plus stacks left running by
                         --serve), at most 12.
  E2E_MAX_RUNS           Maximum number of runs active on this host at once. Further runs wait for a free slot
                         (default 2, 0 disables the limit).
  E2E_ADMISSION_TIMEOUT  Seconds a waiting run waits for a slot before it starts anyway (default 1800,
                         0 waits forever).
  E2E_SLOT_PORT_BASE     First port of the loopback UDP port range used as admission slots (default 30500).
  E2E_PLAYWRIGHT_WITH_DEPS=1  Also install the Playwright browser's Linux system packages (uses sudo), for CI.
"""
import argparse
import atexit
import hashlib
import http.server
import json
import os
import platform
import posixpath
import shutil
import signal
import socket
import socketserver
import subprocess
import sys
import tempfile
import threading
import time
import tomllib
import urllib.error
import urllib.parse
import urllib.request

IS_WINDOWS = platform.system() == "Windows"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = "e2e.toml"

# This script prints output from other processes, such as build errors and server log lines. Use UTF-8 instead of
# the console code page, and escape characters that cannot be encoded so that they do not raise an error.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="backslashreplace")

# Child processes of the stack. They are killed on every exit path unless _leave_stack_running is set.
_tracked_procs = []
# Log file handles held open for those children.
_log_files = []
# Set after a --serve run writes its state files. After that, the cleanup hooks leave the stack running.
_leave_stack_running = False


def log(message):
    print("run_e2e.py: %s" % message, flush=True)


def fail(message, code=1):
    sys.stderr.write("run_e2e.py: %s\n" % message)
    sys.exit(code)


# -----------------------------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------------------------

class Phase:
    def __init__(self, raw, index):
        self.name = raw.get("name", "phase %d" % (index + 1))
        self.command = raw.get("command")
        if not isinstance(self.command, list) or not self.command:
            fail("%s: phases[%d].command must be a non-empty list" % (CONFIG_FILE, index))
        # Whether the shared game server is up while the phase runs. The web client is up for every phase. A phase
        # whose tests start their own server sets this to false; its server binds the shared server's ports
        # (E2E_SERVER_PORT_ARGS), so the web client reaches it.
        self.server = raw.get("server", True)
        # Whether to append `-- NUnit.NumberOfTestWorkers=N` to a `dotnet test` command.
        self.nunit_workers = raw.get("nunitWorkers", False)
        # The build output directory of a Playwright.NET test assembly. The harness installs the browser that
        # assembly's Playwright version needs, with the driver Playwright copies there.
        self.playwright = raw.get("playwright")


class Config:
    def __init__(self, sample_dir):
        path = os.path.join(sample_dir, CONFIG_FILE)
        if not os.path.isfile(path):
            fail("no %s in %s" % (CONFIG_FILE, sample_dir))
        with open(path, "rb") as f:
            raw = tomllib.load(f)

        self.build = raw.get("build")
        if not self.build:
            fail("%s: `build` (the solution or project to build) is required" % CONFIG_FILE)
        # Commands run before the build, each a list of arguments, e.g. to generate a file the build reads.
        self.prebuild = raw.get("prebuild", [])

        server = raw.get("server", {})
        self.server_project = server.get("project")
        if not self.server_project:
            fail("%s: `server.project` is required" % CONFIG_FILE)
        self.server_args = server.get("args", [])
        self.server_test_args = server.get("testArgs", [])
        self.server_test_args_allowed = server.get("testArgsAllowed")
        self.server_env = server.get("env", {})

        client = raw.get("client", {})
        self.client_project = client.get("project")
        self.client_framework = client.get("framework")
        if not self.client_project or not self.client_framework:
            fail("%s: `client.project` and `client.framework` are required" % CONFIG_FILE)

        self.phases = [Phase(p, i) for (i, p) in enumerate(raw.get("phases", []))]
        if not self.phases:
            fail("%s: at least one [[phases]] entry is required" % CONFIG_FILE)

    def exit_unless_test_args_allowed(self):
        # An optional guard: when `testArgsAllowed` is set, every option `testArgs` sets must be listed in it. A
        # sample uses it to keep a timer that a test measures itself against from being shortened by accident.
        if self.server_test_args_allowed is None:
            return
        for arg in self.server_test_args:
            key = arg.lstrip("-").split("=", 1)[0]
            if key not in self.server_test_args_allowed:
                fail("%s: server.testArgs sets %s, which is not in server.testArgsAllowed. See the comments in "
                     "%s before adding it there." % (CONFIG_FILE, key, CONFIG_FILE), code=2)


def ensure_dotnet_on_path():
    # dotnet is commonly installed under ~/.dotnet without being on PATH.
    if shutil.which("dotnet") is None:
        home_dotnet = os.path.join(os.path.expanduser("~"), ".dotnet")
        if os.path.isfile(os.path.join(home_dotnet, "dotnet") + (".exe" if IS_WINDOWS else "")):
            os.environ["PATH"] = home_dotnet + os.pathsep + os.environ.get("PATH", "")


def state_dir(sample_dir):
    # The directory name is derived from the sample's path, so each checkout's --serve stack has its own state.
    digest = hashlib.sha1(sample_dir.encode("utf-8")).hexdigest()[:12]
    return os.path.join(tempfile.gettempdir(), "metaplay-e2e-" + digest)


# -----------------------------------------------------------------------------------------------
# Free-port allocation
#
# Every listener the stack opens gets its own free port, because a listener left on its default port would be
# shared by two concurrent stacks. launch_server passes each port to its server option.
#
# A clash on DIRECT_UDP would go unnoticed. A local server's direct-transport router binds 0.0.0.0 and [::]
# with SO_REUSEADDR, so two servers both bind the default port and datagrams reach only one of them. A failed
# bind is logged and the server starts anyway, so neither the readiness probe nor the bring-up retry detects it.
# -----------------------------------------------------------------------------------------------

# Each listener with the socket type used to probe for a free port. A TCP port being free does not mean the
# same UDP port is free, so DIRECT_UDP is probed with a UDP socket.
PORT_KEYS = [
    ("WEB", socket.SOCK_STREAM),
    ("WS", socket.SOCK_STREAM),
    ("CLIENT_TCP", socket.SOCK_STREAM),
    ("CDN", socket.SOCK_STREAM),
    ("ADMIN", socket.SOCK_STREAM),
    ("PUBWEB", socket.SOCK_STREAM),
    ("REMOTING", socket.SOCK_STREAM),
    ("METRIC", socket.SOCK_STREAM),
    ("SYSHTTP", socket.SOCK_STREAM),
    ("DIRECT_UDP", socket.SOCK_DGRAM),
]


def get_free_port(kind=socket.SOCK_STREAM):
    # Bind on all interfaces, not only loopback, because the CDN emulator listens on "*" and the direct-transport
    # router binds 0.0.0.0. A port that is free on 127.0.0.1 but taken on another interface is not usable.
    sock = socket.socket(socket.AF_INET, kind)
    try:
        sock.bind(("", 0))
        return sock.getsockname()[1]
    finally:
        sock.close()


def allocate_ports():
    ports = {}
    for (key, kind) in PORT_KEYS:
        while True:
            port = get_free_port(kind)
            if port not in ports.values():
                ports[key] = port
                break
    return ports


def client_url(ports):
    return "http://localhost:%d" % ports["WEB"]


def server_public_url(ports):
    return "http://localhost:%d" % ports["PUBWEB"]


def server_admin_url(ports):
    # The Admin API, which the LiveOps Dashboard reads from.
    return "http://localhost:%d" % ports["ADMIN"]


def browsable_url(ports):
    # The URL to open in a browser. The client reads the server ports from the query string. Without them it
    # uses the localhost environment's default ports and connects to whatever server holds them.
    return "%s/?wsPort=%d&cdnPort=%d" % (client_url(ports), ports["WS"], ports["CDN"])


# -----------------------------------------------------------------------------------------------
# Host-wide admission throttle
#
# Concurrent stacks share the CPU, and a cold WASM client start fails with "An unhandled error has occurred"
# when the CPU is overloaded. So a run takes one of E2E_MAX_RUNS host-wide slots before any heavy work, and
# waits while all slots are taken. A slot is a loopback UDP socket bound without SO_REUSEADDR and held while
# the process runs. The OS releases it on any exit, including a kill or a crash, so no stale state is left.
# An unrelated process on a port in the range only lowers the limit. After E2E_ADMISSION_TIMEOUT a waiting run
# starts anyway.
# -----------------------------------------------------------------------------------------------

_run_slot = None

SLOT_POLL_SECONDS = 1.0     # how often a waiting run tries to take a slot
SLOT_NOTICE_SECONDS = 15.0  # how often a waiting run prints that it is still waiting


def _int_env(name, default):
    try:
        return int(os.environ.get(name, str(default)))
    except ValueError:
        return default


def _bind_run_slot(base, count):
    for port in range(base, base + count):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            if IS_WINDOWS:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
            sock.bind(("127.0.0.1", port))
            return sock
        except OSError:
            sock.close()
    return None


def acquire_run_slot():
    global _run_slot
    max_runs = _int_env("E2E_MAX_RUNS", 2)
    if max_runs <= 0:
        return
    base = _int_env("E2E_SLOT_PORT_BASE", 30500)
    if base < 1 or base + max_runs - 1 > 65535:
        # Port 0 would bind a new ephemeral port every time, so every run would get a slot. A base near the
        # top of the port range leaves no room for the slots. Print a warning and run without the limit.
        log("E2E_SLOT_PORT_BASE=%d cannot host %d slots inside 1-65535; throttle disabled." % (base, max_runs))
        return
    timeout_seconds = _int_env("E2E_ADMISSION_TIMEOUT", 1800)
    start = time.monotonic()
    deadline = (start + timeout_seconds) if timeout_seconds > 0 else None
    last_notice_at = None
    while True:
        sock = _bind_run_slot(base, max_runs)
        if sock is not None:
            _run_slot = sock
            if last_notice_at is not None:
                log("a slot came free after %ds — starting." % int(time.monotonic() - start))
            return
        now = time.monotonic()
        if last_notice_at is None or (now - last_notice_at) >= SLOT_NOTICE_SECONDS:
            log("%d E2E runs are already active on this host (the cap); waiting for a slot (%ds so far). Change "
                "it with E2E_MAX_RUNS, or set 0 to disable the throttle." % (max_runs, int(now - start)))
            last_notice_at = now
        if deadline is not None and now >= deadline:
            log("still no slot after %ds — going ahead anyway; the throttle is never a hard block."
                % timeout_seconds)
            return
        time.sleep(SLOT_POLL_SECONDS)


def release_run_slot():
    global _run_slot
    if _run_slot is not None:
        try:
            _run_slot.close()
        except OSError:
            pass
        _run_slot = None


# -----------------------------------------------------------------------------------------------
# Process launch and teardown
# -----------------------------------------------------------------------------------------------

def child_environment():
    # The environment for everything the harness starts, without the METAPLAY_* variables it inherited. The game
    # server reads every METAPLAY_* variable as a runtime option and exits on an unknown one, and a test that starts
    # its own server passes its environment on. CI sets such variables for other tools (for example
    # METAPLAY_CICD_BOT_CREDENTIALS for the Metaplay CLI). A sample sets the ones its server needs in e2e.toml's
    # server.env.
    return {key: value for (key, value) in os.environ.items() if not key.startswith("METAPLAY_")}


def launch_process(name, argv, cwd, logdir, extra_env=None):
    log_path = os.path.join(logdir, name + ".log")
    log_file = open(log_path, "wb")
    env = child_environment()
    if extra_env:
        env.update(extra_env)
    # Start the child in its own process group (Windows) or session (POSIX), so that kill_tree can stop the
    # whole `dotnet run` process tree at once.
    popen_kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if IS_WINDOWS else {"start_new_session": True}
    proc = subprocess.Popen(argv, cwd=cwd, stdin=subprocess.DEVNULL, stdout=log_file,
                            stderr=subprocess.STDOUT, env=env, **popen_kwargs)
    _tracked_procs.append(proc)
    _log_files.append(log_file)
    return proc


def kill_tree(pid, grace_seconds=10.0, proc=None):
    # Returns True when the process group has exited or SIGKILL was sent to it. Returns False when the group
    # could not be signalled, so that the caller keeps tracking it and tries again later.
    #
    # Pass `proc`, the Popen object for `pid`, only if this process launched `pid`. The wait loop below reaps
    # it with proc.poll().
    if IS_WINDOWS:
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return True
    try:
        group = os.getpgid(pid)
    except ProcessLookupError:
        return True
    except (PermissionError, OSError):
        return False

    # Send SIGTERM first so that the server shuts down cleanly, then SIGKILL after the grace period. Without the
    # SIGKILL, a hung `dotnet run` tree or a server that never finishes shutting down would keep its ports after
    # the harness exits.
    try:
        os.killpg(group, signal.SIGTERM)
    except ProcessLookupError:
        return True
    except (PermissionError, OSError):
        return False

    deadline = time.monotonic() + grace_seconds
    while time.monotonic() < deadline:
        # Reap our own child when it exits. A process group exists while any member exists, including a zombie,
        # so an unreaped child would keep the probe below succeeding until the deadline. Reaping removes only
        # the zombie, so a group with a live member still gets SIGKILL at the deadline.
        if proc is not None:
            proc.poll()
        try:
            os.killpg(group, 0)
        except ProcessLookupError:
            return True
        except (PermissionError, OSError):
            return False
        time.sleep(0.2)

    # Return whether SIGKILL was delivered. If it could not be sent, return False so that the group stays in the
    # tracked list and the atexit cleanup tries again.
    try:
        os.killpg(group, signal.SIGKILL)
    except ProcessLookupError:
        return True
    except (PermissionError, OSError):
        return False
    return True


def stop_tracked_processes():
    # Safe to call more than once: at teardown, from the atexit handler, and between bring-up attempts.
    # Processes that kill_tree confirms are removed from the list. The others stay, so a later call tries again.
    if _leave_stack_running:
        return
    unconfirmed = []
    for proc in _tracked_procs:
        if not kill_tree(proc.pid, proc=proc):
            unconfirmed.append(proc)
    _tracked_procs[:] = unconfirmed
    for log_file in _log_files:
        try:
            log_file.close()
        except OSError:
            pass
    _log_files.clear()


def _signal_handler(signum, frame):
    stop_tracked_processes()
    sys.exit(128 + signum)


# -----------------------------------------------------------------------------------------------
# Stopping a --serve stack
#
# The state directory records each process's PID and start time. A PID whose start time no longer matches was
# reused by an unrelated process, so it is not killed.
# -----------------------------------------------------------------------------------------------

def read_process_identity(pid):
    if IS_WINDOWS:
        try:
            probe = subprocess.run(
                ["powershell", "-NoProfile", "-Command", "(Get-Process -Id %d).StartTime.ToString('o')" % pid],
                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=8)
            start = probe.stdout.strip()
            return {"starttime": start} if start and probe.returncode == 0 else None
        except (OSError, subprocess.SubprocessError):
            return None
    if os.path.isdir("/proc"):
        try:
            with open("/proc/%d/stat" % pid, "r") as f:
                data = f.read()
            # Field 2 (comm) is in parentheses and can contain spaces, so split the fields after the last ')'.
            fields = data[data.rfind(")") + 1:].split()
            return {"starttime": fields[19]}
        except (OSError, IndexError):
            return None
    # macOS has no /proc. `ps -o lstart=` prints the start time, or nothing for a PID that does not exist.
    try:
        probe = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True,
                               encoding="utf-8", errors="replace", timeout=8)
        start = probe.stdout.strip()
        return {"starttime": start} if start and probe.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def stop_stack(sample_dir, quiet=False):
    state_path = state_dir(sample_dir)
    pids_file = os.path.join(state_path, "pids")
    if not os.path.isfile(pids_file):
        if not quiet:
            log("no served stack for %s." % sample_dir)
        return

    with open(pids_file) as f:
        pids = [int(token) for token in f.read().split()]

    identities = {}
    identities_file = os.path.join(state_path, "idents")
    if os.path.isfile(identities_file):
        try:
            with open(identities_file) as f:
                identities = json.load(f)
        except (OSError, ValueError):
            pass

    # Skip a PID that no longer exists or whose start time does not match the record. Still stop the other
    # PIDs, because the state directory is deleted afterwards and a server left running would then have no
    # record to stop it by.
    for pid in pids:
        live_identity = read_process_identity(pid)
        if live_identity is None:
            continue
        if identities.get(str(pid)) != live_identity:
            log("pid %d no longer matches what was recorded (recycled?); leaving it alone." % pid)
            continue
        kill_tree(pid)

    url = "unknown"
    url_file = os.path.join(state_path, "url")
    if os.path.isfile(url_file):
        with open(url_file) as f:
            url = f.read().strip()
    log("stopped the served stack (%s)" % url)
    shutil.rmtree(state_path, ignore_errors=True)


def write_serve_state(state_path, pids, url, ports):
    os.makedirs(state_path, exist_ok=True)
    with open(os.path.join(state_path, "pids"), "w") as f:
        f.write(" ".join(str(pid) for pid in pids) + "\n")
    with open(os.path.join(state_path, "url"), "w") as f:
        f.write(url + "\n")
    with open(os.path.join(state_path, "ports"), "w") as f:
        json.dump(ports, f)
    with open(os.path.join(state_path, "idents"), "w") as f:
        json.dump({str(pid): read_process_identity(pid) for pid in pids}, f)


# -----------------------------------------------------------------------------------------------
# Build
#
# One `dotnet build` of the sample's `build` solution builds the game server, the web client and the test
# assemblies, so their shared project graph is evaluated and restored once. Everything is built before anything
# starts, and the processes start with --no-build, because two concurrent `dotnet run` builds would write shared
# projects (such as Metaplay.Core) into the same obj directory at once. The web client's dev server also resolves
# the app's fingerprinted assets once at startup, so the client must be built first.
# -----------------------------------------------------------------------------------------------

def build_stack(sample_dir, config):
    started = time.monotonic()
    for argv in config.prebuild:
        log("prebuild: %s" % " ".join(argv))
        exit_code = subprocess.call(argv, cwd=sample_dir)
        if exit_code != 0:
            fail("prebuild command failed: %s" % " ".join(argv), exit_code)

    log("building %s" % config.build)
    exit_code = subprocess.call(["dotnet", "build", config.build], cwd=sample_dir)
    if exit_code != 0:
        fail("build failed for %s" % config.build, exit_code)

    log("built in %.1fs" % (time.monotonic() - started))


def publish_webclient(sample_dir, config):
    """Publish the client for --trimmed. Returns the wwwroot to serve, or None if the publish failed."""
    argv = ["dotnet", "publish", config.client_project, "-c", "Release", "-f", config.client_framework]
    log("publishing the web client (Release, trimmed) — this is slower than a build")
    started = time.monotonic()
    if subprocess.call(argv, cwd=sample_dir) != 0:
        sys.stderr.write("run_e2e.py: the web client publish failed\n")
        return None
    log("published in %.1fs" % (time.monotonic() - started))

    # Serve the publish's wwwroot, not bin/Release/<tfm>/wwwroot, which holds only _framework, without index.html
    # or the app's static assets.
    client_dir = os.path.dirname(os.path.join(sample_dir, config.client_project))
    wwwroot = os.path.join(client_dir, "bin", "Release", config.client_framework, "publish", "wwwroot")
    if not os.path.isfile(os.path.join(wwwroot, "index.html")):
        sys.stderr.write("run_e2e.py: the publish left no index.html in %s\n" % wwwroot)
        return None
    return wwwroot


def install_playwright_browsers(sample_dir, config):
    # Each Playwright version needs its own browser build, so install it after every build. An install that is
    # already present returns at once. Use the driver that Playwright.NET copies into the build output (a bundled
    # node and cli.js) rather than its playwright.ps1, which needs PowerShell.
    # E2E_PLAYWRIGHT_WITH_DEPS=1 also installs the browser's system packages (Linux only, uses sudo), for CI.
    for output_dir in sorted({phase.playwright for phase in config.phases if phase.playwright}):
        driver_dir = os.path.join(sample_dir, output_dir, ".playwright")
        node_root = os.path.join(driver_dir, "node")
        # One directory per platform the assembly was built for (e.g. darwin-arm64), next to a LICENSE file.
        node_dirs = sorted(d for d in os.listdir(node_root) if os.path.isdir(os.path.join(node_root, d))) \
            if os.path.isdir(node_root) else []
        if not node_dirs:
            fail("no Playwright driver in %s — is the test project built?" % driver_dir)
        node = os.path.join(driver_dir, "node", node_dirs[0], "node.exe" if IS_WINDOWS else "node")
        argv = [node, os.path.join(driver_dir, "package", "cli.js"), "install", "chromium"]
        if os.environ.get("E2E_PLAYWRIGHT_WITH_DEPS") == "1":
            argv.append("--with-deps")
        log("installing the Playwright browser for %s" % output_dir)
        if subprocess.call(argv, cwd=sample_dir) != 0:
            fail("the Playwright browser install failed")


# -----------------------------------------------------------------------------------------------
# Serving a published client (--serve-static)
#
# `python3 -m http.server` is not enough for a published client. It does not send the `.wasm` MIME type, so the
# browser refuses to load the module. It ignores the `.br` and `.gz` files that `dotnet publish` writes next to
# each asset. It returns 404 for a client-side route instead of serving `index.html`.
# -----------------------------------------------------------------------------------------------

# Content types for the file types in a published client. Other files, including Blazor's `.dat` ICU data and
# the game's `.mpa` config archive, are served as application/octet-stream. `.wasm` must be application/wasm,
# or the browser refuses to compile it and the client does not start.
CONTENT_TYPES = {
    ".wasm":        "application/wasm",
    ".js":          "text/javascript",
    ".mjs":         "text/javascript",
    ".json":        "application/json",
    ".css":         "text/css",
    ".html":        "text/html; charset=utf-8",
    ".htm":         "text/html; charset=utf-8",
    ".png":         "image/png",
    ".jpg":         "image/jpeg",
    ".jpeg":        "image/jpeg",
    ".svg":         "image/svg+xml",
    ".ico":         "image/x-icon",
    ".woff2":       "font/woff2",
    ".webmanifest": "application/manifest+json",
    ".txt":         "text/plain; charset=utf-8",
}

# In order of preference. Brotli files are smaller than gzip files for the framework assemblies.
ENCODINGS = (("br", ".br"), ("gzip", ".gz"))


def accepted_encodings(header):
    # Returns the set of encodings the client accepts. The client's q-value ranking is ignored because the
    # server picks by ENCODINGS order. An encoding with q=0 (including `q=0.000`) is refused and left out.
    offered = set()
    for part in (header or "").split(","):
        token = part.strip().lower()
        if not token:
            continue
        name, _, params = token.partition(";")
        if _weight(params) == 0.0:
            continue
        offered.add(name.strip())
    return offered


def _weight(params):
    """The q value in an Accept-Encoding parameter string, defaulting to 1.0 when there is none to read."""
    for parameter in params.split(";"):
        key, _, value = parameter.partition("=")
        if key.strip() == "q":
            try:
                return float(value.strip())
            except ValueError:
                return 1.0
    return 1.0


class StaticHandler(http.server.BaseHTTPRequestHandler):
    # HTTP/1.1 keeps connections open between requests. A client start makes hundreds of requests, and a new
    # connection for each one would slow the start down.
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass  # The harness saves this process's output as the client log, and a line per request would hide the errors.

    def do_GET(self):
        self.serve(send_body=True)

    def do_HEAD(self):
        self.serve(send_body=False)

    def serve(self, send_body):
        path = self.resolve(urllib.parse.urlparse(self.path).path)
        if path is None:
            self.send_error(404)
            return

        encoding, disk_path = self.negotiate(path)
        try:
            f = open(disk_path, "rb")
        except OSError:
            self.send_error(404)
            return

        # The body is streamed from the open file instead of read into memory first, because the framework
        # files are megabytes each and the handler threads serve many of them in parallel.
        with f:
            content_type = CONTENT_TYPES.get(os.path.splitext(path)[1].lower(), "application/octet-stream")
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(os.fstat(f.fileno()).st_size))
            if encoding:
                self.send_header("Content-Encoding", encoding)
            # no-cache makes the browser revalidate every asset, so that a test never runs a cached asset from an
            # earlier build, including when the stack is redeployed while a browser has the page open.
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Vary", "Accept-Encoding")
            self.end_headers()
            if not send_body:
                return
            try:
                shutil.copyfileobj(f, self.wfile)
            except (BrokenPipeError, ConnectionResetError):
                # The browser closed the connection mid-response, for example on navigation or in a test that
                # aborts framework requests. Without this, socketserver would print a traceback into the client log.
                pass

    def resolve(self, url_path):
        """The file this request names, or None. Client-side routes fall back to index.html."""
        relative = posixpath.normpath(urllib.parse.unquote(url_path)).lstrip("/")
        if relative in ("", "."):
            relative = "index.html"

        candidate = os.path.normpath(os.path.join(self.server.wwwroot, relative))
        # Path traversal: a request may not leave the published root.
        if not candidate.startswith(self.server.wwwroot + os.sep) and candidate != self.server.wwwroot:
            return None
        if os.path.isfile(candidate):
            return relative

        # A missing path with a file extension is a missing file, so return 404. Serving index.html for a
        # missing .wasm or .js file would show up as a confusing MIME type error. A missing path without an
        # extension is a client-side route, so serve index.html.
        if os.path.splitext(relative)[1]:
            return None
        return "index.html"

    def negotiate(self, relative):
        """The pre-compressed sibling to serve, as (Content-Encoding, path), or (None, path) for the raw file."""
        raw_path = os.path.join(self.server.wwwroot, relative)
        offered = accepted_encodings(self.headers.get("Accept-Encoding"))
        for name, suffix in ENCODINGS:
            if name in offered and os.path.isfile(raw_path + suffix):
                return name, raw_path + suffix
        return None, raw_path


class StaticServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True
    # Queue up to 128 pending connections, so all the clients that parallel tests start at once can connect.
    request_queue_size = 128


def serve_static(argv):
    """--serve-static WWWROOT PORT [--bind ADDR]: serve a published client until interrupted."""
    bind_address = "0.0.0.0"
    if "--bind" in argv:
        index = argv.index("--bind")
        if index + 1 >= len(argv):
            fail("--serve-static: --bind needs an address", 2)
        bind_address = argv[index + 1]
        del argv[index:index + 2]
    if len(argv) != 2:
        fail("usage: run_e2e.py --serve-static WWWROOT PORT [--bind ADDR]", 2)

    wwwroot = os.path.abspath(argv[0])
    port = int(argv[1])
    if not os.path.isfile(os.path.join(wwwroot, "index.html")):
        fail("%s holds no index.html — is it a *publish* wwwroot?" % wwwroot, 2)

    server = StaticServer((bind_address, port), StaticHandler)
    server.wwwroot = wwwroot
    log("serving %s on http://%s:%d" % (wwwroot, bind_address, port))
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


# -----------------------------------------------------------------------------------------------
# Stack bring-up
# -----------------------------------------------------------------------------------------------

def fresh_database_dir(logdir):
    # The server's SQLite files default to its bin directory, which every run from one checkout shares. A run
    # would then start with the state an earlier run left. Give each stack an empty database directory.
    db_dir = os.path.join(logdir, "db")
    shutil.rmtree(db_dir, ignore_errors=True)
    os.makedirs(db_dir, exist_ok=True)
    return db_dir


def server_port_args(ports):
    # The game server's listeners on the run's ports. Passed to the tests as well, for tests that start their own
    # server on the same ports.
    return [
        "--WebSockets:ListenPorts:0=%d" % ports["WS"],
        "--System:ClientPorts:0=%d" % ports["CLIENT_TCP"],
        "--CdnEmulator:ListenPort=%d" % ports["CDN"],
        "--AdminApi:ListenPort=%d" % ports["ADMIN"],
        "--PublicWebApi:ListenPort=%d" % ports["PUBWEB"],
        "--Clustering:RemotingPort=%d" % ports["REMOTING"],
        "--Environment:MetricPort=%d" % ports["METRIC"],
        "--DirectTransport:PublicUdpPort=%d" % ports["DIRECT_UDP"],
        # Off by default outside the cloud. Enabled for /isReady, which reports that the cluster and the
        # application have started, not only that a socket is listening.
        "--Environment:EnableSystemHttpServer=true",
        "--Environment:SystemHttpPort=%d" % ports["SYSHTTP"],
    ]


def server_system_url(ports):
    return "http://127.0.0.1:%d" % ports["SYSHTTP"]


def launch_server(sample_dir, config, logdir, ports, db_dir, extra_args):
    # --no-launch-profile so that a launchSettings.json profile cannot override the ports.
    argv = ["dotnet", "run", "--no-build", "--no-launch-profile", "--"] + server_port_args(ports)
    argv += ["--Database:SqliteDirectory=%s" % db_dir]
    argv += list(extra_args)
    # Run from the server project's directory, as `dotnet run` does by default, so relative paths in the server's
    # options (Config/*.yaml) resolve the same way as in a hand-started server.
    return launch_process("server", argv, os.path.join(sample_dir, config.server_project), logdir,
                          extra_env=config.server_env)


def launch_webclient(sample_dir, config, logdir, ports, wwwroot=None):
    if wwwroot is not None:
        # A published client is served by this script's --serve-static mode instead of the dev server. It sends the .wasm MIME
        # type, serves the .br and .gz files the publish wrote, and serves index.html for client-side routes.
        argv = [sys.executable, os.path.abspath(__file__), "--serve-static",
                wwwroot, str(ports["WEB"]), "--bind", "0.0.0.0"]
        return launch_process("webclient", argv, sample_dir, logdir)

    # --no-launch-profile so that ASPNETCORE_URLS is used instead of the port in launchSettings.json.
    argv = ["dotnet", "run", "--no-build", "--no-launch-profile", "--project", config.client_project,
            "-f", config.client_framework]
    extra_env = {
        "ASPNETCORE_URLS": "http://0.0.0.0:%d" % ports["WEB"],
        "ASPNETCORE_ENVIRONMENT": "Development",
    }
    return launch_process("webclient", argv, sample_dir, logdir, extra_env=extra_env)


def probe_http(url):
    try:
        with urllib.request.urlopen(url, timeout=5) as response:
            return 200 <= response.status < 300
    except (urllib.error.URLError, OSError, ValueError):
        return False


def wait_for(name, proc, probe, logdir, timeout_seconds=600):
    # Polls until the probe succeeds, the process exits, or the timeout passes.
    deadline = time.monotonic() + timeout_seconds
    log_name = name.replace(" ", "")
    while time.monotonic() < deadline:
        if probe():
            log("%s is up" % name)
            return True
        if proc.poll() is not None:
            sys.stderr.write("run_e2e.py: the %s process exited before it was ready — see %s\n"
                             % (name, os.path.join(logdir, log_name + ".log")))
            return False
        time.sleep(0.1)
    sys.stderr.write("run_e2e.py: timed out waiting for %s — see %s\n"
                     % (name, os.path.join(logdir, log_name + ".log")))
    return False


def wait_for_server(server, ports, logdir):
    return wait_for("server", server, lambda: probe_http(server_system_url(ports) + "/isReady"), logdir)


def bring_up_stack(sample_dir, config, logdir, server_args, wwwroot=None):
    ports = allocate_ports()
    server = launch_server(sample_dir, config, logdir, ports, fresh_database_dir(logdir), server_args)
    web = launch_webclient(sample_dir, config, logdir, ports, wwwroot)
    ready = wait_for_server(server, ports, logdir)
    if ready:
        ready = wait_for("web client", web, lambda: probe_http(client_url(ports) + "/"), logdir)
    if not ready:
        # Pass proc= so that a process that has already exited is reaped at once. Otherwise its zombie keeps the
        # process group alive and kill_tree waits the full grace period.
        kill_tree(server.pid, proc=server)
        kill_tree(web.pid, proc=web)
        return None
    return (server, web), ports


def bring_up_stack_with_retry(sample_dir, config, logdir, server_args, wwwroot=None, attempts=3):
    # Another process can take a port between the probe and the server's bind. A failed bind shows up as a
    # process that exits during bring-up, so the whole stack is retried with new ports.
    for attempt in range(attempts):
        result = bring_up_stack(sample_dir, config, logdir, server_args, wwwroot)
        if result is not None:
            return result
        stop_tracked_processes()
        if attempt < attempts - 1:
            log("bring-up failed (attempt %d/%d); retrying with fresh ports." % (attempt + 1, attempts))
    return None


# -----------------------------------------------------------------------------------------------
# Worker count
# -----------------------------------------------------------------------------------------------

def served_stack_count():
    # Counts stacks left running by --serve, for any sample. Their harness processes have exited and released
    # their admission slots, so they are found through their state directories instead.
    #
    # A stack counts only if one of its recorded PIDs is alive with a matching start time, as in stop_stack.
    # This keeps the state directory of a crashed stack from lowering the worker count of later runs.
    parent = tempfile.gettempdir()
    try:
        entries = os.listdir(parent)
    except OSError:
        return 0

    served = 0
    for entry in entries:
        if not entry.startswith("metaplay-e2e-"):
            continue
        state_path = os.path.join(parent, entry)
        pids_file = os.path.join(state_path, "pids")
        if not os.path.isfile(pids_file):
            continue
        try:
            with open(pids_file) as f:
                pids = [int(token) for token in f.read().split()]
            identities = {}
            identities_file = os.path.join(state_path, "idents")
            if os.path.isfile(identities_file):
                with open(identities_file) as f:
                    identities = json.load(f)
        except (OSError, ValueError):
            continue

        for pid in pids:
            live_identity = read_process_identity(pid)
            if live_identity is not None and identities.get(str(pid)) == live_identity:
                served += 1
                break

    return served


def active_run_count():
    # Counts the runs on this host: runs holding an admission slot, including this one, plus stacks left running
    # by --serve, which hold no slot. A slot port that cannot be bound is held; one that can is closed again.
    #
    # The count is taken once. A run admitted later is not included, which can make this run use too many
    # workers and run slower, but not fail.
    served = served_stack_count()

    max_runs = _int_env("E2E_MAX_RUNS", 2)
    base = _int_env("E2E_SLOT_PORT_BASE", 30500)
    if max_runs <= 0 or base < 1 or base + max_runs - 1 > 65535:
        # The slot range is disabled or invalid, so the number of other runs is unknown. Assume the default
        # limit of 2 runs instead of 1, because overestimating the CPU share makes cold WASM starts fail with
        # "An unhandled error has occurred".
        return max(2, 2 + served)

    held = 0
    for port in range(base, base + max_runs):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            if IS_WINDOWS:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
            sock.bind(("127.0.0.1", port))
        except OSError:
            held += 1
        finally:
            sock.close()

    return max(1, held + served)


def default_test_workers():
    # The number of tests that may run a browser at once. A cold WASM start that gets too little CPU fails with
    # "An unhandled error has occurred" instead of running slowly, so the count is derived from this host.
    #
    # Each run gets half the cores divided by the number of active runs. The other half is left for the game
    # server, the web client and the browsers' own processes. More than 12 workers stopped helping on the hosts
    # this was measured on, while the risk of starved WASM starts keeps growing.
    cores = os.cpu_count() or 4
    return str(max(2, min(12, cores // (2 * active_run_count()))))


# -----------------------------------------------------------------------------------------------
# The test phases
# -----------------------------------------------------------------------------------------------

# A phase may write a short report to this file in the output directory. The harness prints it after the phase,
# because `dotnet test` does not show a passing run's console output at its usual verbosity.
SUMMARY_FILE = "summary.txt"


def test_environment(ports, server_args, logdir, wwwroot):
    env = child_environment()
    env["E2E_OUTPUT_DIR"] = logdir
    # The extra arguments the server was started with (the sample's `args` and, unless --plain-server, its
    # `testArgs`), one per line. A test that has to wait out a shortened window reads its length from here.
    env["E2E_SERVER_ARGS"] = "\n".join(server_args)
    env["E2E_CLIENT_URL"] = client_url(ports)
    env["E2E_SERVER_PUBLIC_URL"] = server_public_url(ports)
    env["E2E_SERVER_ADMIN_URL"] = server_admin_url(ports)
    env["E2E_SERVER_SYSTEM_URL"] = server_system_url(ports)
    env["E2E_WS_PORT"] = str(ports["WS"])
    env["E2E_CDN_PORT"] = str(ports["CDN"])
    # The listener arguments of the shared server, one per line, for a test that starts its own server on the same
    # ports while the shared one is down (a phase with server = false).
    env["E2E_SERVER_PORT_ARGS"] = "\n".join(server_port_args(ports))
    # Set only when the web client is served from a publish (--trimmed), so a test can check what it is served.
    if wwwroot is not None:
        env["E2E_CLIENT_WWWROOT"] = wwwroot
    return env


def take_filter(args):
    # The value of the first --filter in args, or None, and args without it.
    for index, arg in enumerate(args):
        if arg == "--filter" and index + 1 < len(args):
            return args[index + 1], args[:index] + args[index + 2:]
        if arg.startswith("--filter="):
            return arg[len("--filter="):], args[:index] + args[index + 1:]
    return None, list(args)


def phase_argv(command, test_args):
    # dotnet test takes one --filter, so a --filter in the test args is joined with the phase's own into one
    # expression. It narrows the phase rather than replacing what the phase is set up to run.
    phase_filter, argv = take_filter(command)
    test_filter, test_args = take_filter(test_args)
    if phase_filter and test_filter:
        argv += ["--filter", "(%s)&(%s)" % (phase_filter, test_filter)]
    elif phase_filter or test_filter:
        argv += ["--filter", phase_filter or test_filter]
    return argv + test_args


def copy_output(stream, log_file):
    # Keeps draining the pipe even when the console goes away (e.g. `run_e2e.py | head`), or the test process would
    # block on a full pipe.
    show = True
    for line in iter(stream.readline, b""):
        if show:
            try:
                sys.stdout.buffer.write(line)
                sys.stdout.buffer.flush()
            except (OSError, ValueError):
                show = False
        try:
            log_file.write(line)
        except (OSError, ValueError):
            pass


def run_phase(sample_dir, phase, env, logdir, test_args):
    argv = phase_argv(phase.command, test_args)
    if phase.nunit_workers and "--" not in test_args:
        argv += ["--", "NUnit.NumberOfTestWorkers=%s" % (os.environ.get("E2E_TEST_WORKERS") or default_test_workers())]
    summary_path = os.path.join(logdir, SUMMARY_FILE)
    try:
        os.remove(summary_path)
    except OSError:
        pass
    log("%s: %s" % (phase.name, " ".join(argv)))
    # Tracked like the stack's processes, and in a session of its own, so an interrupted run stops the test process
    # tree too (dotnet test, its testhost, the browsers and any server a test started) instead of orphaning it.
    popen_kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if IS_WINDOWS else {"start_new_session": True}
    proc = subprocess.Popen(argv, cwd=sample_dir, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            **popen_kwargs)
    _tracked_procs.append(proc)
    # The test output is shown as it arrives and kept next to the stack's logs, as <phase>.log.
    log_file = open(os.path.join(logdir, phase.name + ".log"), "wb")
    copier = threading.Thread(target=copy_output, args=(proc.stdout, log_file), daemon=True)
    copier.start()
    try:
        return proc.wait()
    finally:
        if proc.poll() is None:
            kill_tree(proc.pid, proc=proc)
        if proc in _tracked_procs:
            _tracked_procs.remove(proc)
        # A process a test started can hold the pipe open after the test process has exited.
        copier.join(timeout=5)
        if not copier.is_alive():
            log_file.close()
        try:
            with open(summary_path, "r") as handle:
                for line in handle:
                    log(line.rstrip())
            os.remove(summary_path)
        except OSError:
            pass


# -----------------------------------------------------------------------------------------------
# main
# -----------------------------------------------------------------------------------------------

class PhaseTimer:
    """Times each step of a run (admission, build, publish, stack up, tests, teardown) and prints them on one line
    at the end, so that the time spent outside the tests is visible."""

    def __init__(self):
        self._order = []
        self._seconds = {}
        self._started = None
        self._name = None

    def start(self, name):
        self.stop()
        self._name = name
        self._started = time.monotonic()

    def stop(self):
        if self._name is None:
            return
        elapsed = time.monotonic() - self._started
        if self._name not in self._seconds:
            self._order.append(self._name)
            self._seconds[self._name] = 0.0
        self._seconds[self._name] += elapsed
        self._name = None

    def report(self):
        self.stop()
        if not self._order:
            return
        total = sum(self._seconds.values())
        parts = ", ".join("%s %.1fs" % (name, self._seconds[name]) for name in self._order)
        log("%s — %.1fs in all" % (parts, total))


TIMER = PhaseTimer()


def parse_args(argv):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--sample", default=SCRIPT_DIR)
    # CI sets E2E_OUTPUT_DIR to collect the output of every task in one place.
    parser.add_argument("--output-dir", default=os.environ.get("E2E_OUTPUT_DIR") or None)
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--stop", action="store_true")
    parser.add_argument("--no-test", action="store_true")
    parser.add_argument("--plain-server", action="store_true")
    parser.add_argument("--phase", action="append", default=[])
    parser.add_argument("--trimmed", action="store_true")
    parser.add_argument("-h", "--help", action="store_true")
    return parser.parse_known_args(argv)


def main():
    # Print the step times on every exit path, including a failed publish, a failed bring-up and the exits in
    # build_stack. The finally block also runs for SystemExit.
    try:
        return _run()
    finally:
        TIMER.report()


def _run():
    global _leave_stack_running

    options, test_args = parse_args(sys.argv[1:])
    if options.help:
        print(__doc__)
        return 0

    ensure_dotnet_on_path()
    sample_dir = os.path.abspath(options.sample)
    config = Config(sample_dir)
    config.exit_unless_test_args_allowed()

    if options.stop:
        stop_stack(sample_dir)
        return 0

    leave_running = options.serve or options.no_test

    # Take a slot before the build, so that a waiting run does no heavy work.
    TIMER.start("admission")
    acquire_run_slot()

    TIMER.start("build")
    build_stack(sample_dir, config)
    if not options.no_test:
        TIMER.start("browsers")
        install_playwright_browsers(sample_dir, config)

    if leave_running:
        # Stop the earlier --serve stack before the publish. The publish directory is the same for every run from
        # this checkout, so the earlier stack serves the files that the publish overwrites.
        stop_stack(sample_dir, quiet=True)
    if options.output_dir:
        logdir = os.path.abspath(options.output_dir)
    elif leave_running:
        logdir = state_dir(sample_dir)
    else:
        logdir = tempfile.mkdtemp(prefix="metaplay-e2e-run-")
    os.makedirs(logdir, exist_ok=True)

    # --trimmed needs a publish because only a publish trims. The build above is still needed for the test
    # assemblies and the game server.
    wwwroot = None
    if options.trimmed:
        TIMER.start("publish")
        wwwroot = publish_webclient(sample_dir, config)
        if wwwroot is None:
            return 1
    log("logs in %s" % logdir)

    server_args = config.server_args + ([] if options.plain_server else config.server_test_args)
    phases = [] if options.no_test else config.phases
    if options.phase:
        wanted = [name.lower() for name in options.phase]
        phases = [phase for phase in phases if any(name in phase.name.lower() for name in wanted)]
        if not phases:
            fail("no phase in %s matches %s" % (CONFIG_FILE, ", ".join(options.phase)))
    if leave_running:
        # --serve and --no-test leave the stack running, with its server up, after the phases.
        phases = phases + [None]

    exit_code = 0
    stack = None
    server_up = False
    for phase in phases:
        wants_server = phase is None or phase.server
        if stack is None:
            TIMER.start("stack up")
            stack = bring_up_stack_with_retry(sample_dir, config, logdir, server_args, wwwroot)
            if stack is None:
                sys.stderr.write("run_e2e.py: could not bring the stack up — see %s/*.log\n" % logdir)
                return 1
            server_up = True
            ports = stack[1]
            log("web=%d ws=%d cdn=%d publicwebapi=%d (admin=%d clienttcp=%d remoting=%d metric=%d "
                "systemhttp=%d directudp=%d)"
                % (ports["WEB"], ports["WS"], ports["CDN"], ports["PUBWEB"], ports["ADMIN"],
                   ports["CLIENT_TCP"], ports["REMOTING"], ports["METRIC"], ports["SYSHTTP"], ports["DIRECT_UDP"]))
            if wwwroot is not None:
                log("serving the trimmed publish from %s" % wwwroot)

        (server, web), ports = stack
        if not wants_server and server_up:
            # The phase's tests start their own server on these ports, so the shared one has to release them.
            TIMER.start("server down")
            log("stopping the shared server for %s" % phase.name)
            kill_tree(server.pid, proc=server)
            _tracked_procs.remove(server)
            server_up = False
        elif wants_server and not server_up:
            TIMER.start("server up")
            log("restarting the shared server")
            server = launch_server(sample_dir, config, logdir, ports, os.path.join(logdir, "db"), server_args)
            if not wait_for_server(server, ports, logdir):
                return 1
            stack = ((server, web), ports)
            server_up = True
        if phase is None:
            continue

        TIMER.start("tests")
        env = test_environment(ports, server_args, logdir, wwwroot)
        phase_exit = run_phase(sample_dir, phase, env, logdir, test_args)
        if phase_exit != 0:
            log("%s failed with exit code %d" % (phase.name, phase_exit))
            exit_code = exit_code or phase_exit

    TIMER.start("teardown")
    if leave_running and stack is not None:
        (server, web), ports = stack
        url = browsable_url(ports)
        write_serve_state(state_dir(sample_dir), [server.pid, web.pid], url, ports)
        _leave_stack_running = True
        print("")
        log("stack live:")
        print("  %s" % url)
        # The server serves the LiveOps Dashboard on its Admin API port.
        print("  dashboard: %s" % server_admin_url(ports))
        print("  logs: %s   stop: run_e2e.py --stop" % logdir)
    else:
        stop_tracked_processes()
        # Delete a temporary log directory, which also holds the SQLite database, only if the tests passed. A
        # failed or interrupted run keeps it. Delete it only after stop_tracked_processes, because the server
        # writes to the database until it stops.
        if options.output_dir:
            pass
        elif exit_code == 0:
            shutil.rmtree(logdir, ignore_errors=True)
        else:
            log("logs kept at %s" % logdir)

    return exit_code


if __name__ == "__main__":
    if sys.argv[1:2] == ["--serve-static"]:
        sys.exit(serve_static(sys.argv[2:]))
    # atexit runs handlers in reverse order of registration, so release_run_slot is registered first to run
    # after stop_tracked_processes has stopped the stack. Otherwise another run could start while this one is still
    # stopping its server. The slot is not released in stop_tracked_processes, because the bring-up retry calls
    # stop_tracked_processes between attempts.
    atexit.register(release_run_slot)
    atexit.register(stop_tracked_processes)
    if not IS_WINDOWS:
        signal.signal(signal.SIGINT, _signal_handler)
        signal.signal(signal.SIGTERM, _signal_handler)
    sys.exit(main())
