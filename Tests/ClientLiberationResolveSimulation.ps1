$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$script:checks = 0

function Check($condition, $label) {
    if (!$condition) { throw $label }
    $script:checks++
}

# Compile the production numeric helpers; engine-dependent overloads are not used.
$constants = Get-Content -Raw (Join-Path $root 'BellumCivileConstants.cs')
$constantNames = @(
    'ClientClanLiberationDesireThreshold', 'ClientRealmLiberationDesireThreshold',
    'ClientSuzerainAllyPowerContribution', 'ClientOtherClientPowerContribution',
    'ClientLiberationTargetScoreScale', 'WarPeaceRevampWarProposalThreshold',
    'RebellionPowerThresholdBase', 'TreatyCouncilMinimumVoteStep',
    'TreatyCouncilMildCommitment', 'TreatyCouncilStrongCommitment',
    'TreatyCouncilMinimumQuorum', 'TreatyCouncilQuorumShare',
    'NpcInfluenceClanReserve', 'NpcInfluenceRulerReserve', 'NpcInfluenceForeignWarReserveBonus',
    'WarPeaceRevampUnaffiliatedOverflowRecoveryMultiplier', 'WarPeaceRevampPopulistOverflowRecoveryMultiplier'
)
$declarations = foreach ($name in $constantNames) {
    $match = [regex]::Match($constants, "(?m)^\s*public const (?:int|float) $name\s*=\s*[^;]+;")
    Check $match.Success "Production constant not found: $name"
    $match.Value.Trim()
}
$stubs = @"
namespace TaleWorlds.CampaignSystem { public sealed class Clan {} }
namespace BellumCivile {
    public static class BellumCivileConstants {
        $($declarations -join "`n")
    }
    public enum NpcInfluenceExpenseKind { CouncilCommitment }
    public static class NpcInfluenceBudgetService {
        public static float GetSpendableInfluence(TaleWorlds.CampaignSystem.Clan clan,
            NpcInfluenceExpenseKind kind, float? balanceOverride = null) {
            throw new System.NotSupportedException("Use the numeric vote overload in this simulation.");
        }
    }
}
"@
$sources = foreach ($file in @('ClientLiberationRules.cs', 'CourtLiberationRules.cs',
        'PoliticalInfluenceVoteHelper.cs', 'TreatyCouncilVoteStance.cs')) {
    $source = Get-Content -Raw (Join-Path $root $file)
    $source = [regex]::Replace($source, '(?m)^\s*using [^;\r\n]+;[ \t]*\r?\n', '').Replace('internal', 'public')
    # Windows PowerShell uses an older compiler; preserve property calculations.
    $source = $source.Replace('{ get; }', '{ get; private set; }')
    [regex]::Replace($source, '(?m)^(\s*public (?:int|bool) \w+) => ([^;]+);', '$1 { get { return $2; } }')
}
$compiled = "using System;`nusing TaleWorlds.CampaignSystem;`nusing C = BellumCivile.BellumCivileConstants;`n" + $stubs + "`n" + ($sources -join "`n")
Add-Type -TypeDefinition $compiled

