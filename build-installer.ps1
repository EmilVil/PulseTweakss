$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'Pulse.csproj'
$publish = Join-Path $root 'artifacts/PULSE-win-x64'
$script = Join-Path $root 'installer/PULSE.iss'

$compiler = @(
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install it, then run .\build-installer.ps1 again.'
}

$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
if ($sdk) {
    & $sdk.Source publish $project --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None --output $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}
elseif (-not (Test-Path -LiteralPath (Join-Path $publish 'Pulse.exe'))) {
    throw 'Install the .NET 8 SDK or provide a previously published Pulse.exe.'
}

& $compiler $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

Write-Host "Installer created at $(Join-Path $root 'Releases/PULSE-Setup.exe')"
