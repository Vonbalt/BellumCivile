param([switch]$SummaryOnly, [switch]$SampleOnly)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Compare-CourtCrownInterest.ps1" -CrownHelpersOnly

# Representative policies with approved stances. Optional personal profiles are
# deliberately absent: these tests exercise the minimum XML-only integration.
$policies = @(
    @{name='Royal Privilege';s=@(-1,0,-1);c=$true},
    @{name='Royal Guard';s=@(-1,1,-1);c=$true},
    @{name='Land Tax';s=@(-1,0,0);c=$true},
    @{name='Sacred Majesty';s=@(-1,0,-1);c=$true},
    @{name='Senate';s=@(1,0,0);c=$false},
    @{name='Serfdom';s=@(1,0,-1);c=$false},
    @{name='Military Coronae';s=@(0,1,0);c=$false},
    @{name='Trial by Jury';s=@(0,0,1);c=$false},
    @{name='Forgiveness of Debts';s=@(-1,0,1);c=$false},
    @{name='Coastal Guard';s=@(1,1,1);c=$false}
)
# Ordered active sets; all four centralizing policies above, then shared defence.
$activeSets = @(@(), @('Royal Privilege'), @('Royal Privilege','Royal Guard','Land Tax','Sacred Majesty'), @('Coastal Guard'))
function Mood($active,[int]$bloc,[double]$other) {
    $m = $other
    foreach ($p in $policies) { if ($active -contains $p.name) {
        $m += 10 * $p.s[$bloc]
        if ($p.c) { $m -= 5 }
    } }
    return Clamp $m -100 100
}
function Relation($a,$b,[double]$rulerRelation) {
    if ($a.id -eq $b.id) { return 0 } # Neutral self-relation assumption, explicit.
    if ($a.ruler -or $b.ruler) { return $rulerRelation }
    if ($a.bloc -eq $b.bloc) { return 40 }
    return 0
}
function Election($houses,$p,$active,[int]$sponsorId,[double]$otherMood,[double]$rulerRelation,[bool]$war) {
    $sponsor = $houses[$sponsorId]
    $reserve = $clanReserve; if ($sponsor.ruler) { $reserve = $rulerReserve; if ($war) { $reserve += $warExtra } }
    # Default proposal cost, no perk/council discount. Check filing AND subsequent
    # commitment using the reduced balance, not the same funds twice.
    $canFile = $sponsor.balance -ge (100 + [Math]::Max($reserve,100))
    $repeal = $active -contains $p.name
    $votes = @(); $yesInitial=0.0; $noInitial=0.0
    foreach ($h in $houses) {
        $r = Relation $h $sponsor $rulerRelation
        $score = CrownMerit $p.s[$h.bloc] $h.honor 0 70 $h.ruler $p.c $repeal
        if ($h.id -ne $sponsorId) {
            $score += Clamp ($r * 0.2) -20 20
            if ($sponsor.ruler) { $score += Clamp ((Mood $active $h.bloc $otherMood)*0.15) -15 15 }
        }
        $funds=$h.balance; if ($h.id -eq $sponsorId) { $funds -= 100 }
        $protected=$clanReserve; if ($h.ruler) { $protected=$rulerReserve; if ($war) { $protected += $warExtra } }
        $votes += [pscustomobject]@{h=$h;score=$score;balance=$funds;reserve=$protected;vote=$null}
        $yesInitial += Clamp $score 0 100
        $noInitial += Clamp (-$score) 0 100
    }
    $yesLikelihood=0.0; $noLikelihood=0.0
    if (($yesInitial+$noInitial) -gt 0) {
        $yesLikelihood=$yesInitial/($yesInitial+$noInitial); $noLikelihood=1-$yesLikelihood
    }
    # Vanilla elects the opposition sponsor from its strongest first-pass
    # supporters; the second pass adjusts spending, not side, for sponsor ties.
    $opposition=$null; $best=0
    foreach ($v in $votes) {
        $likelihood=$yesLikelihood; if ($v.score -lt 0) { $likelihood=$noLikelihood }
        $v.vote=Commitment $v.score $v.balance $v.reserve $likelihood 0
        if ($v.score -lt 0 -and $v.vote.points -gt $best) { $best=$v.vote.points; $opposition=$v.h }
    }
    $yes=0; $no=0; $ownCost=0; $sponsorCost=0; $yesHouses=0; $noHouses=0
    foreach ($v in $votes) {
        $likelihood=$yesLikelihood; $rel=0
        if ($v.score -gt 0) { $rel=Relation $v.h $sponsor $rulerRelation }
        if ($v.score -lt 0) { $likelihood=$noLikelihood; if ($null -ne $opposition) { $rel=Relation $v.h $opposition $rulerRelation } }
        $v.vote=Commitment $v.score $v.balance $v.reserve $likelihood $rel
        Check ($v.vote.cost -eq 0 -or $v.balance-$v.vote.cost -ge $v.reserve) 'Election reserve breach'
        if ($v.vote.side -eq 'Pass') { $yes += $v.vote.points; $yesHouses++ }
        if ($v.vote.side -eq 'Reject') { $no += $v.vote.points; $noHouses++ }
        if ($v.h.ruler) { $ownCost=$v.vote.cost }
        if ($v.h.id -eq $sponsorId) { $sponsorCost=$v.vote.cost }
    }
    $sponsorVote=$votes[$sponsorId]
    $status='NoMajority'
    if ($yes -gt $no) { $status='Majority' }
    if ($yes -eq $no) { $status='Tie' }
    if ($yes -eq 0 -and $no -eq 0) { $status='NoVotes' }
    $override=0.0; $king=$votes[0]
    if ($no -gt $yes -and $king.score -gt 0) {
        $cost=NpcOverrideCost ($no-$yes) ($active -contains 'Royal Privilege')
        $override=OverrideChance $king.score $king.balance $king.reserve $ownCost $cost 20
    }
    $viable=$canFile -and $sponsorVote.score -gt 0 -and $sponsorVote.vote.side -eq 'Pass' -and $status -eq 'Majority'
    $maxSponsorPoints=0
    foreach ($level in @(@(20,1),@(60,2),@(150,3))) {
        if ($sponsorVote.balance - $level[0] -ge $sponsorVote.reserve) { $maxSponsorPoints=$level[1] }
    }
    return [pscustomobject]@{policy=$p.name;repeal=$repeal;sponsor=$sponsorId;can_file=$canFile;sponsor_score=$sponsorVote.score;
        sponsor_cost=$sponsorCost;yes=$yes;no=$no;status=$status;override_pct=$override;viable=$viable;
        sponsor_points=$sponsorVote.vote.points;max_sponsor_points=$maxSponsorPoints;
        yes_houses=$yesHouses;no_houses=$noHouses;house_count=$houses.Count}
}

