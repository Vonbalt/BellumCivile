param(
    [int[]] $Baselines = @(80)
)
$ErrorActionPreference = 'Stop'
# Approved core only, not gameplay code or a campaign population forecast.
# Other lawful successors (-10); inheritance, relations and merit held at zero.
$scenarios = @(
    @{ Name = 'Initial ruler'; Initial = $true; Years = 0; Minor = $false; Regency = $false; Controversy = 0 },
    @{ Name = 'Adult accession'; Initial = $false; Years = 0; Minor = $false; Regency = $false; Controversy = 0 },
    @{ Name = 'Minor accession'; Initial = $false; Years = 0; Minor = $true; Regency = $true; Controversy = 0 },
    @{ Name = 'First anniversary'; Initial = $false; Years = 1; Minor = $false; Regency = $false; Controversy = 0 }
)
foreach ($baseline in $Baselines) {
    foreach ($honor in -2..2) {
      foreach ($mercy in -2..2) {
        foreach ($s in $scenarios) {
            $personality = [Math]::Max(-25, [Math]::Min(25, 10 * $honor + 5 * $mercy))
            $reign = [Math]::Min(15, $(if ($s.Initial) { 0 } else { -15 }) + [Math]::Floor($s.Years))
            $shock = if (!$s.Initial -and $s.Years -lt 1) { if ($s.Minor) { 50 } else { 25 } } else { 0 }
            $controversyPenalty = $s.Controversy * $(if ($s.Regency) { 2 } else { 1 })
            $raw = $baseline - 10 + $personality - $controversyPenalty + $reign - $shock
            [pscustomobject]@{
                Baseline = $baseline
                Honor = $honor
                Mercy = $mercy
                Scenario = $s.Name
                Raw = $raw
                Loyalty = [Math]::Max(0, [Math]::Min(100, $raw))
                BelowProvisionalThreshold = $raw -lt 25
            }
        }
      }
    }
}
