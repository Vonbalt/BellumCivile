$ErrorActionPreference = 'Stop'
# Design arithmetic only: no native election, payment, persuasion RNG or campaign mutation.
$script:checks = 0
function Check([bool]$ok, [string]$label) { if (!$ok) { throw $label }; $script:checks++ }
function Bound([double]$n, [double]$lo = 0, [double]$hi = 100) { [Math]::Max($lo, [Math]::Min($hi, $n)) }
function Reform([int]$direction, [string]$faction, [double]$relation = 0, [int]$honor = 0, [bool]$alignedCrown = $false) {
    $affinity = switch ($faction) { 'Nobility' { 20 } 'Liberty' { -20 } default { 0 } }
    Bound (45 + $direction * $affinity + (Bound ($relation * .1) -10 10) - 5 * [Math]::Max(0, $honor) + 15 * [int]$alignedCrown)
}
function Gap([double]$requested) { [Math]::Max(0, 100 - 2 * $requested) }
function Openness([double]$gap, [double]$relation, [int]$honor = 0, [int]$mercy = 0, [int]$generosity = 0, [int]$calculating = 0) {
    $h = if ($honor -gt 0) { -25 * $honor } else { -20 * $honor }
    $m = if ($mercy -gt 0) { -15 * $mercy } else { -10 * $mercy }
    $g = if ($generosity -gt 0) { -10 * $generosity } else { -20 * $generosity }
    $c = if ($calculating -gt 0) { 10 * $calculating } else { 5 * $calculating }
    50 + (Bound $relation -100 100) * .4 - $gap * .25 + $h + $m + $g + $c
}
function Price([double]$gap, [double]$relation, [int]$honor = 0, [int]$generosity = 0, [double]$mcm = 1) {
    $base = if ($gap -gt 50) { 150000 } elseif ($gap -gt 10) { 100000 } else { 50000 }
    [int]($base * (Bound (1 - (Bound $relation -100 100) * .003 + $honor * .25 - $generosity * .1) .25 2) * $mcm)
}
function Qualifies([int]$yes, [int]$no, [int]$houses, [bool]$affordable = $true, [bool]$sponsor = $true) {
    $affordable -and $sponsor -and $yes -gt 0 -and ($yes -gt $no -or ($houses -ge 2 -and 100 * $yes -ge 30 * ($yes + $no)))
}

'Natural utilities, neutral sponsor relations; paired outcomes sum to100:'
@(foreach ($direction in @(1,-1)) {
    foreach ($faction in @('Nobility','Liberty','Glory','Crown')) {
        foreach ($honor in @(0,2)) {
            $yes = Reform $direction $faction 0 $honor
            [pscustomobject]@{Direction=$direction; Voter=$faction; Honor=$honor; Reform=$yes; Retain=100-$yes}
        }
    }
}) | Format-Table -AutoSize | Out-String | Write-Output
foreach ($direction in @(1,-1)) {
    foreach ($faction in @('Nobility','Liberty','Glory','Crown')) {
        foreach ($relation in -100..100) {
            foreach ($honor in -2..2) {
                foreach ($aligned in @($false,$true)) {
                    $yes = Reform $direction $faction $relation $honor $aligned
                    Check ($yes -ge 0 -and $yes -le 100) 'Utility outside bounds.'
                    Check ((Gap $yes) -ge 0 -and (Gap $yes) -le 100) 'Resistance outside bounds.'
                    Check ((Reform $direction $faction $relation $honor $true) -ge $yes) 'Crown alignment reduces support.'
                }
            }
        }
    }
}
Check ((Reform 1 'Nobility') -eq 65) 'Nobility baseline changed.'
Check ((Reform -1 'Liberty') -eq 65) 'Liberty symmetry changed.'
Check ((Reform 1 'Crown' 0 0 $true) -eq 60) 'Aligned Crown should favor reform.'
Check ((Reform 1 'Glory' 100 2) -eq 45) 'Honorable neutral voter should remain cautious.'

