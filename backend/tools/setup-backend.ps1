[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$backendRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $backendRoot 'TutorManagementSystem.slnx'

Set-Location -LiteralPath $backendRoot

function New-DotNetProject {
    param(
        [Parameter(Mandatory)]
        [string] $Template,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Output,

        [string[]] $AdditionalArguments = @()
    )

    $projectPath = Join-Path $backendRoot "$Output/$Name.csproj"

    if (Test-Path -LiteralPath $projectPath) {
        Write-Host "[skip] $Name"
        return
    }

    Write-Host "[create] $Name"
    & dotnet new $Template `
        --name $Name `
        --output $Output `
        --framework net10.0 `
        --no-restore `
        @AdditionalArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Could not create project $Name."
    }
}

function Add-ProjectReference {
    param(
        [Parameter(Mandatory)]
        [string] $Project,

        [Parameter(Mandatory)]
        [string[]] $References
    )

    & dotnet add $Project reference @References

    if ($LASTEXITCODE -ne 0) {
        throw "Could not add references to $Project."
    }
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    & dotnet new sln --name TutorManagementSystem
}

New-DotNetProject -Template webapi -Name TutorManagement.Api `
    -Output 'src/Hosts/TutorManagement.Api' `
    -AdditionalArguments @('--use-controllers', '--no-openapi')

New-DotNetProject -Template worker -Name TutorManagement.Worker `
    -Output 'src/Hosts/TutorManagement.Worker'

New-DotNetProject -Template console -Name TutorManagement.DbMigrator `
    -Output 'src/Hosts/TutorManagement.DbMigrator'

$buildingBlocks = @(
    'TutorManagement.SharedKernel',
    'TutorManagement.Application.Abstractions',
    'TutorManagement.Infrastructure',
    'TutorManagement.WebApi'
)

foreach ($projectName in $buildingBlocks) {
    New-DotNetProject -Template classlib -Name $projectName `
        -Output "src/BuildingBlocks/$projectName"
}

$modules = @('IdentityAccess', 'Organizations', 'Academics')
$layers = @('Domain', 'Application', 'Infrastructure', 'Contracts', 'Api')

foreach ($module in $modules) {
    foreach ($layer in $layers) {
        $projectName = "TutorManagement.Modules.$module.$layer"
        New-DotNetProject -Template classlib -Name $projectName `
            -Output "src/Modules/$module/$projectName"
    }
}

New-DotNetProject -Template xunit -Name TutorManagement.ArchitectureTests `
    -Output 'tests/ArchitectureTests'

New-DotNetProject -Template xunit -Name TutorManagement.IntegrationTests `
    -Output 'tests/IntegrationTests'

$projectPaths = @(
    Get-ChildItem `
        -LiteralPath (Join-Path $backendRoot 'src'), (Join-Path $backendRoot 'tests') `
        -Recurse `
        -File `
        -Filter '*.csproj' `
        -ErrorAction Stop |
    Select-Object -ExpandProperty FullName
)

& dotnet sln $solutionPath add $projectPaths

$sharedKernel = 'src/BuildingBlocks/TutorManagement.SharedKernel/TutorManagement.SharedKernel.csproj'
$applicationAbstractions = 'src/BuildingBlocks/TutorManagement.Application.Abstractions/TutorManagement.Application.Abstractions.csproj'
$infrastructure = 'src/BuildingBlocks/TutorManagement.Infrastructure/TutorManagement.Infrastructure.csproj'
$webApi = 'src/BuildingBlocks/TutorManagement.WebApi/TutorManagement.WebApi.csproj'

Add-ProjectReference -Project $applicationAbstractions -References @($sharedKernel)
Add-ProjectReference -Project $infrastructure -References @($sharedKernel, $applicationAbstractions)
Add-ProjectReference -Project $webApi -References @($sharedKernel)

foreach ($module in $modules) {
    $prefix = "src/Modules/$module/TutorManagement.Modules.$module"
    $domain = "$prefix.Domain/TutorManagement.Modules.$module.Domain.csproj"
    $application = "$prefix.Application/TutorManagement.Modules.$module.Application.csproj"
    $moduleInfrastructure = "$prefix.Infrastructure/TutorManagement.Modules.$module.Infrastructure.csproj"
    $contracts = "$prefix.Contracts/TutorManagement.Modules.$module.Contracts.csproj"
    $api = "$prefix.Api/TutorManagement.Modules.$module.Api.csproj"

    Add-ProjectReference -Project $domain -References @($sharedKernel)
    Add-ProjectReference -Project $application -References @($domain, $contracts, $applicationAbstractions)
    Add-ProjectReference -Project $moduleInfrastructure -References @($domain, $application, $infrastructure)
    Add-ProjectReference -Project $api -References @($application, $contracts, $webApi)
}

$apiHost = 'src/Hosts/TutorManagement.Api/TutorManagement.Api.csproj'
$workerHost = 'src/Hosts/TutorManagement.Worker/TutorManagement.Worker.csproj'
$migratorHost = 'src/Hosts/TutorManagement.DbMigrator/TutorManagement.DbMigrator.csproj'

$moduleApiProjects = @()
$moduleInfrastructureProjects = @()

foreach ($module in $modules) {
    $prefix = "src/Modules/$module/TutorManagement.Modules.$module"
    $moduleApiProjects += "$prefix.Api/TutorManagement.Modules.$module.Api.csproj"
    $moduleInfrastructureProjects += "$prefix.Infrastructure/TutorManagement.Modules.$module.Infrastructure.csproj"
}

$apiReferences = @($webApi, $infrastructure) + $moduleApiProjects + $moduleInfrastructureProjects
$workerReferences = @($infrastructure) + $moduleInfrastructureProjects
$migratorReferences = @($infrastructure) + $moduleInfrastructureProjects

Add-ProjectReference -Project $apiHost -References $apiReferences
Add-ProjectReference -Project $workerHost -References $workerReferences
Add-ProjectReference -Project $migratorHost -References $migratorReferences

Add-ProjectReference `
    -Project 'tests/IntegrationTests/TutorManagement.IntegrationTests.csproj' `
    -References @($apiHost)

Write-Host 'Backend project scaffold completed.'
