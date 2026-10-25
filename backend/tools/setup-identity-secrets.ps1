[CmdletBinding()]
param(
    [string]$OwnerUsername = 'admin',
    [switch]$SkipMigration
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $backendRoot 'src\Hosts\TutorManagement.Api\TutorManagement.Api.csproj'
$migratorProject = Join-Path $backendRoot 'src\Hosts\TutorManagement.DbMigrator\TutorManagement.DbMigrator.csproj'

function ConvertTo-PlainText {
    param([Parameter(Mandatory)][Security.SecureString]$SecureValue)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

$passwordSecure = Read-Host "Password for development owner '$OwnerUsername'" -AsSecureString
$passwordConfirmationSecure = Read-Host 'Confirm the development owner password' -AsSecureString
$password = ConvertTo-PlainText $passwordSecure
$passwordConfirmation = ConvertTo-PlainText $passwordConfirmationSecure

if ([string]::IsNullOrWhiteSpace($password) -or $password.Length -lt 12) {
    throw 'The development owner password must contain at least 12 characters.'
}

if ($password -cne $passwordConfirmation) {
    throw 'The development owner passwords do not match.'
}

$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 64

try {
    $randomNumberGenerator.GetBytes($keyBytes)
    $jwtSigningKey = [Convert]::ToBase64String($keyBytes)

    @{
        'Jwt:SigningKey' = $jwtSigningKey
    } |
        ConvertTo-Json |
        & dotnet user-secrets set --project $apiProject

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not save the JWT signing key.'
    }

    @{
        'Seed:OwnerUsername' = $OwnerUsername
        'Seed:OwnerPassword' = $password
    } |
        ConvertTo-Json |
        & dotnet user-secrets set --project $migratorProject

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not save the development owner credentials.'
    }

    if (-not $SkipMigration) {
        $previousDotnetEnvironment = [Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
        Push-Location $backendRoot
        try {
            & dotnet run --project '.\src\Hosts\TutorManagement.DbMigrator'
            if ($LASTEXITCODE -ne 0) {
                throw 'DbMigrator failed.'
            }
        }
        finally {
            Pop-Location
            [Environment]::SetEnvironmentVariable(
                'DOTNET_ENVIRONMENT',
                $previousDotnetEnvironment,
                'Process')
        }
    }

    Write-Host ''
    Write-Host 'Identity development secrets configured.' -ForegroundColor Green
    Write-Host "Owner username: $OwnerUsername"
    Write-Host 'JWT signing key and owner password are stored outside the repository.'
}
finally {
    $randomNumberGenerator.Dispose()
    [Array]::Clear($keyBytes, 0, $keyBytes.Length)
    $jwtSigningKey = $null
    $password = $null
    $passwordConfirmation = $null
}
