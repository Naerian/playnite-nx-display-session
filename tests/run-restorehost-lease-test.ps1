# Manual / CI smoke: arm RestoreHost against a disposable parent, kill parent, expect restore log.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "..\bin\Release\PlayniteDisplayManager.RestoreHost.exe"
$pluginDll = Join-Path $root "..\bin\Release\PlayniteDisplayManager.dll"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build RestoreHost first (package.ps1 or dotnet build RestoreHost)."
}

$log = Join-Path $env:TEMP "PlayniteDisplayManager-RestoreHost.log"
if (Test-Path -LiteralPath $log) {
    Remove-Item -LiteralPath $log -Force
}

$holder = $null
$hostProc = $null
try {
    $holder = Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoProfile", "-Command", "Start-Sleep -Seconds 120"
    ) -PassThru -WindowStyle Hidden

    $pipe = "DM_test_" + [guid]::NewGuid().ToString("N")
    $token = [guid]::NewGuid().ToString("N")
    $hostProc = Start-Process -FilePath $exe -ArgumentList @(
        "--pipe", $pipe, "--token", $token, "--parent", "$($holder.Id)"
    ) -PassThru -WindowStyle Hidden

    Start-Sleep -Seconds 1

    $asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $pluginDll).Path)
    $topologyType = $asm.GetType("PlayniteDisplayManager.Displays.DisplayTopologyService", $true)
    $snapshotType = $asm.GetType("PlayniteDisplayManager.Displays.DisplaySnapshot", $true)
    $topology = [Activator]::CreateInstance($topologyType)
    $snapshot = $topologyType.GetMethod("CaptureSnapshot").Invoke($topology, $null)
    $snapPath = [string](Join-Path $env:TEMP ("dm-lease-test-" + [guid]::NewGuid().ToString("N") + ".json"))
    $save = $snapshotType.GetMethod("SaveToFile")
    $null = $save.Invoke($snapshot, [object[]]@($snapPath))

    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($snapPath))
    $session = [guid]::NewGuid().ToString("N")
    $arm = "DM1|$token|$session|ARM|$encoded"

    $pipeClient = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipe, [IO.Pipes.PipeDirection]::Out)
    $pipeClient.Connect(3000)
    $writer = New-Object System.IO.StreamWriter($pipeClient, (New-Object System.Text.UTF8Encoding($false)))
    $writer.WriteLine($arm)
    $writer.Flush()
    $writer.Dispose()
    $pipeClient.Dispose()

    Write-Host "Armed. Killing parent PID $($holder.Id)..."
    Stop-Process -Id $holder.Id -Force
    $holder = $null
    $null = $hostProc.WaitForExit(15000)

    if (-not (Test-Path -LiteralPath $log)) {
        throw "RestoreHost log was not created."
    }

    $logText = Get-Content -LiteralPath $log -Raw
    if ($logText -notmatch 'Parent process lost' -or $logText -notmatch 'Restore applied') {
        Write-Host $logText
        throw "Lease restore did not log a successful parent-lost restore."
    }

    Write-Host "Lease kill test OK."
    Write-Host $logText
}
finally {
    if ($holder -and -not $holder.HasExited) { Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue }
    if ($hostProc -and -not $hostProc.HasExited) { Stop-Process -Id $hostProc.Id -Force -ErrorAction SilentlyContinue }
}
