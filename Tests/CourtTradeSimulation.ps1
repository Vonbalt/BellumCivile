$ErrorActionPreference = 'Stop'
# Design simulation: inspected native arithmetic, not a native election or campaign.
# Fixed sponsors, unmodified 20/60/150 support costs; no perks or runtime patches invoked.
function Clamp([double]$x, [double]$low, [double]$high) { [Math]::Max($low, [Math]::Min($high, $x)) }
function Score([double]$base, [bool]$eligible = $true) {
    Clamp ($base + $(if ($eligible) { 15 } else { 0 })) 0 100
}
$script:checks = 0
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }; $script:checks++
}
function Preference([double]$yes) {
    if ($yes -gt 50) { 'Yes' } elseif ($yes -lt 50) { 'No' } else { 'Tie' }
}
function Vote([double]$yes, [double]$influence, [double]$reserve,
    [double]$yesLikelihood = 0.5, [double]$relation = 0) {
    # Trade initial candidates enumerate Yes first, hence exact utility ties select Yes.
    $side = if ($yes -ge 50) { 'Yes' } else { 'No' }
    $best = if ($side -eq 'Yes') { $yes } else { 100 - $yes }
    $likelihood = if ($side -eq 'Yes') { $yesLikelihood } else { 1 - $yesLikelihood }
    $spend = $best
    if ($influence -lt 2 * $spend) { $spend = [Math]::Min($spend * 0.5, $influence * 0.7) }
    elseif ($influence -gt 10 * $spend) { $spend *= 1.5 }
    if ($likelihood -gt 0.65) { $spend *= 1.6 * (1.2 - $likelihood) }
    $spend *= 1 + 0.008 * (Clamp $relation -100 100)
    $points = if ($spend -gt 150) { 3 } elseif ($spend -gt 60) { 2 } elseif ($spend -gt 20) { 1 } else { 0 }
    $costs = @(0,20,60,150)
    while ($points -gt 0 -and $influence + 0.001 -lt $reserve + $costs[$points]) { $points-- }
    [pscustomobject]@{ Side = $(if ($points) { $side } else { 'Abstain' }); Points = $points; Cost = $costs[$points] }
}

'Scalar support: +15 Yes utility; No remains its complement.'
@(20,34,35,36,40,49,50,60,85,95,100) | ForEach-Object {
    $after = Score $_
    [pscustomobject]@{ BeforeYes = $_; BeforeNo = 100-$_; AfterYes = $after; AfterNo = 100-$after;
        Before = (Preference $_); After = (Preference $after) }
} | Format-Table -AutoSize | Out-String -Width 150 | Write-Output

foreach ($n in 0..1000) {
    $base = $n / 10.0; $after = Score $base
    Check ($after -ge $base -and $after -le 100) 'Bounded bonus decreased support or exceeded cap.'
    Check ((Score $base $false) -eq $base) 'Ineligible voter changed.'
    Check ([Math]::Abs(($after - (100-$after)) - ($base - (100-$base)) - 2*($after-$base)) -lt 0.00001) 'Yes/No gap mismatch.'
    Check (($after -gt 50) -eq ($base -gt 35)) 'Strict Yes preference boundary changed.'
}

'Conditional individual votes: likelihood 0.5, neutral sponsor relation, no perks.'
foreach ($fixture in @(
    @{Name='Opposed'; Base=20; Influence=300; Reserve=0},
    @{Name='Persuadable'; Base=40; Influence=300; Reserve=0},
    @{Name='Stronger endorsement'; Base=49; Influence=300; Reserve=0},
    @{Name='Low influence'; Base=40; Influence=25; Reserve=0},
    @{Name='Protected budget'; Base=40; Influence=100; Reserve=100}
)) {
    $before = Vote $fixture.Base $fixture.Influence $fixture.Reserve
    $after = Vote (Score $fixture.Base) $fixture.Influence $fixture.Reserve
    Check ($after.Cost -le [Math]::Max(0,$fixture.Influence-$fixture.Reserve)) 'Vote spent protected influence.'
    '{0}: {1}/{2} points -> {3}/{4} points; cost {5}->{6}' -f $fixture.Name,$before.Side,$before.Points,$after.Side,$after.Points,$before.Cost,$after.Cost
}
Check ((Vote 40 100 100).Points -eq 0) 'Budget fixture should abstain.'
Check ((Vote 55 100 100).Points -eq 0) 'Motion bypassed budget fixture.'
foreach ($base in @(20,35,40,49,60,85,100)) {
    foreach ($influence in @(0,25,100,300,1000)) {
        foreach ($reserve in @(0,100,200)) {
            foreach ($relation in @(-100,0,100)) {
                foreach ($likelihood in @(0.25,0.5,0.8)) {
                    $v = Vote (Score $base) $influence $reserve $likelihood $relation
                    Check ($v.Cost -le [Math]::Max(0,$influence-$reserve)) 'Sensitivity grid breached reserve.'
                    Check ($v.Points -ge 0 -and $v.Points -le 3) 'Sensitivity grid produced invalid weight.'
                }
            }
        }
    }
}

