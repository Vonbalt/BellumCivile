param(
    [string]$GameFolder = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Mount & Blade II Bannerlord')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script:checks = 0
function Check($condition, $label) {
    if (!$condition) { throw $label }
    $script:checks++
    "PASS: $label"
}
function Element($document, $xpath) {
    $node = $document.SelectSingleNode($xpath)
    if ($null -eq $node) { throw "Missing XML element: $xpath" }
    $node
}

[xml]$panel = Get-Content -Raw -LiteralPath (Join-Path $root 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml')
[xml]$native = Get-Content -Raw -LiteralPath (Join-Path $GameFolder 'Modules/SandBox/GUI/Prefabs/KingdomManagement/Diplomacy/DiplomacyPanel.xml')
[xml]$brushes = Get-Content -Raw -LiteralPath (Join-Path $GameFolder 'Modules/SandBox/GUI/Brushes/Kingdom.xml')
$nativeAmount = Element $native '//TextWidget[@IntText="@InfluenceCost"]'
$nativeIcon = Element $nativeAmount.ParentNode 'Widget[@Sprite]'
$nativeBrush = Element $brushes "//Brush[@Name='$($nativeAmount.Brush)']"
$nativeStyle = Element $nativeBrush 'Styles/Style[@Name="Default"]'
Check ($nativeBrush.Font -eq 'Galahad' -and $nativeStyle.FontSize -eq '24') 'Native proposal costs use 24-point Galahad text.'

$expectedCosts = @('100', '@ProposalInfluenceCostText', '@DismissInfluenceCostText', '@InfluenceCostText', '@SelectedActionCostText')
$icons = $panel.SelectNodes('//*[@Sprite="SPKingdom\influence_icon_small"]')
Check ($icons.Count -eq $expectedCosts.Count -and
    $panel.SelectNodes('//*[@Sprite="General\Icons\Influence@2x"]').Count -eq 0) 'All five faction-tab influence costs use the kingdom icon; no old square influence glyph remains.'
foreach ($cost in $expectedCosts) {
    $amount = Element $panel "//TextWidget[@Text='$cost']"
    $row = $amount.ParentNode.ParentNode
    $icon = Element $amount.ParentNode 'Widget[@Sprite]'
    Check ($amount.Brush -eq $nativeAmount.Brush -and $amount.'Brush.FontSize' -eq $nativeStyle.FontSize) "Cost text matches the native proposal brush and size: $cost"
    Check ($icon.Sprite -eq $nativeIcon.Sprite -and $icon.SuggestedWidth -eq $nativeIcon.SuggestedWidth -and
        $icon.SuggestedHeight -eq $nativeIcon.SuggestedHeight) "Influence icon matches native dimensions and aspect ratio: $cost"
    Check ($amount.MarginRight -eq '4' -and $amount.'Brush.TextVerticalAlignment' -eq 'Center' -and
        !$amount.HasAttribute('PositionYOffset') -and !$icon.HasAttribute('PositionYOffset') -and
        $icon.VerticalAlignment -eq 'Center') "Cost number and icon have centered baselines and explicit separation: $cost"
    $height = if ($row.HeightSizePolicy -eq 'Fixed') { [int]$row.SuggestedHeight } else { [int]$row.ParentNode.ParentNode.SuggestedHeight }
    Check ($height -eq 32 -and [int]$icon.SuggestedHeight -le $height -and
        $amount.CanBreakWords -eq 'false' -and
        [int]$amount.MaxWidth + [int]$amount.MarginRight + [int]$icon.SuggestedWidth -le 190) "Larger cost fits its row and the narrowest 190-unit favor column: $cost"
}
Check ((Element $panel '//ListPanel[@IsVisible="@IsFoundCostVisible"]').SelectSingleNode('Children/TextWidget').Text -eq '100') 'Found cost and its original visibility binding are unchanged.'
Check ((Element $panel '//ListPanel[@IsVisible="@IsSelectedActionCostVisible"]').SelectSingleNode('Children/TextWidget').Text -eq '@SelectedActionCostText') 'Faction-creation confirmation retains its dynamic cost visibility.'
foreach ($command in @('ExecuteJoin', 'ExecuteCreate', 'ExecutePropose', 'ExecuteDismiss', 'ExecuteConfirmCreate', 'ExecuteCancelCreate')) {
    Check ($null -ne (Element $panel "//ButtonWidget[@Command.Click='$command']")) "Existing faction command remains wired: $command"
}
$favorCost = Element $panel '//TextWidget[@Text="@InfluenceCostText"]'
Check ($favorCost.DoNotAcceptEvents -eq 'true' -and (Element $favorCost.ParentNode 'Widget').DoNotAcceptEvents -eq 'true') 'Favor costs remain click-through, preserving banner selection and tooltips.'
$left = Element $panel '//*[@Id="BellumFactionsLeftList"]'
$scroll = Element $left './/ScrollablePanel'
foreach ($visibility in @('@ShowFactionActionButtons', '@IsPrivyCouncilSelected')) {
    $footer = Element $left "Children/ListPanel[@IsVisible='$visibility']"
    Check (45 + 32 + [int]$footer.MarginBottom -le [int]$scroll.MarginBottom) "Larger costs stay below the faction list without overlap: $visibility"
}
$modal = Element $panel '//*[@IsVisible="@IsCreatingFaction"]'
$modalScroll = Element $modal './/ScrollablePanel'
$modalScrollbar = Element $modal './/ScrollbarWidget[@Id="BellumTypeListScrollbar"]'
$confirm = Element $modal './/ButtonWidget[@Command.Click="ExecuteConfirmCreate"]'
$footer = $confirm.ParentNode.ParentNode.ParentNode.ParentNode
Check (45 + 32 + [int]$footer.MarginBottom -le [int]$modalScroll.MarginBottom -and
    $modalScroll.MarginBottom -eq $modalScrollbar.MarginBottom) 'Creation footer has sufficient clearance and its scrollbar matches the adjusted clip height.'
Check ($panel.SelectNodes('//Widget[@SuggestedWidth="10" and @SuggestedHeight="32"]').Count -eq 2) 'Join and Cancel retain matching blank cost space to keep paired buttons aligned.'
"$script:checks faction influence-cost sizing checks passed against $GameFolder. In-game visual testing remains."