function ProposalQualifies($r,[int]$threshold,[bool]$requireOtherSupport) {
    if (-not $r.can_file -or $r.sponsor_score -le 0 -or $r.sponsor_cost -le 0 -or $r.yes -le 0) { return $false }
    if ($requireOtherSupport -and $r.house_count -gt 1 -and $r.yes_houses -lt 2) { return $false }
    return 100 * $r.yes -ge $threshold * ($r.yes + $r.no)
}
$edgeCases=@(
    @{name='30 percent boundary';yes=3;no=7;yes_houses=2;house_count=7;expected=@($true,$false,$false)},
    @{name='One third';yes=2;no=4;yes_houses=2;house_count=7;expected=@($true,$false,$false)},
    @{name='35 percent boundary';yes=7;no=13;yes_houses=3;house_count=10;expected=@($true,$true,$false)},
    @{name='Three eighths';yes=3;no=5;yes_houses=2;house_count=7;expected=@($true,$true,$false)},
    @{name='40 percent boundary';yes=2;no=3;yes_houses=2;house_count=7;expected=@($true,$true,$true)},
    @{name='Tied';yes=2;no=2;yes_houses=2;house_count=7;expected=@($true,$true,$true)},
    @{name='Only sponsor, six abstain';yes=1;no=0;yes_houses=1;house_count=7;expected=@($false,$false,$false)},
    @{name='Two supporters, five abstain';yes=2;no=0;yes_houses=2;house_count=7;expected=@($true,$true,$true)},
    @{name='No votes';yes=0;no=0;yes_houses=0;house_count=7;expected=@($false,$false,$false)}
)
foreach ($e in $edgeCases) {
    $e.can_file=$true; $e.sponsor_score=40; $e.sponsor_cost=20
    $i=0
    foreach ($threshold in @(30,35,40)) {
        Check ((ProposalQualifies $e $threshold $true) -eq $e.expected[$i]) "Threshold edge $($e.name) / $threshold"
        $i++
    }
}
Check (-not (ProposalQualifies @{can_file=$false;sponsor_score=40;sponsor_cost=20;yes=3;no=0} 30 $false)) 'Filing gate'
Check (-not (ProposalQualifies @{can_file=$true;sponsor_score=40;sponsor_cost=0;yes=3;no=0} 30 $false)) 'Commitment gate'

