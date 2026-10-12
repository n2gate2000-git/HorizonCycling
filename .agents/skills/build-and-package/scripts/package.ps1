# HorizonCycling Release Build & Packaging Automation Script
param(
    [string]$OutputDir = "release/HorizonCycling_V1.3.0",
    [string]$ZipPath = "release/HorizonCycling_V1.3.0.zip",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..\..")

Set-Location $repoRoot
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " HorizonCycling: Release Build & Packaging" -ForegroundColor Cyan
Write-Host " Base Directory: $repoRoot" -ForegroundColor Cyan
Write-Host " Output Directory: $OutputDir" -ForegroundColor Cyan
Write-Host " Output ZIP: $ZipPath" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Run Unit Tests (unless -SkipTests is specified)
if (-not $SkipTests) {
    Write-Host "`n[Step 1/5] Running Unit Tests..." -ForegroundColor Yellow
    dotnet test tests/HorizonCycling.Tests/HorizonCycling.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed! Aborting packaging process."
        exit $LASTEXITCODE
    }
    Write-Host "  -> Tests passed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n[Step 1/5] Skipping Unit Tests (-SkipTests specified)." -ForegroundColor DarkGray
}

# 2. Build Release Configuration
Write-Host "`n[Step 2/5] Building Release Configuration..." -ForegroundColor Yellow
dotnet build src/HorizonCyclingApp/HorizonCyclingApp.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed! Aborting packaging process."
    exit $LASTEXITCODE
}
Write-Host "  -> Release build succeeded!" -ForegroundColor Green

# 3. Publish Self-Contained Release
Write-Host "`n[Step 3/5] Publishing Self-Contained Application..." -ForegroundColor Yellow
$absOutputDir = Join-Path $repoRoot $OutputDir
if (-not (Test-Path $absOutputDir)) {
    New-Item -ItemType Directory -Path $absOutputDir -Force | Out-Null
}

dotnet publish src/HorizonCyclingApp/HorizonCyclingApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $absOutputDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed! Aborting packaging process."
    exit $LASTEXITCODE
}
Write-Host "  -> Self-contained publishing completed to $absOutputDir" -ForegroundColor Green

# 4. Synchronize Essential Assets
Write-Host "`n[Step 4/5] Synchronizing Required Assets..." -ForegroundColor Yellow

# Copy vJoyInterface.dll if not present
$vJoyDllTarget = Join-Path $absOutputDir "vJoyInterface.dll"
$vJoyDllSource = Join-Path $repoRoot "src\HorizonCyclingBridge\vJoyInterface.dll"
if (Test-Path $vJoyDllSource) {
    Copy-Item -Path $vJoyDllSource -Destination $vJoyDllTarget -Force
    Write-Host "  -> vJoyInterface.dll synchronized." -ForegroundColor Green
}

# Copy map files
$mapTargetDir = Join-Path $absOutputDir "map"
$mapSourceDir = Join-Path $repoRoot "map"
if (Test-Path $mapSourceDir) {
    if (-not (Test-Path $mapTargetDir)) {
        New-Item -ItemType Directory -Path $mapTargetDir -Force | Out-Null
    }
    Get-ChildItem -Path $mapSourceDir -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $mapTargetDir -Force
    }
    Write-Host "  -> Map assets synchronized." -ForegroundColor Green
}

# Ensure README.txt exists in release directory
$readmeTarget = Join-Path $absOutputDir "README.txt"
if (-not (Test-Path $readmeTarget)) {
    $fallbackReadme = Join-Path $repoRoot "README.md"
    if (Test-Path $fallbackReadme) {
        Copy-Item -Path $fallbackReadme -Destination $readmeTarget -Force
        Write-Host "  -> README.txt created from README.md" -ForegroundColor Green
    }
}

# Synchronize allowed workouts: 01_FTP_Test, 02_SweetSpot_SST, 03_VO2Max_Intervals, 04_Endurance_Recovery
$workoutTargetDir = Join-Path $absOutputDir "workouts"
$workoutSourceDir = Join-Path $repoRoot "workouts"
$allowedWorkouts = @("01_FTP_Test", "02_SweetSpot_SST", "03_VO2Max_Intervals", "04_Endurance_Recovery")

if (-not (Test-Path $workoutTargetDir)) {
    New-Item -ItemType Directory -Path $workoutTargetDir -Force | Out-Null
}

