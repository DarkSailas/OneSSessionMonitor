# PowerShell Скрипт остановки и удаления службы Windows OneSSessionMonitor
$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[ОШИБКА] Для удаления службы Windows запустите PowerShell от имени Администратора!" -ForegroundColor Red
    exit 1
}

$serviceName = "OneSSessionMonitor"

Write-Host "Остановка и удаление службы Windows: $serviceName..." -ForegroundColor Yellow

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Write-Host "[УСПЕХ] Служба $serviceName успешно удалена из системы." -ForegroundColor Green
} else {
    Write-Host "[ИНФО] Служба $serviceName не была установлена." -ForegroundColor Cyan
}