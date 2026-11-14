param([int]$Trials = 5000, [int]$Seed = 4162, [double[]]$Rates = @(0.02, 0.05, 0.10, 0.20))
$ErrorActionPreference = 'Stop'

# Illustrative prototype only: no campaign state or production balance is used.
function Get-Decay([double]$base, [int]$fiefs, [double]$chancellor = 0) {
    return $base * (1 + $chancellor) / [Math]::Min(4, 1 + 0.1 * $fiefs)
}
function Get-Threat([int]$day, [double]$initial, [double]$decay) {
    return [Math]::Max(0, $initial - [Math]::Floor($day / 7) * $decay)
}

Write-Output 'BASE DECAY: one point per seven days, zero new threat'
foreach ($fiefs in @(0, 10, 20, 30)) {
    $decay = Get-Decay 1 $fiefs
    $days25 = [Math]::Ceiling(25 / $decay) * 7
    $days50 = [Math]::Ceiling(50 / $decay) * 7
    Write-Output ("fiefs={0}; decay/week={1:N3}; days_to_lose_25={2}; days_to_lose_50={3}; max_chancellor_decay/week={4:N3}" -f $fiefs, $decay, $days25, $days50, (Get-Decay 1 $fiefs 1))
}

Write-Output ''
Write-Output 'SYNTHETIC PACTS: 4 adjacent and 4 second-degree eligible realms, threat 60, target 10 fiefs'
Write-Output 'Each realm evaluates independently every 30 days with a random initial offset; only flat join chance changes.'
Write-Output 'No wars, strength modifiers, changing relations, competing pacts or new threat; 5000 trials by default.'
foreach ($chance in $Rates) {
    $random = [Random]::new($Seed)
    $any120 = 0; $two120 = 0; $any365 = 0; $two365 = 0
    $sum120 = 0; $sum365 = 0; $joinAdj = 0; $joinSecond = 0
    for ($trial = 0; $trial -lt $Trials; $trial++) {
        $count120 = 0; $count365 = 0
        for ($realm = 0; $realm -lt 8; $realm++) {
            $tier = if ($realm -lt 4) { 25 } else { 50 }
            $offset = $random.Next(0, 30)
            for ($day = $offset; $day -le 365; $day += 30) {
                if ((Get-Threat $day 60 0.5) -lt $tier) { break }
                if ($random.NextDouble() -ge $chance) { continue }
                if ($day -le 120 -and (Get-Threat 120 60 0.5) -ge ($tier / 2.0)) { $count120++ }
                if ((Get-Threat 365 60 0.5) -ge ($tier / 2.0)) { $count365++ }
                if ($realm -lt 4) { $joinAdj++ } else { $joinSecond++ }
                break
            }
        }
        if ($count120 -ge 1) { $any120++ }
        if ($count120 -ge 2) { $two120++ }
        if ($count365 -ge 1) { $any365++ }
        if ($count365 -ge 2) { $two365++ }
        $sum120 += $count120; $sum365 += $count365
    }
    Write-Output ("chance={0:P0}; day120_any={1:P1}; day120_two_plus={2:P1}; day120_mean={3:N2}; day365_any={4:P1}; day365_two_plus={5:P1}; day365_mean={6:N2}; individual_adjacent_joins={7:P1}; individual_second_degree_joins={8:P1}" -f $chance, ($any120 / $Trials), ($two120 / $Trials), ($sum120 / $Trials), ($any365 / $Trials), ($two365 / $Trials), ($sum365 / $Trials), ($joinAdj / (4.0 * $Trials)), ($joinSecond / (4.0 * $Trials)))
}

Write-Output ''
Write-Output 'ILLUSTRATIVE TREATY: 30 WS unclaimed fief + 40 WS claimed fief'
$unclaimed = 30 / 4.0 * 2
$claimed = 40 / 4.0
$client = 60 / 4.0
$total = [Math]::Min(50, $unclaimed + $claimed)
Write-Output ("unclaimed=+{0}; claimed=+{1}; treaty_gain=+{2} (50-point treaty cap assumed)" -f $unclaimed, $claimed, $total)
Write-Output ("A separate 60 WS forced-clientage term would add +{0}; draft legality is not modeled." -f $client)

Write-Output ''
Write-Output 'REPEATED AGGRESSION: unjustified war (+10) and one unclaimed 40 WS fief (+20) every 84 days'
Write-Output 'Fief count is held constant to isolate the decay rule; each annual interval has 12 seven-day ticks.'
foreach ($fiefs in @(0, 10, 30)) {
    $threat = 0.0
    $values = @()
    for ($i = 0; $i -lt 4; $i++) {
        if ($i -gt 0) { $threat = [Math]::Max(0, $threat - 12 * (Get-Decay 1 $fiefs)) }
        $threat = [Math]::Min(100, $threat + 30)
        $values += $threat.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
    }
    Write-Output ("fiefs={0}; threat_after_each_war={1}" -f $fiefs, ($values -join ', '))
}
