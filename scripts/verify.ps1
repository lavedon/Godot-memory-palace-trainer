[CmdletBinding()]
param([switch]$Export, [switch]$Visual)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6)
$outputRoot = Join-Path $projectRoot "artifacts/verification/$runName"
$fixtures = Join-Path $outputRoot 'fixtures'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/.gdignore') -Value ''
Push-Location $projectRoot
try {
    dotnet run --project Tests/PalaceRoomViewer.Tests.csproj -- $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Core acceptance checks failed.' }
    if ($Export) {
        $application = Join-Path $projectRoot 'artifacts/windows/PalaceRoomViewer.exe'
        if (-not (Test-Path -LiteralPath $application)) { & "$PSScriptRoot/export.ps1" }
    } else {
        $application = Join-Path $projectRoot '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
        if (-not (Test-Path -LiteralPath $application)) { & "$PSScriptRoot/bootstrap.ps1" }
        dotnet build PalaceRoomViewer.csproj --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        & $application --headless --path $projectRoot --editor --import *> (Join-Path $outputRoot 'import.log')
        if ($LASTEXITCODE -ne 0) { throw 'Godot import failed.' }
    }
    $fixtureDatabase = Join-Path $fixtures 'acceptance.db'
    $fixtureHash = (Get-FileHash -LiteralPath $fixtureDatabase).Hash
    $sharedDatabase = 'C:\tools\Data\palace.db'
    $sharedHash = if (Test-Path -LiteralPath $sharedDatabase) { (Get-FileHash -LiteralPath $sharedDatabase).Hash } else { $null }
    $summaries = [Collections.Generic.List[object]]::new()
    function Invoke-Case([string]$Name, [string[]]$UserArguments, [bool]$Loaded, [int]$Count = 0, [int]$Warnings = 0, [string]$ErrorContains = '') {
        $destination = Join-Path $outputRoot $Name
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $application
        $start.WorkingDirectory = $fixtures
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.Environment['PALACE_VIEWER_SMOKE_DIR'] = $destination
        if (-not $Visual) { $start.ArgumentList.Add('--headless') }
        if (-not $Export) { $start.ArgumentList.Add('--path'); $start.ArgumentList.Add($projectRoot) }
        if ($Export) {
            $start.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
            $start.Environment.Remove('DOTNET_ROOT') | Out-Null
            $start.Environment.Remove('DOTNET_ROOT_X64') | Out-Null
        }
        $start.ArgumentList.Add('--')
        foreach ($argument in $UserArguments) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw "Timed out: $Name" }
            $stdout.Result | Set-Content -LiteralPath (Join-Path $destination 'stdout.log')
            $stderr.Result | Set-Content -LiteralPath (Join-Path $destination 'stderr.log')
            if ($process.ExitCode -ne 0) { throw "Runtime failed: $Name. See $destination" }
        } finally { $process.Dispose() }
        $report = Get-Content -LiteralPath (Join-Path $destination 'result.json') -Raw | ConvertFrom-Json
        if (-not $report.passed -or $report.loaded -ne $Loaded) { throw "Unexpected load result: $Name" }
        if ($Loaded -and (@($report.loci).Count -ne $Count -or @($report.warnings).Count -ne $Warnings)) { throw "Unexpected placement/warning count: $Name" }
        if ($ErrorContains -and $report.error -notlike "*$ErrorContains*") { throw "Unexpected error text: $Name" }
        if ($Export -and $Loaded) {
            $exportDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $application)) + [IO.Path]::DirectorySeparatorChar
            foreach ($moduleName in @('e_sqlite3.dll','coreclr.dll')) {
                $module = @($report.nativeModules | Where-Object { [IO.Path]::GetFileName($_) -eq $moduleName })
                if ($module.Count -ne 1 -or -not $module[0].StartsWith($exportDirectory,[StringComparison]::OrdinalIgnoreCase)) {
                    throw "$Name did not load $moduleName from its own export directory."
                }
            }
        }
        $summaries.Add([pscustomobject]@{case=$Name;passed=$true;checks=@($report.checks).Count;loaded=$Loaded})
        Write-Host "PASS runtime $Name ($(@($report.checks).Count) checks)"
    }
    Invoke-Case 'full' @('--room','8','--db',$fixtureDatabase) $true 26 0
    Invoke-Case 'overflow' @('--room','7','--db',$fixtureDatabase) $true 26 3
    Invoke-Case 'gaps' @('--room','20','--db',$fixtureDatabase) $true 3 0
    Invoke-Case 'empty' @('--room','21','--db',$fixtureDatabase) $true 0 0
    Invoke-Case 'invalid-positions' @('--room','22','--db',$fixtureDatabase) $true 2 8
    Invoke-Case 'long-text' @('--room','23','--db',$fixtureDatabase) $true 3 0
    Invoke-Case 'missing-room' @('--room','9999','--db',$fixtureDatabase) $false -ErrorContains 'does not exist'
    Invoke-Case 'missing-arguments' @() $false -ErrorContains '--room'
    Invoke-Case 'invalid-id' @('--room','oops') $false -ErrorContains 'Invalid Room ID'
    Invoke-Case 'missing-file' @('--room','8','--db',(Join-Path $fixtures 'never-created.db')) $false -ErrorContains 'does not exist'
    Invoke-Case 'bad-schema' @('--room','8','--db',(Join-Path $fixtures 'bad-schema.db')) $false -ErrorContains 'schema'
    Invoke-Case 'corrupt-file' @('--room','8','--db',(Join-Path $fixtures 'corrupt.db')) $false -ErrorContains 'not a SQLite'
    Invoke-Case 'special-path' @('--room','8','--db',(Join-Path $fixtures "a;b 'quoted' database.db")) $true 26 0
    if ($sharedHash) {
        Invoke-Case 'shared-room8' @('--room','8') $true 26 0
        Invoke-Case 'shared-room7' @('--room','7') $true 26 3
        if ((Get-FileHash -LiteralPath $sharedDatabase).Hash -ne $sharedHash) { throw 'Shared database bytes changed during verification.' }
    }
    if ((Get-FileHash -LiteralPath $fixtureDatabase).Hash -ne $fixtureHash) { throw 'Fixture database bytes changed during runtime verification.' }
    if (Test-Path -LiteralPath (Join-Path $fixtures 'never-created.db')) { throw 'Missing database was created.' }
    $summaries | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'summary.json')
    Write-Host "All $($summaries.Count) runtime cases passed. Database hashes unchanged. Results: $outputRoot"
} finally { Pop-Location }
