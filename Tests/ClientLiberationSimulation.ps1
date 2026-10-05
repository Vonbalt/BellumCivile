$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -TypeDefinition ((Get-Content -Raw (Join-Path $root 'ClientLiberationRules.cs')).Replace('internal', 'public'))
$script:checks = 0
function Check($condition, $label) { if (!$condition) { throw $label }; $script:checks++ }
function Assess($powers, $desires, $bonus, $bloc, $ratio = 0.8, $will = 75, $cooldown = 0) {
    $total = 0.0; $weighted = 0.0; $effective = 0.0; $crown = 0.0
    for ($i = 0; $i -lt $powers.Count; $i++) {
        $d = [Math]::Min(100,[Math]::Max(0,$desires[$i]+$bonus))
        if ($i -eq 0) { $crown = $d }
        $total += [Math]::Max(1,$powers[$i]); $weighted += $d * [Math]::Max(1,$powers[$i])
        $effective += $powers[$i] * [BellumCivile.ClientLiberationRules]::EffectivePowerMultiplier($d)
    }
    $desire = $weighted/$total
    $ready = [BellumCivile.ClientLiberationRules]::Readiness($effective,$bloc,$ratio)
    $willingness = [BellumCivile.ClientLiberationRules]::EffectiveWarWill($will, $crown)
    [pscustomobject]@{ Desire=$desire; Crown=$crown; Readiness=$ready; Eligible=($desire -ge 60 -and $crown -ge 60 -and $ready -ge 100 -and $willingness -ge 75 -and $cooldown -le 0) }
}
Check ([BellumCivile.ClientLiberationRules]::BlocContribution(1000,$true,$true,0.5,0.5) -eq 500) 'A client ally contributes once, not twice.'
Check ([BellumCivile.ClientLiberationRules]::BlocContribution(1000,$true,$false,0.5,0.5) -eq 500) 'Ordinary ally still contributes half.'
Check ([BellumCivile.ClientLiberationRules]::BlocContribution(1000,$false,$false,0.5,0.5) -eq 0) 'Unrelated realm contributes nothing.'
$fixtures = @(
    @{Name='Restless, sufficient power'; Powers=@(500,300,200); Desires=@(40,40,40); Bloc=1000},
    @{Name='Already near threshold'; Powers=@(500,300,200); Desires=@(45,45,45); Bloc=1000},
    @{Name='Reluctant Crown, divided realm'; Powers=@(500,300,200); Desires=@(35,55,70); Bloc=1000},
    @{Name='Weak but discontented'; Powers=@(300,180,120); Desires=@(50,50,50); Bloc=1000},
    @{Name='Contented client'; Powers=@(600,360,240); Desires=@(20,20,20); Bloc=1000}
)
foreach ($f in $fixtures) {
    foreach ($bonus in @(0,15,20,25)) {
        $a = Assess $f.Powers $f.Desires $bonus $f.Bloc
        '{0}: bonus {1}; realm {2:0.0}; Crown {3}; readiness {4:0.0}%; eligible {5}' -f $f.Name,$bonus,$a.Desire,$a.Crown,$a.Readiness,$a.Eligible
    }
}
foreach ($desire in 0..100) {
    foreach ($power in @(400,800,1000,1500)) {
        foreach ($ratio in @(0.4,0.8,1.2)) {
            $previous = $null
            foreach ($bonus in @(0,15,20,25)) {
                $a = Assess @($power) @($desire) $bonus 1000 $ratio
                Check ($a.Desire -ge 0 -and $a.Desire -le 100 -and $a.Readiness -ge 0 -and $a.Readiness -le 200) 'Assessment bounds.'
                if ($previous) { Check ($a.Desire -ge $previous.Desire -and $a.Readiness -ge $previous.Readiness) 'Increasing desire never reduces readiness.' }
                $previous = $a
            }
        }
    }
}
Check ((Assess @(1000) @(40) 20 1000).Eligible) '+20 mobilizes a sufficient-power restless realm.'
Check (!(Assess @(1000) @(40) 15 1000).Eligible) '+15 does not bridge 40 to 60.'
Check (!(Assess @(600) @(50) 25 1000).Eligible) 'Desire cannot overcome inadequate full strength.'
Check ((Assess @(1000) @(45) 20 1000 0.8 74).Eligible) 'Personal desire of 65 supplies enough resolve to bridge 74 War Will.'
Check (!(Assess @(1000) @(45) 20 1000 0.8 70).Eligible) 'Insufficient War Will plus resolve still blocks a proposal.'
Check (!(Assess @(1000) @(45) 20 1000 0.8 75 1).Eligible) 'Binding settlement cooldown remains a separate gate.'
'Suzerain1000 + client-ally1000: old bloc2000, corrected bloc1500. With effective client1200 / required0.8: readiness75% ->100%.'
Check ([Math]::Abs([BellumCivile.ClientLiberationRules]::Readiness(1200,1500,0.8)-100) -lt 0.001) 'Corrected bloc readiness.'
"PASS: $script:checks liberation simulation assertions. Production preparations use +20 desire; alternative preparation bonuses are comparative fixtures."
