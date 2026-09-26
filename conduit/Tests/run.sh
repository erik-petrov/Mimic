#!/bin/sh
# Runs the autopick tests with Mono (apt install mono-devel). They don't need Windows or League.
set -e
cd "$(dirname "$0")/.."
out="${TMPDIR:-/tmp}/conduit-tests"
mkdir -p "$out"
mcs -nologo -out:"$out/AutopickTests.exe" -r:Microsoft.CSharp -r:System.Runtime.Serialization \
    Autopick.cs SimpleJson.cs Tests/AutopickTests.cs 2>&1 | grep -v "^SimpleJson.cs" || true
mono "$out/AutopickTests.exe"
