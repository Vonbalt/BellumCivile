$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = Read 'CourtObjectiveReports.cs'
foreach ($kind in @('Peace', 'Campaign', 'Subjugation')) {
    $behavior = Read "Behaviors/Court${kind}Agendas.cs"
    $texts += $behavior
    Check ($behavior.Contains('float actualMoodChange = 0;') -and $behavior.Contains('float before = agenda.Faction.Mood;') -and
        $behavior.Contains('actualMoodChange = agenda.Faction.Mood - before;')) "$kind measures observed mood rather than reporting nominal shock."
    Check ($behavior.IndexOf('TryClaimResult()') -lt $behavior.IndexOf('float actualMoodChange = 0;')) "$kind keeps the one-time outcome receipt before effects."
    Check ($behavior.Contains('CourtObjectiveReports.WithMood') -and $behavior.Contains('CourtObjectiveReports.Color(result)')) "$kind shares outcome formatting and colors."
    Check ($behavior.Contains('primaryKingdom: agenda.Realm') -and !$behavior.Contains('isMajorEvent: true')) "$kind preserves ordinary realm notification filtering."
    Check ($behavior.Contains('actual_mood={actualMoodChange}')) "$kind logs nominal and observed changes separately."
}
$helper = Read 'CourtObjectiveReports.cs'
Check ($helper.Contains('CourtSessionEventReports.ForTarget') -and $helper.Contains('CourtSessionEventReports.Change')) 'Objective reports reuse existing named-effect formatting.'
Check ($helper.Contains('{REPORT}\n{EFFECT}')) 'Narrative and effect remain on separate lines.'
$ids = @('BC_CourtObjectiveMoodUnchanged','BC_CourtObjectiveMoodUnit','BC_CourtSessionReport',
    'BC_CourtPeaceSucceeded','BC_CourtPeaceFailed','BC_CourtPeaceCancelled',
    'BC_CourtCampaignSucceeded','BC_CourtCampaignFailed','BC_CourtCampaignCancelled','BC_CourtCampaignAttacked',
    'BC_CourtClientageSucceeded','BC_CourtClientageFailed','BC_CourtClientageCancelled')
foreach ($id in $ids) {
    $nodes = @($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "One localized entry: $id"
    $fallback = [regex]::Match($texts, ('\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)')).Groups[1].Value.Replace('\n', "`n")
    Check ($nodes[0].text.Replace('\n', "`n") -eq $fallback) "Matching localized fallback: $id"
}
