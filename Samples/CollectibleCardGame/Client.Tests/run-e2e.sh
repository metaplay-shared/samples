#!/bin/zsh
#
# The end-to-end suites, in the one order that works: a game server and a web client for the fixtures that
# need both, then the three suites that own a game-server process of their own and therefore cannot share the
# machine with one — MatchAbandonTests, which restarts its server, MatchStrikeTests, which paces its own
# differently, and HeistTests, which needs a third pacing again (Client.Tests/MatchAbandonTests.cs,
# Client.Tests/MatchStrikeTests.cs, Client.Tests/HeistTests.cs).
#
# MatchmakingTests runs between them, and it is the other suite that cannot share a machine: there is one
# global queue, so two taps inside one fill wait are pooled into a single match with two humans in it.
# HeistTests owns a server as well — it needs a five-second delivery retry and a thirty-second pick clock —
# and is two-browser besides, so it runs with those two, after the shared server is down.
#
# Usage, from the sample root:
#
#   Client.Tests/run-e2e.sh <log-directory> [match-runs] [matchmaking-runs] [heist-runs]
#
# Everything is built first with a plain `dotnet build`; the runs themselves are --no-build, so a rebuild in
# the middle cannot swap the binaries under a fixture.
#
# WHY THE PORT SWEEP: `dotnet run --project X` is a wrapper that launches the built app as a CHILD process, so
# killing the pid you started leaves the app alive and still listening. The next run's Kestrel then fails to
# bind, the readiness probe is answered by the STALE process, and the suite fails on every locator or refuses
# to start its own server — which reads exactly like a product regression and is not one. So the ports are
# freed by owner before and after, and again before each of the two suites that take the game server's ports
# over. Every number the sweep touches comes from STICKYPAWS_PORT_OFFSET, so a run in a parallel worktree frees
# its own listeners rather than the other worktree's.
set -u

SAMPLE=${0:a:h:h}
LOGDIR=${1:?usage: run-e2e.sh <log-directory> [match-runs] [matchmaking-runs] [heist-runs]}
RUNS=${2:-1}
# The two-browser suites are timing-shaped, so they are worth running more often than the rest at a
# landing a change that touches the match.
QUEUE_RUNS=${3:-1}
HEIST_RUNS=${4:-1}

# STICKYPAWS_PORT_OFFSET shifts every port this script binds, probes and frees, so a parallel worktree can run
# the whole thing while somebody else holds the stock set. Zero — the default — is exactly the run that always
# happened. It matters that the SWEEP is shifted along with the listeners and not on its own: this script frees
# ports with `kill -9`, and a sweep left on the stock numbers under an offset would kill the other worktree's
# server rather than its own. The three suites that start their own server read the same variable
# (Client.Tests/GameServerProcess.cs), and the fixtures are pointed at the right web client by
# STICKYPAWS_WEB_BASE below.
OFFSET=${STICKYPAWS_PORT_OFFSET:-0}

# 5290 web client · 9339 game client TCP · 9380 WebSocket · 8899 the readiness endpoint the two
# server-owning suites use, all plus the offset.
WEB_PORT=$((5290 + OFFSET))
PORTS=($WEB_PORT $((9339 + OFFSET)) $((9380 + OFFSET)) $((8899 + OFFSET)))

# What the offset costs on the command line, and nothing at all when it is zero. Every listener is named here
# rather than in a fourth options file, because the offset is only known at run time; the clustering cookie is
# shifted too, so an offset node cannot join a stock one's cluster.
SERVER_ARGS=()
if [[ $OFFSET -ne 0 ]]; then
  SERVER_ARGS=(
    --System:ClientPorts=$((9339 + OFFSET))
    --WebSockets:ListenPorts=$((9380 + OFFSET))
    --CdnEmulator:ListenPort=$((5552 + OFFSET))
    --AdminApi:ListenPort=$((5550 + OFFSET))
    --PublicWebApi:ListenPort=$((5560 + OFFSET))
    --Clustering:RemotingPort=$((6000 + OFFSET))
    --Clustering:Cookie=stickypaws-offset-$OFFSET
    --Environment:MetricPort=$((9090 + OFFSET))
  )
  PORTS+=($((5552 + OFFSET)) $((5550 + OFFSET)) $((5560 + OFFSET)) $((6000 + OFFSET)) $((9090 + OFFSET)))
  export STICKYPAWS_WEB_BASE=http://127.0.0.1:$WEB_PORT
