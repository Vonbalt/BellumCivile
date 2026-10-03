$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'Fixtures/CourtLegacySweep.json') | ConvertFrom-Json
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
Push-Location $root
try {
    $files = @(rg --files -g '*.cs' -g '*.xml' -g '!Tests/**' -g '!**/obj/**' -g '!**/bin/**' -g '!ModuleData/Languages/**')
    if ($LASTEXITCODE -ne 0) { throw 'Source enumeration failed' }
    $runtime = ($files | ForEach-Object { Get-Content -Raw -LiteralPath $_ }) -join "`n"
    foreach ($name in @($manifest.removedHelpers) + @($manifest.removedConstants)) {
        Check (!([regex]::IsMatch($runtime, '\b' + [regex]::Escape($name) + '\b'))) "No runtime reference to retired symbol: $name"
    }
    [xml]$xml = Get-Content -Raw ModuleData/Languages/EN/strings.xml
    $ids = @($xml.base.strings.string | ForEach-Object { [string]$_.id })
    foreach ($id in $manifest.removedLocalizationIds) {
        Check (!$runtime.Contains($id) -and $ids -notcontains $id) "Unused legacy text retired: $id"
    }
    Check (!$runtime.Contains('"BC_CourtAgenda_"') -and !$runtime.Contains('"{=BC_CourtAgenda_"')) 'Legacy localization family is not built from a bare dynamic prefix.'
    $live = @([regex]::Matches($runtime, '\{=(BC_CourtAgenda_[A-Za-z0-9_]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($id in $live) { Check (@($ids | Where-Object { $_ -eq $id }).Count -eq 1) "Live shared court label retained uniquely: $id" }
    Check ($live -contains 'BC_CourtAgenda_Approve' -and $live -contains 'BC_CourtAgenda_Substitute') 'Current picker retains shared approve/substitute wording.'
    Check ((Get-Content -Raw Behaviors/CourtAgendaPlayerFlow.cs).Contains('CalculateRebellionSuppressionCost')) 'Current restraint still uses its shared cost helper.'
    Check ((Get-Content -Raw CourtCouncilObjectiveSource.cs).Contains('BellumCivileOptions.CourtTermDays')) 'Current appointment source still uses term-based tenure.'
} finally { Pop-Location }
