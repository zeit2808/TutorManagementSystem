[CmdletBinding()]
param(
    [string]$PostgresHome = 'C:\Program Files\PostgreSQL\18',
    [string]$DatabaseHost = 'localhost',
    [ValidateRange(1, 65535)]
    [int]$Port = 5432,
    [ValidatePattern('^[a-z_][a-z0-9_]*$')]
    [string]$AdminUser = 'postgres',
    [ValidatePattern('^[a-z_][a-z0-9_]*$')]
    [string]$AppUser = 'tutor_app',
    [ValidatePattern('^[a-z_][a-z0-9_]*$')]
    [string]$Database = 'tutor_management',
    [switch]$SkipMigration
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$psqlPath = Join-Path $PostgresHome 'bin\psql.exe'
$pgIsReadyPath = Join-Path $PostgresHome 'bin\pg_isready.exe'

if (-not (Test-Path -LiteralPath $psqlPath)) {
    $psqlCommand = Get-Command psql -ErrorAction SilentlyContinue
    if ($null -eq $psqlCommand) {
        throw "psql.exe was not found. Pass -PostgresHome with the PostgreSQL installation directory."
    }

    $psqlPath = $psqlCommand.Source
    $pgIsReadyPath = Join-Path (Split-Path -Parent $psqlPath) 'pg_isready.exe'
}

if (-not (Test-Path -LiteralPath $pgIsReadyPath)) {
    throw "pg_isready.exe was not found at '$pgIsReadyPath'."
}

& $pgIsReadyPath -h $DatabaseHost -p $Port
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL is not accepting connections at ${DatabaseHost}:$Port. Check the Windows service first."
}

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

$adminPasswordSecure = Read-Host "Password for PostgreSQL administrator '$AdminUser'" -AsSecureString
$appPasswordSecure = Read-Host "New password for application user '$AppUser'" -AsSecureString
$appPasswordConfirmationSecure = Read-Host 'Confirm the application user password' -AsSecureString

$adminPassword = ConvertTo-PlainText $adminPasswordSecure
$appPassword = ConvertTo-PlainText $appPasswordSecure
$appPasswordConfirmation = ConvertTo-PlainText $appPasswordConfirmationSecure

if ([string]::IsNullOrWhiteSpace($appPassword)) {
    throw 'The application user password cannot be empty.'
}

if ($appPassword -cne $appPasswordConfirmation) {
    throw 'The application user passwords do not match.'
}

$escapedAppPassword = $appPassword.Replace("'", "''")
$bootstrapSql = @"
\set ON_ERROR_STOP on
DO `$setup`$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '$AppUser') THEN
        ALTER ROLE "$AppUser" WITH LOGIN PASSWORD '$escapedAppPassword';
    ELSE
        CREATE ROLE "$AppUser" WITH LOGIN PASSWORD '$escapedAppPassword';
    END IF;
END
`$setup`$;

SELECT 'CREATE DATABASE "$Database" OWNER "$AppUser"'
WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_database WHERE datname = '$Database')
\gexec

ALTER DATABASE "$Database" OWNER TO "$AppUser";
"@

$previousPgPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$previousDotnetEnvironment = [Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Process')

try {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $adminPassword, 'Process')
    $bootstrapSql | & $psqlPath `
        -X `
        -v ON_ERROR_STOP=1 `
        -h $DatabaseHost `
        -p $Port `
        -U $AdminUser `
        -d postgres

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the role/database. Check the administrator password and PostgreSQL permissions.'
    }

    $escapedConnectionPassword = $appPassword.Replace('"', '""')
    $connectionString = "Host=$DatabaseHost;Port=$Port;Database=$Database;Username=$AppUser;Password=`"$escapedConnectionPassword`";Include Error Detail=false"
    $secretProjects = @(
        'src\Hosts\TutorManagement.Api\TutorManagement.Api.csproj',
        'src\Hosts\TutorManagement.Worker\TutorManagement.Worker.csproj',
        'src\Hosts\TutorManagement.DbMigrator\TutorManagement.DbMigrator.csproj'
    )

    foreach ($project in $secretProjects) {
        $projectPath = Join-Path $backendRoot $project
        & dotnet user-secrets set `
            'ConnectionStrings:MainDatabase' `
            $connectionString `
            --project $projectPath

        if ($LASTEXITCODE -ne 0) {
            throw "Could not save User Secrets for '$project'."
        }
    }

    [Environment]::SetEnvironmentVariable('PGPASSWORD', $appPassword, 'Process')
    & $psqlPath `
        -X `
        -v ON_ERROR_STOP=1 `
        -h $DatabaseHost `
        -p $Port `
        -U $AppUser `
        -d $Database `
        -c 'SELECT current_database() AS database, current_user AS app_user;'

    if ($LASTEXITCODE -ne 0) {
        throw 'The role/database was created, but the application user connection check failed.'
    }

    if (-not $SkipMigration) {
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
        Push-Location $backendRoot
        try {
            & dotnet tool restore
            if ($LASTEXITCODE -ne 0) {
                throw 'dotnet tool restore failed.'
            }

            & dotnet run --project '.\src\Hosts\TutorManagement.DbMigrator'
            if ($LASTEXITCODE -ne 0) {
                throw 'DbMigrator failed.'
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host ''
    Write-Host 'Local PostgreSQL setup completed.' -ForegroundColor Green
    Write-Host "Server:   ${DatabaseHost}:$Port"
    Write-Host "Database: $Database"
    Write-Host "User:     $AppUser"
    Write-Host 'The connection string was saved in .NET User Secrets for the API, Worker, and DbMigrator.'
}
finally {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $previousPgPassword, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', $previousDotnetEnvironment, 'Process')
    $adminPassword = $null
    $appPassword = $null
    $appPasswordConfirmation = $null
    $escapedAppPassword = $null
    $escapedConnectionPassword = $null
    $connectionString = $null
    $bootstrapSql = $null
}
