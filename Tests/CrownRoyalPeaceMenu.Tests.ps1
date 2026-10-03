$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$menu = Get-Content (Join-Path $root 'Behaviors/CourtPlayerCrownActions.cs') -Raw
$flow = Get-Content (Join-Path $root 'Behaviors/CourtPlayerRoyalPeace.cs') -Raw
$oldVm = Get-Content (Join-Path $root 'UI/ClaimFeudItemVM.cs') -Raw
$xml = [xml](Get-Content (Join-Path $root 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml') -Raw)
function Check($condition, $message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}
Check ($menu.Contains('choices.Add(RoyalPeaceAction(realm))') -and $menu.Contains('ShowPlayerRoyalPeace(realm, refresh)')) 'Crown menu includes and dispatches royal peace'
Check ($flow.Contains('GetActiveFeuds()') -and $flow.Contains('ResolveKingdomForFeud(f) == realm')) 'Picker includes active feuds of the selected realm, not just armed wars'
Check ($flow.Contains('GetClaimFeudPreview') -and $flow.Contains('preview?.IsEnabled == true')) 'Choices reuse authoritative cost and eligibility preview'
Check ($flow.Contains('TryEnforceClaimFeudPeace(feud, Clan.PlayerClan, out report)')) 'Confirmation calls existing validated enforcement service'
Check (!$flow.Contains('TryPay(') -and !$flow.Contains('StartCrownActionCooldown(')) 'Menu adds neither extra payment nor term cooldown'
Check ($flow.Contains('preview.InfluenceCost') -and $flow.Contains('BC_RoyalPeace_ConfirmDesc')) 'Confirmation preserves quoted price and original consequences'
Check ($xml.SelectNodes('//*[@Command.Click="ExecuteEnforcePeace"]').Count -eq 0) 'Old feud enforcement button removed'
Check ($xml.SelectNodes('//*[@Command.Click="ExecutePetition"]').Count -eq 1) 'Existing feud petition button retained'
Check (!$oldVm.Contains('EnforcePeace')) 'Unused feud-card enforcement bindings removed'
