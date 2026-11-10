param(
    [Parameter(Mandatory = $true)][string]$GameFolder,
    [switch]$NativeRetirementOnly
)

$ErrorActionPreference = 'Stop'
# Use .NET Framework and preload the requested game's DLL before the built harness
# can resolve the stable game's copied dependencies from its output directory.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $arguments = @('-NoProfile', '-File', $PSCommandPath, '-GameFolder', $GameFolder)
    if ($NativeRetirementOnly) { $arguments += '-NativeRetirementOnly' }
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" @arguments
    exit $LASTEXITCODE
}

$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'Tests\SuccessionEngineHarness\bin\Release\net472'
$nativePath = Join-Path $GameFolder 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'
if (!(Test-Path -LiteralPath $nativePath)) { throw "Game DLL not found: $nativePath" }
if (!(Test-Path -LiteralPath (Join-Path $build 'SuccessionEngineHarness.exe'))) {
    throw 'Build Tests/SuccessionEngineHarness in Release before running this test.'
}
$resolver = [ResolveEventHandler] {
    param($sender, $request)
    $name = ([Reflection.AssemblyName]$request.Name).Name + '.dll'
    if ($name -eq 'BellumCivile.dll') {
        return [Reflection.Assembly]::LoadFrom((Join-Path $build $name))
    }
    $path = [IO.Directory]::EnumerateFiles($GameFolder, $name, [IO.SearchOption]::AllDirectories) |
        Where-Object { $_ -like '*Win64_Shipping_Client*' } | Select-Object -First 1
    if ($path) { return [Reflection.Assembly]::LoadFrom($path) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $native = [Reflection.Assembly]::LoadFrom($nativePath)
    $tests = [Reflection.Assembly]::LoadFrom((Join-Path $build 'SuccessionEngineHarness.exe'))
    $testArguments = @($GameFolder, '--landless-realms')
    if ($NativeRetirementOnly) { $testArguments += '--native-retirement-only' }
    $invokeArguments = New-Object object[] 1
    $invokeArguments[0] = [string[]]$testArguments
    $code = $tests.EntryPoint.Invoke($null, $invokeArguments)
    Write-Output "Native campaign DLL: $($native.Location)"
    exit $code
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
