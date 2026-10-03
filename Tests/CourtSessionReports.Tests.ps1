$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$events = Read 'CourtSessionEvents.cs'
$catalog = Read 'CourtActivityCatalog.cs'
$reports = Read 'CourtSessionEventReports.cs'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$ids = @([regex]::Matches($catalog, '"(BC_Court\w+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
Check ($ids.Count -eq 30) 'All 30 current positive and negative court events are covered.'
foreach ($id in $ids) {
    $match = [regex]::Match($reports, 'case "' + $id + '": text = "([^"]+)";')
    Check ($match.Success) "Narrative fallback exists: $id"
    $entry = @($xml.base.strings.string | Where-Object id -eq ($id + '_Report'))
    Check ($entry.Count -eq 1 -and $entry[0].text -ceq $match.Groups[1].Value) "Unique localization matches fallback: $id"
    $variables = @([regex]::Matches($match.Groups[1].Value, '\{([A-Z_]+)\}') | ForEach-Object { $_.Groups[1].Value })
    Check (@($variables | Where-Object { $_ -notin @('FACTION', 'REALM', 'RULER', 'TARGETS') }).Count -eq 0) "Report variables are supplied: $id"
    $label = [regex]::Match($catalog, '"' + $id + '", FactionType\.\w+, (?:true|false), "([^"]+)"')
    $agenda = @($xml.base.strings.string | Where-Object id -eq ($id + '_Agenda'))
    Check ($label.Success -and $agenda.Count -eq 1 -and $agenda[0].text -ceq $label.Groups[1].Value) "Agenda localization matches catalog: $id"
}
Check (!$events.Contains('BC_CourtSessionEvent}')) 'Generic ledger-style notification is no longer used.'
Check ($events.Contains('CourtSessionEventReports.Narrative(plan.EventId)')) 'Saved activity uses its own report without another random roll.'
Check ($events.Contains('beforeSecurity, town.Security') -and $events.Contains('beforeLoyalty, town.Loyalty') -and $events.Contains('beforeProsperity, town.Prosperity')) 'Settlement details use measured post-cap deltas.'
Check ($events.Contains('Change(beforeCohesion, army.Cohesion') -and $events.Contains('Change(beforeHearths, village.Hearth')) 'Army cohesion and village hearth details use measured deltas.'
Check ($events.Contains('d.AgendaText.ToString()')) 'Relation-memory context uses the translated agenda label, separate from the narrative report.'
Check ((Read 'CourtActivityRules.cs').Contains('positive ? mood >= 21 : mood <= -21') -and $events.Contains('Math.Min(3, targets.Count)')) 'Mood gate and maximum target count are unchanged.'
Check ($events.Contains('plan.Positive ? BellumNotificationColors.Success : BellumNotificationColors.Danger, primaryKingdom: realm')) 'Existing notification colors and realm filtering remain unchanged.'
'Source/localization contracts only; rendered campaign notifications require an in-game check.'