# Copy allowed workouts from source if present
if (Test-Path $workoutSourceDir) {
    foreach ($category in $allowedWorkouts) {
        $catSrc = Join-Path $workoutSourceDir $category
        $catDst = Join-Path $workoutTargetDir $category
        if (Test-Path $catSrc) {
            if (-not (Test-Path $catDst)) {
                New-Item -ItemType Directory -Path $catDst -Force | Out-Null
            }
            Get-ChildItem -Path $catSrc -File | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination $catDst -Force
            }
        }
    }
}

# Remove any non-allowed workouts directories or loose files in workouts
Get-ChildItem -Path $workoutTargetDir -Directory | ForEach-Object {
    if ($allowedWorkouts -notcontains $_.Name) {
        Remove-Item -Path $_.FullName -Recurse -Force
        Write-Host "  -> Removed non-whitelisted workout directory: $($_.Name)" -ForegroundColor Yellow
    }
}
Get-ChildItem -Path $workoutTargetDir -File | ForEach-Object {
    Remove-Item -Path $_.FullName -Force
    Write-Host "  -> Removed loose file in workouts: $($_.Name)" -ForegroundColor Yellow
}
Write-Host "  -> Workouts filtered to allowed set: $($allowedWorkouts -join ', ')." -ForegroundColor Green

# Ensure logs/ folder is NOT included in the distribution package
$logsTargetDir = Join-Path $absOutputDir "logs"
if (Test-Path $logsTargetDir) {
    Remove-Item -Path $logsTargetDir -Recurse -Force
    Write-Host "  -> Excluded/removed 'logs/' directory from release package." -ForegroundColor Green
}

# Clean WebView2 runtime cache if present
$wv2CacheDir = Join-Path $absOutputDir "HorizonCyclingApp.exe.WebView2"
if (Test-Path $wv2CacheDir) {
    Remove-Item -Path $wv2CacheDir -Recurse -Force
    Write-Host "  -> Removed WebView2 runtime cache from release package." -ForegroundColor Green
}

# Reset config.json in release directory to default initial state
$configTarget = Join-Path $absOutputDir "config.json"
$defaultConfigJson = @'
{
  "PowerSourceType": 0,
  "PowerSourceMacAddress": 0,
  "PowerSourceName": "",
  "HeartRateSourceMacAddress": 0,
  "HeartRateSourceName": "",
  "HeartRateEnabled": true,
  "MaxHeartRate": 190,
  "RestingHeartRate": 60,
  "CadenceSourceMacAddress": 0,
  "CadenceSourceName": "",
  "CadenceSourceType": 0,
  "CadenceSourceEnabled": true,
  "PreferPowerMeterWatts": true,
  "DefaultMode": 2,
  "TrainerDifficulty": 0.5,
  "Ftp": 200,
  "RiderWeightKg": 70,
  "PedalBrakeEnabled": false,
  "FreerideOverlayEnabled": false,
  "ProfileRecentDistanceM": 200,
  "Language": "ja",
  "MapOriginX": 0,
  "MapOriginZ": 0,
  "MapScale": 0.263,
  "InvertZ": true
}
'@
[System.IO.File]::WriteAllText($configTarget, $defaultConfigJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "  -> Reset config.json to initial default state in release package." -ForegroundColor Green

# 5. Compress into ZIP Archive
Write-Host "`n[Step 5/5] Creating ZIP Archive..." -ForegroundColor Yellow
$absZipPath = Join-Path $repoRoot $ZipPath
$zipParent = Split-Path -Parent $absZipPath
if (-not (Test-Path $zipParent)) {
    New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
}

if (Test-Path $absZipPath) {
    Remove-Item -Path $absZipPath -Force
}

Compress-Archive -Path "$absOutputDir\*" -DestinationPath $absZipPath -Force
Write-Host "  -> ZIP archive generated successfully: $absZipPath" -ForegroundColor Green

# Summary
$exePath = Join-Path $absOutputDir "HorizonCyclingApp.exe"
$exeInfo = Get-Item $exePath
$zipInfo = Get-Item $absZipPath

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Packaging Completed Successfully!" -ForegroundColor Green
Write-Host " Executable : $($exeInfo.FullName)" -ForegroundColor White
Write-Host " Exe Date   : $($exeInfo.LastWriteTime.ToString('yyyy/MM/dd HH:mm:ss')) ($([Math]::Round($exeInfo.Length / 1KB)) KB)" -ForegroundColor White
Write-Host " ZIP File   : $($zipInfo.FullName)" -ForegroundColor White
Write-Host " ZIP Date   : $($zipInfo.LastWriteTime.ToString('yyyy/MM/dd HH:mm:ss')) ($([Math]::Round($zipInfo.Length / 1MB, 2)) MB)" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
