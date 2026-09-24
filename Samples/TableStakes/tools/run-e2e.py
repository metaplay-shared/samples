#!/usr/bin/env python3
"""Run the Playwright suite against a temporary game server and web client that use free ports, so that several
worktrees can run the E2E suite at the same time.

The default ports (the web client's port and every port the game server binds) belong to the main checkout, so
this script refuses to run there. A stack started from a worktree on the default ports would either fail to bind
or take the ports over, and then the main checkout's browser tabs would connect to the worktree's branch. Each
listener gets a free port instead, and the port numbers are passed to the test process in environment variables
(see WebClient.Tests/PlaywrightPageTest.cs).

Uses only the Python standard library. `tools/run-e2e.sh` runs this script with python3.

Usage:
  tools/run-e2e.sh [dotnet test args]    Bring a stack up, run the suite on it, tear it down.
  tools/run-e2e.sh --serve [test args]   Same, but leave the stack running and print its URL. The stack
                                         keeps running after this shell exits.
  tools/run-e2e.sh --stop                Stop the stack this worktree left running with --serve.
  tools/run-e2e.sh --no-test             Bring the stack up without running the suite (implies --serve).
  tools/run-e2e.sh --trimmed             Publish the web client (Release, IL-trimmed) and serve that instead
                                         of the dev server, so that the suite tests the client players get.
                                         Only this mode tests trimming, because the dev server serves an
                                         untrimmed Debug build. The publish makes startup slower.
  tools/run-e2e.sh --shipped-pacing      Start the server with its shipped match timings instead of the
                                         shortened test timings. Use it to play on the stack by hand.

  e.g. tools/run-e2e.sh --filter "FullyQualifiedName~ShellPageTests"

By default the server starts with shortened timings: the bot think delay, the trick-resolve pause and the
matchmaking fill wait (see MATCH_PACING_ARGS and SANCTIONED_PACING_KEYS). Timers that tests measure against keep
their shipped values. The fill wait in use is passed to the test process as TABLESTAKES_E2E_FILL_WAIT_MS.

Environment:
  E2E_TEST_WORKERS       NUnit worker count. If unset, half the host's cores divided by the number of active
                         runs (runs holding an admission slot plus stacks left running by --serve), at most 12.
  E2E_MAX_RUNS           Maximum number of E2E runs active on this host at once. Further runs wait for a free
                         slot (default 2, 0 disables the limit).
  E2E_ADMISSION_TIMEOUT  Seconds a waiting run waits for a slot before it starts anyway (default 1800,
                         0 waits forever).
  E2E_SLOT_PORT_BASE     First port of the loopback UDP port range used as admission slots (default 30500).
"""
import argparse
import atexit
import hashlib
import json
import os
import platform
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

IS_WINDOWS = platform.system() == "Windows"

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


# -----------------------------------------------------------------------------------------------
# Repo, worktree guard and per-worktree state
# -----------------------------------------------------------------------------------------------

def ensure_dotnet_on_path():
    # dotnet is commonly installed under ~/.dotnet without being on PATH.
    if shutil.which("dotnet") is None:
        home_dotnet = os.path.join(os.path.expanduser("~"), ".dotnet")
        if os.path.isfile(os.path.join(home_dotnet, "dotnet") + (".exe" if IS_WINDOWS else "")):
            os.environ["PATH"] = home_dotnet + os.pathsep + os.environ.get("PATH", "")


def project_root():
    # Returns the project root, the directory above tools/. This is not git's top level, because the project is
    # at Samples/TableStakes inside the SDK repository.
    script_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.dirname(script_dir)


