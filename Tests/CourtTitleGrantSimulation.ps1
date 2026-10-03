$ErrorActionPreference='Stop'
# Proposed design arithmetic only. Rank is a proxy for political value, not a live
# forecast of hierarchy, service obligations or clan power. No campaign is mutated.
function Clamp([double]$x,[double]$lo,[double]$hi) { [Math]::Max($lo,[Math]::Min($hi,$x)) }
function Acceptance([int]$rank,[string]$claim,[double]$relation,[int]$generosity,
    [int]$honor,[bool]$favored,[bool]$deFacto=$true,[bool]$full=$true) {
    if ($rank -lt 1 -or $rank -gt 3) { throw 'Only county, duchy and kingdom fixtures are supported.' }
    if ($full -and !$deFacto) { throw 'Full rights must include practical control.' }
    if ($claim -eq 'DeJure' -and $full) { throw 'A de jure claimant already owns the legal rights.' }
    $claimBonus=switch($claim) { 'Weak' {10} 'Strong' {25} 'DeJure' {35} default {throw 'Unknown claim'} }
    $rankCost=10*($rank-1)*$(if($deFacto){1}else{0.5})
    $raw=40+$claimBonus+(Clamp ($relation*0.15) -15 15)+8*$generosity+5*$honor+
        $(if($favored){15}else{0})-$rankCost-$(if($full){5}else{0})
    Clamp $raw 0 100
}
function CanGrant([double]$score,[bool]$legal,[double]$influence,[double]$reserve) {
    # Mirrors the current discretionary budget shape: preserve max(role reserve,cost).
    $legal -and $score -ge 60 -and $influence+0.001 -ge 100+[Math]::Max(100,$reserve)
}
$script:checks=0
function Check($ok,$message) { if(!$ok){throw $message}; $script:checks++ }

'Proposed NPC utility: baseline 40, acceptance >=60. Not a probability.'
$cases=@(
 @{Name='Neutral strong county';Rank=1;Claim='Strong';Relation=0;G=0;H=0;Fav=$false;Fact=$true;Full=$true},
 @{Name='Neutral strong duchy';Rank=2;Claim='Strong';Relation=0;G=0;H=0;Fav=$false;Fact=$true;Full=$true},
 @{Name='Neutral strong kingdom';Rank=3;Claim='Strong';Relation=0;G=0;H=0;Fav=$false;Fact=$true;Full=$true},
 @{Name='Favored strong duchy';Rank=2;Claim='Strong';Relation=0;G=0;H=0;Fav=$true;Fact=$true;Full=$true},
 @{Name='Favored strong kingdom';Rank=3;Claim='Strong';Relation=0;G=0;H=0;Fav=$true;Fact=$true;Full=$true},
 @{Name='Favored kingdom, relation 40';Rank=3;Claim='Strong';Relation=40;G=0;H=0;Fav=$true;Fact=$true;Full=$true},
 @{Name='Generous honorable kingdom';Rank=3;Claim='Strong';Relation=0;G=2;H=1;Fav=$false;Fact=$true;Full=$true},
 @{Name='Neutral weak county';Rank=1;Claim='Weak';Relation=0;G=0;H=0;Fav=$false;Fact=$true;Full=$true},
 @{Name='Generous weak county';Rank=1;Claim='Weak';Relation=0;G=2;H=0;Fav=$false;Fact=$true;Full=$true},
 @{Name='De jure kingdom restoration';Rank=3;Claim='DeJure';Relation=0;G=0;H=0;Fav=$false;Fact=$true;Full=$false},
 @{Name='Favored de jure restoration';Rank=3;Claim='DeJure';Relation=0;G=0;H=0;Fav=$true;Fact=$true;Full=$false},
 @{Name='Kingdom legal rights only';Rank=3;Claim='Strong';Relation=0;G=0;H=0;Fav=$false;Fact=$false;Full=$false},
 @{Name='Hostile miser, favored kingdom';Rank=3;Claim='Strong';Relation=-100;G=-2;H=-1;Fav=$true;Fact=$true;Full=$true}
)
$rows=foreach($c in $cases) {
    $s=Acceptance $c.Rank $c.Claim $c.Relation $c.G $c.H $c.Fav $c.Fact $c.Full
    [pscustomobject]@{Scenario=$c.Name;Score=$s;Decision=$(if($s -ge 60){'Grant'}else{'Refuse'})}
}
$rows | Format-Table -AutoSize | Out-String -Width 160 | Write-Output

# Cartesian counts only describe the deliberately uniform fixture grid.
foreach($rank in 1..3) {
    $accepted=0; $total=0
    foreach($claim in @('Weak','Strong','DeJure')) {
        foreach($relation in @(-100,-50,0,50,100)) {
            foreach($g in -2..2) {
                foreach($h in -2..2) {
                    foreach($fav in @($false,$true)) {
                        $full=$claim -ne 'DeJure'
                        $s=Acceptance $rank $claim $relation $g $h $fav $true $full
                        $total++; if($s -ge 60){$accepted++}
                        Check ($s -ge 0 -and $s -le 100) 'Score escaped bounds.'
                        Check ((Acceptance $rank $claim ([Math]::Min(100,$relation+1)) $g $h $fav $true $full) -ge $s) 'Better relations harmed willingness.'
                        Check ((Acceptance $rank $claim $relation $g $h $true $true $full) -ge $s) 'Favoritism harmed willingness.'
                        if($rank -lt 3) { Check ((Acceptance ($rank+1) $claim $relation $g $h $fav $true $full) -le $s) 'Higher rank became easier to grant.' }
                        if($claim -eq 'Weak') { Check ((Acceptance $rank 'Strong' $relation $g $h $fav) -ge $s) 'Stronger claim harmed willingness.' }
                    }
                }
            }
        }
    }
    "Uniform grid rank $rank : $accepted/$total willing. Not campaign frequency."
}
foreach($legal in @($false,$true)) {
    foreach($influence in @(99,100,199,200,299,300,500)) {
        foreach($reserve in @(0,100,200)) {
            Check ((CanGrant 100 $legal $influence $reserve) -eq ($legal -and $influence -ge 100+[Math]::Max(100,$reserve))) 'Acceptance bypassed legal/budget gates.'
            Check (!(CanGrant 59 $legal $influence $reserve)) 'Below-threshold fixture granted.'
        }
    }
}
Check ((Acceptance 1 'Strong' 0 0 0 $false) -eq 60) 'Neutral strong county boundary changed.'
Check ((Acceptance 3 'Strong' 40 0 0 $true) -eq 61) 'Favored friendly kingdom boundary changed.'

'Reward fixtures: established grant scale; one faction reward per delivery, no court jealousy.'
foreach($rank in 1..3) {
    foreach($full in @($false,$true)) {
        $relations=10+5*$rank+5+$(if($full){5}else{0})
        $approval=$relations*0.5
        "Rank $rank full=$full : relations +$relations; recipient faction +$approval; other factions 0."
        Check ($relations -eq (15+5*$rank+$(if($full){5}else{0}))) 'Reward rank scale changed.'
    }
}
"PASS: $script:checks design assertions. No native grant, save/load, timing, AI frequency or transfer recovery simulated."
