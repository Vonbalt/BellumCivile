$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$shock = Read 'Behaviors/IdeologyEventShockBehavior.cs'
$ideology = Read 'Behaviors/IdeologyBehavior.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$record = Read 'CrownAccessionRecord.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($shock.Contains('BC_RecordedRoyalExecutions') -and $shock.Contains('_recordedRoyalExecutions.Contains(victim.StringId)')) 'Royal execution replay receipt persists by victim.'
Check ($ideology.Contains('if (nobleSentence && victim.IsDead)') -and $ideology.Contains('.RecordRoyalExecution(sentencingRealm, victim)')) 'Treason fallback records only a confirmed death against captured sentencing realm.'
Check ([regex]::Matches($ideology, 'ExecuteTreasonSentence\(kingdom, targetLeader, ruler\)').Count -eq 2) 'Player and NPC treason routes share completion handling.'
$submission = $ideology.Substring($ideology.IndexOf('private void SubmitPlayerTreasonJudgment('))
$submission = $submission.Substring(0, $submission.IndexOf('private void DefyPlayerTreasonJudgment('))
Check ($submission.IndexOf('ApplyByLeaveKingdom') -lt $submission.IndexOf('ExecuteTreasonSentence')) 'Player clan-transfer/death ordering remains unchanged.'
Check ($submission.Contains('execute && targetLeader?.IsDead == true')) 'Blocked player execution is not announced as a death.'
foreach ($field in @('AccessionReactionsApplied', 'ElectionReactionsApplied', 'ElectionEndorsements', 'ElectionEndorsementsCaptured')) {
    Check ($record -match ('\[SaveableField\(\d+\)\] public [^;]+\b' + $field + '\b')) "Saved episode field: $field"
}
Check ($save.Contains('ConstructContainerDefinition(typeof(Dictionary<FactionType, Clan>))')) 'Endorsement snapshot container registered.'
Check ($shock.Contains('electionData.SourceDecision == accession.EmergencyElection')) 'Native endorsements require exact decision identity.'
Check ($shock.Contains('new Dictionary<FactionType, Clan>(electionData.Endorsements)')) 'Native snapshot does not alias transient cache.'
Check ($shock.Contains('accession.ElectiveElection && !accession.Emergency')) 'Emergency resolution takes precedence over obsolete elective standings.'
Check ($shock.Contains('ballot.Realm != kingdom || ballot.Winner != accession.Heir')) 'Standing ballot must belong to the same realm and elected winner.'
$election = $shock.Substring($shock.IndexOf('internal bool CompleteElectionReactions('))
$election = $election.Substring(0, $election.IndexOf('public bool HasRecentFiefAward'))
Check (!$election.Contains('faction.Mood = 0f') -and !$election.Contains('BC_Shock_Ascension')) 'Ballot aftermath neither resets moods nor announces accession.'
Check ($election.Contains('_recentCandidateWon.Remove(key)') -and $election.Contains('_recentCandidateLost.Remove(key)')) 'Every concluded ballot replaces earlier election history.'
Check ($election.Contains('accession.ElectionReactionsApplied = true')) 'Ballot delivery has its own replay receipt.'
Check ($crown.IndexOf('!shocks.CompleteElectionReactions(record,') -lt $crown.IndexOf('record.Completed = true;', $crown.IndexOf('private void Finish('))) 'Accession remains pending if reaction completion is deferred.'
