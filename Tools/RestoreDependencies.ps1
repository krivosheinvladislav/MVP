param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $projectRoot 'Temp/NuGetCli'
$cliPath = Join-Path $toolRoot 'package/tools/net8.0/any/NuGetForUnity.Cli.dll'
if (-not (Test-Path -LiteralPath $cliPath)) {
    New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
    $archive = Join-Path $toolRoot 'cli.zip'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/nugetforunity.cli/4.5.0/nugetforunity.cli.4.5.0.nupkg' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolRoot 'package') -Force
}
& $DotnetPath $cliPath restore $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed. Install .NET 8 Runtime or provide -DotnetPath.' }
