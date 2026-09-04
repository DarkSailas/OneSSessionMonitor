# PowerShell Скрипт установки и запуска службы Windows OneSSessionMonitor
$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[ОШИБКА] Для установки службы Windows запустите PowerShell от имени Администратора!" -ForegroundColor Red
    exit 1
}

$serviceName = "OneSSessionMonitor"
$serviceDisplayName = "OneSSessionMonitor"
$serviceDescription = "Служба автоматического поиска и завершения спящих / зависших сеансов 1С:Предприятие 8.3 через протокол RAS."

$currentDir = $PSScriptRoot
if ($currentDir.EndsWith("scripts")) {
    $currentDir = Split-Path -Parent $currentDir
}
Set-Location $currentDir

$exePath = Join-Path $currentDir "OneSSessionMonitor.Service.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[ОШИБКА] Исполняемый файл службы не найден: $exePath" -ForegroundColor Red
    exit 1
}

Write-Host "Установка службы Windows: $serviceName..." -ForegroundColor Yellow

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Остановка и удаление предыдущей версии службы..." -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName -BinaryPathName "`"$exePath`"" -DisplayName $serviceDisplayName -Description $serviceDescription -StartupType Automatic | Out-Null

Write-Host "Настройка параметров восстановления при сбоях (авто-перезапуск)..." -ForegroundColor Yellow
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Host "Запуск службы $serviceName..." -ForegroundColor Yellow
Start-Service -Name $serviceName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

$svc = Get-Service -Name $serviceName
if ($svc.Status -ne "Running") {
    sc.exe start $serviceName | Out-Null
    Start-Sleep -Seconds 3
    $svc = Get-Service -Name $serviceName
}

if ($svc.Status -eq "Running") {
    Write-Host "[УСПЕХ] Служба $serviceName успешно установлена и запущена! Текущий статус: $($svc.Status)" -ForegroundColor Green
    Write-Host "Журнал работы службы пишется в: $(Join-Path $currentDir 'logs')" -ForegroundColor Cyan
} else {
    Write-Host "[ОШИБКА] Служба не перешла в статус Running. Текущий статус: $($svc.Status)" -ForegroundColor Red
    Write-Host "Диагностика системного журнала Windows (последние ошибки):" -ForegroundColor Yellow
    try {
        Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Service Control Manager'; Level=2} -MaxEvents 3 -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "  -> [$($_.TimeCreated)] $($_.Message)" -ForegroundColor Red
        }
        Get-WinEvent -FilterHashtable @{LogName='Application'; Level=2} -MaxEvents 3 -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "  -> [$($_.TimeCreated)] $($_.Message)" -ForegroundColor Red
        }
    } catch { }
}