def refuse_main_checkout(project_dir):
    # In the main checkout, the git dir and the git common dir are the same directory. In a worktree they differ.
    # This test does not depend on where the checkouts are on disk.
    # --git-common-dir prints a relative path in the main checkout and an absolute path in a worktree, so both
    # paths are resolved against the project directory, not this process's working directory.
    git_dir = os.path.realpath(os.path.join(project_dir, subprocess.check_output(
        ["git", "-C", project_dir, "rev-parse", "--absolute-git-dir"],
        text=True, encoding="utf-8", errors="replace").strip()))
    common_dir = os.path.realpath(os.path.join(project_dir, subprocess.check_output(
        ["git", "-C", project_dir, "rev-parse", "--git-common-dir"],
        text=True, encoding="utf-8", errors="replace").strip()))
    if git_dir == common_dir:
        sys.stderr.write(
            "run-e2e.py: refusing to run in the main checkout.\n"
            "  The default ports are the main checkout's, and its dev stack is what answers on them. Run the\n"
            "  harness from a worktree instead: from the repository root, git worktree add .claude/worktrees/<name>\n"
            "  -b <branch>, then run tools/run-e2e.sh from Samples/TableStakes inside it.\n")
        sys.exit(1)


def state_dir(project_dir):
    # The directory name is derived from the worktree path, so each worktree's --serve stack has its own state.
    digest = hashlib.sha1(project_dir.encode("utf-8")).hexdigest()[:12]
    return os.path.join(tempfile.gettempdir(), "table-stakes-e2e-" + digest)


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
    # The Admin API, which the LiveOps Dashboard reads from. Tests that check what the Dashboard shows, such as
    # the player's event log, read it from here.
    return "http://localhost:%d" % ports["ADMIN"]


def browsable_url(ports):
    # The URL to open in a browser. The client reads the server ports from the query string. Without them it
    # uses the localhost environment's default ports and connects to the main checkout's server.
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
        print("run-e2e.py: E2E_SLOT_PORT_BASE=%d cannot host %d slots inside 1-65535; throttle disabled."
              % (base, max_runs))
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
                print("run-e2e.py: a slot came free after %ds — starting." % int(time.monotonic() - start))
            return
        now = time.monotonic()
        if last_notice_at is None or (now - last_notice_at) >= SLOT_NOTICE_SECONDS:
            print("run-e2e.py: %d E2E runs are already active on this host (the cap); waiting for a slot "
                  "(%ds so far). Change it with E2E_MAX_RUNS, or set 0 to disable the throttle."
                  % (max_runs, int(now - start)))
            last_notice_at = now
        if deadline is not None and now >= deadline:
            print("run-e2e.py: still no slot after %ds — going ahead anyway; the throttle is never a hard "
                  "block." % timeout_seconds)
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

def launch_process(name, argv, cwd, logdir, extra_env=None):
    log_path = os.path.join(logdir, name + ".log")
    log_file = open(log_path, "wb")
    env = dict(os.environ)
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
    # Safe to call more than once. _run calls it at teardown, the atexit handler calls it again, and the bring-up
    # retry calls it between attempts. Processes that kill_tree confirms are removed from the list. The others
    # stay, so that a later call tries them again.
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
    try:
        with open("/proc/%d/stat" % pid, "r") as f:
            data = f.read()
        # Field 2 (comm) is in parentheses and can contain spaces, so split the fields after the last ')'.
        fields = data[data.rfind(")") + 1:].split()
        return {"starttime": fields[19]}
    except (OSError, IndexError):
        return None


def stop_stack(project_dir, quiet=False):
    state_path = state_dir(project_dir)
    pids_file = os.path.join(state_path, "pids")
    if not os.path.isfile(pids_file):
        if not quiet:
            print("run-e2e.py: no served stack for this worktree.")
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
            print("run-e2e.py: pid %d no longer matches what was recorded (recycled?); leaving it alone." % pid)
            continue
        kill_tree(pid)

    url = "unknown"
    url_file = os.path.join(state_path, "url")
    if os.path.isfile(url_file):
        with open(url_file) as f:
            url = f.read().strip()
    print("run-e2e.py: stopped the served stack (%s)" % url)
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
# One `dotnet build` of tools/e2e-stack.slnx builds the game server, the web client and the test assembly, so
# their shared project graph is evaluated and restored once. Everything is built before anything starts, and
# the processes start with --no-build, because two concurrent `dotnet run` builds would write shared projects
# (such as Metaplay.Core) into the same obj directory at once. The web client's dev server also resolves the
# app's fingerprinted assets once at startup, so the client must be built first.
# -----------------------------------------------------------------------------------------------

