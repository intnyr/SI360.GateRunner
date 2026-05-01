param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x86"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts\GateRunner\$Version"
$wpfOutput = Join-Path $artifactRoot "wpf"
$cliOutput = Join-Path $artifactRoot "cli"
$assemblyVersion = if ($Version -match '^(\d+\.\d+\.\d+)(?:\.\d+)?') { $Matches[1] } else { "1.0.0" }

if (-not $repoRoot.EndsWith("GateRunner")) {
    throw "Unexpected repository root: $repoRoot"
}

New-Item -ItemType Directory -Force -Path $wpfOutput, $cliOutput | Out-Null

function Invoke-Dotnet {
    dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $args failed with exit code $LASTEXITCODE"
    }
}

Invoke-Dotnet restore (Join-Path $repoRoot "SI360.GateRunner.sln")
Invoke-Dotnet build (Join-Path $repoRoot "SI360.GateRunner.sln") -c $Configuration --no-restore /p:Version=$Version /p:AssemblyVersion=$assemblyVersion /p:FileVersion=$assemblyVersion
Invoke-Dotnet test (Join-Path $repoRoot "SI360.GateRunner.Tests\SI360.GateRunner.Tests.csproj") -c $Configuration --no-build

Invoke-Dotnet publish (Join-Path $repoRoot "SI360.GateRunner.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $wpfOutput

Invoke-Dotnet publish (Join-Path $repoRoot "SI360.GateRunner.Cli\SI360.GateRunner.Cli.csproj") `
    -c $Configuration `
    --self-contained false `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $cliOutput

Write-Host "Published GateRunner $Version to $artifactRoot"
