[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Database, [switch]$Check)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$databasePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Database)
$migrationArguments = @('--database', $databasePath)
if ($Check) { $migrationArguments += '--check' }
Push-Location $projectRoot
try {
    dotnet run --project Migrations/PalaceRoomViewer.Migrations.csproj -- @migrationArguments
    if ($LASTEXITCODE -ne 0) { throw 'Room image migration failed.' }
} finally { Pop-Location }
