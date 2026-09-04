# PowerShell Скрипт перезапуска службы OneSSessionMonitor после обновления
$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[ОШИБКА] Для перезапуска службы Windows запустите PowerShell от имени Администратора!" -ForegroundColor Red
    exit 1
}

$serviceName = "OneSSessionMonitor"

Write-Host "Остановка службы $serviceName..." -ForegroundColor Yellow
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Запуск службы $serviceName..." -ForegroundColor Yellow
Start-Service -Name $serviceName

$status = (Get-Service -Name $serviceName).Status
Write-Host "[УСПЕХ] Служба $serviceName успешно перезапущена. Текущий статус: $status" -ForegroundColor Green