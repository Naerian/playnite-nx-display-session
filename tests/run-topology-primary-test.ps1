# Smoke: make-primary on current primary (no-op translate) then restore snapshot.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll = Join-Path $root "..\bin\Release\PlayniteDisplayManager.dll"
$sdk = Get-ChildItem "$env:USERPROFILE\.nuget\packages\playnitesdk\6.16.0" -Recurse -Filter 'Playnite.SDK.dll' |
    Select-Object -First 1
if (-not $sdk) { throw "Playnite.SDK not found" }
[void][Reflection.Assembly]::LoadFrom($sdk.FullName)
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll).Path)
$enumType = $asm.GetType("PlayniteDisplayManager.Displays.DisplayEnumerator", $true)
$topoType = $asm.GetType("PlayniteDisplayManager.Displays.DisplayTopologyService", $true)
$reqType = $asm.GetType("PlayniteDisplayManager.Displays.DisplayTopologyRequest", $true)

$enumerator = [Activator]::CreateInstance($enumType)
$displays = $enumType.GetMethod("GetDisplays").Invoke($enumerator, $null)
if ($displays.Count -lt 1) { throw "No displays" }
$primary = $null
foreach ($d in $displays) { if ($d.IsPrimary) { $primary = $d; break } }
if (-not $primary) { $primary = $displays[0] }

$topology = [Activator]::CreateInstance($topoType)
$before = $topoType.GetMethod("CaptureSnapshot").Invoke($topology, $null)
$req = [Activator]::CreateInstance($reqType)
$req.TargetDisplayId = [string]$primary.Id
$req.MakePrimary = $true
$req.TurnOffOtherDisplays = $false

$apply = $topoType.GetMethod("TryApplyRequest").Invoke($topology, @($req))
if (-not $apply.Success) {
    throw ("TryApplyRequest failed: " + $apply.Error)
}

$restoreOk = $false
$errorOut = $null
$args = [object[]]@($before, $errorOut)
# TryRestoreSnapshot(DisplaySnapshot, out string)
$methods = $topoType.GetMethods() | Where-Object { $_.Name -eq "TryRestoreSnapshot" }
$mi = $methods | Select-Object -First 1
$outArgs = [object[]]@($before, $null)
$restoreOk = [bool]$mi.Invoke($topology, $outArgs)
if (-not $restoreOk) {
    throw ("Restore failed: " + $outArgs[1])
}

Write-Host ("Topology primary trial OK on " + $primary.EffectiveName + " (" + $primary.Id + ")")
