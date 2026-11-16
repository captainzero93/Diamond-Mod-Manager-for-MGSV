# DMM checked release build
$ErrorActionPreference = 'Stop'

$Project = 'DMM'
$Marker  = 'DMM_V0_9_86'
$Root    = $PSScriptRoot

function Fail([string]$msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

# layout
$required = @(
    'DMM.sln', 'Directory.Build.props', 'data\qar_dictionary.txt', 'data\fpk_dictionary.txt',
    'src\DMM.Core\DMM.Core.csproj', 'src\DMM.App\DMM.App.csproj', 'src\DMM.App\Assets\logo.png', 'src\DMM.App\Assets\icon.ico', 'src\DMM.App\Assets\Inter-Regular.ttf', 'src\DMM.App\Assets\AtkinsonHyperlegibleNext-Regular.ttf',
    'tests\DMM.Tests\DMM.Tests.csproj', 'tests\fixtures\qar\gzs_all.dat', 'tests\fixtures\fpk\gzs_write.fpk', 'tests\fixtures\hashes\gzstool_hashes.tsv', 'tests\fixtures\makebite\expected\metadata.xml'
)
foreach ($f in $required) {
    if (-not (Test-Path (Join-Path $Root $f))) { Fail "missing $f" }
}

# source marker
if (-not (Select-String -Path (Join-Path $Root 'src\DMM.Core\DMM.Core.csproj') -SimpleMatch $Marker -Quiet)) {
    Fail "marker $Marker not in src\DMM.Core\DMM.Core.csproj"
}

# .NET SDK 10
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail 'dotnet not found. Install the .NET 10 SDK (Visual Studio 2026 includes it).' }
$sdks = @(& $dotnet.Source --list-sdks)
$sdk10 = $sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1
if (-not $sdk10) { Fail "no .NET 10 SDK found. Installed SDKs:`n$($sdks -join "`n")" }
Write-Host "dotnet: $($dotnet.Source)"
Write-Host "SDK:    $sdk10"

# next verified-v<NNN>, never overwrite an earlier one
$highest = 0
foreach ($dir in Get-ChildItem -Path $Root -Directory -Filter 'verified-v*') {
    if ($dir.Name -match '^verified-v(\d{3})$') { $highest = [Math]::Max($highest, [int]$Matches[1]) }
}
$Version = 'v{0:D3}' -f ($highest + 1)
$OutDir  = Join-Path $Root "verified-$Version"
$AppDir  = Join-Path $OutDir $Project

# clean Release build and tests
& $dotnet.Source build (Join-Path $Root 'DMM.sln') -c Release --no-incremental -nologo -v:minimal -nodeReuse:false
if ($LASTEXITCODE -ne 0) { Fail "build exit code $LASTEXITCODE" }
# test games go beside the source, not in %TEMP%, so a full system drive does not fail the tests
$TestTemp = Join-Path $Root '.test-temp'
New-Item -ItemType Directory -Path $TestTemp -Force | Out-Null
$oldTemp, $oldTmp = $env:TEMP, $env:TMP
$env:TEMP = $env:TMP = $TestTemp
& $dotnet.Source (Join-Path $Root 'tests\DMM.Tests\bin\Release\net10.0\DMM.Tests.dll')
$testExit = $LASTEXITCODE
$env:TEMP, $env:TMP = $oldTemp, $oldTmp
Remove-Item -Path $TestTemp -Recurse -Force -ErrorAction SilentlyContinue
if ($testExit -ne 0) { Fail "tests failed (exit code $testExit)" }

# publish the app as a single-file Release exe
# (framework-dependent, needs the .NET 10 Desktop Runtime; dictionaries and symbols are inside the exe)
$publish = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishSingleFile=true', '-p:DebugType=embedded',
             '-p:GenerateDocumentationFile=false', '-o', $AppDir, '-nologo', '-v:minimal', '-nodeReuse:false')
& $dotnet.Source publish (Join-Path $Root 'src\DMM.App\DMM.App.csproj') @publish
if ($LASTEXITCODE -ne 0) { Fail "publish DMM.App exit code $LASTEXITCODE" }

foreach ($f in 'DMM.exe') {
    if (-not (Test-Path (Join-Path $AppDir $f))) { Fail "staging missing $f" }
}
$loose = @(Get-ChildItem -Path $AppDir -File | Where-Object { $_.Name -notin 'DMM.exe' })
if ($loose.Count -gt 0) { Fail "unexpected files in the output: $(($loose | ForEach-Object Name) -join ', ')" }

# portable mods folder next to the exe (the placeholder keeps it in the zip)
$mods = Join-Path $AppDir 'Mods'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
Set-Content -Path (Join-Path $mods 'Put .mgsv and .zip mods here.txt') -Value 'DMM lists every .mgsv and .zip in this folder.' -Encoding ascii
$docs = Join-Path $AppDir 'docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
foreach ($f in 'README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md', 'LICENSE') {
    if (Test-Path (Join-Path $Root $f)) { Copy-Item (Join-Path $Root $f) $docs }
}

# marker in the binary (DMM.Core assembly metadata inside the single-file bundle, stored as UTF-8)
foreach ($f in 'DMM.exe') {
    $bin = Join-Path $AppDir $f
    $text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($bin))
    if (-not $text.Contains($Marker)) { Fail "marker $Marker not in $bin" }
}

$zip = Join-Path $OutDir "DMM-$Version.zip"
Compress-Archive -Path $AppDir -DestinationPath $zip

Write-Host ''
Write-Host "PASS: $Project $Version" -ForegroundColor Green
foreach ($f in 'DMM.exe') {
    Write-Host ("  {0,-17} SHA256 {1}" -f $f, (Get-FileHash (Join-Path $AppDir $f) -Algorithm SHA256).Hash)
}
Write-Host "  Output:  $AppDir"
Write-Host "  Zip:     $zip"
Write-Host '  Install: copy the DMM folder anywhere outside the game folder and run DMM.exe. Needs the .NET 10 Desktop Runtime.'
Write-Host '           Put .mgsv and .zip mods in its Mods folder. Close the game first. On a SnakeBite install, use Take over in the bar at the top.'
Write-Host '  Logs:    <game>\D-Mod\logs\dmod_YYYYMMDD.log'
Write-Host "  Loaded:  the log starts with 'DMM 0.9.86 $Marker log opened'"
exit 0
