<#
.SYNOPSIS
    Bật gửi email thật cho Worker (SMTP), hoặc quay về ghi file (-UseFile).

.DESCRIPTION
    Lưu cấu hình vào User Secrets của TutorManagement.Worker, không ghi mật khẩu vào appsettings.
    Mặc định dùng Gmail: smtp.gmail.com, cổng 587, StartTls. Mật khẩu là App Password 16 ký tự
    (Google Account → Security → 2-Step Verification → App passwords), không phải mật khẩu Gmail.

.EXAMPLE
    .\tools\setup-smtp.ps1 -Sender ten.ban@gmail.com
.EXAMPLE
    .\tools\setup-smtp.ps1 -UseFile
#>
[CmdletBinding()]
param(
    [string]$Sender,
    [string]$SmtpHost = 'smtp.gmail.com',
    [int]$Port = 587,
    [ValidateSet('StartTls', 'SslOnConnect', 'StartTlsWhenAvailable', 'Auto', 'None')]
    [string]$Security = 'StartTls',
    [string]$FromName = 'Quản Lý Dạy Thêm',
    [switch]$UseFile
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$workerProject = Join-Path $backendRoot 'src\Hosts\TutorManagement.Worker\TutorManagement.Worker.csproj'

function Save-WorkerSecrets([hashtable]$values) {
    $values | ConvertTo-Json | & dotnet user-secrets set --project $workerProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Không lưu được User Secrets của Worker.' }
}

if ($UseFile) {
    Save-WorkerSecrets @{ 'EmailDelivery:Provider' = 'File' }
    Write-Host 'Đã chuyển Worker về ghi email ra file (.local\emails). Khởi động lại Worker.' -ForegroundColor Green
    return
}

if ([string]::IsNullOrWhiteSpace($Sender)) { $Sender = Read-Host 'Email gửi đi (ví dụ ten.ban@gmail.com)' }
$Sender = $Sender.Trim()
if ($Sender -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw "Email không hợp lệ: $Sender" }

$secure = Read-Host 'App Password (16 ký tự, có thể có dấu cách)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $password = ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)) -replace '\s', '' }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'Chưa nhập App Password.' }
if ($SmtpHost -eq 'smtp.gmail.com' -and $password.Length -ne 16) {
    Write-Warning 'App Password của Gmail thường có đúng 16 ký tự. Kiểm tra lại nếu gửi thử bị lỗi đăng nhập.'
}
if ($Port -eq 465 -and $Security -eq 'StartTls') { $Security = 'SslOnConnect' }

Save-WorkerSecrets @{
    'EmailDelivery:Provider'                    = 'Smtp'
    'EmailDelivery:MaximumConcurrency'          = '2'
    'EmailDelivery:Smtp:Host'                   = $SmtpHost
    'EmailDelivery:Smtp:Port'                   = "$Port"
    'EmailDelivery:Smtp:Security'               = $Security
    'EmailDelivery:Smtp:AuthenticationRequired' = 'true'
    'EmailDelivery:Smtp:Username'               = $Sender
    'EmailDelivery:Smtp:Password'               = $password
    'EmailDelivery:Smtp:FromAddress'            = $Sender
    'EmailDelivery:Smtp:FromName'               = $FromName
}
$password = $null

Write-Host "Đã lưu SMTP ($SmtpHost`:$Port, $Security) với người gửi $Sender." -ForegroundColor Green
Write-Host 'Khởi động lại Worker: log phải có "worker started with Smtp email delivery".'
Write-Host 'Gửi thử: đăng nhập admin → Quản trị nền tảng → Email hệ thống → Gửi email thử.'
