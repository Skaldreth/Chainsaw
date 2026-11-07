#!/usr/bin/env bash
# Builds Chainsaw's WireGuard helper for Windows (x64) into dist/, with its SHA-256 beside it. The build is
# reproducible (no paths, build id or version control data in the file) with the toolchain go.mod names, so the same
# source gives the same hash; the toolchain is fetched by Go itself when it is missing. Chainsaw only starts a helper
# with the hash it was built with (HelperProcessStarter in the Network module), so run this, then build Chainsaw, and
# check in both files.
set -euo pipefail
cd "$(dirname "$0")"

wanted="$(sed -n 's/^toolchain //p' go.mod)"
export GOTOOLCHAIN="$wanted"
actual="$(go env GOVERSION)"
if [ "$actual" != "$wanted" ]; then
  echo "Go $wanted is needed (go.mod), not $actual." >&2
  exit 1
fi

go vet ./...
GOOS=windows go vet ./...
go test -race ./...
go mod tidy -diff
# govulncheck is built for this computer, then asked about the Windows build, which is what ships.
tools="$(mktemp -d)"
trap 'rm -rf "$tools"' EXIT
GOBIN="$tools" go install golang.org/x/vuln/cmd/govulncheck@v1.9.0
GOOS=windows "$tools/govulncheck" ./...

GOOS=windows GOARCH=amd64 CGO_ENABLED=0 \
  go build -trimpath -buildvcs=false -ldflags "-s -w -buildid=" -o dist/chainsaw-tunnel.exe .

(cd dist && sha256sum chainsaw-tunnel.exe > chainsaw-tunnel.exe.sha256)
cat dist/chainsaw-tunnel.exe.sha256
