<#
.SYNOPSIS
    Builds the launcher, signs it with Azure Artifact Signing, scans it with Defender,
    and (with -Publish) creates the GitHub release with the exe as the asset.

.DESCRIPTION
    Run from anywhere in PowerShell:
        .\launcher\release.ps1                 build + sign + scan into launcher\dist
        .\launcher\release.ps1 -Publish        the same, then a GitHub release tagged v<version>
        .\launcher\release.ps1 -NoSign         skip signing (only for a build that never leaves this PC)

    Signing needs, once per PC:
      * Windows SDK signtool (Visual Studio installs it).
      * The Trusted Signing client unpacked at launcher\tools\signing\client
        (Microsoft.Trusted.Signing.Client from nuget.org; the folder is ignored by git) and
        metadata.json beside it naming the account endpoint, account name and certificate profile.
      * Azure CLI signed in as a user with the 'Artifact Signing Certificate Profile Signer'
        role on the account:  az login
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$NoSign
)

$ErrorActionPreference = 'Stop'
$launcher = $PSScriptRoot
$project  = Join-Path $launcher 'VanadreamsLauncher\VanadreamsLauncher.csproj'
$dist     = Join-Path $launcher 'dist'
$exe      = Join-Path $dist 'VanadreamsLauncher.exe'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
Write-Host "Vanadreams Launcher $version"

# 1. Build and test
dotnet build $project -c Release --nologo -v q
if ($LASTEXITCODE) { throw 'Build failed.' }
dotnet test (Join-Path $launcher 'VanadreamsLauncher.Tests') -c Release --nologo -v q
if ($LASTEXITCODE) { throw 'Tests failed.' }

New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $launcher 'VanadreamsLauncher\bin\Release\net48\VanadreamsLauncher.exe') $exe -Force

# 2. Sign
if (-not $NoSign) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found under the Windows SDK.' }
    $signing  = Join-Path $launcher 'tools\signing'
    $dlib     = Join-Path $signing 'client\bin\x64\Azure.CodeSigning.Dlib.dll'
    $metadata = Join-Path $signing 'metadata.json'
    foreach ($f in @($dlib, $metadata)) { if (-not (Test-Path $f)) { throw "Missing $f" } }

    & $signtool.FullName sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $dlib /dmdf $metadata $exe
    if ($LASTEXITCODE) { throw 'Signing failed. Is az login current, and does the account have the Certificate Profile Signer role?' }
    & $signtool.FullName verify /pa /v $exe | Select-String 'Issued to|Successfully'
    if ($LASTEXITCODE) { throw 'Signature did not verify.' }
}

# 3. Defender scan: never ship a build the classifier dislikes
$mpcmd = Get-ChildItem 'C:\ProgramData\Microsoft\Windows Defender\Platform\*\MpCmdRun.exe' |
         Sort-Object FullName -Descending | Select-Object -First 1
& $mpcmd.FullName -Scan -ScanType 3 -File $exe -DisableRemediation | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Defender flagged $exe (exit $LASTEXITCODE). Do not publish." }
Write-Host "Defender: clean"

Compress-Archive -Path $exe -DestinationPath (Join-Path $dist 'VanadreamsLauncher.zip') -Force

# 4. Publish
if ($Publish) {
    $account = gh api user --jq .login
    if ($account -ne 'Finalferrin') { throw "gh is signed in as $account; run: gh auth switch -u Finalferrin" }
    gh release create "v$version" $exe --repo Finalferrin/vanadreams-ashita --title "Vanadreams Launcher $version" --notes "Signed build. Download VanadreamsLauncher.exe and run it."
    if ($LASTEXITCODE) { throw 'gh release create failed.' }
}

Write-Host "Done: $exe"
