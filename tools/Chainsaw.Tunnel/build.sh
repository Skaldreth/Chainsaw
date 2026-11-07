#!/usr/bin/env bash
# Builds Chainsaw's WireGuard helper for Windows (x64) into dist/, with its SHA-256 beside it. The build is
# reproducible (no paths, build id or version control data in the file), so the same source gives the same hash.
# Chainsaw only starts a helper with the hash it was built with (HelperProcessStarter in the Network module), so
# run this, then build Chainsaw, and check in both files.
set -euo pipefail
cd "$(dirname "$0")"

go vet ./...
go test ./...

export GOOS=windows GOARCH=amd64 CGO_ENABLED=0
go build -trimpath -buildvcs=false -ldflags "-s -w -buildid=" -o dist/chainsaw-tunnel.exe .

(cd dist && sha256sum chainsaw-tunnel.exe > chainsaw-tunnel.exe.sha256)
cat dist/chainsaw-tunnel.exe.sha256
