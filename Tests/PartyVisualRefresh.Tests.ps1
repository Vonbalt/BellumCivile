$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -Raw -LiteralPath (Join-Path $root 'KingdomVisualHelper.cs')
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
Check ($source.Contains('GetPartyVisualClan(mobileParty)?.Kingdom == kingdom') -and
    $source.Contains('GetPartyVisualClan(mobileParty) == clan')) 'Kingdom and clan visual scans share the guarded ownership resolver.'
foreach ($component in @('MilitiaPartyComponent', 'GarrisonPartyComponent', 'VillagerPartyComponent', 'PatrolPartyComponent')) {
    Check ($source.Contains("component is $component")) "Settlement ownership is resolved before reading a hero owner: $component"
}
Check ($source.Contains('settlement.Village.Bound?.Town?.OwnerClan') -and
    $source.Contains('settlement?.Town?.OwnerClan')) 'Missing village bindings and town owners are guarded without requiring a clan leader.'
Check (!$source.Contains('mobileParty?.Party?.Owner?.Clan')) 'Global scan predicates no longer invoke unchecked settlement-party owner getters.'
'Source contracts passed; run --party-visual-refresh against each game installation.'
