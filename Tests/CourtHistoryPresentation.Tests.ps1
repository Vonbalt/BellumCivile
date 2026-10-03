$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$ui = Get-Content -Raw (Join-Path $root 'UI/FactionsWindowVM.cs')
$history = Get-Content -Raw (Join-Path $root 'Behaviors/CourtReactionHistory.cs')
$catalog = Get-Content -Raw (Join-Path $root 'CourtMoodPresentation.cs')
$shocks = Get-Content -Raw (Join-Path $root 'Behaviors/IdeologyEventShockBehavior.cs')
[xml]$xml = Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml')
$strings = @{}
foreach ($s in $xml.base.strings.string) {
    if ($strings.ContainsKey($s.id)) { throw "Duplicate localization ID: $($s.id)" }
    $strings[$s.id] = $s.text
}
foreach ($match in [regex]::Matches($catalog, '\["([^"]+)"\] = \("([^"]*)", "([^"]*)"\)')) {
    $id = $match.Groups[1].Value
    Check ($strings["BC_MoodName_$id"] -ceq $match.Groups[2].Value) "Localized history name matches fallback: $id"
    Check ($strings["BC_MoodHint_$id"] -ceq $match.Groups[3].Value) "Localized explanation matches fallback: $id"
}
Check (!$ui.Contains('BC_CourtRecentEvent')) 'No generic placeholder event tooltip remains.'
Check ($ui.Contains('RecentResults(selectedBackendFaction)')) 'UI enumerates retained reaction history, not just the latest agenda.'
Check ($ui.Contains('HasRecentLordImprisoned')) 'Captured nobles have their missing history row.'
Check ($ui.Contains('BC_CourtMoodContribution')) 'Standing conditions explicitly identify target-mood contribution.'
Check ($history.Contains('CampaignTime.Days(Math.Abs(shock))')) 'History duration uses reaction magnitude, not a fixed window.'
Check ($history.Contains('shock == 0') -and $history.Contains('_recentResults.Contains(agenda)')) 'Zero and duplicate results cannot create or extend history.'
Check (!$shocks.Contains('_recentCouncilAppointments.Remove(key)')) 'Other council outcomes do not erase appointment history early.'
Check ($shocks.Contains('return HasRecentMaterialEvent(kingdom, "VillageRaids")')) 'Raid display uses applied-shock history instead of the counting window.'
foreach ($name in @('CourtAgendaBehavior', 'CourtExecutiveAgendas', 'CourtCampaignAgendas', 'CourtClaimAgendas', 'CourtDynasticAgendas', 'CourtPeaceAgendas', 'CourtRallyAgendas', 'CourtSubjugationAgendas', 'CourtTitleGrantAgendas', 'CourtTradeAgendas')) {
    $source = Get-Content -Raw (Join-Path $root "Behaviors/$name.cs")
    Check ($source.Contains('RecordResultHistory(')) "Result producer uses retained history: $name"
    Check (!$source.Contains('ResultVisibleUntil =') -and !$source.Contains('ResultVisibleUntil=')) "Result duration is centralized: $name"
}
