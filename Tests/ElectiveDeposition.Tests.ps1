$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$deposition = Read 'Behaviors/ElectiveDeposition.cs'
$war = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$ballot = Read 'Behaviors/ElectiveSuccessionBehavior.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$court = Read 'Behaviors/CourtAgendaBehavior.cs'
Check (([regex]::Matches($war, 'BeginDeposition\(')).Count -eq 4) 'Concession, normal victory, restored victory and legacy queued elections enter deliberation.'
Check ($deposition.IndexOf('record.ScheduleDeposition') -lt $deposition.IndexOf('TryPrepareInterim(record)')) 'Deposition is saved before caretaker callbacks.'
Check ($deposition.Contains('PoliticalDeliberationDays') -and $deposition.Contains('legalTransfer: false')) 'MCM deliberation window and temporary title custody are used.'
Check ($deposition.Contains('record.DepositionElectionDate.IsPast') -and $deposition.Contains('BeginMandateElection(realm)')) 'Election starts only after saved deliberation date through normal accession.'
Check ($ballot.Contains('accession.ElectiveExcluded ??') -and $crown.Contains('clan.Leader != record.ElectiveExcluded')) 'Deposed ruler exclusion reaches both custom ballots and emergency fallback.'
Check ($ballot.Contains('record.DepositionPending = false;') -and $crown.Contains('DepositionElection = deposition != null')) 'Final accession closes deliberation and distinguishes caretaker election from reelection.'
Check ($court.Contains('ReplaceCrownAgendaForElection') -and $court.Contains('realm.RemoveDecision(decision)') -and $court.Contains('DepositionAgenda(realm)')) 'Election replaces Crown agenda, including its live policy decision and display.'
Check ($war.Contains('rewardKingdom.RulingClan != deposition.InterimHouse')) 'Caretaker conducts the tribunal once installed.'
Check ($war.Contains('_tribunalRewardKingdomIds.ContainsValue(id)') -and $war.Contains('_postWarExecutionRewardKingdomIds.ContainsValue(id)')) 'Player judgments and deferred executions both hold the election countdown.'
Check ($deposition.Contains('Maintain(realm, renewal: true)') -and $deposition.IndexOf('Maintain(realm, renewal: true)') -lt $deposition.IndexOf('record.DepositionElectionDate = CampaignTime.Now')) 'Preferences refresh before the full deliberation window starts.'
Check ((Read 'Behaviors/SuccessionLawBehavior.cs').Contains('PendingDeposition(kingdom) != null')) 'Caretaker cannot rewrite succession rules during deliberation.'
Check ((Read 'Behaviors/ElectiveContestBehavior.cs').Contains('snapshot.AutomaticChallengeEnabled = !snapshot.ManualTest;')) 'Normal post-election contests remain enabled.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_Deposition_Deliberation', 'BC_Deposition_Agenda', 'BC_Deposition_Caretaker', 'BC_Deposition_JudgmentsPending', 'BC_Deposition_TribunalAgenda')) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localized text: $id"
}
'Structural checks only; engine save/load and live settlement require campaign tests.'
