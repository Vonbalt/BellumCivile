$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$queue = Read 'Behaviors/CouncilAppointmentDeliberationBehavior.cs'
$decision = Read 'PrivyCouncilAppointmentDecision.cs'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
$council = Read 'Behaviors/CourtCouncilAgendas.cs'
$source = Read 'CourtCouncilObjectiveSource.cs'
Check ($agenda.Contains('selector.Register(new CourtCouncilObjectiveSource());')) 'Council motions participate in the ordinary selector.'
Check ($council.Contains('agenda.VoteDate, agenda.CouncilMotionId, out _')) 'Filing passes the frozen vote date and unique saved identity.'
Check ($queue.Contains('decision.CourtAgendaId = agendaId')) 'Native decisions retain their agenda link after the queue entry is removed.'
Check ($queue.Contains('_pendingCreatedDay[pendingKey] = (float)scheduledVoteDate.ToDays;')) 'Long saved deliberation windows do not expire before the promised ballot date.'
Check ($queue.Contains('CourtAgendaBehavior.Current?.ValidateCouncilProceeding(agendaId, kingdom, office, proposer) != true')) 'Queued agenda ballots are revalidated before legacy sponsor fallback.'
Check ($decision.Contains('.ValidateCouncilProceeding(CourtAgendaId, Kingdom, _office, ProposerClan) == true')) 'An invalidated agenda cannot proceed through a native ballot.'
Check ($council.Contains('decision.AppliedCandidate == agenda.PreferredCouncilCandidate')) 'Success requires the preferred house, not just any appointment.'
Check (!$council.Contains('CourtAgendaSuccessShock')) 'Council results do not duplicate the appointment decision mood effects.'
Check ($council.Contains('Cancel(agenda, "queue_rejected_after_payment")')) 'Rejected filing follows the existing technical refund path.'
Check ($queue.Contains('.Where(d => d.CourtAgendaId == agendaId)')) 'Cancellation only removes ballots with the exact agenda identity.'
Check ($council.Contains('record.VacancyStartedDay <= day')) 'A genuinely newer vacancy releases the same-office restriction.'
Check ($source.Contains('incumbent == null ? "fill" : "replace"')) 'Fill and replacement share one council category.'
Check ($source.Contains('GetOfficeTenureDays(facts.Realm, office) >= BellumCivileOptions.CourtTermDays')) 'Ordinary replacement tenure follows the court term instead of a fixed 365 days.'
Check ($council.Contains('agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree')) 'Vacancy priority respects royal decree business.'
Check ($queue.Contains('CourtAgendaBehavior.Current?.IsCouncilOfficeSettled(kingdom, office)')) 'Office cooldown queries use only term-owned restrictions.'
Check ($agenda.Contains('_objectiveSelector.SelectBatch(context,')) 'Term opening uses batch arbitration.'
Check ($queue.Contains('_pendingVoteDate[pendingKey] = scheduledVoteDate;')) 'New filing requires and preserves the frozen agenda date.'
Check (!$queue.Contains('if (!scheduledVoteDate.HasValue')) 'NPC votes have no immediate unscheduled fallback.'
Check ($queue.Contains('"court_agenda", out failureReason, voteDate, agendaId')) 'The agenda entry point passes its date to the existing queue.'
Check ($decision.Contains('if (_outcomeAttempted) return;')) 'A repeated outcome callback cannot repeat the appointment.'
Check ($decision.Contains('council.GetOfficeHolder(Kingdom, _office) != outcome.CandidateClan')) 'An appointment receipt requires verified office ownership.'
Check ($decision.Contains('if (_aftermathApplied || _appliedCandidate != chosen.CandidateClan')) 'Unverified or repeated outcomes cannot apply Bellum political aftermath.'
Check ($queue.Contains('if (appointment.AppliedCandidate != null) CompleteAppointmentLifecycle(key);')) 'Failed outcomes do not count as settled offices in the conclusion callback.'
foreach ($field in @('_appliedCandidate', '_aftermathApplied', '_outcomeAttempted')) {
    Check ($decision -match ('\[SaveableField\(\d+\)\] private \w+ ' + $field + ';')) "Outcome receipt persists: $field"
}
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_CourtCouncilCandidateObjective', 'BC_CourtCouncilTermOnly', 'BC_CourtCouncilNominationHint')) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique council localization: $id"
}
'29 source/XML contracts passed. Campaign votes and save roundtrips still require live tests.'
