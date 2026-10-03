$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$player = Read 'Behaviors/SuccessionChallengePlayer.cs'
$behavior = Read 'Behaviors/SuccessionChallengeBehavior.cs'
foreach ($path in @('Behaviors/SuccessionChallengeBehavior.cs', 'Behaviors/SuccessionChallengeDispatch.cs', 'Behaviors/SuccessionChallengeWar.cs')) {
    $source = Read $path
    Check ($source.Contains('SuccessionChallengeRules.CanProceed(') -and $source.Contains('record.Challenger == Hero.MainHero')) "Player military-risk exception survives dispatch gate: $path"
}
$pledges = Read 'Behaviors/SuccessionChallengePledges.cs'
$responses = Read 'Behaviors/SuccessionChallengeTesting.cs'
$vm = Read 'UI/VanillaTabs/Kingdoms/Hierarchy/HierarchyTitleNodeVM.cs'
$loyalty = Read 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs'
Check ($player.Contains('GetRealmSovereignTitle(realm)?.TitleId == title.TitleId') -and
    $player.Contains('GetLine(realm).Contains(Hero.MainHero)')) 'Player action requires own sovereign title and lawful succession eligibility.'
foreach ($gate in @('Available(Hero.MainHero)', 'Clan.PlayerClan.Leader != Hero.MainHero',
    'r.IsOpen', 'r.RealmBlockedUntil > Day', 'r.PersonalBlockedUntil > Day',
    'IsClanPacified', 'GetRebelFaction', 'IsCivilWarActive', 'IsPending(realm)')) {
    Check ($player.Contains($gate)) "Player preview retains gate: $gate"
}
Check ($behavior.Contains('bool playerInitiated = false') -and
    $behavior.Contains('challenger == Hero.MainHero && !playerInitiated') -and
    (Read 'Behaviors/SuccessionChallengeInitiation.cs').Contains('if (heir == Hero.MainHero')) 'Automatic and ordinary console initiation still exclude the player.'
Check ($behavior.Contains('playerInitiated ? null : HereditaryLoyaltyBehavior') -and
    $behavior.Contains('Demand = playerInitiated ? SuccessionChallengeDemand.Crown')) 'Player selects a Crown demand without a personality loyalty threshold.'
Check ($player.IndexOf('PlayerChallengeBlock(title)') -lt $player.IndexOf('TryBegin(') -and
    $player.Contains('ResolveAppeal(record)')) 'Confirmation revalidates and uses actual pledge gathering.'
Check ($pledges.Contains('AnswerPlayerUltimatum(report, true)') -and
    $pledges.Contains('AnswerPlayerUltimatum(report, false)') -and
    $responses.Contains('!r.ReportPending || r.Realm != Clan.PlayerClan?.Kingdom')) 'Saved support report waits for explicit send/withdraw before NPC response.'
Check ($player.Contains('RefreshResponseBacking(record)') -and
    $player.Contains('WithdrawForInsufficientBacking(record)')) 'Sending rechecks support and withdrawal uses existing cooldowns.'
Check ($loyalty.Contains('if (IsPlayerHeir) return "--";') -and
    $loyalty.Contains('BC_Loyalty_PlayerReasons') -and $loyalty.Contains('IsPlayerHeir ? (double?)null')) 'Player loyalty has no numeric score or state color.'
[xml]$panel = Read 'GUI/Prefabs/KingdomManagement/Hierarchy/BellumHierarchyPanel.xml'
Check ($panel.SelectNodes('//*[@Command.Click="ExecuteChallenge"]').Count -eq 1 -and
    $vm.Contains('ActionMenuHeight => IsChallengeVisible ? 322 : 288')) 'Hierarchy menu contains one challenge action with room for its additional row.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $player + $pledges + $vm + $loyalty
foreach ($id in ([regex]::Matches($texts, '\{=(BC_PlayerChallenge_[^}]+|BC_Hierarchy_ActionChallenge|BC_Loyalty_PlayerReasons)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localized player text: $id"
}
'Source contracts only; player succession, menu layout and save/load require in-game verification.'
