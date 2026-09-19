param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$ToolboxPath = "C:\Playnite\Toolbox.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "PlayniteDisplayManager.csproj"
$extensionYaml = Join-Path $root "extension.yaml"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project file was not found at $project"
}

if (-not (Test-Path -LiteralPath $ToolboxPath)) {
    throw "Playnite Toolbox was not found at $ToolboxPath"
}

$manifestVersion = (
    Select-String -LiteralPath $extensionYaml -Pattern '^\s*Version:\s*(.+)\s*$' |
        Select-Object -First 1
).Matches[0].Groups[1].Value.Trim().Trim("'`"")
if ([string]::IsNullOrWhiteSpace($manifestVersion)) {
    throw "Could not read Version from extension.yaml"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $manifestVersion
}
elseif (-not [string]::Equals($Version, $manifestVersion, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Version '$Version' does not match extension.yaml ($manifestVersion). Update extension.yaml first."
}

Write-Host "Building Display Manager $Version ($Configuration)..."
dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet clean $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed with exit code $LASTEXITCODE"
}

dotnet build $project -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$build = Join-Path $root "bin\$Configuration"
$required = @(
    (Join-Path $build "PlayniteDisplayManager.dll"),
    (Join-Path $build "extension.yaml"),
    (Join-Path $build "README.md"),
    (Join-Path $build "Localization"),
    (Join-Path $build "media"),
    (Join-Path $build "Examples")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing build output: $path"
    }
}

$stage = Join-Path $env:TEMP "playnite-display-manager-pext-stage"
$dist = Join-Path $root "dist"
$distVersion = Join-Path $dist $Version
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage | Out-Null
if (-not (Test-Path -LiteralPath $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}
if (Test-Path -LiteralPath $distVersion) {
    Remove-Item -LiteralPath $distVersion -Recurse -Force
}
New-Item -ItemType Directory -Path $distVersion | Out-Null

Copy-Item -LiteralPath (Join-Path $build "PlayniteDisplayManager.dll") -Destination $stage
$pdb = Join-Path $build "PlayniteDisplayManager.pdb"
if (Test-Path -LiteralPath $pdb) {
    Copy-Item -LiteralPath $pdb -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $build "extension.yaml") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "README.md") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "Localization") -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $build "media") -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $build "Examples") -Destination $stage -Recurse

& $ToolboxPath pack $stage $distVersion
$packExit = $LASTEXITCODE
Remove-Item -LiteralPath $stage -Recurse -Force
if ($packExit -ne 0) {
    throw "Playnite Toolbox pack failed with exit code $packExit"
}

$package = Get-ChildItem -LiteralPath $distVersion -Filter '*.pext' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) {
    throw "Playnite Toolbox did not create a .pext package."
}

Write-Host "Package created: $($package.FullName)"
Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256
