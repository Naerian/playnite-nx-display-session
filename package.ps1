param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$ToolboxPath = "C:\Playnite\Toolbox.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "PlayniteDisplayManager.csproj"
$hostProject = Join-Path $root "RestoreHost\PlayniteDisplayManager.RestoreHost.csproj"
$extensionYaml = Join-Path $root "extension.yaml"

function Assert-FileHasContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MinNonZeroBytes = 32
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing required file: $Path"
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -le 0) {
        throw "File is empty: $Path"
    }

    $nonZero = 0
    foreach ($b in $bytes) {
        if ($b -ne 0) {
            $nonZero++
            if ($nonZero -ge $MinNonZeroBytes) {
                return
            }
        }
    }

    throw "File looks wiped/zero-filled (power loss or copy glitch): $Path"
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project file was not found at $project"
}

if (-not (Test-Path -LiteralPath $hostProject)) {
    throw "RestoreHost project was not found at $hostProject"
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

# Wipe intermediates and previous outputs. PreserveNewest can keep zero-filled
# Localization copies after a power loss because size/timestamps still match.
foreach ($dir in @(
    (Join-Path $root "obj"),
    (Join-Path $root "RestoreHost\obj"),
    (Join-Path $root "bin\$Configuration"),
    (Join-Path $root "RestoreHost\bin\$Configuration")
)) {
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet restore $hostProject
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore (RestoreHost) failed with exit code $LASTEXITCODE"
}

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

dotnet build $hostProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build (RestoreHost) failed with exit code $LASTEXITCODE"
}

$build = Join-Path $root "bin\$Configuration"
$hostExe = Join-Path $build "PlayniteDisplayManager.RestoreHost.exe"
Write-Host "Running RestoreHost --self-test..."
& $hostExe --self-test
if ($LASTEXITCODE -ne 0) {
    throw "RestoreHost --self-test failed with exit code $LASTEXITCODE"
}

$sourceLocalization = Join-Path $root "Localization"
$required = @(
    (Join-Path $build "PlayniteDisplayManager.dll"),
    $hostExe,
    (Join-Path $build "extension.yaml"),
    (Join-Path $build "README.md"),
    $sourceLocalization,
    (Join-Path $build "media"),
    (Join-Path $build "Examples")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing build output: $path"
    }
}

Get-ChildItem -LiteralPath $sourceLocalization -Filter *.xaml | ForEach-Object {
    Assert-FileHasContent -Path $_.FullName
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
Assert-FileHasContent -Path (Join-Path $stage "PlayniteDisplayManager.dll") -MinNonZeroBytes 256
$pdb = Join-Path $build "PlayniteDisplayManager.pdb"
if (Test-Path -LiteralPath $pdb) {
    Copy-Item -LiteralPath $pdb -Destination $stage
}
Copy-Item -LiteralPath $hostExe -Destination $stage
$hostPdb = Join-Path $build "PlayniteDisplayManager.RestoreHost.pdb"
if (Test-Path -LiteralPath $hostPdb) {
    Copy-Item -LiteralPath $hostPdb -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $build "extension.yaml") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "README.md") -Destination $stage
# Always stage Localization from source — never reuse a possibly zero-filled bin copy.
Copy-Item -LiteralPath $sourceLocalization -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $build "media") -Destination $stage -Recurse
if (Test-Path -LiteralPath (Join-Path $build "Icons")) {
    Copy-Item -LiteralPath (Join-Path $build "Icons") -Destination $stage -Recurse
}
Copy-Item -LiteralPath (Join-Path $build "Examples") -Destination $stage -Recurse

Get-ChildItem -LiteralPath (Join-Path $stage "Localization") -Filter *.xaml | ForEach-Object {
    Assert-FileHasContent -Path $_.FullName
}

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

# Toolbox occasionally leaves a truncated zip (EOCD missing). Also reject packs that
# contain zero-filled localization (seen after unclean shutdowns).
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entryCount = $zip.Entries.Count
        if ($entryCount -lt 3) {
            throw "Package looks empty ($entryCount entries)."
        }

        $locEntries = @($zip.Entries | Where-Object {
            $_.FullName -match '(?i)(^|[/\\])Localization[/\\].+\.xaml$'
        })
        if ($locEntries.Count -lt 1) {
            throw "Package has no Localization/*.xaml entries."
        }

        foreach ($entry in $locEntries) {
            $buffer = New-Object byte[] ([Math]::Min(64, [int]$entry.Length))
            $stream = $entry.Open()
            try {
                $read = $stream.Read($buffer, 0, $buffer.Length)
            }
            finally {
                $stream.Dispose()
            }

            $nonZero = 0
            for ($i = 0; $i -lt $read; $i++) {
                if ($buffer[$i] -ne 0) {
                    $nonZero++
                }
            }
            if ($nonZero -lt 8) {
                throw "Localization entry is zero-filled: $($entry.FullName)"
            }
        }

        Write-Host "Package ZIP OK ($entryCount entries, $($locEntries.Count) localization files)."
    }
    finally {
        $zip.Dispose()
    }
}
catch {
    throw "Generated .pext failed validation (Playnite will show broken/missing text): $($_.Exception.Message)"
}

Write-Host "Package created: $($package.FullName)"
Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256