STACK_SOLUTION = os.path.join("tools", "e2e-stack.slnx")


# Build output that appears only when a --no-restore build failed for lack of a restore. NETSDK1004 reports a
# missing project.assets.json. The text markers are what the SDK prints when a package is missing from the
# assets file. build_stack retries with a restore only when one of these appears, so a real compile error fails
# once instead of being built twice. A missing restore can also show up as CS0246, but so can a real error, so
# CS0246 is not a marker.
RESTORE_NEEDED_MARKERS = ("NETSDK1004", "run a NuGet package restore", "run NuGet package restore")

# Build output (warning CS8784) when the Metaplay source generator cannot load Metaplay.Attributes.dll. The build
# still succeeds, but the assemblies have no generated integration or serialization code, and every test then
# fails in one-time setup with "Integration root assembly 'SharedCode.Client' doesn't contain source generated
# Metaplay integration type info". The project's Directory.Build.targets builds Metaplay.Attributes first to
# prevent this, so this marker catches the cases it misses. The broken assemblies are up to date, so a rebuild
# does not replace them, and build_stack deletes their obj/ and bin/ first. It does not shut down the compiler
# server, because runs from other worktrees share it.
GENERATOR_FAILED_MARKER = "Generator 'MetaplayGenerator' failed to initialize"

# The stack's projects that run the generator as an analyzer project, and whose obj/ and bin/ are deleted when it
# fails to load. MetaplaySDK projects are not listed: they build the generator but do not run it.
GENERATED_PROJECTS = ("SharedCode", "WebClientBase", "WebClient")


