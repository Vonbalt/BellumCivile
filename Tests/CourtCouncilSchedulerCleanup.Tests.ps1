$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$queue = Read 'Behaviors/CouncilAppointmentDeliberationBehavior.cs'
$council = Read 'Behaviors/PrivyCouncilBehavior.cs'
$calendar = Read 'Behaviors/CourtAgendaBehavior.cs'
foreach ($old in @('OfficeMotionCooldownDays', '_officeMotionCooldownUntil', 'BellumCivile_CouncilDelib_Cooldowns', 'scheduledVoteDate ??', 'chargeInfluence')) {
    Check (!$queue.Contains($old)) "Retired appointment fallback absent: $old"
}
Check (!$council.Contains('TryScheduleAppointmentDecision')) 'Privy council no longer initiates or duplicates vacancy scheduling.'
Check ($calendar.Contains('MaintainCouncilVacancyPriority(realm);')) 'Court calendar still owns vacancy priority.'
Check ($queue.Contains('CampaignTime scheduledVoteDate, string agendaId') -and $queue.Contains('_pendingVoteDate[pendingKey] = scheduledVoteDate;')) 'New ballots require the calendar date and identity.'
Check ($queue.Contains('BC_CouncilDelib_PendingSettlements') -and $queue.Contains('ReconcileOfficeSettlements();')) 'Interrupted completion delivery persists for reconciliation.'
Check ($queue.Contains('DelayedVoteReliability.RegisterFailure') -and $queue.Contains('preserveCommitted: true')) 'Existing ballot retries and committed nominations remain.'
Check ($queue.Contains('_pendingCourtAgendaIds.ContainsKey(pendingKey) && CourtAgendaBehavior.Current == null')) 'Missing calendar pauses existing agenda ballots instead of cancelling them.'
Check ($council.Contains('AssignmentCooldownDays = 30f') -and $council.Contains('TryReviewAiAssignment(kingdom);')) 'Councillor assignment scheduling is independent and unchanged.'
Check ($council.Contains('PayDailyCouncilSalary(kingdom, record);') -and $council.Contains('TryDismissDisgracedOfficeHolder(kingdom, record, currentDay)')) 'Daily salary and disgrace consequences remain.'
