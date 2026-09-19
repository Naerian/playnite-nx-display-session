# Smoke: write HDR off on capable targets (no toggle on — avoids flashing the room).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll = Join-Path $root "..\bin\Release\PlayniteDisplayManager.dll"
$sdk = Get-ChildItem "$env:USERPROFILE\.nuget\packages\playnitesdk\6.16.0" -Recurse -Filter 'Playnite.SDK.dll' |
    Select-Object -First 1
[void][Reflection.Assembly]::LoadFrom($sdk.FullName)
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll).Path)
$enumType = $asm.GetType("PlayniteDisplayManager.Displays.DisplayEnumerator", $true)
$hdrType = $asm.GetType("PlayniteDisplayManager.Hdr.HdrService", $true)
$enumerator = [Activator]::CreateInstance($enumType)
$displays = $enumType.GetMethod("GetDisplays").Invoke($enumerator, $null)
$hdr = [Activator]::CreateInstance($hdrType)
# Unary comma keeps the IReadOnlyList as one argument (PowerShell unwraps single-element collections).
$writes = $hdrType.GetMethod("BuildForceOffWrites").Invoke($hdr, (,$displays))
Write-Host ("HDR force-off targets: " + $writes.Count)
$args = [object[]]@($writes, $null)
$count = [int]$hdrType.GetMethod("ApplyHdrWrites").Invoke($hdr, $args)
Write-Host ("Writes applied: " + $count)
if ($writes.Count -gt 0 -and $count -eq 0) {
    throw ("ApplyHdrWrites failed: " + $args[1])
}
Write-Host "HDR write-off smoke OK."