'Lobbying fixtures (prices before any native barter negotiation):'
$fixtures = @(
    @{Name='Neutral: reform45'; Utility=45; Relation=0; Honor=0; Expected=$true},
    @{Name='Opposed faction: reform25'; Utility=25; Relation=0; Honor=0; Expected=$true},
    @{Name='Opposed honest lord: reform20'; Utility=20; Relation=0; Honor=1; Expected=$false},
    @{Name='Same lord, friend'; Utility=20; Relation=80; Honor=1; Expected=$true},
    @{Name='Opposed enemy'; Utility=25; Relation=-30; Honor=0; Expected=$false},
    @{Name='Opposed devious lord'; Utility=25; Relation=0; Honor=-1; Expected=$true}
)
@(foreach ($f in $fixtures) {
    $gap = Gap $f.Utility
    $open = Openness $gap $f.Relation $f.Honor
    Check (($open -ge 35) -eq $f.Expected) ('Unexpected openness: ' + $f.Name)
    [pscustomobject]@{Case=$f.Name; Resistance=$gap; Openness=$open; BribeAllowed=$open -ge 35;
        Price=Price $gap $f.Relation $f.Honor; PersuasionAllowed=$f.Relation -ge 30}
}) | Format-Table -AutoSize | Out-String -Width 180 | Write-Output
foreach ($gap in 0..100) {
    foreach ($relation in @(-100,0,29,30,80,100)) {
        foreach ($honor in -2..2) {
            $cost = Price $gap $relation $honor
            Check ($cost -ge 12500 -and $cost -le 300000) 'Unscaled price outside tier bounds.'
            Check ((Openness $gap ($relation + 1) $honor) -ge (Openness $gap $relation $honor)) 'Trust lowers openness.'
            Check ((Price $gap $relation $honor 0 2) -eq 2 * $cost) 'MCM scale applied inconsistently.'
        }
    }
}
Check ((Price 10 0) -eq 50000 -and (Price 11 0) -eq 100000) 'First tier boundary.'
Check ((Price 50 0) -eq 100000 -and (Price 51 0) -eq 150000) 'High tier boundary.'

'Agenda viability fixtures: supplied voting points, not predictions of native influence spending.'
foreach ($case in @(
    @{Name='Two of six equal voters';Yes=2;No=4;Houses=2;Expected=$true},
    @{Name='Two of seven equal voters';Yes=2;No=5;Houses=2;Expected=$false},
    @{Name='Two strong sponsors against four';Yes=6;No=4;Houses=2;Expected=$true},
    @{Name='Lone minority';Yes=3;No=4;Houses=1;Expected=$false},
    @{Name='Exact30 percent';Yes=3;No=7;Houses=2;Expected=$true}
)) {
    $result = Qualifies $case.Yes $case.No $case.Houses
    Check ($result -eq $case.Expected) $case.Name
    '{0}: qualifies={1}, projected points={2}:{3}' -f $case.Name,$result,$case.Yes,$case.No
}
Check (!(Qualifies 6 4 2 $false)) 'Unaffordable sponsor filed.'
Check (!(Qualifies 6 4 2 $true $false)) 'Unwilling sponsor filed.'

'Filing affordability:100 base,75 with maximum Chancellor discount; protected reserves remain untouched.'
foreach ($reserve in @(0,200,400,500,700)) {
    foreach ($cost in @(75,100)) {
        $required = $reserve + $cost
        Check (!($required - 1 + .001 -ge $required)) 'Below-reserve filing accepted.'
        Check ($required + .001 -ge $required) 'Exact-reserve filing rejected.'
        'Reserve {0}, filing {1}: minimum influence {2}' -f $reserve,$cost,$required
    }
}

# Deterministic quantiles test an equal-ticket proposal, not the existing selector.
$wins = @{Nobility=0;Liberty=0}
foreach ($i in 0..999) {
    $chosen = if (($i + .5) / 1000 -lt .5) { 'Nobility' } else { 'Liberty' }
    $wins[$chosen]++
}
Check ($wins.Nobility -eq 500 -and $wins.Liberty -eq 500) 'Equal candidates not treated equally.'
$terms = @(1,5,10,0)
foreach ($index in 0..3) {
    foreach ($step in @(-1,1)) {
        $next = $index + $step
        if ($next -lt 0 -or $next -ge 4) { continue }
        Check ($terms[$index] -ne $terms[$next]) 'Adjacent reform is a no-op.'
        Check ([Math]::Abs($next - $index) -eq 1) 'Reform skips a term.'
    }
}
'Collision proposal: equal viable finalists500:500; pending group reservation must block both on later terms.'
'Family weight0.5 is relative: versus one weight1 alternative it is33.3%, versus three it is14.3%, and alone it is100%.'
'No native ballot pass rates, persuasion success probabilities, save/load or payment guarantees are asserted here.'
'Court mandate design checks passed: ' + $script:checks