fi

# Every options file the game server reads, in order, ending with the suites' pacing profile: beats and bot
# think delays at zero, so no fixture waits on the presentation or on a bot pretending to think.
# METAPLAY_OPTIONS replaces the SDK's built-in list rather than appending to it, which is why the two
# built-in files are restated here; AGENTS.md says why that trade is the right way round. Paths are relative
# to the server's working directory, which `dotnet run --project` sets to the project directory.
#
# Set on this server rather than exported: the abandon suite's own server is started by GameServerProcess.cs,
# which sets it there for the same reason — each owns a server, so each says so.
OPTIONS_PATHS='Config/Options.base.yaml;Config/Options.local.yaml;Config/Options.e2e.yaml'

mkdir -p "$LOGDIR"
cd "$SAMPLE"

SERVER_PID=""
CLIENT_PID=""

# free_ports [port...] — all of them by default. The abandon suite needs the web client left alone, so the
# sweep before it names the game server's ports only.
free_ports() {
  local ports=(${@:-$PORTS})
  for port in $ports; do
    for pid in $(lsof -ti:$port 2>/dev/null); do kill -9 $pid 2>/dev/null; done
  done
  sleep 2
}

cleanup() {
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null
  [[ -n "$CLIENT_PID" ]] && kill "$CLIENT_PID" 2>/dev/null
  sleep 2
  free_ports
  return 0
}
trap cleanup EXIT INT TERM

free_ports

echo "### building"
dotnet build StickyPaws.slnx > "$LOGDIR/build.log" 2>&1 || { tail -20 "$LOGDIR/build.log"; exit 1; }

echo "### starting the game server"
METAPLAY_OPTIONS=$OPTIONS_PATHS dotnet run --project Backend/Server --no-build -- "${SERVER_ARGS[@]}" > "$LOGDIR/server.log" 2>&1 &
SERVER_PID=$!
for i in {1..180}; do grep -q 'Server is now ready to serve' "$LOGDIR/server.log" && break; sleep 1; done
grep -q 'Server is now ready to serve' "$LOGDIR/server.log" || { echo "the server never became ready"; tail -30 "$LOGDIR/server.log"; exit 1; }

echo "### starting the web client"
# At an offset the client also needs its own --urls and the ignored Client/wwwroot/appsettings.Development.json
# carrying the matching LocalServer:PortOffset, WRITTEN BEFORE THE BUILD ABOVE — static web assets are served
# through a build-time manifest, so a file created afterwards is not in it and the app silently dials the stock
# ports (AGENTS.md, "Ports and parallel copies").
if [[ $OFFSET -ne 0 ]]; then
  ASPNETCORE_ENVIRONMENT=Development dotnet run --project Client/Client.csproj --no-build --no-launch-profile \
    --urls http://127.0.0.1:$WEB_PORT > "$LOGDIR/client.log" 2>&1 &
else
  dotnet run --project Client/Client.csproj --no-build > "$LOGDIR/client.log" 2>&1 &
fi
CLIENT_PID=$!
for i in {1..180}; do curl -fsS -o /dev/null http://localhost:$WEB_PORT/ 2>/dev/null && break; sleep 1; done
curl -fsS -o /dev/null http://localhost:$WEB_PORT/ || { echo "the web client never came up"; tail -30 "$LOGDIR/client.log"; exit 1; }

# Each suite's wall time is printed with its tally, because "did that change make the suites faster" is a
# question asked of this script often enough that timing it by hand is the wrong answer.
run_suite() {
  local name=$1 filter=$2 log=$3
  echo "### $name"
  local started=$SECONDS
  dotnet test Client.Tests/Client.Tests.csproj --no-build --filter "$filter" \
    --settings Client.Tests/playwright.runsettings > "$log" 2>&1
  echo "wall $((SECONDS - started))s"
  grep -E '^(Passed!|Failed!)' "$log" || tail -5 "$log"
}

