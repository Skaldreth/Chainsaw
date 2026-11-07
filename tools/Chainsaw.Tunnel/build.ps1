# Builds Chainsaw's WireGuard helper for Windows (x64) into dist\, with its SHA-256 beside it; the same as build.sh.
# Chainsaw only starts a helper with the hash it was built with, so run this, then build Chainsaw.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

go vet ./...
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
go test ./...
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$env:GOOS = 'windows'; $env:GOARCH = 'amd64'; $env:CGO_ENABLED = '0'
go build -trimpath -buildvcs=false -ldflags "-s -w -buildid=" -o dist/chainsaw-tunnel.exe .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$hash = (Get-FileHash -Algorithm SHA256 dist/chainsaw-tunnel.exe).Hash.ToLowerInvariant()
Set-Content -NoNewline -Encoding ascii dist/chainsaw-tunnel.exe.sha256 "$hash  chainsaw-tunnel.exe`n"
Get-Content dist/chainsaw-tunnel.exe.sha256
