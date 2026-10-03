$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$panel = Get-Content (Join-Path $root 'GUI/Prefabs/KingdomManagement/Hierarchy/BellumHierarchyPanel.xml') -Raw
[xml]$node = Get-Content (Join-Path $root 'GUI/Prefabs/KingdomManagement/Hierarchy/BellumHierarchyTitleTreeNode.xml') -Raw
[xml]$strings = Get-Content (Join-Path $root 'ModuleData/Languages/EN/strings.xml') -Raw
$overlay = $panel.SelectSingleNode('//*[@Id="TitleActionOverlay"]')
if (!$overlay -or $overlay.SelectSingleNode('ancestor::*[@ClipContents="true"]')) { throw 'Menu must be outside clipped tree.' }
if ($overlay.NextSibling) { throw 'Overlay must render after the hierarchy.' }
if ($panel.SelectSingleNode('//*[@Id="HierarchyActionClipRect"]')) { throw 'Legacy bottom bar remains.' }
$menu = $overlay.SelectSingleNode('.//*[@Id="TitleActionMenu"]')
if ($menu.DataSource -ne '{SelectedTitle}') { throw 'Menu must target the selected title.' }
$commands = @('Claim','Usurp','Revoke','Form','Service','Grant','Rename','Dissolve')
foreach ($command in $commands) {
    $matches = $menu.SelectNodes(".//HierarchyActionButtonWidget[@Command.Click='Execute$command']")
    if ($matches.Count -ne 1) { throw "Missing or duplicate action: $command" }
    if (!$matches[0].ParentNode.SelectSingleNode('HintWidget')) { throw "Missing tooltip: $command" }
}
$slot = $node.SelectSingleNode('//*[@Id="ButtonContainer"]')
if ($slot.HeightSizePolicy -ne 'Fixed') { throw 'Selection must not change node height.' }
if (!$slot.SelectSingleNode('.//HierarchyOptionsButtonWidget[@IsVisible="@IsSelected"]')) { throw 'Options must be selection-only.' }
if ($strings.SelectNodes('//string[@id="BC_Hierarchy_TitleOptions"]').Count -ne 1) { throw 'Options localization invalid.' }
'Hierarchy action menu structure checks passed (8 commands, tooltip bindings, overlay layering, fixed node slot).'
