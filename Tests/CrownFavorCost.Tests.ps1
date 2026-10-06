$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$flow = Read 'Behaviors/CourtCrownBehavior.cs'
$rules = Read 'CrownFavorSelectionRules.cs'
$budget = Read 'NpcInfluenceBudgetService.cs'
Check ($rules.Contains('const int Cost = 100')) 'Single sponsorship price is 100 influence.'
Check (([regex]::Matches($flow, 'NpcInfluenceBudgetService.TrySpend\(realm.RulingClan, CrownFavorSelectionRules.Cost,')).Count -eq 2) 'NPC and player commitments use the shared payment service.'
Check ($flow.Contains('CrownFavorChoiceOpen(realm) && NpcInfluenceBudgetService.CanAfford')) 'Banner availability includes affordability and the term lock.'
$player = $flow.Substring($flow.IndexOf('public bool TrySelectCrownFavor'), $flow.IndexOf('private void AnnounceCrownFavor') - $flow.IndexOf('public bool TrySelectCrownFavor'))
Check ($player.IndexOf('Contains(faction) != true') -lt $player.IndexOf('TrySpend') -and
    $player.IndexOf('TrySpend') -lt $player.IndexOf('_crownFavor[realm.StringId] =')) 'Stale selection is rejected before payment; failed payment cannot commit favor.'
Check ($player.Contains('!CanSelectCrownFavor(realm)') -and $player.Contains('GetCrownFavorExpiry(realm) != expectedExpiry')) 'Repeated and stale-term clicks cannot charge again.'
Check ($flow.Contains('BC_CrownFavorUnaffordable') -and $flow.Contains('if (CrownFavorChoiceOpen(realm))')) 'Low funds are distinguished from the cooldown in hints.'
Check ($budget.Contains('case NpcInfluenceExpenseKind.Discretionary:') -and $budget.Contains('return Math.Max(promised, Math.Max(GetRoleReserve(clan), cost));')) 'NPC sponsorship protects the existing discretionary reserve and outstanding vote promises.'
[xml]$panel = Read 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
$banner = $panel.SelectSingleNode('//ListPanel[@DataSource="{FavorBanners}"]/ItemTemplate/ListPanel/Children')
$cost = $banner.SelectSingleNode('ListPanel/Children/TextWidget[@Text="@InfluenceCostText"]')
Check ($null -ne $cost -and $cost.GetAttribute('DoNotAcceptEvents') -eq 'true') 'Every repeated banner has a click-through cost below its name.'
Check ($cost.ParentNode.SelectSingleNode('Widget').GetAttribute('Sprite') -eq 'General\Icons\Influence@2x') 'Cost uses the existing native influence icon.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_CrownFavorSelect', 'BC_CrownFavorUnaffordable')) {
    Check ($strings.SelectSingleNode("//string[@id='$id']").text.Contains('{COST}')) "Localized price hint: $id"
}
'Sponsorship source/XML contracts passed; live UI testing remains.'
