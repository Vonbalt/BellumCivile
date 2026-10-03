$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
foreach ($path in @('FactionObject.cs', 'Behaviors/CivilWarResolutionBehavior.cs', 'Behaviors/ExiledClanRecoveryBehavior.cs',
    'Behaviors/RebellionSummaryBehavior.cs', 'BellumCivileConstants.cs', 'LeaveFactionBarterable.cs',
    'NotificationHelper.cs', 'CheatCommands.cs', 'UI/FactionsWindowVM.cs')) {
    Check ((Read $path) -notmatch 'FiefRedistribution|fief_redistribution') "Retired faction removed from $path"
}
$faction = Read 'FactionObject.cs'
foreach ($helper in @('CalculateDesiredFiefs', 'CaptureCivilWarStartFiefCounts', 'CaptureCivilWarStartInfluence',
    'ExportCivilWarStartFiefSnapshot', 'MoveClanToKingdomPreservingCivilWarInfluence')) {
    Check ($faction.Contains($helper)) "Shared live helper retained: $helper"
}
Check ((Read 'Behaviors/SuccessionChallengeWar.cs').Contains('FactionType.InstallRuler')) 'Hereditary challenges still use Install Ruler, not retired redistribution.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
Check (@($strings.base.strings.string | Where-Object { $_.id -match 'FiefRedistribution|^BC_Demand_Fiefs$|^BC_DemandDesc_Fief$|^BC_FacName_Fief$|^BC_FacName_Revolt$|^BC_Resolution_FiefSeized$' }).Count -eq 0) 'Retired-only localized messages removed.'
