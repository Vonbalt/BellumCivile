$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$behavior = Read 'Behaviors/CourtAgendaBehavior.cs'
$preparation = Read 'Behaviors/CourtTermPreparation.cs'
$scheduler = Read 'CourtTermScheduler.cs'
$tick = $behavior.Substring($behavior.IndexOf('private void Tick()'),
    $behavior.IndexOf('private void OpenTerm(') - $behavior.IndexOf('private void Tick()'))
Check ($tick.Contains('realms.Where(QueueCourtTerm)') -and !$tick.Contains('OpenTerm(realm, manager, ideology)')) 'Daily coordination queues due realms instead of opening every court synchronously.'
$batch = $tick.IndexOf('PrepareQueuedCourtTerms(realms, manager, ideology);')
foreach ($daily in @('CheckMandate(realm)', 'Maintain(realm, renewal: true)', 'HereditaryLoyaltyBehavior.Instance?.Maintain(realm)',
    'ScanDecreeCases(realm, ideology)', 'MaintainCouncilVacancyPriority(realm)', 'ideology.TryLaunchCourtChallenge(faction, realm)', 'Advance(agenda, ideology)')) {
    Check ($tick.IndexOf($daily) -ge 0 -and $tick.IndexOf($daily) -lt $batch) "Daily political maintenance remains outside the preparation budget: $daily"
}
Check ($behavior.Contains('SyncTermPreparationData(dataStore);') -and $preparation.Contains('store.SyncData("BC_CourtPendingTerms"') -and
    $preparation.Contains('store.SyncData("BC_CourtLastPreparationDay"')) 'The pending queue and same-day guard are part of campaign persistence.'
Check ($preparation.Contains('CampaignTime.Days((float)term.EndDay)') -and !$preparation.Contains('CampaignTime.Now +')) 'The next official boundary comes from the calendar snapshot, not the processing day.'
Check ($preparation.Contains('if (next.ToDays > now) return false;') -and
    $preparation.Contains('if (_pendingCourtTerms.Count == 0) return;')) 'Idle days avoid settings reads for future terms and realm-index construction for an empty queue.'
Check ($behavior.Contains('CourtTermScheduler.SessionOffsets(term, CampaignTime.Now.ToDays') -and
    $behavior.Contains('agenda.FreezeSchedule(term.TermDays, term.DeliberationDays);') -and
    $behavior.Contains('FreezeTerm(CampaignTime.Now.ToDays, term.EndDay)')) 'Session, deliberation, and objective deadlines use the saved term snapshot.'
Check ($scheduler.Contains('DailyRealmBudget = 5;') -and $scheduler.Contains('Math.Ceiling((i + 1) / days)')) 'Five-realm batching includes the deadline-aware capacity safeguard.'
Check ($scheduler.Contains('OrderByDescending(p => p.RealmId == playerRealmId)') -and $scheduler.Contains('StringComparer.Ordinal')) 'Player priority and deterministic tie-breaking are explicit.'
Check ($preparation.Contains('TryGetCourtRuler(realm, out _, out _)') -and $preparation.Contains('term.HasPreparationWindow(now)')) 'Execution revalidates ruler availability and rejects newly doomed agendas.'
Check ($behavior.Contains('AwaitingTermAnnouncement(realm, faction, agenda)') -and
    $preparation.Contains('agenda?.IsUnopened == true || agenda?.IsFiled == true || agenda?.IsOngoingObjective == true')) 'Pending presentation preserves live business.'
$definer = Read 'BellumCivileSaveDefiner.cs'
Check ($definer.Contains('AddClassDefinition(typeof(CourtTermPreparationRecord), 145);') -and
    $definer.Contains('typeof(Dictionary<string, CourtTermPreparationRecord>)')) 'Pending term records and their dictionary have registered native save definitions.'
$ids = [regex]::Matches($definer, 'Add(?:Class|Enum)Definition\(typeof\([^)]+\),\s*(\d+)\)') |
    ForEach-Object { $_.Groups[1].Value }
Check (@($ids | Group-Object | Where-Object Count -gt 1).Count -eq 0) 'Save definition IDs remain unique across classes and enums.'
[xml]$catalog = Read 'ModuleData/Languages/EN/strings.xml'
Check (@($catalog.SelectNodes('//string[@id="BC_CourtAgendaAnnouncementPending"]')).Count -eq 1) 'The pending-announcement display has one localization entry.'
'Source and save-registration contracts passed; campaign performance still requires in-game profiling.'
