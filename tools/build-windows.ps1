param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$ToolchainPath = $env:CSII_TOOLPATH
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Build the game adapter on Windows with Cities: Skylines II installed.' }
if (-not $ToolchainPath) { $ToolchainPath = [Environment]::GetEnvironmentVariable('CSII_TOOLPATH', 'User') }
if (-not $ToolchainPath -or -not (Test-Path (Join-Path $ToolchainPath 'Mod.props')) -or
    -not (Test-Path (Join-Path $ToolchainPath 'Mod.targets'))) {
    throw 'Install the modding toolchain from inside the game, then supply -ToolchainPath or CSII_TOOLPATH.'
}
$gameAssembly = Join-Path $GameDirectory 'Cities2_Data\Managed\Game.dll'
if (-not (Test-Path $gameAssembly)) { throw "Cannot find the game assembly: $gameAssembly" }
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet run --project tests/SpatialDemand.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $buildOutput = Join-Path $repo ('artifacts\build-' + [Guid]::NewGuid().ToString('N'))
    dotnet build src/SpatialDemand.Mod -c Release "-p:CSIIToolPath=$ToolchainPath" --output $buildOutput
    if ($LASTEXITCODE -ne 0) { throw 'Actual game mod build failed. No package produced.' }

    $dll = Join-Path $buildOutput 'SpatialDemand.dll'
    if (-not (Test-Path $dll)) { throw 'The toolchain changed its output path; locate the actual new build before packaging.' }
    $resolvedPathFile = Join-Path $repo 'src\SpatialDemand.Mod\obj\Release\netstandard2.1\game-assembly-path.txt'
    if (-not (Test-Path $resolvedPathFile)) { throw 'The build did not record its resolved game assembly.' }
    $resolvedGame = (Get-Content $resolvedPathFile -Raw).Trim()
    if (-not $resolvedGame -or -not (Test-Path $resolvedGame)) { throw 'Cannot verify the resolved game assembly.' }
    $gameHash = (Get-FileHash $gameAssembly -Algorithm SHA256).Hash
    if ((Get-FileHash $resolvedGame -Algorithm SHA256).Hash -ne $gameHash) {
        throw 'The toolchain built against a different game assembly than GameDirectory. Fix the toolchain game path before packaging.'
    }
    $package = Join-Path $repo 'artifacts\SpatialDemand'
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item $dll (Join-Path $package 'SpatialDemand.dll') -Force
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine repository revision.' }
    $dirty = [bool](git status --porcelain)
    [ordered]@{
        revision = $revision
        workingTreeModified = $dirty
        builtAtUtc = [DateTime]::UtcNow.ToString('o')
        gameAssemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($gameAssembly).FileVersion
        gameAssemblySha256 = $gameHash
        modSha256 = (Get-FileHash $dll -Algorithm SHA256).Hash
        inGameValidation = 'not recorded; follow docs/game-validation.md'
    } | ConvertTo-Json | Set-Content (Join-Path $package 'build-manifest.json') -Encoding UTF8
    Write-Host "Built local package: $package. Nothing was published. Run the in-game acceptance procedure."
}
finally { Pop-Location }
