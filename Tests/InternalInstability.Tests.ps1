$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$court = Read 'Behaviors/CourtAgendaBehavior.cs'
$windows = Read 'Behaviors/CourtInstability.cs'
Check ($court.Contains('SyncInstabilityData(dataStore)') -and $windows.Contains('BC_CourtChallengeWindowUntil') -and
    $windows.Contains('BC_CourtChallengeWindowAllowed')) 'Shared court decision and boundary are saved with legacy-safe containers.'
Check ($court.Contains('if (!MayStartAutomaticCourtChallenge(faction, realm)) continue;') -and
    $court.Contains('ShouldStartCourtCrisis(owner, realm)') -and $court.Contains('ShouldStartCourtCrisis(agenda.Faction, agenda.Realm)')) 'Emergency, term opening and mid-term ordinary crises use the instability gates.'
Check ($windows.Contains('faction.Leader == Clan.PlayerClan') -and $windows.Contains('State == CourtAgendaState.Crisis') -and
    $windows.Contains('NextEvaluation(realm).ToDays')) 'Player leadership, announced crises and the existing next-term date are preserved.'
$heirs = Read 'Behaviors/SuccessionChallengeInitiation.cs'
$entry = Read 'Behaviors/SuccessionChallengeBehavior.cs'
Check ($heirs.Contains('BellumCivileOptions.HeirChallengeLoyaltyThreshold') -and
    $entry.Contains('BellumCivileOptions.HeirChallengeLoyaltyThreshold') -and $entry.Contains('if (playerInitiated)')) 'Both automatic heir gates use the new threshold outside the manual player branch.'
$contest = Read 'ElectiveContestRecord.cs'
Check ($contest.Contains('HasChallengeChanceSnapshot ? ChallengeChancePercent : 100') -and
    $contest.Contains('if (Decision != ElectiveContestDecision.Pending) return;') -and
    (Read 'Behaviors/ElectiveContestBehavior.cs').Contains('ChallengeChancePercent = BellumCivileOptions.ElectionChallengeChancePercent')) 'New election snapshots capture the multiplier; legacy and resolved candidates retain their decisions.'
$feuds = Read 'Behaviors/ClaimFeudBehavior.cs'
Check (!$feuds.Contains('C.ClaimFeudStartThreshold') -and $feuds.Contains('bestCandidate.Score < BellumCivileOptions.ClaimFeudStartThreshold') -and
    $feuds.Contains('InternalInstabilityRules.ScaleChance(chance, BellumCivileOptions.ClaimedRevocationChancePercent)')) 'Feud initiation, diagnostics and claimed revocation use their separate settings.'
Check ($feuds.Contains('if (clan == null || clan == Clan.PlayerClan || clan.Leader == Hero.MainHero)') -and
    $feuds.Contains('GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false)')) 'Player feud candidate enumeration does not acquire the AI score gate.'
$treason = Read 'Behaviors/IdeologyBehavior.cs'
Check ($treason.Contains('targetClan == Clan.PlayerClan ? 100 : BellumCivileOptions.TreasonResistanceChancePercent') -and
    $treason.Contains('rebelChance > 0 && MBRandom.RandomFloat <= rebelChance') -and $treason.Contains('if (rebelFaction == null)')) 'Only spontaneous NPC expulsion rolls are scaled, with an exact zero guard.'
$client = Read 'Behaviors/ClientKingdomBehavior.cs'
Check ($client.Contains('float requiredWillingness = LiberationWillingnessThreshold(proposer)') -and
    (Read 'ClientLiberationProposalAssessment.cs').Contains('ClientKingdomBehavior.LiberationWillingnessThreshold(entry.Clan)') -and
    (Read 'CourtLiberationObjectiveSource.cs').Contains('ClientKingdomBehavior.LiberationWillingnessThreshold(realm?.RulingClan)')) 'Proposal execution, tooltip assessment and new Crown preparations share client-specific willingness.'
Check ((Read 'Behaviors/CourtLiberationAgendas.cs').Contains('agenda.Liberation.RequiredWillingness') -and
    (Read 'CourtLiberationRecord.cs').Contains('BellumCivileConstants.WarPeaceRevampWarProposalThreshold')) 'Announced and legacy preparations revalidate with their original threshold.'
$settings = Read 'BellumCivileSettings.cs'
[xml]$catalog = Read 'ModuleData/Languages/EN/strings.xml'
$entries = @{}
foreach ($s in $catalog.base.strings.string) { $entries[$s.id] = $s.text }
$section = $settings.Substring($settings.IndexOf('[SettingPropertyInteger("{=BC_MCM_RebellionThreshold}'),
    $settings.IndexOf('[SettingPropertyFloatingInteger("{=BC_MCM_CourtTerm}') - $settings.IndexOf('[SettingPropertyInteger("{=BC_MCM_RebellionThreshold}'))
$matches = [regex]::Matches($section, 'HintText = "\{=([^}]+)\}([^"]+)"')
Check ($matches.Count -eq 9) 'Internal Instability contains the retained slider and eight new controls.'
foreach ($match in $matches) {
    $id = $match.Groups[1].Value; $text = $match.Groups[2].Value
    Check ($text -ceq $entries[$id] -and $text.Contains('Bellum default:') -and
        $text.Contains('Increasing') -and $text.ToLowerInvariant().Contains('decreasing')) "$id matches localization and explains default and both directions."
}
'Source contracts only; run SuccessionEngineHarness --internal-instability and --client-liberation for compiled behavior checks.'
