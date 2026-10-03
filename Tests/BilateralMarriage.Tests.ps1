$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$strategy = Get-Content -Raw (Join-Path $root 'Behaviors/StrategicMarriageBehavior.cs')
$helper = Get-Content -Raw (Join-Path $root 'Behaviors/BellumMarriageStrategyHelper.cs')
$evaluation = Get-Content -Raw (Join-Path $root 'Behaviors/BellumMarriageEvaluation.cs')
$outcome = Get-Content -Raw (Join-Path $root 'Behaviors/MarriageOutcome.cs')
$model = Get-Content -Raw (Join-Path $root 'BellumMarriageModel.cs')
$titles = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs')
function Check([bool] $condition, [string] $label) {
    if (!$condition) { throw $label }
    Write-Output "PASS: $label"
}
Check ($strategy.Contains('DailyTickClanEvent') -and !$strategy.Contains('DailyTickHeroEvent')) 'One scheduled house search replaces per-man scheduling.'
Check ($strategy.Contains('BellumCivile_LastMarriageHouseEvaluationYear') -and $outcome.Contains('2166136261') -and !$strategy.Contains('GetHashCode()')) 'Year receipts persist and calendar offsets use a stable hash.'
Check (!$helper.Contains('(!hero.IsFemale || clan.Leader == hero)')) 'Women are eligible household participants without needing leadership.'
Check ($helper.Contains('HasHouseholdFertileCouple(clan)') -and (Get-Content -Raw (Join-Path $root 'Behaviors/MarriageMatchmakingRules.cs')).Contains('h.Spouse.Clan == clan')) 'Reproductive households require a living couple in the same household.'
Check (!$helper.Contains('_titleClaimCache') -and $evaluation.Contains('One context per scheduled house search')) 'No static day-only campaign object cache remains.'
Check ($evaluation.Contains('BothAccept') -and $evaluation.Contains('EvaluateHouse(first, second') -and $evaluation.Contains('EvaluateHouse(second, first')) 'Both sides independently evaluate the same outcome.'
Check (!$helper.Contains('MarriageScoreDynasticHeirPenalty') -and !$helper.Contains('MarriageScoreLowerTierRoyalMatchPenalty')) 'Blanket heir penalty and duplicate royal status penalty are absent.'
Check ($helper.Contains('if (!personalCarrier) continue;')) 'Unrelated clan members cannot convey someone else''s claim value.'
Check ($strategy.Contains('FindBestHouseMatch(clan, factionManager, CanSendPlayerOffer()')) 'Unavailable player offers are excluded before searching for executable alternatives.'
Check ($strategy.IndexOf('MarriageAction.Apply') -lt $strategy.IndexOf('_yearlySummary.RecordCompletedMarriage(match)') -and $strategy.Contains('match.Candidate.Spouse != match.Suitor')) 'Success follows execution and reciprocal marriage verification.'
Check (!$titles.Contains('IsRoyalHeiressMarriage') -and !$model.Contains('TryResolveDynasticMarriageClanOverride')) 'Retired heiress exceptions no longer alter birthrights or household placement.'
Check ($evaluation.Contains('PartitionSuccessionBehavior.OrderEstateHeirs') -and !$evaluation.Contains('BuildPlan(') -and !$evaluation.Contains('MarriageAction.Apply')) 'Read-only parental prospects reuse succession rules without allocating or transferring estates.'
Check ($evaluation.Contains('Dictionary<Hero, List<Hero>> _parentHeirs') -and $evaluation.Contains('Dictionary<Clan, HouseHealth> _health')) 'House health and parent forecasts are cached within each evaluation.'
Check (!$helper.Contains('FactionType.Royalists') -and $helper.Contains('GetFavoredBloc') -and $helper.Contains('RebellionIntentCalculator.Assess')) 'Court scoring follows current sponsorship and rebellious intent.'
Write-Output 'Source contracts only; campaign frequency, accepted offers and claim persistence require in-game tests.'
