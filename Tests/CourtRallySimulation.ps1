$ErrorActionPreference='Stop'
# Proposed design arithmetic only. No production modifier, treaty action or campaign is invoked.
$script:checks=0
function Check([bool]$ok,[string]$label) { if(!$ok){throw $label}; $script:checks++ }
function Clamp([double]$value,[double]$low=0,[double]$high=100) { [Math]::Max($low,[Math]::Min($high,$value)) }
function Effective([double]$base,[bool]$eligible=$true) { Clamp ($base+$(if($eligible){15}else{0})) }
function Result([bool]$verified,[bool]$white,[int]$recognized,[double]$net,[bool]$ongoing=$false) {
    if(!$verified){return 0}
    if($ongoing){return -5}
    if($white){return -10}
    if($recognized -gt 0){return 10}
    if($recognized -lt 0){return -10}
    if($net -ge 10){return 10}
    return -10
}
function Scoped([double]$base,[string]$war,[bool]$member,[bool]$crown,[bool]$favored,[bool]$player,[double]$now,[double]$end) {
    Effective $base ($war -eq 'original_foreign' -and !$player -and ($member -or ($crown -and $favored)) -and $now -ge 20 -and $now -lt $end)
}

'Enthusiasm and isolated peace utility (other motives held at zero):'
@(foreach($base in @(0,10,11,25,40,55,85,95,100)) {
    $effective=Effective $base
    [pscustomobject]@{Base=$base; Effective=$effective; OrdinaryBefore=55-$base; OrdinaryAfter=55-$effective;
        TreatyBefore=(50-$base)*0.5; TreatyAfter=(50-$effective)*0.5}
}) | Format-Table -AutoSize | Out-String -Width 150 | Write-Output

foreach($step in 0..1000) {
    $base=$step/10.0; $effective=Effective $base; $gain=$effective-$base
    Check ($gain -ge -0.00001 -and $gain -le 15.00001 -and $effective -le 100) 'Scoped increase must be capped.'
    Check ((Effective $base $false) -eq $base) 'Ineligible base changed.'
    Check ([Math]::Abs(((55-$effective)-(55-$base))+$gain) -lt 0.00001) 'Ordinary utility delta mismatch.'
    Check ([Math]::Abs((((50-$effective)*0.5)-((50-$base)*0.5))+$gain*0.5) -lt 0.00001) 'Treaty snapshot delta mismatch.'
    Check (($effective -le 25) -eq ($base -le 10)) 'Default peace-proposer boundary mismatch.'
    foreach($other in @(-30,0,30)) {
        Check ((55-$effective+$other) -le (55-$base+$other)) 'Rally cannot increase peace desire.'
        Check (((50-$effective)*0.5+$other+15) -ge ((50-$base)*0.5+$other)) 'Liberty counterweight unexpectedly weaker than rally delta.'
    }
}

'Power-weighted realm averages: uniform base20, other enemy also exhausted; default threshold25.'
foreach($share in @(0,0.2,(1.0/3),0.4,0.6,1)) {
    $average=20+15*$share
    'Rallied power {0:P1}: average {1:N2}; ordinary mutual-exhaustion gate {2}' -f $share,$average,($average -le 25)
    Check ($average -ge 20 -and $average -le 35) 'Realm average outside weighted bounds.'
}
foreach($base in @(0,10,20,25,50,95)) {
    foreach($share in @(0,0.1,0.5,1)) {
        $average=(Effective $base)*$share+$base*(1-$share)
        foreach($threshold in @(10,25,40)) {
            if($base+15*$share -le $threshold){Check ($average -le $threshold) 'A capped rally cannot exceed the uncapped gate prediction.'}
            if($base -gt $threshold){Check ($average -gt $threshold) 'Rally cannot make an exhausted-peace gate easier.'}
        }
    }
}

