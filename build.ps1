# PowerShell Script for OneSSessionMonitor Build, Test & Distribution
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$publishServiceDir = Join-Path $root "publish\Service"
$publishGuiDir = Join-Path $root "publish\Gui"

Write-Host ""
Write-Host "  OneSSessionMonitor Build Script" -ForegroundColor Cyan
Write-Host "  ===============================" -ForegroundColor Cyan
Write-Host ""

# --- [1/5] Cleanup ---
Write-Host "[1/5] Stopping running processes..." -ForegroundColor Yellow

Get-Process | Where-Object Name -Match "OneSSessionMonitor" | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path $publishServiceDir) { Remove-Item $publishServiceDir -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $publishGuiDir) { Remove-Item $publishGuiDir -Recurse -Force -ErrorAction SilentlyContinue }

New-Item -ItemType Directory -Path $publishServiceDir -Force | Out-Null
New-Item -ItemType Directory -Path $publishGuiDir -Force | Out-Null
Write-Host "       OK" -ForegroundColor Green

# Проверка наличия .NET SDK на сервере
$sdkCheck = & dotnet --list-sdks 2>$null
if (-not $sdkCheck) {
    Write-Host ""
    Write-Host "[FATAL] .NET SDK is not installed on this server." -ForegroundColor Red
    Write-Host "Binaries are located at:" -ForegroundColor Yellow
    Write-Host "  - Windows Service: .\publish\Service\OneSSessionMonitor.Service.exe" -ForegroundColor Cyan
    Write-Host "  - GUI App:         .\publish\Gui\OneSSessionMonitor.Gui.exe" -ForegroundColor Cyan
    Write-Host ""
    exit 0
}

# --- [2/5] Restore ---
Write-Host "[2/5] dotnet restore..." -ForegroundColor Yellow
$slnPath = Join-Path $root "OneSSessionMonitor.slnx"
& dotnet restore $slnPath --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FATAL] dotnet restore failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "       OK" -ForegroundColor Green

# --- [3/5] Build ---
Write-Host "[3/5] dotnet build (Release)..." -ForegroundColor Yellow
& dotnet build $slnPath -c Release --no-restore --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FATAL] Build failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "       OK" -ForegroundColor Green

# --- [4/5] Tests ---
Write-Host "[4/5] dotnet test (Unit tests)..." -ForegroundColor Yellow
$testProject = Join-Path $root "tests\OneSSessionMonitor.Tests\OneSSessionMonitor.Tests.csproj"
& dotnet test $testProject -c Release --no-build --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[WARN] Some tests failed." -ForegroundColor Yellow
} else {
    Write-Host "       OK" -ForegroundColor Green
}

# --- [5/5] Publish Separate Service & GUI Packages ---
Write-Host "[5/5] Publishing Service to ./publish/Service and GUI to ./publish/Gui..." -ForegroundColor Yellow

$serviceProject = Join-Path $root "src\OneSSessionMonitor.Service\OneSSessionMonitor.Service.csproj"
$guiProject = Join-Path $root "src\OneSSessionMonitor.Gui\OneSSessionMonitor.Gui.csproj"

# Direct Publish Service (Framework-Dependent win-x64)
& dotnet publish $serviceProject -c Release -r win-x64 --no-self-contained -o $publishServiceDir --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FATAL] Service Publish failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Direct Publish GUI (Framework-Dependent win-x64)
& dotnet publish $guiProject -c Release -r win-x64 --no-self-contained -o $publishGuiDir --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FATAL] GUI Publish failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Copy Install and Update Scripts directly into Service publish root
Copy-Item -Path (Join-Path $root "scripts\INSTALL_SERVICE.ps1") -Destination $publishServiceDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $root "scripts\UNINSTALL_SERVICE.ps1") -Destination $publishServiceDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $root "scripts\UPDATE_SERVICE.ps1") -Destination $publishServiceDir -Force -ErrorAction SilentlyContinue

# Copy AppSettings files
Copy-Item -Path (Join-Path $root "src\OneSSessionMonitor.Service\appsettings.json") -Destination $publishServiceDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $root "src\OneSSessionMonitor.Gui\appsettings.json") -Destination $publishGuiDir -Force -ErrorAction SilentlyContinue

Write-Host "       OK" -ForegroundColor Green
Write-Host ""
Write-Host "  ==============================" -ForegroundColor Cyan
Write-Host "  BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host "  Service Output: $publishServiceDir" -ForegroundColor Cyan
Write-Host "  GUI Output:     $publishGuiDir" -ForegroundColor Cyan
Write-Host "  ==============================" -ForegroundColor Cyan
Write-Host ""
