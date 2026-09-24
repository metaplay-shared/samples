#!/usr/bin/env bash
# Runs the E2E harness, tools/run-e2e.py. Requires python3 on PATH.
# -u disables output buffering. When stdout is redirected to a file, Python buffers the harness's progress lines,
# and they would appear after the build and test output they belong with.
exec python3 -u "$(dirname "$0")/run-e2e.py" "$@"