Check ((Mood @('Royal Privilege','Royal Guard','Land Tax','Sacred Majesty') 0 40) -eq -20) 'Nobility policy pressure'
Check ((Mood @('Royal Privilege','Royal Guard','Land Tax','Sacred Majesty') 1 40) -eq 30) 'Glory policy pressure'
Check ((Mood @('Royal Privilege','Royal Guard','Land Tax','Sacred Majesty') 2 40) -eq -10) 'Liberty policy pressure'
$worlds=[Collections.Generic.List[object]]::new(); $rows=[Collections.Generic.List[object]]::new(); $worldId=0
foreach ($mix in @(@(0,1,2),@(0,0,1,1,2,2),@(0,0,0,0,1,2),@(0,1,1,1,1,2),@(0,1,2,2,2,2))) {
 foreach ($kingBloc in 0..2) { foreach ($balance in @(200,350,650,1000)) {
 foreach ($other in @(0,40)) { foreach ($friendship in @(0,80)) { foreach ($war in @($false,$true)) {
 foreach ($active in $activeSets) {
    if ($SampleOnly -and (($mix -join ',') -ne '0,0,1,1,2,2' -or $kingBloc -ne 0 -or $balance -ne 1000 -or $other -ne 40 -or $friendship -ne 80 -or $war)) { continue }
    $houses=@([pscustomobject]@{id=0;bloc=$kingBloc;ruler=$true;honor=0;balance=$balance})
    foreach ($bloc in $mix) { $houses += [pscustomobject]@{id=$houses.Count;bloc=$bloc;ruler=$false;honor=0;balance=$balance} }
    $worldId++; $crownViable=0; $blocViable=@(0,0,0); $tested=0
    foreach ($p in $policies) {
        $sponsors=@()
        if ($p.c -and $active -notcontains $p.name) { $sponsors += 0 }
        foreach ($bloc in 0..2) {
            $stance=$p.s[$bloc]; if ($active -contains $p.name) { $stance=-$stance }
            if ($stance -gt 0) { $sponsors += ($houses | Where-Object { -not $_.ruler -and $_.bloc -eq $bloc } | Select-Object -First 1).id }
        }
        foreach ($sponsorId in $sponsors) {
            $r=Election $houses $p $active $sponsorId $other $friendship $war
            if ($r.viable) { if ($sponsorId -eq 0) { $crownViable++ } else { $blocViable[$houses[$sponsorId].bloc]++ } }
            $rows.Add([pscustomobject]@{world=$worldId;balance=$balance;war=$war;active_count=$active.Count;
                ruler_bloc=$kingBloc;sponsor_is_ruler=($sponsorId -eq 0);result=$r})
            $tested++
        }
    }
    $worlds.Add([pscustomobject]@{id=$worldId;mix=($mix -join ',');ruler_bloc=$kingBloc;balance=$balance;
        other_mood=$other;ruler_relation=$friendship;war=$war;active=($active -join ',');crown_viable=$crownViable;bloc_viable=$blocViable;tested=$tested})
 } } } } } }
}
$summary=@()
foreach ($b in @(200,350,650,1000)) {
    $w=@($worlds | Where-Object balance -eq $b); $r=@($rows | Where-Object balance -eq $b)
    $summary += [pscustomobject]@{balance=$b;worlds=$w.Count;motions=$r.Count;
        viable=@($r | Where-Object {$_.result.viable}).Count;
        no_votes=@($r | Where-Object {$_.result.status -eq 'NoVotes'}).Count;
        override_only=@($r | Where-Object {$_.result.can_file -and $_.result.override_pct -gt 0 -and -not $_.result.viable}).Count;
        no_bloc_agenda=@($w | Where-Object {($_.bloc_viable | Measure-Object -Sum).Sum -eq 0}).Count}
}
Check (@($rows | Where-Object {$_.result.viable -and (-not $_.result.can_file -or $_.result.yes -le $_.result.no)}).Count -eq 0) 'Invalid viable proposal'
$thresholdResults=@()
foreach ($threshold in @(30,35,40)) {
 foreach ($guard in @($false,$true)) {
    $accepted=0; $majorities=0; $ties=0; $minorities=0; $sponsorOnly=0; $lowTurnout=0; $spendingCanWin=0
    $worldsWithBloc=[Collections.Generic.HashSet[int]]::new()
    $byBalance=@{'200'=0;'350'=0;'650'=0;'1000'=0}
    foreach ($row in $rows) {
        $r=$row.result
        if (-not (ProposalQualifies $r $threshold $guard)) { continue }
        $accepted++; $byBalance[[string]$row.balance]++
        if (-not $row.sponsor_is_ruler) { [void]$worldsWithBloc.Add($row.world) }
        if ($r.yes -gt $r.no) { $majorities++ }
        elseif ($r.yes -eq $r.no) { $ties++ }
        else {
            $minorities++
            if ($r.yes - $r.sponsor_points + $r.max_sponsor_points -gt $r.no) { $spendingCanWin++ }
        }
        if ($r.yes_houses -eq 1) { $sponsorOnly++ }
        if (2*($r.yes_houses+$r.no_houses) -lt $r.house_count) { $lowTurnout++ }
    }
    $thresholdResults += [pscustomobject]@{threshold=$threshold;require_other_support=$guard;accepted=$accepted;
        expected_majorities=$majorities;ties=$ties;contested_minorities=$minorities;sponsor_only=$sponsorOnly;
        below_half_turnout=$lowTurnout;minority_winnable_by_sponsor_spending=$spendingCanWin;
        worlds_with_bloc_agenda=$worldsWithBloc.Count;by_balance=$byBalance}
 }
}
$output=[ordered]@{assertions='passed';world_count=$worlds.Count;motion_count=$rows.Count;summary=$summary;threshold_comparison=$thresholdResults}
$selectiveGuard=@()
foreach ($threshold in @(30,35,40)) {
    $unguarded=$thresholdResults | Where-Object {$_.threshold -eq $threshold -and -not $_.require_other_support}
    $guarded=$thresholdResults | Where-Object {$_.threshold -eq $threshold -and $_.require_other_support}
    $selectiveGuard += [pscustomobject]@{threshold=$threshold;
        accepted=($unguarded.expected_majorities+$guarded.ties+$guarded.contested_minorities);
        expected_majorities=$unguarded.expected_majorities;ties=$guarded.ties;contested_minorities=$guarded.contested_minorities}
}
$output.selective_guard_comparison=$selectiveGuard
foreach ($guard in @($false,$true)) {
    $ordered=@($thresholdResults | Where-Object require_other_support -eq $guard | Sort-Object threshold)
    Check ($ordered[0].accepted -ge $ordered[1].accepted -and $ordered[1].accepted -ge $ordered[2].accepted) 'Threshold monotonicity'
}
foreach ($threshold in @(30,35,40)) {
    $pair=@($thresholdResults | Where-Object threshold -eq $threshold)
    Check ($pair[0].accepted -ge $pair[1].accepted -and $pair[1].sponsor_only -eq 0) 'Participation guard'
}
if (-not $SummaryOnly) { $output.worlds=$worlds; $output.motions=$rows }
[pscustomobject]$output | ConvertTo-Json -Depth 7
