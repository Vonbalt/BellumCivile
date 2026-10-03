$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$marriage = Get-Content (Join-Path $root 'Behaviors/DynasticHeirBehavior.cs') -Raw
$settlement = Get-Content (Join-Path $root 'Behaviors/CrownAbdication.cs') -Raw
$titles = Get-Content (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs') -Raw
function Check([bool] $condition, [string] $label) {
    if (!$condition) { throw $label }
    Write-Output "PASS: $label"
}
Check ($marriage.Contains('Hero carrier = hereditary ? dynasticHeir : Clan.PlayerClan.Leader ?? spouse;')) 'Hereditary player marriage retains the actual blood carrier.'
Check ($marriage.Contains('if (!hereditary && IsPlayerClanAttachedToForeignKingdom(kingdom))')) 'Foreign player marriage does not discard hereditary birthrights.'
Check ($marriage -match 'if \(!hereditary\)\s+Campaign.Current.GetCampaignBehavior<DynasticClaimBehavior>') 'Legacy spouse-based dynastic claim is not granted in hereditary realms.'
Check ($settlement.IndexOf('titles.MoveCrownHeirClaims(record.Heir, source, record.Cadet);') -gt $settlement.IndexOf('partition?.PrepareAbdicationCadet(record)')) 'Claim relocation follows cadet household formation.'
Check ($titles -match 'foreach \(FeudalClaimRecord claim in _claims.Where\(IsClaimCurrentlyActive\)\)\s+if \(claim.MoveWithCarrier') 'Only unexpired active claims belonging to this carrier are relocated.'
Write-Output 'Source contracts only; live claim indexes and save/load require campaign testing.'
