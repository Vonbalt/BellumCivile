$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prefab = [xml](Get-Content -Raw (Join-Path $root 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'))
$court = $prefab.SelectSingleNode("//ListPanel[@Id='BellumCourtFactionsParentWidget']")
if ($null -eq $court -or $court.IsVisible -ne '@ShowCourtFactions') {
    throw 'Court entries must share the court-section visibility binding.'
}
$crown = $prefab.SelectNodes("//ButtonWidget[@Command.Click='ExecuteSelectCrown']")
if ($crown.Count -ne 1 -or $crown[0].ParentNode.ParentNode -ne $court) {
    throw 'Exactly one Crown entry must live inside the court section.'
}
if ($court.Children.FirstChild.GetAttribute('Command.Click') -ne 'ExecuteSelectPrivyCouncil' -or
    $court.Children.ChildNodes[1].GetAttribute('Command.Click') -ne 'ExecuteSelectCrown') {
    throw 'Privy Council must appear first within the court, followed by Crown.'
}
Write-Output 'PASS: Privy Council leads the collapsible court section, followed by Crown.'
if ($prefab.SelectNodes("//TextWidget[@Text='@AgendaText']").Count -ne 0) { throw 'Old policy Agenda heading remains.' }
foreach ($heading in @('@PolicySupportText', '@PolicyOpposeText')) {
    if ($prefab.SelectNodes("//TextWidget[@Text='$heading']").Count -ne 2) {
        throw "Both Crown and court factions require $heading."
    }
}
Write-Output 'PASS: Crown and court factions have Support/Oppose headings, without the old Agenda heading.'
$crownHeader = $prefab.SelectSingleNode("//ListPanel[@Id='CrownContents']/Children")
$expected = @('@Title', '@ReignText', '@Demands')
for ($i = 0; $i -lt $expected.Count; $i++) {
    if ($crownHeader.ChildNodes[$i].GetAttribute('Text') -ne $expected[$i]) { throw 'Crown header order changed.' }
}
if ($crownHeader.ChildNodes[3].GetAttribute('Text') -ne '@Agenda') { throw 'Current agenda must be last in Crown header.' }
if ($prefab.SelectNodes("//HintWidget[@DataSource='{AgendaHint}' or @DataSource='{RivalsHint}']").Count -ne 0) {
    throw 'Redundant agenda hover hints must not return.'
}
foreach ($binding in @('@Agenda', '@RivalsText')) {
    $line = $prefab.SelectSingleNode("//TextWidget[@Text='$binding']")
    if ($line.GetAttribute('Brush.FontColor') -ne '@AgendaColor') { throw 'Agenda outcome color binding missing.' }
}
Write-Output 'PASS: Matching Crown header and agenda outcome colors are bound.'
$banners = $prefab.SelectSingleNode("//ListPanel[@DataSource='{FavorBanners}']")
if ($null -eq $banners.SelectSingleNode(".//ButtonWidget[@Command.Click='ExecuteSelect' and @IsEnabled='@IsEnabled' and @IsSelected='@IsSelected']")) {
    throw 'Crown banners need guarded selection and selected-state bindings.'
}
if ($null -eq $banners.SelectSingleNode(".//HintWidget[@DataSource='{Hint}']/Children/ButtonWidget")) {
    throw 'Banner hint must wrap button so disabled choices retain hover information.'
}
if ($prefab.SelectNodes("//TextWidget[@Text='@LegitimacyText']").Count -ne 0) { throw 'Retired legitimacy placeholder must not return.' }
$flow = Get-Content -Raw (Join-Path $root 'Behaviors/CourtAgendaPlayerFlow.cs')
if ($flow.Contains('TryShowCrownInquiry')) { throw 'Annual Crown favor popup must not return.' }
Write-Output 'PASS: Crown favor banners and disabled hover hints remain; legitimacy and annual popup are removed.'
foreach ($width in @('@LoyalistWidth', '@UncommittedWidth', '@RebelWidth')) {
    if ($prefab.SelectNodes("//Widget[@SuggestedWidth='$width']").Count -ne 0) { throw "Retired Crown power segment returned: $width" }
}
if ($prefab.SelectNodes("//HintWidget[@DataSource='{PowerHint}']").Count -ne 0) { throw 'Retired Crown power breakdown hint returned.' }
Write-Output 'PASS: Crown leaves room for Support/Oppose instead of the retired aggregate power display.'
