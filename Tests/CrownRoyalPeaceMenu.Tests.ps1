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
Check (!$flow.Contains('ShowMultiSelectionInquiry') -and !$flow.Contains('GetActiveFeuds()')) 'Royal peace confirmation covers the realm instead of selecting a single feud'
Check ($flow.Contains('GetRealmPreview(realm, Clan.PlayerClan)') -and $flow.Contains('preview?.IsEnabled == true')) 'Crown action reuses authoritative realm cost and eligibility preview'
Check ($flow.Contains('TryScheduleRoyalPeace(realm, Clan.PlayerClan, out report)') -and !$flow.Contains('TryEnforceRealmPeace(')) 'Confirmation announces the decree without immediate settlement'
Check (!$flow.Contains('TryPay(') -and !$flow.Contains('StartCrownActionCooldown(')) 'Menu adds neither extra payment nor term cooldown'
Check ($flow.Contains('QuoteStillValid(preview.Wartime, preview.InfluenceCost, current.Wartime, current.InfluenceCost)') -and $flow.Contains('realm.Leader != ruler')) 'Confirmation revalidates price, pricing mode and ruler identity'
Check ($flow.Contains('BC_RealmPeaceConfirm') -and $flow.Contains('PoliticalDeliberationDays') -and $flow.Contains('HasPendingRoyalPeace(realm)')) 'Confirmation explains notice timing and prevents duplicate announcements'
Check ($xml.SelectNodes('//*[@Command.Click="ExecuteEnforcePeace"]').Count -eq 0) 'Old feud enforcement button removed'
Check ($xml.SelectNodes('//*[@Command.Click="ExecutePetition"]').Count -eq 1) 'Existing feud petition button retained'
Check (!$oldVm.Contains('EnforcePeace')) 'Unused feud-card enforcement bindings removed'
