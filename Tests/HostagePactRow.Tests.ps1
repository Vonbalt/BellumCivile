$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$prefab = Get-Content -LiteralPath (Join-Path $root 'GUI/Prefabs/Parley/BellumPeaceParley.xml') -Raw
$row = $prefab.SelectSingleNode('//*[@Id="PoliticsDraftList"]/ItemTemplate/Widget')
if ($row.SuggestedHeight -ne '@RowHeight') { throw 'Politics rows must use their individual heights.' }
$toggle = $row.SelectSingleNode('Children/EncyclopediaFilterListItemButtonWidget')
$controls = $row.SelectSingleNode('Children/ListPanel[@IsVisible="@HasDurationControls"]')
if (!$controls -or $toggle.MarginRight -ne '@ToggleRightMargin') { throw 'Duration controls must sit outside the toggle hit area.' }
$hint = $row.SelectSingleNode('Children/HintWidget')
if (!$hint -or $hint.IsVisible -ne '@IsDisabled' -or $hint.IsDisabled -ne 'true' -or
    $hint.MarginRight -ne '@ToggleRightMargin' -or $hint.GetAttribute('Command.HoverBegin') -ne 'ExecuteBeginHint' -or
    $hint.GetAttribute('Command.HoverEnd') -ne 'ExecuteEndHint') {
    throw 'Disabled politics rows need a sibling hover hint outside the disabled toggle and duration controls.'
}
if ($controls.SuggestedWidth -ne '136') { throw 'Inline controls need their reserved 136-unit column.' }
$buttons = $controls.SelectNodes('Children/ButtonWidget')
if ($buttons.Count -ne 2) { throw 'Expected minus and plus controls.' }
$wealthMinus = $prefab.SelectSingleNode('//*[@Command.Click="ExecuteDecreaseTribute"]')
foreach ($button in $buttons) {
    if ($button.SuggestedWidth -ne $wealthMinus.SuggestedWidth -or $button.SuggestedHeight -ne $wealthMinus.SuggestedHeight) {
        throw 'Hostage controls must match Wealth button dimensions.'
    }
}
if ($buttons[0].Brush -ne $wealthMinus.Brush -or $buttons[0].GetAttribute('Command.Click') -ne 'ExecuteDecreaseDuration' -or
    $buttons[1].GetAttribute('Command.Click') -ne 'ExecuteIncreaseDuration') { throw 'Incorrect inline button styling or commands.' }
$cost = $controls.SelectSingleNode('Children/TextWidget')
if ($cost.Text -ne '@CostText' -or $cost.SuggestedWidth -ne '58' -or $cost.MarginLeft -ne '4') {
    throw 'Hostage cost must follow the buttons in the Wealth-style cost column.'
}
if ($prefab.OuterXml.Contains('@HostageControlsHeight') -or $prefab.OuterXml.Contains('@HostageDurationText')) {
    throw 'Standalone hostage duration header must be removed.'
}
$vm = Get-Content -LiteralPath (Join-Path $root 'UI/Parley/TreatyClaimDraftOptionVM.cs') -Raw
foreach ($attribute in $row.SelectNodes('.//@*')) {
    if ($attribute.Value.StartsWith('@')) {
        $name = $attribute.Value.Substring(1)
        if ($vm -notmatch ('\b' + [regex]::Escape($name) + '\b')) { throw "Unknown row binding: $name" }
    }
}
'PASS: inline hostage layout, separate click areas, Wealth styling, bindings and removed header.'
[xml]$strings = Get-Content -LiteralPath (Join-Path $root 'ModuleData/Languages/EN/strings.xml') -Raw
$parley = Get-Content -LiteralPath (Join-Path $root 'UI/Parley/PeaceParleyVM.cs') -Raw
foreach ($id in @('BC_Parley_HostageNoCandidates', 'BC_Parley_HostageWinnerRequired', 'BC_Parley_HostageDurationRequired', 'BC_Parley_HostageNoHolding')) {
    $entries = @($strings.base.strings.string | Where-Object id -eq $id)
    if ($entries.Count -ne 1 -or !$parley.Contains(('{=' + $id + '}' + $entries[0].text))) {
        throw "Missing, duplicate or mismatched hostage requirement localization: $id"
    }
}
'PASS: disabled hostage explanations have unique localization entries matching their fallbacks.'
