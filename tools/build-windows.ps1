param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$ToolchainPath = $env:CSII_TOOLPATH,
    [switch]$NoDeploy
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Build the game adapter on Windows with Cities: Skylines II installed.' }
if (-not $NoDeploy -and (Get-Process Cities2 -ErrorAction SilentlyContinue)) {
    throw 'Save your city and exit Cities: Skylines II before building. The official toolchain replaces local mod files that the running game locks.'
}
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
    # The official toolchain chooses the target framework and clears OutDir.
    # Keep the reference receipt beside that directory, with a unique name.
    $resolvedPathFile = $buildOutput + '.game-assembly-path.txt'
    New-Item -ItemType Directory -Force (Split-Path $buildOutput -Parent) | Out-Null
    $buildArguments = @('build', 'src/SpatialDemand.Mod', '-c', 'Release',
        "-p:CSIIToolPath=$ToolchainPath", "-p:GameAssemblyRecordPath=$resolvedPathFile", '--output', $buildOutput)
    # DeployWIP always runs in the official toolchain. A global MSBuild property
    # redirects its copy/removal to an isolated directory when staging a build.
    if ($NoDeploy) { $buildArguments += "-p:DeployDir=$buildOutput-staged" }
    dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Actual game mod build failed. No package produced.' }

    $dll = Join-Path $buildOutput 'SpatialDemand.dll'
    if (-not (Test-Path $dll)) { throw 'The toolchain changed its output path; locate the actual new build before packaging.' }
    if (-not (Test-Path $resolvedPathFile)) { throw 'The build did not record its resolved game assembly.' }
    $resolvedGame = (Get-Content $resolvedPathFile -Raw).Trim()
    if (-not $resolvedGame -or -not (Test-Path $resolvedGame)) { throw 'Cannot verify the resolved game assembly.' }
    $gameHash = (Get-FileHash $gameAssembly -Algorithm SHA256).Hash
    if ((Get-FileHash $resolvedGame -Algorithm SHA256).Hash -ne $gameHash) {
        throw 'The toolchain built against a different game assembly than GameDirectory. Fix the toolchain game path before packaging.'
    }
    $package = Join-Path $repo 'artifacts\SpatialDemand'
    if (Test-Path $package) { Remove-Item $package -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    # Include this mod's generated native/Burst libraries as well as its managed DLL.
    Get-ChildItem $buildOutput -File -Filter 'SpatialDemand*' | Copy-Item -Destination $package -Force
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
        deployment = $(if ($NoDeploy) { 'staged only; installed mod unchanged' } else { 'local game mod directory' })
        inGameValidation = 'not recorded; follow docs/game-validation.md'
    } | ConvertTo-Json | Set-Content (Join-Path $package 'build-manifest.json') -Encoding UTF8
    Write-Host "Built local package: $package. NoDeploy=$NoDeploy. Nothing was published. Run the in-game acceptance procedure."
}
finally { Pop-Location }
