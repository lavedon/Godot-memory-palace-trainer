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
    & "$projectRoot/Tests/MakeTextureFixtures.ps1" -Directory $fixtures
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
    $imagesDatabase = Join-Path $fixtures 'room-images.db'
    $imagesHash = (Get-FileHash -LiteralPath $imagesDatabase).Hash
    $sharedDatabase = 'C:\tools\Data\palace.db'
    # Get-FileHash refuses files another program has open for writing (an open sqlite3 session,
    # for example), so the shared database is read while allowing writers. A real write still changes the hash.
    function Get-SharedHash([string]$Path) {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
    }
    $sharedHash = if (Test-Path -LiteralPath $sharedDatabase) { Get-SharedHash $sharedDatabase } else { $null }
    $summaries = [Collections.Generic.List[object]]::new()
    function Invoke-Case([string]$Name, [string[]]$UserArguments, [bool]$Loaded, [int]$Count = 0, [int]$Warnings = 0, [string]$ErrorContains = '', [hashtable]$Textures = @{}, [int]$TextureWarningCount = 0) {
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
        if (@($report.wallTextures.PSObject.Properties).Count -ne $Textures.Count -or @($report.textureWarnings).Count -ne $TextureWarningCount) { throw "Unexpected texture/warning count: $Name" }
        foreach ($wall in $Textures.Keys) {
            if ($report.wallTextures.$wall -ne [IO.Path]::GetFullPath($Textures[$wall])) { throw "Wrong $wall texture selected: $Name" }
        }
        if ($Export -and $Loaded) {
            $exportDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $application)) + [IO.Path]::DirectorySeparatorChar
            foreach ($moduleName in @('e_sqlite3.dll','coreclr.dll','fsrs_ffi.dll')) {
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
    Invoke-Case 'menu-without-room' @('--db',$fixtureDatabase) $false
    Invoke-Case 'invalid-id' @('--room','oops') $false -ErrorContains 'Invalid Room ID'
    Invoke-Case 'missing-file' @('--room','8','--db',(Join-Path $fixtures 'never-created.db')) $false -ErrorContains 'does not exist'
    Invoke-Case 'bad-schema' @('--room','8','--db',(Join-Path $fixtures 'bad-schema.db')) $false -ErrorContains 'schema'
    Invoke-Case 'corrupt-file' @('--room','8','--db',(Join-Path $fixtures 'corrupt.db')) $false -ErrorContains 'not a SQLite'
    Invoke-Case 'special-path' @('--room','8','--db',(Join-Path $fixtures "a;b 'quoted' database.db")) $true 26 0
    if ($sharedHash) {
        Invoke-Case 'shared-room8' @('--room','8') $true 26 0
        Invoke-Case 'shared-room7' @('--room','7') $true 26 3
        if ((Get-SharedHash $sharedDatabase) -ne $sharedHash) { throw 'Shared database bytes changed during verification.' }
    }
    $walls = Join-Path $fixtures 'wall images'
    $expectedWalls = @{}
    foreach ($wall in @('left','right','forward','back')) { $expectedWalls[$wall] = Join-Path $walls "$wall.png" }
    Invoke-Case 'texture-folder' @('--room','8','--db',$fixtureDatabase,'--room-textures',$walls) $true 26 0 -Textures $expectedWalls
    $rightPhoto = Join-Path $fixtures 'right photo.jpg'
    $explicitWalls = $expectedWalls.Clone()
    $explicitWalls['right'] = $rightPhoto
    Invoke-Case 'texture-explicit' @('--left',$expectedWalls.left,'--right',$rightPhoto,'--forward',$expectedWalls.forward,'--back',$expectedWalls.back,'--room','8','--db',$fixtureDatabase) $true 26 0 -Textures $explicitWalls
    $partialWalls = Join-Path $fixtures 'partial walls'
    Invoke-Case 'texture-partial' @('--room','8','--db',$fixtureDatabase,'--room-textures',$partialWalls) $true 26 0 -Textures @{left=(Join-Path $partialWalls 'left.png')}
    $override = Join-Path $fixtures 'left override.png'
    $overrideWalls = $expectedWalls.Clone()
    $overrideWalls['left'] = $override
    Invoke-Case 'texture-override' @('--room','8','--db',$fixtureDatabase,'--left',$override,'--room-textures',$walls) $true 26 0 -Textures $overrideWalls
    $missingOverrideWalls = $expectedWalls.Clone()
    $missingOverrideWalls.Remove('left')
    Invoke-Case 'texture-missing-override' @('--room','8','--db',$fixtureDatabase,'--room-textures',$walls,'--left',(Join-Path $fixtures 'absent.png')) $true 26 0 -Textures $missingOverrideWalls -TextureWarningCount 1
    Invoke-Case 'texture-missing-folder' @('--room','8','--db',$fixtureDatabase,'--room-textures',(Join-Path $fixtures 'absent folder'),'--right',$rightPhoto) $true 26 0 -Textures @{right=$rightPhoto} -TextureWarningCount 1
    Invoke-Case 'texture-invalid-image' @('--room','8','--db',$fixtureDatabase,'--left',(Join-Path $fixtures 'corrupt.png'),'--right',$rightPhoto) $true 26 0 -Textures @{right=$rightPhoto} -TextureWarningCount 1
    Invoke-Case 'texture-empty-folder' @('--room','8','--db',$fixtureDatabase,'--room-textures',(Join-Path $fixtures 'empty walls')) $true 26 0
    $roomImages = @{}
    foreach ($surface in @('left','right','forward','back','floor','ceiling')) { $roomImages[$surface] = Join-Path $fixtures "room images/$surface.png" }
    Invoke-Case 'database-six-images' @('--room','8','--db',$imagesDatabase) $true 26 0 -Textures $roomImages
    Invoke-Case 'database-partial-images' @('--room','20','--db',$imagesDatabase) $true 3 0 -Textures @{left=$roomImages.left;floor=$roomImages.floor}
    Invoke-Case 'database-empty-images' @('--room','7','--db',$imagesDatabase) $true 26 3
    Invoke-Case 'database-invalid-images' @('--room','21','--db',$imagesDatabase) $true 0 0 -TextureWarningCount 2
    Invoke-Case 'database-invalid-image-type' @('--room','22','--db',$imagesDatabase) $true 2 8 -Textures @{floor=$roomImages.floor} -TextureWarningCount 1
    $databaseOverride = $roomImages.Clone()
    $databaseOverride['left'] = $override
    Invoke-Case 'database-explicit-override' @('--room','8','--db',$imagesDatabase,'--left',$override) $true 26 0 -Textures $databaseOverride
    $databaseOverride['left'] = Join-Path $partialWalls 'left.png'
    Invoke-Case 'database-folder-override' @('--room','8','--db',$imagesDatabase,'--room-textures',$partialWalls) $true 26 0 -Textures $databaseOverride
    if ((Get-FileHash -LiteralPath $imagesDatabase).Hash -ne $imagesHash) { throw 'Image database bytes changed during runtime verification.' }
    if ((Get-FileHash -LiteralPath $fixtureDatabase).Hash -ne $fixtureHash) { throw 'Fixture database bytes changed during runtime verification.' }
    if (Test-Path -LiteralPath (Join-Path $fixtures 'never-created.db')) { throw 'Missing database was created.' }
    $summaries | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'summary.json')
    Write-Host "All $($summaries.Count) runtime cases passed. Database hashes unchanged. Results: $outputRoot"
} finally { Pop-Location }