# The offline suites and the browser-only ones in one pass: everything that needs the web client but no game
# server, plus HomePageTests, which needs both. The board's own fixtures drive /dev/board?env=offline and the
# four CSS/JS ones build their own markup and need no navigation at all, so none of them costs a match.
OFFLINE_FILTER="FullyQualifiedName~OfflineModeTests\
|FullyQualifiedName~CollectionTests\
|FullyQualifiedName~CollectionPortraitTests\
|FullyQualifiedName~DeckbuilderTests\
|FullyQualifiedName~LockTests\
|FullyQualifiedName~BoardChromeTests\
|FullyQualifiedName~BoardEffectsTests\
|FullyQualifiedName~BoardInteractionTests\
|FullyQualifiedName~BoardPlaySequenceTests\
|FullyQualifiedName~BoardReadabilityTests\
|FullyQualifiedName~CardFrameCompositionTests\
|FullyQualifiedName~DenGuardCueTests\
|FullyQualifiedName~BoardGeometryCssTests\
|FullyQualifiedName~BoardMotionCssTests\
|FullyQualifiedName~BoardMotionResizeTests\
|FullyQualifiedName~BoardTargetingTests\
|FullyQualifiedName~BoardScreenshots\
|FullyQualifiedName~HomePageTests"

run_suite "offline, board and HomePageTests fixtures" "$OFFLINE_FILTER" "$LOGDIR/offline-home.log"

for run in $(seq 1 $RUNS); do
  run_suite "MatchTests run $run" "FullyQualifiedName~MatchTests" "$LOGDIR/matchtests-$run.log"
done

# The ranked queue, after MatchTests and before the abandon suite. It has to have the server to itself for the
# same reason MatchTests does and one more: there is ONE global queue, so two taps from two suites inside one
# fill wait are pooled into a single match with two humans in it, which is the product behaving correctly and
# every "the opponent is a bot" assertion failing at once.
for run in $(seq 1 $QUEUE_RUNS); do
  run_suite "MatchmakingTests run $run" "FullyQualifiedName~MatchmakingTests" "$LOGDIR/matchmaking-$run.log"
done

echo "### stopping the game server; the last three suites start their own"
kill "$SERVER_PID" 2>/dev/null
for i in {1..60}; do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 1; done
SERVER_PID=""
free_ports $((9339 + OFFSET)) $((9380 + OFFSET)) $((8899 + OFFSET))

run_suite "MatchAbandonTests" "FullyQualifiedName~MatchAbandonTests" "$LOGDIR/abandon.log"

# The Heist owns a server too, and for the timings rather than the process: its delivery retry interval and its
# pick clock are five seconds and thirty, against the shared profile's thirty and forty-five. Both of the cases
# it exists for — a winner slower than one retry, and a clock that lapses — are minutes long at the shared
# numbers and were silently lost before the fix, so they have to be cheap enough to run every time. It is also
# two-browser, so it needs the machine to itself for MatchmakingTests' reason as well.
for run in $(seq 1 $HEIST_RUNS); do
  free_ports $((9339 + OFFSET)) $((9380 + OFFSET)) $((8899 + OFFSET))
  run_suite "HeistTests run $run" "FullyQualifiedName~HeistTests" "$LOGDIR/heist-$run.log"
done

# The strike suite owns a server too, for a different reason: it needs a turn deadline of a few seconds, which
# the shared profile cannot have because MatchTests asserts against the five-minute one. Its server's output
# goes to its own file rather than $LOGDIR/server.log; GameServerProcess.LogPath names it.
free_ports $((9339 + OFFSET)) $((9380 + OFFSET)) $((8899 + OFFSET))
run_suite "MatchStrikeTests" "FullyQualifiedName~MatchStrikeTests" "$LOGDIR/strikes.log"


# The community fixture owns an isolated database and restarts its server to prove ladder persistence.
run_suite "CommunityTests" "FullyQualifiedName~CommunityTests" "$LOGDIR/community.log"

echo "### match-related warnings and errors in the server log (IAP excluded)"
grep -Ei 'warn|error|fail|desync|mismatch' "$LOGDIR/server.log" | grep -viE 'iap|applestore|googleplay' | head -40

echo "### done"
