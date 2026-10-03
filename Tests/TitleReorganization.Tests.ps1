$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$titles = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs')
$aftermath = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalTitleReorganization.cs')
$fabrication = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalClaimFabricationBehavior.cs')
$ui = Get-Content -Raw (Join-Path $root 'UI/VanillaTabs/Kingdoms/Hierarchy/HierarchyTitleNodeVM.cs')
Check ($titles.IndexOf('ApplyReorganizationResentment(reorganizer') -gt $titles.IndexOf('Feudal sovereign formation rolled back')) 'Formation resentment occurs after both rollback paths.'
Check ($titles.IndexOf('ApplyReorganizationResentment(dissolver') -gt $titles.IndexOf('if (!DissolveTitleInternal(title')) 'Dissolution resentment follows successful mutation.'
Check ($titles.Contains('!HasActiveTitleDispute(title)')) 'Automatic orphan cleanup keeps the conservative dispute guard.'
Check ($titles.Contains('childTitle.SetAssociatedKingdom(historicOrigins[childTitle.TitleId])')) 'Formation preserves the historical realm of surviving titles.'
Check ($titles.Contains('deJureChildren.Where(child => string.IsNullOrWhiteSpace(child.AssociatedKingdomId))')) 'Dissolution materializes inherited origins before removing parents.'
Check ($aftermath.Contains('10, RelationMemoryScope.House, context')) 'Resentment uses the standard ten-year house memory.'
Check ($aftermath.Contains('RelationMemoryDurationMultiplier')) 'Confirmation duration follows MCM scaling.'
Check ($fabrication.Contains('CancelFabrication(record, clan, title, reason, refundGold: true)')) 'Invalid fabrication uses the established cancellation and refund path.'
Check ($ui.Contains('description.ToString() + resentmentWarning') -and $ui.Contains('candidate.ChildTitleIds.Concat(candidate.AffectedParentTitleIds)')) 'Both action confirmations warn about claimant resentment.'
$xml = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
foreach ($id in 'BC_RelationMemory_DisregardedAncestralClaims', 'BC_TitleClaimResentmentWarning') {
    Check (@($xml.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localization entry: $id"
}