def run_build(project_dir, argv):
    # Prints the build output as it arrives and also returns it, because the callers check it for markers.
    # Printing as it arrives shows progress during a long cold build.
    proc = subprocess.Popen(argv, cwd=project_dir, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace")
    lines = []
    for line in proc.stdout:
        sys.stdout.write(line)
        sys.stdout.flush()
        lines.append(line)
    proc.stdout.close()
    return proc.wait(), "".join(lines)


def build_stack(project_dir):
    print("run-e2e.py: building %s" % STACK_SOLUTION)
    started = time.monotonic()
    # --no-restore makes a build of an unchanged tree faster. After a csproj or package version change the
    # build fails for lack of a restore, so a failure with a RESTORE_NEEDED_MARKERS match is retried once with
    # a restore.
    exit_code, output = run_build(project_dir, ["dotnet", "build", STACK_SOLUTION, "--no-restore"])
    if exit_code != 0:
        if not any(marker in output for marker in RESTORE_NEEDED_MARKERS):
            sys.stderr.write("run-e2e.py: build failed for %s\n" % STACK_SOLUTION)
            sys.exit(exit_code)
        print("run-e2e.py: the build needs a restore it was not allowed to run; retrying with one")
        exit_code, output = run_build(project_dir, ["dotnet", "build", STACK_SOLUTION])
        if exit_code != 0:
            sys.stderr.write("run-e2e.py: build failed for %s\n" % STACK_SOLUTION)
            sys.exit(exit_code)

    # A successful build can still lack the generated code (see GENERATOR_FAILED_MARKER). Delete the affected
    # obj/ and bin/ directories and build once more. If the generator fails again, stop instead of running a
    # suite that would fail in one-time setup.
    if GENERATOR_FAILED_MARKER in output:
        print("run-e2e.py: the Metaplay source generator did not load; clearing %s and building again"
              % ", ".join(GENERATED_PROJECTS))
        for project in GENERATED_PROJECTS:
            for tree in ("obj", "bin"):
                shutil.rmtree(os.path.join(project_dir, project, tree), ignore_errors=True)
        exit_code, output = run_build(project_dir, ["dotnet", "build", STACK_SOLUTION])
        if exit_code != 0 or GENERATOR_FAILED_MARKER in output:
            sys.stderr.write(
                "run-e2e.py: the Metaplay source generator failed to load twice. The assemblies it writes carry "
                "no generated integration or serialization code, so every test would fail in one-time setup. "
                "Build MetaplaySDK/Backend/Attributes first, then run again.\n")
            sys.exit(exit_code or 1)

    print("run-e2e.py: built in %.1fs" % (time.monotonic() - started))


# -----------------------------------------------------------------------------------------------
# Stack bring-up
# -----------------------------------------------------------------------------------------------

def fresh_database_dir(logdir):
    # The server's SQLite files default to its bin directory, which every run from one worktree shares. A run
    # would then start with the state an earlier run left, such as players in the matchmaking queue. Give each
    # stack an empty database directory next to its logs.
    db_dir = os.path.join(logdir, "db")
    shutil.rmtree(db_dir, ignore_errors=True)
    os.makedirs(db_dir, exist_ok=True)
    return db_dir


# Server timings shortened for a test stack. Live-server tests wait these out, and no test depends on their
# length. Timers that tests measure against, such as the move deadline, the grace windows and the join window,
# keep their shipped values, and SANCTIONED_PACING_KEYS enforces this.
#
# - Bot think delay: imitates a human opponent. MatchPacingTests covers the pacing rules.
# - Trick-resolve pause: MatchClocks.GetBeatEndsAt ends the client's trick animation when the pause ends. Tests
#   of the animation force the pause at the offline table with resolvePauseMs.
# - Matchmaking fill wait: live fixtures rely on two searches within one wait sharing a table, which holds for
#   any length. The cancelled-search fixture must wait longer than it, so run_dotnet_test passes the value on.

# The fill wait for a test stack, and the shipped default. SHIPPED_FILL_WAIT_MS must match the default in
# Backend/Server/Matchmaking/MatchmakingOptions.cs. It is repeated here because --shipped-pacing passes no
# fill wait argument, so fill_wait_ms has no argument to read it from.
TEST_FILL_WAIT_MS = 1500
SHIPPED_FILL_WAIT_MS = 5000


def as_timespan(milliseconds):
    # Formats a duration as hh:mm:ss.fff, the form the server's runtime options parse.
    seconds, remainder_ms = divmod(int(milliseconds), 1000)
    minutes, seconds = divmod(seconds, 60)
    hours, minutes = divmod(minutes, 60)
    return "%02d:%02d:%02d.%03d" % (hours, minutes, seconds, remainder_ms)


MATCH_PACING_ARGS = [
    "--Match:BotThinkDelayMin=00:00:00.150",
    "--Match:BotThinkDelayMax=00:00:00.300",
    "--Match:BotThinkDelayOccasionalMax=00:00:00.300",
    "--Match:ResolvePause=00:00:00.200",
    "--Matchmaking:FillWait=%s" % as_timespan(TEST_FILL_WAIT_MS),
]

# The options MATCH_PACING_ARGS may set, checked at startup by exit_unless_pacing_args_sanctioned. Do not add a
# timer that a fixture measures against: the fixture would keep passing while testing a different case. Either
# leave such a timer at its shipped value, or pass its value to the test process the way the fill wait is
# passed and have the fixture use that value.
SANCTIONED_PACING_KEYS = {
    "Match:BotThinkDelayMin",
    "Match:BotThinkDelayMax",
    "Match:BotThinkDelayOccasionalMax",
    "Match:ResolvePause",
    "Matchmaking:FillWait",
}


def exit_unless_pacing_args_sanctioned():
    for arg in MATCH_PACING_ARGS:
        key = arg.lstrip("-").split("=", 1)[0]
        if key not in SANCTIONED_PACING_KEYS:
            sys.stderr.write(
                "run-e2e.py: MATCH_PACING_ARGS sets %s, which is not in SANCTIONED_PACING_KEYS.\n"
                "  Before adding it there: if any fixture measures itself against that window rather than\n"
                "  waiting it out, shortening it here makes that fixture pass while testing something else.\n"
                "  Export the window to the test process instead (see TABLESTAKES_E2E_FILL_WAIT_MS in\n"
                "  run_dotnet_test) and size the fixture off the exported value — then add %s to the set.\n"
                % (key, key))
            sys.exit(2)


def fill_wait_ms(pacing_args):
    # Reads the fill wait from the server's arguments, so that the value passed to the suite matches the
    # server's. Under --shipped-pacing there is no such argument, and the server uses its default.
    for arg in pacing_args:
        if arg.startswith("--Matchmaking:FillWait="):
            hours, minutes, seconds = arg.split("=", 1)[1].split(":")
            return round((int(hours) * 3600 + int(minutes) * 60 + float(seconds)) * 1000)
    return SHIPPED_FILL_WAIT_MS


def launch_server(project_dir, logdir, ports, db_dir, pacing_args):
    argv = [
        # --no-launch-profile so that a launchSettings.json profile cannot override the ports set below.
        "dotnet", "run", "--no-build", "--no-launch-profile", "--project", "Server.csproj", "--",
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
        "--Database:SqliteDirectory=%s" % db_dir,
        # Enables the unauthenticated test/ routes that the suite uses to force timers and create weekly events.
        # They are off by default (Backend/Server/TestRoutes/TestRoutesOptions.cs).
        "--TestRoutes:Enabled=true",
    ]
    argv += list(pacing_args)
    return launch_process("server", argv, os.path.join(project_dir, "Backend", "Server"), logdir)


def served_stack_count():
    # Counts stacks left running by --serve. Their harness processes have exited and released their admission
    # slots, so they are found through their state directories instead, one per worktree.
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
        if not entry.startswith("table-stakes-e2e-"):
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
    # The number of tests that may run a browser at once. WebClient.Tests has its own default for a plain
    # `dotnet test`, which cannot know the host. A cold WASM start that gets too little CPU fails with
    # "An unhandled error has occurred" instead of running slowly, so the count is derived from this host.
    #
    # Each run gets half the cores divided by the number of active runs. The other half is left for the game
    # server, the web client and the browsers' own processes.
    #
    # The upper limit exists because more workers stop helping: the non-parallel weekly-event fixture runs alone
    # at the end, and no worker count shortens it, while the risk of starved WASM starts keeps growing. Measure
    # on the target host before changing the limit.
    cores = os.cpu_count() or 4
    return str(max(2, min(12, cores // (2 * active_run_count()))))


# Where `dotnet publish -c Release` writes the client. Serve this directory, not bin/Release/<tfm>/wwwroot, which
# holds only _framework, without index.html or the app's static assets.
PUBLISHED_WWWROOT = os.path.join("WebClient", "bin", "Release", "net10.0-browser", "publish", "wwwroot")


def publish_webclient(project_dir):
    """Publish the client for the trimmed pass. Returns the wwwroot to serve, or None if the publish failed."""
    argv = ["dotnet", "publish", os.path.join("WebClient", "WebClient.csproj"), "-c", "Release",
            "-f", "net10.0-browser"]
    print("run-e2e.py: publishing the web client (Release, trimmed) — this is slower than a build")
    started = time.monotonic()
    exit_code, output = run_build(project_dir, argv)
    if exit_code != 0:
        sys.stderr.write("run-e2e.py: the web client publish failed\n")
        return None
    # The publish can hit the same generator failure as build_stack (see GENERATOR_FAILED_MARKER), and then
    # succeeds without the generated code.
    if GENERATOR_FAILED_MARKER in output:
        sys.stderr.write("run-e2e.py: the publish lost the Metaplay source-generator race (%s).\n"
                         "run-e2e.py: clear the obj/bin of %s and publish again.\n"
                         % (GENERATOR_FAILED_MARKER, ", ".join(GENERATED_PROJECTS)))
        return None
    print("run-e2e.py: published in %.1fs" % (time.monotonic() - started))

    wwwroot = os.path.join(project_dir, PUBLISHED_WWWROOT)
    if not os.path.isfile(os.path.join(wwwroot, "index.html")):
        sys.stderr.write("run-e2e.py: the publish left no index.html in %s\n" % wwwroot)
        return None
    return wwwroot


def launch_webclient(project_dir, logdir, ports, wwwroot=None):
    if wwwroot is not None:
        # A published client is served by tools/serve-wasm-static.py instead of the dev server. It sends the
        # .wasm MIME type, serves the .br and .gz files the publish wrote, and serves index.html for
        # client-side routes.
        argv = [sys.executable, os.path.join("tools", "serve-wasm-static.py"),
                wwwroot, str(ports["WEB"]), "--bind", "0.0.0.0"]
        return launch_process("webclient", argv, project_dir, logdir)

    # --no-launch-profile so that ASPNETCORE_URLS is used instead of the port in launchSettings.json.
    argv = ["dotnet", "run", "--no-build", "--no-launch-profile", "--project",
            os.path.join("WebClient", "WebClient.csproj"), "-f", "net10.0-browser"]
    extra_env = {
        "ASPNETCORE_URLS": "http://0.0.0.0:%d" % ports["WEB"],
        "ASPNETCORE_ENVIRONMENT": "Development",
    }
    return launch_process("webclient", argv, project_dir, logdir, extra_env=extra_env)


def probe_http(url):
    try:
        with urllib.request.urlopen(url, timeout=5) as response:
            return 200 <= response.status < 300
    except (urllib.error.URLError, OSError, ValueError):
        return False


def wait_for(name, proc, probe, logdir, timeout_seconds=600):
    # Polls until the probe succeeds, the process exits, or the timeout passes.
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if probe():
            print("run-e2e.py: %s is up" % name)
            return True
        if proc.poll() is not None:
            sys.stderr.write("run-e2e.py: the %s process exited before it was ready — see %s\n"
                             % (name, os.path.join(logdir, name + ".log")))
            return False
        time.sleep(0.1)
    sys.stderr.write("run-e2e.py: timed out waiting for %s — see %s\n"
                     % (name, os.path.join(logdir, name + ".log")))
    return False


def bring_up_stack(project_dir, logdir, pacing_args, wwwroot=None):
    ports = allocate_ports()
    server = launch_server(project_dir, logdir, ports, fresh_database_dir(logdir), pacing_args)
    web = launch_webclient(project_dir, logdir, ports, wwwroot)
    ready = wait_for("server", server, lambda: probe_http("http://127.0.0.1:%d/isReady" % ports["SYSHTTP"]), logdir)
    if ready:
        ready = wait_for("web client", web, lambda: probe_http(client_url(ports) + "/"), logdir)
    if not ready:
        # Pass proc= so that a process that has already exited is reaped at once. Otherwise its zombie keeps the
        # process group alive and kill_tree waits the full grace period.
        kill_tree(server.pid, proc=server)
        kill_tree(web.pid, proc=web)
        return None
    return (server, web), ports


def bring_up_stack_with_retry(project_dir, logdir, pacing_args, wwwroot=None, attempts=3):
    # Another process can take a port between the probe and the server's bind. A failed bind shows up as a
    # process that exits during bring-up, so the whole stack is retried with new ports.
    for attempt in range(attempts):
        result = bring_up_stack(project_dir, logdir, pacing_args, wwwroot)
        if result is not None:
            return result
        stop_tracked_processes()
        if attempt < attempts - 1:
            print("run-e2e.py: bring-up failed (attempt %d/%d); retrying with fresh ports." % (attempt + 1, attempts))
    return None


# -----------------------------------------------------------------------------------------------
# The test run
# -----------------------------------------------------------------------------------------------

def run_dotnet_test(project_dir, ports, pacing_args, test_args, wwwroot=None):
    env = dict(os.environ)
    env["TABLESTAKES_E2E_CLIENT_URL"] = client_url(ports)
    # In --trimmed mode, the suite's ServedClientBuildCheck check compares the served client with the published app
    # assembly instead of the Debug build in bin/.
    if wwwroot is not None:
        env["TABLESTAKES_E2E_CLIENT_WWWROOT"] = wwwroot
    env["TABLESTAKES_E2E_SERVER_PUBLIC_URL"] = server_public_url(ports)
    env["TABLESTAKES_E2E_SERVER_ADMIN_URL"] = server_admin_url(ports)
    env["TABLESTAKES_E2E_WS_PORT"] = str(ports["WS"])
    env["TABLESTAKES_E2E_CDN_PORT"] = str(ports["CDN"])
    # The server's matchmaking fill wait. A fixture that must wait longer than the fill wait uses this value.
    # Without it, the suite assumes the shipped default, which a server started by hand uses.
    env["TABLESTAKES_E2E_FILL_WAIT_MS"] = str(fill_wait_ms(pacing_args))

    # The file where the suite writes how long its serial gates took. It is printed below, because the test
    # process's console output is not shown for a passing run.
    gate_report_path = os.path.join(tempfile.gettempdir(), "table-stakes-gates-%d.txt" % os.getpid())
    env["TABLESTAKES_E2E_GATE_REPORT"] = gate_report_path

    # Delete the file before the run as well as after it. The name contains this process's PID, and a killed
    # run never deletes its file. A later run with the same PID whose tests write no report (for example, a
    # --filter that matches nothing) would otherwise print the old run's gate times.
    try:
        os.remove(gate_report_path)
    except OSError:
        pass
    argv = ["dotnet", "test", os.path.join("WebClient.Tests", "WebClient.Tests.csproj"), "--no-build"] + test_args
    test_worker_count = os.environ.get("E2E_TEST_WORKERS") or default_test_workers()
    if test_worker_count and "--" not in test_args:
        argv += ["--", "NUnit.NumberOfTestWorkers=%s" % test_worker_count]
    print("run-e2e.py: %s" % " ".join(argv))
    try:
        return subprocess.call(argv, cwd=project_dir, env=env)
    finally:
        # The gates run serially, so more workers do not shorten them. Print their times so they can be found.
        try:
            with open(gate_report_path, "r") as handle:
                for line in handle:
                    print("run-e2e.py: %s" % line.rstrip())
            os.remove(gate_report_path)
        except OSError:
            pass


# -----------------------------------------------------------------------------------------------
# main
# -----------------------------------------------------------------------------------------------

class PhaseTimer:
    """
    Times each phase of a run (admission, build, publish, stack up, tests, teardown) and prints them on one line
    at the end, so that the time spent outside the tests is visible.
    """

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
        print("run-e2e.py: %s — %.1fs in all" % (parts, total))


PHASES = PhaseTimer()


def parse_args(argv):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--stop", action="store_true")
    parser.add_argument("--no-test", action="store_true")
    parser.add_argument("--shipped-pacing", action="store_true")
    parser.add_argument("--trimmed", action="store_true")
    parser.add_argument("-h", "--help", action="store_true")
    options, test_args = parser.parse_known_args(argv)
    return options, test_args


def main():
    # Print the phase times on every exit path, including a failed publish, a failed bring-up and the
    # sys.exit calls in build_stack. The finally block also runs for SystemExit.
    try:
        return _run()
    finally:
        PHASES.report()


def _run():
    global _leave_stack_running

    exit_unless_pacing_args_sanctioned()

    options, test_args = parse_args(sys.argv[1:])
    if options.help:
        print(__doc__)
        return 0

    ensure_dotnet_on_path()
    project_dir = project_root()
    refuse_main_checkout(project_dir)

    if options.stop:
        stop_stack(project_dir)
        return 0

    leave_running = options.serve or options.no_test

    # Take a slot before the build, so that a waiting run does no heavy work.
    PHASES.start("admission")
    acquire_run_slot()

    PHASES.start("build")
    build_stack(project_dir)

    if leave_running:
        # Stop the earlier --serve stack before the publish. The publish directory is the same for every run from
        # this worktree, so the earlier stack serves the files that the publish overwrites.
        stop_stack(project_dir, quiet=True)
        logdir = state_dir(project_dir)
        os.makedirs(logdir, exist_ok=True)
    else:
        logdir = tempfile.mkdtemp(prefix="table-stakes-e2e-")

    # --trimmed needs a publish because only a publish trims. The build above is still needed for the test
    # assembly and the game server.
    wwwroot = None
    if options.trimmed:
        PHASES.start("publish")
        wwwroot = publish_webclient(project_dir)
        if wwwroot is None:
            return 1
    print("run-e2e.py: logs in %s" % logdir)

    pacing_args = [] if options.shipped_pacing else MATCH_PACING_ARGS
    PHASES.start("stack up")
    stack = bring_up_stack_with_retry(project_dir, logdir, pacing_args, wwwroot)
    if stack is None:
        sys.stderr.write("run-e2e.py: could not bring the stack up — see %s/*.log\n" % logdir)
        return 1

    (server, web), ports = stack
    print("run-e2e.py: web=%d ws=%d cdn=%d publicwebapi=%d (admin=%d clienttcp=%d remoting=%d metric=%d "
          "systemhttp=%d directudp=%d)"
          % (ports["WEB"], ports["WS"], ports["CDN"], ports["PUBWEB"], ports["ADMIN"],
             ports["CLIENT_TCP"], ports["REMOTING"], ports["METRIC"], ports["SYSHTTP"], ports["DIRECT_UDP"]))
    # Print the fill wait in every mode, because a fixture depends on it and it is useful when playing by hand.
    print("run-e2e.py: matchmaking fill wait %d ms" % fill_wait_ms(pacing_args))
    if wwwroot is not None:
        print("run-e2e.py: serving the trimmed publish from %s" % wwwroot)

    url = browsable_url(ports)
    if leave_running:
        write_serve_state(state_dir(project_dir), [server.pid, web.pid], url, ports)
        _leave_stack_running = True

    PHASES.start("tests")
    exit_code = 0 if options.no_test else run_dotnet_test(project_dir, ports, pacing_args, test_args, wwwroot)

    PHASES.start("teardown")
    if leave_running:
        branch = subprocess.check_output(["git", "-C", project_dir, "rev-parse", "--abbrev-ref", "HEAD"],
                                         text=True, encoding="utf-8", errors="replace").strip()
        print("")
        print("run-e2e.py: stack live (branch %s):" % branch)
        print("  %s" % url)
        # The server serves the LiveOps Dashboard on its Admin API port.
        print("  dashboard: %s" % server_admin_url(ports))
        print("  logs: %s   stop: tools/run-e2e.sh --stop" % logdir)
    else:
        stop_tracked_processes()
        # Delete the run's temporary directory, which holds the logs and the SQLite database, only if the tests
        # passed. A failed or interrupted run keeps it for its logs. Delete it only after stop_tracked_processes,
        # because the server writes to the database until it stops.
        if exit_code == 0:
            shutil.rmtree(logdir, ignore_errors=True)
        else:
            print("run-e2e.py: logs kept at %s" % logdir)

    return exit_code


if __name__ == "__main__":
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
