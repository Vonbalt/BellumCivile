$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$constants = Get-Content -Raw (Join-Path $root 'BellumCivileConstants.cs')
function Constant($name) {
    $match = [regex]::Match($constants, ('public const float ' + [regex]::Escape($name) + '\s*=\s*([0-9.]+)f;'))
    if (!$match.Success) { throw "Missing numeric constant: $name" }
    [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
}
$bonus = 15
$nobility = Constant 'FiefClaimAristocratMultiplier'
$crown = Constant 'FiefClaimNeutralMultiplier'
$parent = Constant 'FiefClaimParentTitleMultiplier'
# Component comparisons, not predictions of campaign vote frequency.
$rows = foreach ($claim in @('DeJure','Strong','Weak')) {
    $base = Constant "FiefClaim${claim}Bonus"
    foreach ($voter in @('Nobility','Aligned Crown')) {
        $multiplier = if ($voter -eq 'Nobility') { $nobility } else { $crown }
        foreach ($scope in @('Direct','Parent title')) {
            $weight = if ($scope -eq 'Direct') { 1 } else { $parent }
            $current = $base * $multiplier * $weight
            [pscustomobject]@{ Claim = $claim; Voter = $voter; Scope = $scope; Existing = $current; WithMotion = $current + $bonus }
        }
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 140 | Write-Output
$checks = 0
$wins = 0
$ties = 0
# Hold every other factor fixed. A positive gap is the rival's initial lead.
foreach ($gap in -100..100) {
    $beneficiary = 100 + $bonus
    $rival = 100 + $gap
    if (($beneficiary -gt $rival) -ne ($gap -lt $bonus)) { throw 'Unexpected preference boundary.' }
    if ($gap -gt 0 -and $beneficiary -gt $rival) { $wins++ }
    if ($beneficiary -eq $rival) { $ties++ }
    $checks++
}
"PASS: $checks fixed-gap comparisons. Reverses rival leads 1..$wins; $ties tie at gap $bonus remains order-dependent."
'Eligibility, committed votes, nomination aggregation and live allocation timing are not simulated here.'
