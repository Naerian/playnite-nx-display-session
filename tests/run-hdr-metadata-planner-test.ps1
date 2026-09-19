# Smoke: HDR metadata matcher + session planner (no Playnite host).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll = Join-Path $root "..\bin\Release\PlayniteDisplayManager.dll"
$sdk = Get-ChildItem "$env:USERPROFILE\.nuget\packages\playnitesdk\6.16.0" -Recurse -Filter 'Playnite.SDK.dll' |
    Select-Object -First 1
if ($null -eq $sdk) { throw "Playnite.SDK 6.16 not found." }
[void][Reflection.Assembly]::LoadFrom($sdk.FullName)
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll).Path)

$matcher = $asm.GetType("PlayniteDisplayManager.Hdr.HdrMetadataMatcher", $true)
$planner = $asm.GetType("PlayniteDisplayManager.Hdr.HdrSessionPlanner", $true)
$profileType = $asm.GetType("PlayniteDisplayManager.Profiles.GameDisplayProfile", $true)
$overrideType = $asm.GetType("PlayniteDisplayManager.Profiles.GameHdrOverride", $true)
$policyType = $asm.GetType("PlayniteDisplayManager.Hdr.GlobalHdrPolicy", $true)
$actionType = $asm.GetType("PlayniteDisplayManager.Hdr.HdrSessionAction", $true)

$names = [string[]]$matcher.GetField("DefaultMatchNames").GetValue($null)
$indicate = $matcher.GetMethod("NamesIndicateHdr")
function Test-Indicate([string[]]$candidates) {
    return [bool]$indicate.Invoke($null, @([string[]]$candidates, [string[]]$names))
}
if (-not (Test-Indicate @("HDR"))) { throw "Expected HDR to match." }
if (Test-Indicate @("Multiplayer")) { throw "Unexpected Multiplayer match." }
if (-not (Test-Indicate @(" hdr10 "))) { throw "Expected HDR10 match." }

$planMethod = $planner.GetMethod("Plan")
$doNotManage = [Enum]::ToObject($policyType, 0)
$onAll = [Enum]::ToObject($policyType, 1)
$onMeta = [Enum]::ToObject($policyType, 2)
$enable = [Enum]::ToObject($actionType, 1)
$disable = [Enum]::ToObject($actionType, 2)
$none = [Enum]::ToObject($actionType, 0)

function Invoke-Plan($game, $policy, $profile) {
    return $planMethod.Invoke($null, @($game, $policy, $profile, [string[]]$names, $false))
}

$forceOn = [Activator]::CreateInstance($profileType)
$forceOn.HdrOverride = [Enum]::ToObject($overrideType, 1)
$plan = Invoke-Plan $null $doNotManage $forceOn
if ($plan.Action -ne $enable) { throw "ForceOn must Enable under DoNotManage." }

$forceOff = [Activator]::CreateInstance($profileType)
$forceOff.HdrOverride = [Enum]::ToObject($overrideType, 2)
$plan = Invoke-Plan $null $onAll $forceOff
if ($plan.Action -ne $disable) { throw "ForceOff must Disable under OnForAllGames." }

$skip = [Activator]::CreateInstance($profileType)
$skip.HdrOverride = [Enum]::ToObject($overrideType, 3)
$plan = Invoke-Plan $null $onAll $skip
if ($plan.Action -ne $none) { throw "DoNotTouch must skip." }

$plan = Invoke-Plan $null $onMeta $null
if ($plan.Action -ne $none) { throw "Metadata policy with null game must skip." }

Write-Host "HDR metadata/planner smoke OK."