function Council([double[]]$bases, [int[]]$participants, [bool]$motion) {
    $scores = for ($i=0; $i -lt $bases.Count; $i++) { Score $bases[$i] ($motion -and $participants -contains $i) }
    $likelihood = ($scores | Measure-Object -Average).Average / 100
    $yes=0; $no=0
    foreach ($score in $scores) {
        $v = Vote $score 300 0 $likelihood 0
        if ($v.Side -eq 'Yes') { $yes += $v.Points } elseif ($v.Side -eq 'No') { $no += $v.Points }
    }
    # Same support-share denominator as native DetermineOfficialSupport, not a win probability.
    [pscustomobject]@{ Yes=$yes; No=$no; Share=$yes/($yes+$no+0.001) }
}
'Synthetic councils: six NPC voters, influence 300, reserve 0, neutral fixed sponsors.'
foreach ($case in @(
    @{Name='Two supporters cannot overturn four opponents'; Bases=@(40,40,40,40,40,40); Members=@(0,1)},
    @{Name='Three swing lords'; Bases=@(40,40,40,65,20,20); Members=@(0,1,2)},
    @{Name='Two Liberty lords, Crown unaligned'; Bases=@(40,40,40,65,20,20); Members=@(0,1)},
    @{Name='Same council, Crown aligned'; Bases=@(40,40,40,65,20,20); Members=@(0,1,2)}
)) {
    $a=Council $case.Bases $case.Members $false; $b=Council $case.Bases $case.Members $true
    '{0}: Yes/No {1}/{2} -> {3}/{4}; support share {5:P1}->{6:P1}' -f $case.Name,$a.Yes,$a.No,$b.Yes,$b.No,$a.Share,$b.Share
    Check ($a.Share -ge 0 -and $b.Share -lt 1) 'Invalid council share.'
}
$foreignBefore=Council @(40,40,40,40,40,40) @() $false
$unaligned=Council @(40,40,40,65,20,20) @(0,1) $true
$aligned=Council @(40,40,40,65,20,20) @(0,1,2) $true
Check ($unaligned.Yes -eq 4 -and $unaligned.No -eq 5) 'Unaligned fixture changed.'
Check ($aligned.Yes -eq 5 -and $aligned.No -eq 4) 'Aligned fixture changed.'
$foreignAfter=Council @(40,40,40,40,40,40) @() $true
Check ($foreignBefore.Share -eq $foreignAfter.Share) 'Domestic initiative affected foreign voters.'
Check ($foreignAfter.Share -lt 0.5) 'Foreign refusal fixture unexpectedly passed.'
'Foreign refusal fixture: unchanged by domestic Liberty support; proposal remains blocked.'

'Mood accounting: target-baseline contribution changes are NOT instantaneous mood shocks.'
foreach ($case in @(
    @{Name='First agreement through motion'; Before=-10; After=10; Shock=10},
    @{Name='Additional agreement through motion'; Before=10; After=10; Shock=10},
    @{Name='First agreement without motion'; Before=-10; After=10; Shock=0},
    @{Name='Unfulfilled motion, still no agreement'; Before=-10; After=-10; Shock=-10},
    @{Name='Unfulfilled motion, another agreement remains'; Before=10; After=10; Shock=-10}
)) {
    '{0}: standing component {1}->{2}; one-time shock {3:+0;-0;0}' -f $case.Name,$case.Before,$case.After,$case.Shock
    foreach ($mood in @(-95,-60,0,60,95)) {
        $after = Clamp ($mood+$case.Shock) -100 100
        Check ($after -ge -100 -and $after -le 100) 'Mood shock exceeded bounds.'
    }
}
"PASS: $script:checks design assertions. No campaign passage rates, native election execution, UI or save/load tested."
