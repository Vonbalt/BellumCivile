$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$xml = [xml](Get-Content -Raw (Join-Path $root 'GUI/Prefabs/KingdomManagement/Succession/BellumSuccessionPolicyPanel.xml'))
$vm = Get-Content -Raw (Join-Path $root 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs')
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
Check ($xml.SelectSingleNode("//*[@Id='HereditarySuccessionHeader']").IsVisible -eq '@IsHereditary' -and
    $xml.SelectSingleNode("//*[@Id='ElectiveSuccessionHeader']").IsVisible -eq '@IsElective') 'Independent hereditary/elective headers use classified visibility.'
foreach ($pair in @(@('SuccessionLeftLaws', 'GenderLaws', 'ElectiveTerms'), @('SuccessionRightLaws', 'HereditaryLaws', 'ElectiveLaws'))) {
    $lists = $xml.SelectNodes("//*[@Id='$($pair[0])']//NavigatableListPanel")
    Check ($lists.Count -eq 2 -and $lists[0].DataSource -eq "{$($pair[1])}" -and $lists[1].DataSource -eq "{$($pair[2])}") "Correct group order in $($pair[0])."
}
Check ($xml.SelectNodes("//*[@DataSource='{SuccessionLaws}']").Count -eq 0) 'Old mixed succession list removed.'
$scroll = $xml.SelectSingleNode('//ScrollablePanel')
Check ($scroll.ClipRect -eq 'SuccessionPolicyLawClip' -and
    $scroll.InnerPanel -eq 'SuccessionPolicyLawClip\SuccessionPolicyLawColumns' -and
    $scroll.VerticalScrollbar -eq '..\SuccessionPolicyLawScrollbar' -and
    $xml.SelectSingleNode("//*[@Id='SuccessionPolicyLawClip']").ClipContents -eq 'true') 'Both law columns share a clipped scroll surface.'
Check ($xml.SelectNodes("//*[@Id='HereditarySuccessionHeader']//*[@Command.Click='ExecuteOpenHero']").Count -eq 2 -and
    $xml.SelectNodes("//*[@DataSource='{AdditionalHeirsHint}']").Count -eq 1) 'Heir portrait links and remainder tooltip remain.'
Check ($vm -match 'line.Skip\(1\).Take\(3\)' -and $vm -match 'line.Skip\(4\)') 'Crown heir plus three successors, then full remainder.'
Check ($vm -match 'ward.IsAlive && dynasty != _kingdom\?\.RulingClan' -and
    $vm -match 'RegencyText = text.ToString\(\)' -and $xml.SelectNodes("//*[@Text='@RegencyText']").Count -eq 1) 'Reigning ward is not reinserted as their own heir; regency is identified separately.'
Check ($vm -match 'InGroup\(RealmLawRegistry.GenderGroup\)' -and $vm -match 'InGroup\(RealmLawRegistry.SuccessionGroup\)' -and
    $vm -match 'TryApplyPlayerLaw' -and $vm -match 'if \(apply\(\)\) RefreshValues\(\);') 'Both XML law groups share validated enactment and refresh flow.'
Check ($vm.Contains('registry.InGroup(RealmLawRegistry.TermGroup)') -and $vm.Contains('canChange && IsElective')) 'Mandate controls use registered laws and require elective government.'
Check ($vm -notmatch 'DetermineInitialCandidates|new KingdomElection|ActiveElections' -and
    $vm.Contains('HereditaryLoyaltyBehavior.Instance?.Get(')) 'No side-effecting election preview; loyalty reads the shared assessment.'
Check ($xml.SelectNodes('//*[@Id="HereditarySuccessionHeader"]//*[@Text="@LoyaltyText"]').Count -eq 2) 'Crown heir and successor template each bind the loyalty meter.'
Check ($xml.SelectNodes('//TextWidget[@Text="@LoyaltyLabel" and @Brush.FontColor="#F1D8A4FF"]').Count -eq 2) 'Both Loyalty prefixes stay neutral beige.'
Check ($xml.SelectNodes('//TextWidget[@Text="@LoyaltyText" and @Brush.FontColor="@LoyaltyColor"]').Count -eq 2) 'Only the state and percentage use the loyalty color.'
Check ($xml.SelectNodes('//*[@Id="ElectiveSuccessionHeader"]//*[@Text="@AcceptanceText"]').Count -eq 1) 'Elective candidate template binds the acceptance meter.'
Check ([int]$xml.SelectSingleNode('//*[@DataSource="{ElectionCandidates}"]').SuggestedHeight + [int]$xml.SelectSingleNode('//*[@DataSource="{ElectionCandidates}"]').MarginTop -le
    [int]$xml.SelectSingleNode('//*[@Id="ElectiveSuccessionHeader"]').SuggestedHeight) 'Expanded elective rows fit inside the reserved header.'
$ids = $xml.SelectNodes('//*[@Id]') | Group-Object Id | Where-Object Count -gt 1
Check ($xml.SelectNodes('//*[@DataSource="{BannerVisual}"]').Count -eq 3) 'Both hereditary portrait templates and elective portraits carry clan banners.'
$factions = [xml](Get-Content -Raw (Join-Path $root 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'))
foreach ($constant in $xml.SelectNodes('/Prefab/Constants/Constant')) {
    Check ($constant.OuterXml -eq $factions.SelectSingleNode("/Prefab/Constants/Constant[@Name='$($constant.Name)']").OuterXml) "Succession defines its own matching faction banner constant: $($constant.Name)."
}
foreach ($banner in $xml.SelectNodes('//*[@DataSource="{BannerVisual}"]')) {
    foreach ($dimension in @('Width', 'Height')) {
        $name = "Bellum.Banner.$dimension.Scaled"
        Check ($banner.GetAttribute("Suggested$dimension") -eq "!$name" -and $null -ne $xml.SelectSingleNode("/Prefab/Constants/Constant[@Name='$name']")) "Succession banner $dimension resolves locally."
    }
}
foreach ($file in @('UI/FactionItemVM.cs', 'UI/FactionMemberVM.cs', 'UI/Parley/TreatyCouncilMemberVM.cs',
    'UI/VanillaTabs/Kingdoms/Factions/PrivyCouncilVM.cs', 'UI/VanillaTabs/Kingdoms/Hierarchy/HierarchyTitleNodeVM.cs',
    'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs')) {
    $source = Get-Content -Raw (Join-Path $root $file)
    Check ($source.Contains('PortraitAppearance.Create(') -and !$source.Contains('CampaignUIHelper.GetCharacterCode(')) "Encounter-independent helmet-free portrait helper in $file."
}
$appearance = Get-Content -Raw (Join-Path $root 'UI/PortraitAppearance.cs')
Check ($appearance.Contains('character.Equipment.Clone()') -and $appearance.Contains('equipment[EquipmentIndex.Head] = default(EquipmentElement)') -and
    $appearance.Contains('CharacterCode.CreateFrom(character, equipment)') -and !$appearance.Contains('IsHeroInformationHidden')) 'Portrait equipment is copied and stripped without hiding unmet heroes.'
$names = $xml.SelectNodes('//*[@Id="HereditarySuccessionHeader"]//*[@Text="@Name"] | //*[@Id="ElectiveSuccessionHeader"]//*[@Text="@ClanName"]')
Check ($names.Count -eq 3 -and @($names | Where-Object { $_.GetAttribute('Brush.FontSize') -ne '24' }).Count -eq 0) 'All succession names match the 24-point court member name font.'
Check ($vm.Contains('3 * 228') -and $xml.SelectSingleNode('//*[@DataSource="{NextHeirs}"]/ItemTemplate/Widget').SuggestedWidth -eq '220') 'Wider hereditary columns match the view-model track width.'
Check ($xml.SelectNodes('//*[@Id="ElectiveSuccessionHeader"]//ButtonWidget[@Command.HoverBegin="ExecuteBeginSupportHint" and @Command.HoverEnd="ExecuteEndSupportHint"]').Count -eq 1 -and
    $xml.SelectNodes('//*[@DataSource="{SupportHint}"]').Count -eq 0) 'Electoral support tooltip is owned by the clickable portrait, not the support row.'
Check (-not $ids) 'Prefab widget IDs are unique.'
Write-Output 'Structural/source checks only; actual Gauntlet rendering and interaction require campaign verification.'
