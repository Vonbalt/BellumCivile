$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$commands = Read 'CheatCommands.cs'
$testing = Read 'Behaviors/ElectiveContestTesting.cs'
$accession = Read 'Behaviors/CrownAccessionBehavior.cs'
$contest = Read 'Behaviors/ElectiveContestBehavior.cs'
foreach ($name in @('force_election', 'force_three_way_war', 'election_test_status', 'stop_election_test')) {
    Check ($commands.Contains("CommandLineArgumentFunction(`"$name`", `"civilwars`")")) "Console command registered: $name"
}
Check ($testing.Contains('BeginMandateElection(realm, testContest: true)') -and $accession.Contains('ElectiveContestTest = testContest')) 'Test elections use the normal mandate-election backend with an explicit saved test marker.'
Check ($contest.Contains('snapshot.ManualTest = accession.ElectiveContestTest')) 'Only the corresponding completed election snapshot inherits test activation.'
Check ($testing.IndexOf('elections.Maintain(realm);') -ge 0 -and $testing.IndexOf('elections.Maintain(realm);') -lt $testing.IndexOf('BeginMandateElection(realm, testContest: true)')) 'Repeat tests refresh completed ballot state before starting another accession.'
Check ($testing.Contains('c.ShouldAdvance(CampaignTime.Now.ToDays)') -and (Read 'ElectiveContestRecord.cs').Contains('(ManualTest || AutomaticChallengeEnabled)')) 'Scheduler advances newly enabled normal ballots and explicit tests, excluding dormant historical ballots.'
Check ($contest.Contains('snapshot.AutomaticChallengeEnabled = !snapshot.ManualTest') -and $contest.Contains('accession.Emergency')) 'Automatic activation is attached to newly captured non-emergency elective ballots.'
Check ($testing.Contains('if (record.ManualTest)') -and $testing.Contains('ActiveState is TaleWorlds.CampaignSystem.GameState.MapState')) 'Test status chat remains test-only and inquiries wait for map state.'
Check ($testing.Contains('ElectiveSuccessionBehavior.UsesElection(realm)') -and $testing.Contains('accession.IsPending(realm)') -and $testing.Contains('f.HasTrackedRebelKingdom')) 'Force command validates elective laws, pending accession and existing civil wars.'
Check ($testing.Contains('AdvanceAppeal(record, () => MBRandom.RandomFloat)') -and $testing.Contains('AnswerNpcUltimata(record, () => MBRandom.RandomFloat)')) 'Candidate and ruler decisions reuse normal saved-roll logic.'
Check ($testing -notmatch '\.Acceptance\s*=(?!=)|\.Decision\s*=(?!=)|SetActiveLaw|StartFrozenSuccessionRebellion|new FactionObject') 'Test orchestration does not force acceptance, change laws or directly create a rebellion.'
Check ($testing.Contains('DispatchUltimata(record)') -and (Read 'Behaviors/ElectiveContestDispatch.cs').Contains('!StartRivalry(record,')) 'Controlled tests can dispatch both claimant wars and their rivalry.'
Check ($testing.Contains('record.WarDispatchStarted || record.CrownTransferStarted || _inquiry == record')) 'Stop command cannot discard an in-progress transfer or outstanding player inquiry.'
Check ($testing.Contains('if (!record.WarDispatchStarted && !record.CrownTransferStarted)') -and $testing.Contains('record.RevalidateParticipants(')) 'Prewar validation continues after the response, but stops once war or Crown transfer starts.'
Check ($testing.Contains('if (record.TestStatus == status) return;')) 'Unchanged deferred states do not spam chat each hourly retry.'
'Source contracts only; command-driven elections and player inquiries still need an in-game test.'
$forced = $testing.Substring($testing.IndexOf('internal string ForceThreeWayTest'), $testing.IndexOf('internal string TestReport') - $testing.IndexOf('internal string ForceThreeWayTest'))
Check ($forced.Contains('challengers.Count != 2') -and $forced.Contains('crown.Fiefs.Any') -and $forced.Contains('SuccessionChallengeBehavior.Available(c.Leader)')) 'Forced fixture requires three landed leaders and two available challengers.'
Check ($forced.Contains('ManualTest = true, ForcedThreeWayTest = true') -and $forced.Contains('RulerAnswered = true') -and !$forced.Contains('MBRandom')) 'Explicit forced fixture skips chance and surrender decisions, including player decisions.'
Check ($forced.Contains('ThenBy(c => c.StringId, StringComparer.Ordinal)') -and $forced.Contains('!record.Candidates.Any(c => c.House == h)')) 'Deterministic coalition allocation excludes already seated leaders.'
Check ($forced.IndexOf('challengers.Count != 2') -lt $forced.IndexOf('_contests.Add(record)') -and !$forced.Contains('BeginMandateElection')) 'Invalid fixture requests do not start an election or mutate the contest list.'
Check ((Read 'ElectiveContestRecord.cs').Contains('[SaveableField(33)] public bool ForcedThreeWayTest') -and
    (Read 'Behaviors/ElectiveContestDispatch.cs').Contains('candidate.Candidate == Hero.MainHero || record.BypassTestPowerGate')) 'Saved scenario flag only bypasses the power check in the existing dispatcher.'
