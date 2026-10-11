$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; Write-Output "PASS: $message" }
$behavior = Read 'Behaviors/ElectionLobbyingBehavior.cs'
$integration = Read 'Behaviors/ElectionLobbyingIntegration.cs'
$dialogue = Read 'Behaviors/ElectionLobbyingDialogues.cs'
$barter = Read 'ElectionVoteBribeBarterable.cs'
$patch = Read 'Patches/ElectionVoteBarterPatch.cs'
$election = Read 'Behaviors/ElectiveSuccessionBehavior.cs'
$court = Read 'Behaviors/CourtAgendaBehavior.cs'
Check ($behavior.Contains('_candidates[index] == Hero.MainHero') -and
    $behavior.Contains('{=BC_Fief_Delib_PushPlayer}I would like to nominate myself for such an honor.') -and
    $behavior.Contains(': _candidates[index].Name')) 'Self-nomination reuses the existing localized first-person line; other candidates retain their names.'
Check ($dialogue.Contains('i < 6') -and $behavior.Contains('_used >= 3') -and $behavior.Contains('BlockTheOption(true)')) 'Six arguments, at most three distinct attempts.'
Check ($integration.Contains('Hero.MainHero.GetRelation(speaker) < 30') -and $behavior.Contains('argument == 5 && Relation < 60')) 'General persuasion and personal-trust gates differ.'
Check ($behavior.Contains('SyncData("BC_ElectionLobbyingAttempts"') -and $integration.Contains('_attempts[AttemptKeyFor(realm, speaker)] = until') -and
    $integration.Contains('realm.StringId + "|" + speaker.StringId') -and $behavior.Contains('TryBeginAttempt(')) 'Attempts persist across conversations, integration chats and candidate changes.'
Check ($behavior.Contains('C.FiefPersuasionCriticalFailValue, 0, PersuasionDifficulty.Medium')) 'Native automatic relation progress is suppressed.'
Check ($dialogue.IndexOf('"el_success"') -lt $dialogue.IndexOf('"el_failed"')) 'A successful third argument takes precedence over exhausted attempts.'
Check ($election.Contains('NextEvaluation(realm)') -and $election.Contains('vote.Until = until;') -and
    $court.Contains('_nextTerms.TryGetValue(realm.StringId')) 'Promises use the realm court schedule, not a rolling new year.'
Check (!$election.Contains('Until.IsPast') -and (Read 'ElectiveSuccessionRules.cs').Contains('old.Until.ToDays > day')) 'Promise expiry includes the boundary itself before the ballot freezes.'
Check ($election.Contains('RetainPromise(old, candidates, record.Frozen') -and $election.Contains('vote.Source = old.Source; vote.Until = old.Until;')) 'Rebuilt frozen ballots retain valid promises without extending the stored expiry.'
Check ($behavior.Contains('_ballot.MandateNumber == _mandateNumber') -and $barter.Contains('_ballot.MandateNumber != _mandateNumber')) 'Dialogue and barter reject offers from an earlier mandate after completion or ruler replacement.'
Check ($patch.Contains('votes.Count == 0) return true') -and $patch.Contains('votes.Count == 1') -and
    $patch.Contains('IsOffered') -and $patch.Contains('IsOfferAcceptable') -and $patch.Contains('CancelAndFinalizePlayerBarter')) 'Only electoral barter is intercepted; invalid or removed promises cancel before payment.'
Check ($barter.Contains('until != _until') -and $barter.Contains('CanCommitPromise') -and $barter.Contains('TryCommitPromise') -and
    $barter.Contains('public override void Apply() { }')) 'Pledge is secured at finalization, not after payment items apply.'
Check (!$behavior.Contains('DailyTickEvent') -and !$behavior.Contains('TickEvent')) 'Lobbying adds no recurring campaign scan.'
Check ((Read 'SubModule.cs').Contains('AddBehavior(new ElectionLobbyingBehavior())')) 'Lobbying behavior is registered.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$all = $behavior + $dialogue + $barter + $integration
foreach ($match in [regex]::Matches($all, '\{=(BC_EL_[A-Za-z0-9_]+)\}([^"\r\n]*)')) {
    $id = $match.Groups[1].Value
    $nodes = $xml.SelectNodes("//string[@id='$id']")
    Check ($nodes.Count -eq 1 -and $nodes[0].text -eq $match.Groups[2].Value) "Unique matching English text: $id"
}
Write-Output 'Source/XML contracts only; native dialogue, barter and save/load still require in-game tests.'
