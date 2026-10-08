param([string]$GameFolder = $env:GameFolder)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$GameFolder) {
    $GameFolder = Join-Path ${env:ProgramFiles(x86)} 'Steam/steamapps/common/Mount & Blade II Bannerlord'
}
function Check($condition, [string]$message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}
$source = Get-Content -Raw (Join-Path $root 'UI/MarriageOfferHouseholdPrefabExtension.cs')
$body = [regex]::Match($source, 'LoadXml\(@"([\s\S]*?)"\);').Groups[1].Value
[xml]$extension = $body.Replace('""', '"')
[xml]$native = Get-Content -Raw (Join-Path $GameFolder 'Modules/SandBox/GUI/Prefabs/Map/MarriageOfferPopup.xml')
$nodes = $native.SelectNodes("descendant::ListPanel[@DataSource='{ConsequencesList}']")
Check ($nodes.Count -eq 1) 'Offer extension targets exactly one installed native consequences list.'
$nativeTermsWidth = [double]$nodes[0].SelectSingleNode('ItemTemplate/RichTextWidget').SuggestedWidth
$replacement = $native.ImportNode($extension.DocumentElement, $true)
Check ($replacement.WidthSizePolicy -eq 'Fixed' -and
    [double]$replacement.SuggestedWidth -gt 0 -and
    [double]$replacement.SuggestedWidth -le $nativeTermsWidth -and
    $replacement.HorizontalAlignment -eq 'Center') 'Offer terms stay inside the native centered text column, clear of both skill panels.'
[void]$nodes[0].ParentNode.ReplaceChild($replacement, $nodes[0])
Check ($native.SelectNodes('//CharacterTableauWidget').Count -eq 2) 'Native portraits remain intact.'
Check ($native.SelectNodes("//ButtonWidget[@Id='MarriagePopupOkButton' or @Id='MarriagePopupCancelButton']").Count -eq 2) 'Native accept and decline buttons remain intact.'
Check ($replacement.SuggestedHeight -eq '180' -and $replacement.ClipContents -eq 'true') 'Long or translated terms stay within a bounded scroll area.'
Check ($replacement.SelectSingleNode("Children/Widget[@Id='Clip']/Children/ListPanel[@Id='Terms']") -ne $null) 'InnerPanel and clipping paths resolve.'
Check ($replacement.SelectSingleNode("Children/ScrollbarWidget[@Id='Scrollbar']/Children/Widget[@Id='Handle']") -ne $null) 'Scrollbar and handle paths resolve.'
$clip = $replacement.SelectSingleNode("Children/Widget[@Id='Clip']")
$terms = $clip.SelectSingleNode("Children/ListPanel[@Id='Terms']")
$paragraph = $terms.SelectSingleNode('ItemTemplate/RichTextWidget')
$scrollbar = $replacement.SelectSingleNode("Children/ScrollbarWidget[@Id='Scrollbar']")
Check ($clip.WidthSizePolicy -eq 'StretchToParent' -and $clip.ClipContents -eq 'true' -and
    $terms.WidthSizePolicy -eq 'StretchToParent' -and
    $paragraph.WidthSizePolicy -eq 'StretchToParent' -and $paragraph.HeightSizePolicy -eq 'CoverChildren') 'Wrapped paragraphs stay within the clipped text width.'
Check ($scrollbar.WidthSizePolicy -eq 'Fixed' -and $scrollbar.HorizontalAlignment -eq 'Right' -and
    [double]$clip.MarginRight -ge [double]$scrollbar.SuggestedWidth -and
    [double]$clip.MarginRight -lt [double]$replacement.SuggestedWidth) 'Scrollbar space is reserved inside the column, without covering text or skills.'
Check ($replacement.SelectNodes(".//*[@DataSource='{ConsequencesList}']").Count -eq 1) 'Offer terms have exactly one data binding.'
$definer = Get-Content -Raw (Join-Path $root 'BellumCivileSaveDefiner.cs')
$ids = [regex]::Matches($definer, 'Add(?:Class|Enum)Definition\(typeof\([^\r\n]+\), (\d+)\)') |
    ForEach-Object { [int]$_.Groups[1].Value }
Check (($ids | Sort-Object -Unique).Count -eq $ids.Count) 'All save class and enum IDs remain unique.'
Check ($definer.Contains('AddClassDefinition(typeof(PlayerMarriageAgreement), 128)') -and
    $definer.Contains('ConstructContainerDefinition(typeof(List<PlayerMarriageAgreement>))')) 'Offer record and saved list have explicit registrations.'
$dialogue = Get-Content -Raw (Join-Path $root 'Behaviors/SpecificMarriageProposalBehavior.cs')
Check ($dialogue.Contains('BC_Marriage_ChooseMatrilineal') -and $dialogue.Contains('BC_Marriage_ChoosePatrilineal') -and
    $dialogue.Contains('PlayerMarriageAgreementBehavior.ConfirmDeparture')) 'Both household choices and own-heir confirmation are wired into dialogue.'
'Static prefab/save contracts passed. Rendered popup and native save-file roundtrip still require an in-game test.'
