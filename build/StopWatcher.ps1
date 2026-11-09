# Asks a running background process (Chainsaw.Watcher.exe) to stop before Chainsaw.App is built.
#
# Why: the background watching runs from Chainsaw's output folder while a watch is active, also when Chainsaw is
# closed, and holds the module DLLs there. A build that copies new DLLs over them fails with MSB3027. The process
# listens for the named stop event (BackgroundNames.StopEvent), so it is asked politely and given a moment to leave;
# Chainsaw starts it again the next time it runs with an active watch. Nothing here touches the Run key.

$stopEvent = 'Local\Chainsaw.Watcher.Stop'
$processName = 'Chainsaw.Watcher'
$waitFor = [TimeSpan]::FromSeconds(10)

$handle = $null
if (-not [System.Threading.EventWaitHandle]::TryOpenExisting($stopEvent, [ref]$handle)) {
    exit 0
}

try {
    $handle.Set() | Out-Null
}
finally {
    $handle.Dispose()
}

$deadline = (Get-Date) + $waitFor
while ((Get-Date) -lt $deadline -and (Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 200
}

if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    Write-Host "Chainsaw.Watcher did not stop within $($waitFor.TotalSeconds) s; the build may fail to copy its files."
}
else {
    Write-Host 'Chainsaw.Watcher was asked to stop before the build and has left.'
}
exit 0