$warSource = Get-Content -Raw (Join-Path $root 'Behaviors/WarPeaceRevampBehavior.cs')
$utility = [regex]::Match($warSource, 'float raw = warWill - ([0-9.]+)f \+ targetValue \* ([0-9.]+)f \+ trait;')
Check $utility.Success 'Production council utility formula must be reviewed if it changes.'
$utilityOffset = [double]::Parse($utility.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
$targetUtilityScale = [double]::Parse($utility.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
Check ($warSource.Contains('45f + valor * 8f - mercy * 6f')) 'Neutral Crown recovery threshold remains 45.'
Check ($warSource.Contains('return 40f;')) 'Liberty recovery threshold remains 40.'
Check ($warSource.Contains('baseStep = -0.65f;') -and $warSource.Contains('baseStep = -1.25f;')) 'Wartime drain assumptions match production.'
$options = Get-Content -Raw (Join-Path $root 'BellumCivileOptions.cs')
Check ($options.Contains('WarTargetMinimumScore => C.WarPeaceRevampMinimumTargetScore')) 'Target minimum uses the expected production constant.'
$minimumScoreMatch = [regex]::Match($constants, 'WarPeaceRevampMinimumTargetScore\s*=\s*([0-9.]+)f;')
Check $minimumScoreMatch.Success 'Production target minimum found.'
$minimumTargetScore = [double]::Parse($minimumScoreMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
$declarationThreshold = [BellumCivile.BellumCivileConstants]::WarPeaceRevampWarProposalThreshold
$desireThreshold = [BellumCivile.BellumCivileConstants]::ClientClanLiberationDesireThreshold
$caps = @(0, 20, 30)

# +30 uses production rules; 0 and +20 retain the balance comparison baselines.
function Resolve-Bonus([double]$desire, [double]$cap, [bool]$liberation = $true) {
    if (!$liberation) { return 0.0 }
    if ($cap -eq 30) { return [BellumCivile.ClientLiberationRules]::ResolveBonus($desire) }
    return [Math]::Min($cap, [Math]::Max(0.0, ($desire - $desireThreshold) * $cap / (100.0 - $desireThreshold)))
}
function Effective-Will([double]$will, [double]$desire, [double]$cap, [bool]$liberation = $true) {
    if ($liberation -and $cap -eq 30) { return [BellumCivile.ClientLiberationRules]::EffectiveWarWill($will, $desire) }
    return [Math]::Min(100.0, [Math]::Max(0.0, $will) + (Resolve-Bonus $desire $cap $liberation))
}
function New-Clan($name, [double]$desire, [double]$will, [double]$influence = 600,
        [double]$military = 600, [bool]$crown = $false, [double]$targetAdjustment = 0) {
    [pscustomobject]@{ Name=$name; Desire=$desire; Will=$will; Influence=$influence;
        Military=$military; Crown=$crown; TargetAdjustment=$targetAdjustment }
}
function New-Clans([double]$desire, [double]$will) {
    New-Clan 'Crown' $desire $will 1200 1500 $true
    foreach ($name in @('Glory', 'Nobility', 'Liberty')) { New-Clan $name $desire $will }
}
function New-Scenario($name, $clans, [double]$cooldown = 0, [bool]$pending = $false,
        [double]$suzerain = 5000, [double]$allies = 1000, [double]$otherClients = 1000,
        [double]$ratio = 0.8, [double]$courtBonus = 0, [bool]$foreignWar = $false) {
    [pscustomobject]@{ Name=$name; Clans=@($clans); Cooldown=$cooldown; Pending=$pending;
        Suzerain=$suzerain; Allies=$allies; OtherClients=$otherClients; Ratio=$ratio;
        CourtBonus=$courtBonus; ForeignWar=$foreignWar }
}
function Measure-Scenario($scenario, [double]$cap) {
    $bloc = $scenario.Suzerain
    $bloc += [BellumCivile.ClientLiberationRules]::BlocContribution($scenario.Allies, $true, $false,
        [BellumCivile.BellumCivileConstants]::ClientSuzerainAllyPowerContribution,
        [BellumCivile.BellumCivileConstants]::ClientOtherClientPowerContribution)
    $bloc += [BellumCivile.ClientLiberationRules]::BlocContribution($scenario.OtherClients, $true, $true,
        [BellumCivile.BellumCivileConstants]::ClientSuzerainAllyPowerContribution,
        [BellumCivile.BellumCivileConstants]::ClientOtherClientPowerContribution)
    $members = @(foreach ($clan in $scenario.Clans) {
        $desire = [Math]::Min(100.0, [Math]::Max(0.0, $clan.Desire + $scenario.CourtBonus))
        $reserve = if ($clan.Crown) { [BellumCivile.BellumCivileConstants]::NpcInfluenceRulerReserve }
            else { [BellumCivile.BellumCivileConstants]::NpcInfluenceClanReserve }
        if ($scenario.ForeignWar -and $clan.Crown) {
            $reserve += [BellumCivile.BellumCivileConstants]::NpcInfluenceForeignWarReserveBonus
        }
        [pscustomobject]@{ Clan=$clan; Desire=$desire; Power=($clan.Military + $clan.Influence);
            Multiplier=[BellumCivile.ClientLiberationRules]::EffectivePowerMultiplier($desire);
            EffectiveWill=(Effective-Will $clan.Will $desire $cap); Reserve=$reserve;
            TargetScore=($desire * [BellumCivile.BellumCivileConstants]::ClientLiberationTargetScoreScale + $clan.TargetAdjustment) }
    })
    $totalWeight = 0.0; $weightedDesire = 0.0; $effectivePower = 0.0
    foreach ($member in $members) {
        $weight = [Math]::Max(1.0, $member.Power)
        $totalWeight += $weight; $weightedDesire += $member.Desire * $weight
        $effectivePower += $member.Power * $member.Multiplier
    }
    $realmDesire = $weightedDesire / $totalWeight
    $readiness = [BellumCivile.ClientLiberationRules]::Readiness($effectivePower, $bloc, $scenario.Ratio)
    $commonEligible = $realmDesire -ge [BellumCivile.BellumCivileConstants]::ClientRealmLiberationDesireThreshold `
        -and $readiness -ge 100 -and $scenario.Cooldown -le 0 -and !$scenario.Pending
    $proposalCost = 200.0
    $proposers = @($members | Where-Object {
        $commonEligible -and $_.Desire -ge $desireThreshold -and $_.EffectiveWill -ge $declarationThreshold `
            -and $_.TargetScore -ge $minimumTargetScore `
            -and $_.Clan.Influence -ge ($proposalCost + [Math]::Max($_.Reserve, $proposalCost))
    })
    $proposer = if ($proposers.Count -gt 0) { $proposers[0] } else { $null }
    $yay = 0; $nay = 0; $capacity = 0; $postFilingPower = $effectivePower; $postVotePower = $effectivePower
    $voteRows = @(foreach ($member in $members) {
        $balance = $member.Clan.Influence
        if ($null -ne $proposer -and $member.Clan.Name -eq $proposer.Clan.Name) {
            $balance -= $proposalCost
            $postFilingPower -= $proposalCost * $member.Multiplier
            $postVotePower -= $proposalCost * $member.Multiplier
        }
        $available = [Math]::Max(0.0, $balance - $member.Reserve)
        $capacity += [BellumCivile.PoliticalInfluenceVoteHelper]::GetAffordableCommitment(
            [BellumCivile.BellumCivileConstants]::TreatyCouncilStrongCommitment, $available)
        $target = if ($member.TargetScore -ge $minimumTargetScore) { $member.TargetScore } else { 0 }
        $utilityValue = $member.EffectiveWill - $utilityOffset + $target * $targetUtilityScale
        $stance = [BellumCivile.TreatyCouncilVoteStance]::Abstain
        $commitment = [BellumCivile.PoliticalInfluenceVoteHelper]::GetCommitment($utilityValue, $available, [ref]$stance)
        if ($stance -eq [BellumCivile.TreatyCouncilVoteStance]::Yay) { $yay += $commitment }
        if ($stance -eq [BellumCivile.TreatyCouncilVoteStance]::Nay) { $nay += $commitment }
        if ($null -ne $proposer) { $postVotePower -= $commitment * $member.Multiplier }
        [pscustomobject]@{ Clan=$member.Clan.Name; Desire=$member.Desire; ActualWill=$member.Clan.Will;
            Resolve=(Resolve-Bonus $member.Desire $cap); EffectiveWill=$member.EffectiveWill;
            Utility=$utilityValue; Stance=$stance; Commitment=$commitment }
    })
    $tally = [BellumCivile.PoliticalInfluenceVoteHelper]::Calculate($yay, $nay, $capacity)
    [pscustomobject]@{ Name=$scenario.Name; Cap=$cap; RealmDesire=$realmDesire; Readiness=$readiness;
        CanPropose=($null -ne $proposer); Yay=$yay; Nay=$nay; Quorum=$tally.Quorum;
        CouncilPass=$tally.IsRatified; AfterFiling=[BellumCivile.ClientLiberationRules]::Readiness($postFilingPower, $bloc, $scenario.Ratio);
        AfterVotes=[BellumCivile.ClientLiberationRules]::Readiness($postVotePower, $bloc, $scenario.Ratio); Votes=$voteRows }
}

'Production +30 resolve vs historical zero-resolve and hypothetical +20 alternatives.'
'Power is military strength + influence. Foreign allies/other clients retain production shares.'
'Fixtures assume a 200-influence filing cost, neutral traits, no other Crown bonuses or hostage penalties.'
'Council uses production commitments/quorum and role reserves; no ruler override or native campaign execution.'

$scenarios = @(
    (New-Scenario 'Moderate desire, some fatigue' (New-Clans 65 70)),
    (New-Scenario 'High desire, milder fatigue' (New-Clans 80 65)),
    (New-Scenario 'High desire, wartime fatigue' (New-Clans 80 60) -foreignWar $true),
    (New-Scenario 'Very high desire, milder fatigue' (New-Clans 90 60)),
    (New-Scenario 'Very high desire, tired realm' (New-Clans 90 55)),
    (New-Scenario 'Maximum desire, limited stamina' (New-Clans 100 45)),
    (New-Scenario 'Maximum desire, severe exhaustion' (New-Clans 100 30)),
    (New-Scenario 'Maximum desire, zero War Will' (New-Clans 100 0)),
    (New-Scenario 'Preparation lifts 60 to 80' (New-Clans 60 60) -courtBonus ([BellumCivile.CourtLiberationRules]::DesireBonus)),
    (New-Scenario 'Preparation lifts 40 to 60' (New-Clans 40 60) -courtBonus ([BellumCivile.CourtLiberationRules]::DesireBonus)),
    (New-Scenario 'Insufficient client power' (New-Clans 100 100) -suzerain 15000),
    (New-Scenario 'Settlement remains binding' (New-Clans 100 100) -cooldown 30),
    (New-Scenario 'War decision already pending' (New-Clans 100 100) -pending $true),
    (New-Scenario 'Desire below realm threshold' @(
        (New-Clan 'Crown' 20 100 1200 1500 $true), (New-Clan 'Glory' 30 100),
        (New-Clan 'Nobility' 40 100), (New-Clan 'Liberty' 100 100))),
    (New-Scenario 'Insufficient proposal influence' @(
        (New-Clan 'Crown' 100 100 400 1900 $true), (New-Clan 'Glory' 100 100 250 1000),
        (New-Clan 'Nobility' 100 100 250 1000), (New-Clan 'Liberty' 100 100 250 1000))),
    (New-Scenario 'Strategically deterred target' @(
        (New-Clan 'Crown' 100 100 1200 1500 $true -70), (New-Clan 'Glory' 100 100 600 600 $false -70),
        (New-Clan 'Nobility' 100 100 600 600 $false -70), (New-Clan 'Liberty' 100 100 600 600 $false -70))),
    (New-Scenario 'Defiant Crown, reluctant council' @(
        (New-Clan 'Crown' 100 45 1200 1500 $true), (New-Clan 'Glory' 40 20),
        (New-Clan 'Nobility' 50 25), (New-Clan 'Liberty' 60 10))),
    (New-Scenario 'Tired council, shared high desire' @(
        (New-Clan 'Crown' 90 55 1200 1500 $true), (New-Clan 'Glory' 90 25),
        (New-Clan 'Nobility' 90 25), (New-Clan 'Liberty' 90 25))),
    (New-Scenario 'Existing payment boundary' (New-Clans 100 80) -suzerain 7875 -allies 0 -otherClients 0)
)
$results = @(foreach ($scenario in $scenarios) {
    foreach ($cap in $caps) { Measure-Scenario $scenario $cap }
})
$summary = foreach ($scenario in $scenarios) {
    $rows = @($results | Where-Object { $_.Name -eq $scenario.Name })
    [pscustomobject]@{ Scenario=$scenario.Name; Desire=[Math]::Round($rows[0].RealmDesire, 1);
        Ready=[Math]::Round($rows[0].Readiness, 1); Previous=$rows[0].CanPropose;
        Cap20=$rows[1].CanPropose; Cap30=$rows[2].CanPropose }
    Check ($rows[0].Readiness -eq $rows[1].Readiness -and $rows[1].Readiness -eq $rows[2].Readiness) 'Resolve does not alter strength readiness.'
    Check ($rows[0].RealmDesire -eq $rows[1].RealmDesire -and $rows[1].RealmDesire -eq $rows[2].RealmDesire) 'Resolve does not inflate Liberty Desire.'
}
$summary | Format-Table -AutoSize | Out-String -Width 180
foreach ($name in @('High desire, wartime fatigue', 'Maximum desire, limited stamina',
        'Defiant Crown, reluctant council', 'Tired council, shared high desire')) {
    foreach ($row in @($results | Where-Object { $_.Name -eq $name })) {
        '{0}, cap {1}: can propose {2}; hypothetical council {3} yes / {4} no; quorum {5}; council passes {6}' -f `
            $name, $row.Cap, $row.CanPropose, $row.Yay, $row.Nay, $row.Quorum, $row.CouncilPass
    }
}
$mixed = $results | Where-Object { $_.Name -eq 'Defiant Crown, reluctant council' -and $_.Cap -eq 30 }
$mixed.Votes | Format-Table -AutoSize | Out-String -Width 180
Check ($mixed.CanPropose -and !$mixed.CouncilPass -and $mixed.Yay -eq 150 -and $mixed.Nay -eq 300) 'Resolve enables a sponsor, not an automatic council majority.'
foreach ($name in @('Maximum desire, severe exhaustion', 'Maximum desire, zero War Will',
        'Preparation lifts 40 to 60', 'Insufficient client power', 'Settlement remains binding',
        'War decision already pending', 'Desire below realm threshold', 'Insufficient proposal influence', 'Strategically deterred target')) {
    Check (@($results | Where-Object { $_.Name -eq $name -and $_.CanPropose }).Count -eq 0) "Preserved blocker: $name"
}
foreach ($row in @($results | Where-Object { $_.Name -eq 'Existing payment boundary' })) {
    Check ($row.CanPropose -and $row.AfterFiling -lt 100) 'Existing influence-payment readiness sensitivity is preserved.'
}
Check (($results | Where-Object { $_.Name -eq 'Preparation lifts 60 to 80' -and $_.Cap -eq 30 }).CanPropose) 'Preparations can help a tired sponsor through resolve.'

$counts = @{ 0=0; 20=0; 30=0 }
foreach ($desire in 0..100) {
    foreach ($will in 0..100) {
        $previousWill = -1.0; $previousEligible = $false; $previousUtility = [double]::NegativeInfinity
        foreach ($cap in $caps) {
            $bonus = Resolve-Bonus $desire $cap
            $effective = Effective-Will $will $desire $cap
            $eligible = $desire -ge $desireThreshold -and $effective -ge $declarationThreshold
            $target = $desire * [BellumCivile.BellumCivileConstants]::ClientLiberationTargetScoreScale
            if ($target -lt $minimumTargetScore) { $target = 0 }
            $voteUtility = $effective - $utilityOffset + $target * $targetUtilityScale
            Check ($bonus -ge 0 -and $bonus -le $cap -and $effective -le 100) 'Resolve and willingness bounds.'
            Check ($effective -ge $previousWill -and $voteUtility -ge $previousUtility) 'Resolve cannot reduce willingness or council utility.'
            Check (!$previousEligible -or $eligible) 'A larger resolve cap cannot disable a willing sponsor.'
            Check ($will -ge 45 -or !$eligible) 'Resolve never lets a sponsor below 45 real War Will initiate liberation.'
            Check ((Effective-Will $will $desire $cap $false) -eq $will) 'Unrelated wars receive no resolve.'
            if ($desire -le $desireThreshold) { Check ($bonus -eq 0) 'Resolve begins only above the personal desire threshold.' }
            if ($eligible) { $counts[$cap]++ }
            $previousWill = $effective; $previousEligible = $eligible; $previousUtility = $voteUtility
        }
    }
}
Check ($counts[0] -eq 1066 -and $counts[20] -eq 1466 -and $counts[30] -eq 1666) 'Controlled integer willingness grid totals.'
'Controlled grid: all other gates satisfied; 10,201 equally weighted desire/War Will pairs, not campaign probabilities.'
foreach ($cap in $caps) { 'Cap {0}: {1} willing sponsor combinations.' -f $cap, $counts[$cap] }
foreach ($ratio in @(0.4, [BellumCivile.BellumCivileConstants]::RebellionPowerThresholdBase, 1.2)) {
    Check ([Math]::Abs([BellumCivile.ClientLiberationRules]::Readiness(1000 * $ratio, 1000, $ratio) - 100) -lt 0.001) 'Personality-based power threshold remains unchanged.'
}

function Recovery-Days([double]$start, [double]$desire, [double]$cap, [double]$soft, [double]$overflow) {
    $will = $start
    for ($day = 0; $day -le 400; $day++) {
        if ((Effective-Will $will $desire $cap) -ge $declarationThreshold) { return $day }
        $step = if ($will -ge $soft) { $overflow } else { 1.0 }
        $will = [Math]::Min(100.0, $will + $step)
    }
    throw 'Recovery fixture never reaches willingness.'
}
$recovery = @(foreach ($ideology in @('Crown', 'Liberty')) {
    $soft = if ($ideology -eq 'Crown') { 45 } else { 40 }
    $overflow = if ($ideology -eq 'Crown') { [BellumCivile.BellumCivileConstants]::WarPeaceRevampUnaffiliatedOverflowRecoveryMultiplier }
        else { [BellumCivile.BellumCivileConstants]::WarPeaceRevampPopulistOverflowRecoveryMultiplier }
    foreach ($desire in @(80, 90, 100)) {
        [pscustomobject]@{ Clan=$ideology; Desire=$desire; StartingWill=20;
            CurrentDays=(Recovery-Days 20 $desire 0 $soft $overflow);
            Cap20Days=(Recovery-Days 20 $desire 20 $soft $overflow);
            Cap30Days=(Recovery-Days 20 $desire 30 $soft $overflow) }
    }
})
'Recovery: days to willingness only, uninterrupted peace, neutral Crown traits, no event shocks.'
$recovery | Format-Table -AutoSize | Out-String -Width 180
foreach ($row in $recovery) {
    Check ($row.Cap30Days -le $row.Cap20Days -and $row.Cap20Days -le $row.CurrentDays) 'Resolve shortens rather than eliminates recovery.'
}
$wartime = @(foreach ($ideology in @('Crown', 'Liberty')) {
    $drain = if ($ideology -eq 'Crown') { 0.65 } else { 1.25 }
    foreach ($days in @(30, 60, 100)) {
        $will = [Math]::Max(0.0, 100.0 - $days * $drain)
        [pscustomobject]@{ Clan=$ideology; WarDays=$days; Desire=80; ActualWill=$will;
            Current=((Effective-Will $will 80 0) -ge $declarationThreshold);
            Cap20=((Effective-Will $will 80 20) -ge $declarationThreshold);
            Cap30=((Effective-Will $will 80 30) -ge $declarationThreshold) }
    }
})
'Compulsory-war illustration: starts at 100 War Will, one front, no battle/event shocks; willingness only.'
$wartime | Format-Table -AutoSize | Out-String -Width 180
"PASS: $script:checks simulation assertions using production +30 resolve; this runner does not change campaign or save data."
