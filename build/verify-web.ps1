[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'Tool restore failed.' }
    $expected = (Get-Content .config/dotnet-tools.json -Raw | ConvertFrom-Json).tools.'dotnet-ef'.version
    $actual = (dotnet tool run dotnet-ef --version) -join ' '
    if ($LASTEXITCODE -or $actual -notmatch ([regex]::Escape($expected) + '$')) { throw 'EF tool version does not match the repository manifest.' }
    $project = [xml](Get-Content src/Quickfire.Blazor/Quickfire.Blazor.csproj -Raw)
    $efPackages = $project.Project.ItemGroup.PackageReference | Where-Object { $_.Include -like 'Microsoft.EntityFrameworkCore.*' }
    if ($efPackages | Where-Object { $_.Version -ne $expected }) { throw 'EF package/tool versions do not match.' }
    dotnet restore Quickfire.Web.slnf -p:NuGetAudit=true -p:NuGetAuditMode=all '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'
    if ($LASTEXITCODE) { throw 'Restore or dependency audit failed.' }
    dotnet build Quickfire.Web.slnf --no-restore -c Release
    if ($LASTEXITCODE) { throw 'Web build failed.' }
    dotnet test Quickfire.Web.slnf --no-build --no-restore -c Release --logger 'trx;LogFileName=foundation.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE) { throw 'Foundation tests failed.' }
    dotnet ef migrations has-pending-model-changes --project src/Quickfire.Blazor --no-build --configuration Release
    if ($LASTEXITCODE) { throw 'SQLite model and migration snapshot differ.' }
} finally { Pop-Location }
