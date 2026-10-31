<#
  Builds everything a release needs:
    1. runs the unit tests
    2. publishes a self-contained single BrowserProfileManager.exe (no .NET install needed on other PCs)
    3. zips it, with LICENSE and NOTICE, as the portable version
    4. builds the installer with Inno Setup 6
  Output: artifacts\BrowserProfileManager-Setup-<version>.exe and artifacts\BrowserProfileManager-<version>-portable.zip

  Builds only from committed code, so the commit stamped into the exe's version is the code inside it.

  Usage:  .\build.ps1                 (version from Directory.Build.props)
          .\build.ps1 -Version 1.1.0
          .\build.ps1 -SkipTests
          .\build.ps1 -AllowUncommitted   (test builds only, never for a release)
#>
param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$AllowUncommitted
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

if (-not $Version) { $Version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version }

if (-not $AllowUncommitted) {
    $changes = git -C $root status --porcelain
    if ($changes) { throw "Commit your changes first (or use -AllowUncommitted for a test build):`n$($changes -join "`n")" }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $artifacts | Out-Null

if (-not $SkipTests) {
    Step 'Running tests'
    dotnet test "$root\tests\BPM.Core.Tests\BPM.Core.Tests.csproj" -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }
}

Step "Publishing BrowserProfileManager.exe $Version (self-contained, single file)"
dotnet publish "$root\src\BPM.App\BPM.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'Publish failed.' }
Copy-Item "$root\LICENSE", "$root\NOTICE" $publish

Step 'Creating the portable zip'
$zip = Join-Path $artifacts "BrowserProfileManager-$Version-portable.zip"
Compress-Archive -Path (Join-Path $publish 'BrowserProfileManager.exe'), (Join-Path $publish 'LICENSE'), (Join-Path $publish 'NOTICE') `
    -DestinationPath $zip -Force

Step 'Building the installer'
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }
& $iscc /Qp "/DAppVersion=$Version" "$root\installer\BrowserProfileManager.iss"
if ($LASTEXITCODE) { throw 'Installer build failed.' }

Step 'Done'
Get-ChildItem $artifacts -File | Select-Object Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