foreach($war in @('original_foreign','other_foreign','civil','restarted_foreign')) {
    foreach($role in @('member','outsider','aligned_crown','unaligned_crown','player_member','departed')) {
        foreach($day in @(19,20,40.99,41,70,84)) {
            $member=$role -in @('member','player_member'); $crown=$role -in @('aligned_crown','unaligned_crown')
            $actual=Scoped 40 $war $member $crown ($role -eq 'aligned_crown') ($role -eq 'player_member') $day 41
            $expected=if($war -eq 'original_foreign' -and $role -in @('member','aligned_crown') -and $day -ge 20 -and $day -lt 41){55}else{40}
            Check ($actual -eq $expected) 'Named war, member, Crown, player or expiry scope leaked.'
        }
    }
}
# Independent front selection, not an early return after encountering the rallied war.
$fronts=@((Effective 20),20)
Check (@($fronts | Where-Object {$_ -le 25}).Count -eq 1) 'Unrallied front should remain available for peace.'
foreach($year in @(24,84,365)) {
    foreach($remaining in @(1,5,30,100)) {
        $end=20+[Math]::Min($year/4.0,$remaining)
        Check ($end -le 20+$remaining -and $end -le 20+$year/4.0) 'Boost exceeds season or original term.'
        Check ((Scoped 40 'original_foreign' $true $false $false $false $end $end) -eq 40) 'Expiry mutates base or leaves modifier active.'
    }
}

'Settlement fixtures (net values are frozen verified concessions, not UsedWarScore):'
$fixtures=@(
    @{Name='Opponent concedes';Verified=$true;White=$false;Recognized=1;Net=0;Expected=10},
    @{Name='We concede despite positive battlefield score';Verified=$true;White=$false;Recognized=-1;Net=20;Expected=-10},
    @{Name='White peace despite occupations';Verified=$true;White=$true;Recognized=0;Net=30;Expected=-10},
    @{Name='Gain30, offer30';Verified=$true;White=$false;Recognized=0;Net=0;Expected=-10},
    @{Name='Gain30, offer10';Verified=$true;White=$false;Recognized=0;Net=20;Expected=10},
    @{Name='Reciprocal prisoners or consensual marriage only';Verified=$true;White=$false;Recognized=0;Net=0;Expected=-10},
    @{Name='Unverified native peace';Verified=$false;White=$false;Recognized=0;Net=50;Expected=0},
    @{Name='Third-party elimination';Verified=$false;White=$false;Recognized=0;Net=0;Expected=0},
    @{Name='Proposed but undelivered terms';Verified=$false;White=$false;Recognized=0;Net=30;Expected=0},
    @{Name='Unfinished campaign';Verified=$true;White=$false;Recognized=0;Net=0;Ongoing=$true;Expected=-5}
)
foreach($f in $fixtures) {
    $value=Result $f.Verified $f.White $f.Recognized $f.Net ([bool]$f.Ongoing)
    Check ($value -eq $f.Expected) "Settlement fixture failed: $($f.Name)"
    '{0}: {1:+0;-0;0} approval' -f $f.Name,$value
}
foreach($gain in 0..50) {
    foreach($offering in 0..50) {
        $net=$gain-$offering; $value=Result $true $false 0 $net
        Check (($value -eq 10) -eq ($net -ge 10)) 'Material-settlement threshold mismatch.'
        Check (!((Result $true $false 0 $net) -eq 10 -and (Result $true $false 0 (-$net)) -eq 10)) 'Both realms cannot claim net-material victory.'
    }
}
foreach($day in @(83.9,84,84.1,90)) {
    $value=if($day -le 84){Result $true $false 1 0}else{-5}
    Check ($value -eq $(if($day -le 84){10}else{-5})) 'Late victory cannot undo a settled expiry.'
}

'Repeated annual terms: frozen base40, session day10, vanilla year84; no battle/drift model.'
foreach($term in 0..9) {
    $start=$term*84; $session=$start+10; $end=$session+21
    $base=40
    $active=Effective $base; $expired=Effective $base $false
    Check ($active -eq 55 -and $expired -eq 40) 'Repeated terms accumulate enthusiasm.'
    Check (($end-$session)/84.0 -eq 0.25) 'Annual fixture duty cycle mismatch.'
}
'Annual fixture: 21/84 days active (25%); newly renewed terms do not compound the bonus.'
'Expected mood delta sensitivity (synthetic outcomes, NOT predicted campaign frequencies):'
foreach($p in @(0,(1.0/3),0.5,0.75,1)) {
    'Victory share {0:P1}: remaining outcomes all unresolved = {1:N2}; all failed peace/defeat = {2:N2}' -f $p,(10*$p-5*(1-$p)),(10*$p-10*(1-$p))
}
foreach($mood in @(-100,-95,0,95,100)) {
    foreach($delta in @(-10,-5,0,10)) {
        $after=Clamp ($mood+$delta) -100 100
        Check ($after -ge -100 -and $after -le 100) 'Approval cap exceeded.'
    }
}
"PASS: $script:checks proposed-design assertions. No production execution, treaty delivery, save/load or campaign-rate simulation."
