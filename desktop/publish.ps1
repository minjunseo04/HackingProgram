$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $PSScriptRoot 'BoundaryCheck.csproj'
$downloadFolder = Join-Path $projectRoot 'website\downloads'
$publishFolder = Join-Path $downloadFolder 'publish'
$zipFile = Join-Path $downloadFolder 'BoundaryCheck-win-x64.zip'
$packageReadme = Join-Path $downloadFolder 'README.txt'

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.nuget\http-cache'

New-Item -ItemType Directory -Force -Path $downloadFolder | Out-Null
dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $publishFolder
if ($LASTEXITCODE -ne 0) { throw 'Desktop app publish failed.' }

Copy-Item (Join-Path $PSScriptRoot 'README.md') $packageReadme -Force
Compress-Archive -Path (Join-Path $publishFolder 'BoundaryCheck.exe'), $packageReadme -DestinationPath $zipFile -Force
Write-Output "Created $zipFile"
