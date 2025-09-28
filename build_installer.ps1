[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$VersionTag
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host "[Stenographer] $message"
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$projectPath = Join-Path $scriptRoot "Stenographer\Stenographer.csproj"
$targetFramework = "net8.0-windows"
$publishRoot = Join-Path $scriptRoot "Stenographer\bin\$Configuration\$targetFramework\$RuntimeIdentifier\publish"
$distRoot = Join-Path $scriptRoot "dist"

if (-not (Test-Path $projectPath)) {
    throw "Unable to locate project file at $projectPath"
}

Write-Step "Restoring NuGet packages"
& dotnet restore $projectPath

Write-Step "Publishing $Configuration build for $RuntimeIdentifier"
& dotnet publish $projectPath --configuration $Configuration --runtime $RuntimeIdentifier --self-contained true

if (-not (Test-Path $publishRoot)) {
    throw "Publish output not found at $publishRoot"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ([string]::IsNullOrWhiteSpace($VersionTag)) {
    $packageName = "Stenographer-$timestamp"
} else {
    $packageName = "Stenographer-$VersionTag"
}
$packageDir = Join-Path $distRoot $packageName
$zipPath = "$packageDir.zip"

Write-Step "Preparing staging directory at $packageDir"
if (-not (Test-Path $distRoot)) {
    New-Item -ItemType Directory -Path $distRoot | Out-Null
}
if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir | Out-Null

Write-Step "Copying published output"
Copy-Item -Path (Join-Path $publishRoot '*') -Destination $packageDir -Recurse -Force

$rootFiles = @("setup_models.bat", "Readme.md")
foreach ($file in $rootFiles) {
    $sourcePath = Join-Path $scriptRoot $file
    if (Test-Path $sourcePath) {
        Write-Step "Including $file"
        Copy-Item -Path $sourcePath -Destination $packageDir -Force
    }
}

Write-Step "Creating archive $zipPath"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force

Write-Step "Package created"
$zipInfo = Get-Item $zipPath
Write-Host ("`nOutput:`n  Folder: {0}`n  Archive: {1}`n  Size:   {2:N0} bytes" -f $packageDir, $zipPath, $zipInfo.Length)