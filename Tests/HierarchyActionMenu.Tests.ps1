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
$hints = @{ Challenge='Challenge'; Claim='Claim'; Renounce='Renounce'; Usurp='Usurp'; Revoke='Revocation'; Form='Formation'; Service='Service'; Grant='Grant'; Rename='Rename'; Dissolve='Dissolution' }
foreach ($command in $hints.Keys) {
    $matches = $menu.SelectNodes(".//HierarchyActionButtonWidget[@Command.Click='Execute$command']")
    if ($matches.Count -ne 1) { throw "Missing or duplicate action: $command" }
    $expected = '{' + $hints[$command] + 'Hint}'
    $enabledHint = $matches[0].SelectSingleNode('Children/HintWidget')
    $disabledHint = $matches[0].ParentNode.SelectSingleNode('HintWidget')
    foreach ($hint in @($enabledHint, $disabledHint)) {
        if (!$hint -or $hint.DataSource -ne $expected -or
            $hint.GetAttribute('Command.HoverBegin') -ne 'ExecuteBeginHint' -or
            $hint.GetAttribute('Command.HoverEnd') -ne 'ExecuteEndHint') { throw "Incomplete enabled or disabled tooltip: $command" }
    }
}
foreach ($command in @('Grant','Revoke')) {
    $row = $menu.SelectSingleNode(".//HierarchyActionButtonWidget[@Command.Click='Execute$command']").ParentNode.ParentNode
    if ($row.HasAttribute('IsVisible')) { throw "Lordship action must not be hidden on personal grants: $command" }
}
foreach ($command in @('Claim','Usurp','Service','Rename','Dissolve')) {
    $row = $menu.SelectSingleNode(".//HierarchyActionButtonWidget[@Command.Click='Execute$command']").ParentNode.ParentNode
    if ($row.IsVisible -ne '@IsTitleNode') { throw "Title-only action must stay hidden on lordships: $command" }
}
$revoke = $menu.SelectSingleNode(".//HierarchyActionButtonWidget[@Command.Click='ExecuteRevoke']")
if ($revoke.IsEnabled -ne '@IsRevocationEnabled' -or
    $revoke.ParentNode.SelectSingleNode('HintWidget').DataSource -ne '{RevocationHint}') {
    throw 'Revocation availability and tooltip must share the selected title preview.'
}
$view = Get-Content (Join-Path $root 'UI/VanillaTabs/Kingdoms/Hierarchy/HierarchyTitleNodeVM.cs') -Raw
if ($view -notmatch 'IsRevocationEnabled\s*=\s*revocation\.CanRevoke' -or
    $view -notmatch 'RevocationHint\s*=\s*new HintViewModel\(BuildRevocationHint\(revocation\)\)') {
    throw 'Hierarchy revocation must bind both its disabled state and hint from the same preview.'
}
$usurp = $menu.SelectSingleNode(".//HierarchyActionButtonWidget[@Command.Click='ExecuteUsurp']")
if ($usurp.IsEnabled -ne '@IsUsurpEnabled' -or $view -notmatch 'IsUsurpEnabled\s*=\s*usurpation\.CanUsurp' -or
    $view -notmatch 'UsurpHint\s*=\s*new HintViewModel\(BuildUsurpHint\(usurpation\)\)') {
    throw 'Hierarchy usurpation must bind both its disabled state and hint from the same preview.'
}
foreach ($id in @('BC_Hierarchy_UsurpIndependentBranchCount', 'BC_Hierarchy_ActionReasonUsurpIndependentControl')) {
    if ($strings.SelectNodes("//string[@id='$id']").Count -ne 1) { throw "Missing or duplicate usurpation localization: $id" }
}
$slot = $node.SelectSingleNode('//*[@Id="ButtonContainer"]')
if ($slot.HeightSizePolicy -ne 'Fixed') { throw 'Selection must not change node height.' }
if (!$slot.SelectSingleNode('.//HierarchyOptionsButtonWidget[@IsVisible="@IsSelected"]')) { throw 'Options must be selection-only.' }
if ($strings.SelectNodes('//string[@id="BC_Hierarchy_TitleOptions"]').Count -ne 1) { throw 'Options localization invalid.' }
if ($node.Prefab.Window.FirstChild.Name -ne 'HierarchyTitleTreeNodeWidget') { throw 'Hierarchy must use scoped dynamic connector cleanup.' }
'Hierarchy action menu structure checks passed (10 commands, lordship visibility, enabled/disabled tooltip bindings, overlay layering, fixed node slot, dynamic tree widget).'
