$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/GamePath.ps1"

function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Output "PASS: $Message"
}

function Read-BuildGameFolder([string]$Project, [string]$ExplicitPath) {
    $arguments = @('msbuild', (Join-Path $root $Project), '-nologo', '-getProperty:GameFolder')
    if ($ExplicitPath) { $arguments += "-p:GameFolder=$ExplicitPath" }
    $result = & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Could not evaluate GameFolder for $Project" }
    return ($result -join "`n").Trim()
}

$savedGameFolder = $env:GameFolder
try {
    $default = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Steam\steamapps\common\Mount & Blade II Bannerlord'
    $environmentPath = Join-Path ([IO.Path]::GetTempPath()) 'Bellum Environment Install'
    $explicitPath = Join-Path ([IO.Path]::GetTempPath()) 'Bellum Test Install & Overrides'
    $moduleData = Join-Path $explicitPath 'Custom Module Data'

    $env:GameFolder = $environmentPath
    Check ((Resolve-BellumGameFolder) -eq $environmentPath) 'PowerShell uses the GameFolder environment variable.'
    Check ((Resolve-BellumGameFolder $explicitPath) -eq $explicitPath) 'Explicit game paths override the environment and preserve spaces.'
    Check ((Resolve-BellumModuleData) -eq (Join-Path $environmentPath 'Modules\SandBox\ModuleData')) 'ModuleData follows the configured installation.'
    Check ((Resolve-BellumModuleData $moduleData) -eq $moduleData) 'Explicit ModuleData overrides the installation default.'

    $env:GameFolder = $null
    Check ((Resolve-BellumGameFolder) -eq $default) 'The default uses the Windows Program Files location.'
    Check ((Resolve-BellumModuleData) -eq (Join-Path $default 'Modules\SandBox\ModuleData')) 'ModuleData follows the standard Steam default.'

    foreach ($project in @('BellumCivile.csproj', 'BellumCivile.NavalDLCPatch/BellumCivile.NavalDLCPatch.csproj', 'Tests/SuccessionEngineHarness/SuccessionEngineHarness.csproj')) {
        $env:GameFolder = $null
        Check ((Read-BuildGameFolder $project) -eq $default) "$project uses the shared default."
        $env:GameFolder = $environmentPath
        Check ((Read-BuildGameFolder $project) -eq $environmentPath) "$project respects the environment override."
        Check ((Read-BuildGameFolder $project $explicitPath) -eq $explicitPath) "$project gives explicit MSBuild properties priority."
    }
}
finally {
    $env:GameFolder = $savedGameFolder
}
