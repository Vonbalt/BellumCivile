$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Check($condition, $message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}

$display = Read 'EstateInheritanceDisplay.cs'
$mixin = Read 'UI/EncyclopediaHeroFeudalTitlesMixin.cs'
$prefab = Read 'UI/EncyclopediaHeroFeudalTitlesPrefabExtension.cs'
$patch = Read 'Patches/EncyclopediaClanSuccessionPatch.cs'
$xmlText = [regex]::Match($prefab, '(?s)_document\.LoadXml\(@"(.*?)"\);').Groups[1].Value
$xml = [xml]$xmlText

Check ($patch.Contains('EstateInheritanceDisplay.BuildClanTooltip(clan)') -and
    $patch.Contains('{=BC_EstatePreview_ClanSuccessor}Clan successor:')) 'Clan row explicitly names leadership and uses the augmented tooltip.'
Check ($display.Contains('SuccessionLawHelper.BuildSuccessionLineTooltipProperties(clan)')) 'The established law and ordered clan-succession tooltip is preserved.'
Check ($display.Contains('GetPendingEstatePreviews()') -and $display.Contains('!record.Completed') -and
    $display.Contains('!share.Completed')) 'Pending rows read actual incomplete records and shares.'
Check ($display.Contains('pendingSources.Contains(source)') -and
    $display.IndexOf('pendingSources.Contains(source)') -lt $display.IndexOf('EstateInheritancePolicy.BuildProjection(source, predecessor)')) 'Active source snapshots suppress hero-page recalculation.'
Check ($display -match '(?s)if \(pending.Count > 0\).*?AddClanShares\(rows, record, true\);.*?else if.*?EstateInheritancePolicy.BuildProjection\(clan\)') 'Clan tooltips do not project over pending estates.'
Check ($display.Contains('ids.Except(delivered ?? Enumerable.Empty<string>()).Except(superseded ?? Enumerable.Empty<string>())')) 'Delivered and superseded assets are not displayed as outstanding.'
Check ($display.Contains('titleIds.Union(Outstanding(share.SovereignTransfers,') -and
    $display.Contains('share.DeliveredSovereignTransfers, share.SupersededTitles, true)')) 'Unfinished sovereign settlement remains visible after legal-title delivery.'
Check ($display.Contains('share.IsEscheat ? record.Realm?.RulingClan : share.Heir?.Clan') -and
    $display.Contains('pending ? share.Recipient : null')) 'Expected recipient houses use current heir membership or escheat; pending shares retain recorded recipients.'
Check ($display.Contains('FeudalTitleDisplayHelper.FormatTitleName(title, record.Source)') -and
    !$display.Contains('ResolveDisplayTitle') -and !$display.Contains('BC_EstatePreview_TitleRank') -and
    $display.Contains('FromPerson(record.Deceased, record.Source')) 'Inheritance uses full landed titles without duplicate holder ranks and retains predecessor houses.'
Check ($display.Contains('title.TitleType == FeudalTitleType.Barony') -and
    $display.Contains('shownBaronySettlements.Add(title.CapitalSettlementId)') -and
    $display.Contains('if (shownBaronySettlements.Contains(id))') -and
    $display.IndexOf('Outstanding(share.Titles, share.DeliveredTitles, share.SupersededTitles, pending)') -lt
        $display.IndexOf('shownBaronySettlements.Add(title.CapitalSettlementId)')) 'Only displayed baronies deduplicate fiefs, so a delivered title cannot hide pending possession.'
Check ($display.Contains('id == record.RealmCrownTitleId || id == politicalCrownId') -and
    $display.Contains('HereditaryRealmSuccession.GetLine(realm).FirstOrDefault()') -and
    $display.Contains('!share.DomesticOnly && record.PolicyVersion > 0')) 'Political crowns use realm succession while sovereign estate packages are distinctly labeled.'
Check ($display.Contains('bool sovereign = crownEstate && id == share.RootTitleId') -and
    $display.Contains('sovereign ? SovereignTitle(name) : crownEstate ? CrownEstateAsset(name) : name') -and
    $display.Contains('crownEstate ? CrownEstateAsset(text.ToString()) : text.ToString()')) 'Only sovereign roots use sovereign succession labels; package assets use Crown estate.'
Check ($display -match '(?s)pending.Any\(record => record.PolicyVersion == 0\)\s*\? new TextObject\("\{=BC_EstatePreview_LegacyPending\}.*?: new TextObject\("\{=BC_EstatePreview_SameRealm\}') 'Legacy pending allocations use a preservation caveat instead of a contradictory current-rule statement.'
Check ($display.Contains('private const int TooltipAssetLimit = 5;') -and
    $display.Contains('assets.Take(TooltipAssetLimit)') -and $display.Contains('assets.Count - TooltipAssetLimit') -and
    $display.Contains('group.SelectMany(share => GetAssets(record, share, pending))')) 'Tooltips cap combined beneficiary packages at five assets plus a remaining count.'
$heroShares = [regex]::Match($display, '(?s)private static void AddHeroShares\(.*?(?=private static void AddClanShares)').Value
Check ($heroShares.Contains('foreach (string asset in assets)') -and $heroShares -notmatch 'SummarizeAssets|\.Take\(') 'Hero-page inheritance retains every asset.'
Check ($display.Contains('BC_EstatePreview_SameRealm') -and $display.Contains('BC_EstatePreview_Caveat')) 'Same-realm ordinary-land restrictions and current-circumstances caveats are present.'
Check ($display -notmatch 'DailyTick|HourlyTick|RegisterEvents|\.Shares\.Add\(|share\.\w+\s*=(?!=)|record\.\w+\s*=(?!=)') 'Presentation has no scheduled scans or estate mutations.'

$refresh = $mixin.Substring($mixin.IndexOf('public override void OnRefresh()'))
Check (([regex]::Matches($refresh, 'EstateInheritanceDisplay.BuildHeroEntries\(_hero\)')).Count -eq 1 -and
    $refresh.IndexOf('EstateInheritanceDisplay.BuildHeroEntries(_hero)') -lt
        $refresh.IndexOf('if (_hero?.Clan == null || _hero.Clan.Leader != _hero)')) 'Inheritance is built once per refresh before the clan-leader-only guard.'
Check ($refresh.Contains('BellumInheritance.Clear()') -and $refresh.Contains('HasBellumInheritance = BellumInheritance.Count > 0')) 'Refresh clears old inheritance rows and updates empty-state visibility.'
Check ($xml.SelectNodes("//ListPanel[@IsVisible='@HasBellumInheritance']").Count -eq 1 -and
    $xml.SelectNodes("//GridWidget[@DataSource='{BellumInheritance}'][@ColumnCount='2'][@UseDynamicCellHeight='true']").Count -eq 1 -and
    $xml.SelectNodes("//*[@Text='@BellumInheritanceCaveat']").Count -eq 0) 'Prefab binds the two-column inheritance grid without repeating the tooltip caveat.'
$labels = $xml.SelectNodes("//GridWidget[@DataSource='{BellumInheritance}']//TextWidget")
Check ($labels.Count -eq 2 -and @($labels | Where-Object {
    $_.WidthSizePolicy -ne 'StretchToParent' -or $_.HeightSizePolicy -ne 'CoverChildren'
}).Count -eq 0) 'Title and provenance occupy separate full-width wrapping rows without fixed-height cells.'
Check ($xml.SelectNodes("//*[@Id='AlliesDivider']").Count -eq 1 -and
    !($xml.SelectNodes('//*[@Id]') | Group-Object Id | Where-Object Count -gt 1)) 'The vanilla allies divider remains and prefab IDs are unique.'
$ids = [regex]::Matches($display, '\{=([^}]+)\}') | ForEach-Object { $_.Groups[1].Value }
Check (@($ids | Where-Object { $_ -notlike 'BC_EstatePreview_*' }).Count -eq 0) 'Every new display localization ID uses BC_EstatePreview_*.'
$strings = [xml](Read 'ModuleData/Languages/EN/strings.xml')
$sources = $display + (Read 'UI/EncyclopediaHeroFeudalTitlesMixin.cs') + (Read 'Patches/EncyclopediaClanSuccessionPatch.cs') + (Read 'Behaviors/PartitionCrossClanInheritance.cs')
$localized = [regex]::Matches($sources, '"\{=(BC_Estate(?:Preview|Inheritance)_[^}]+)\}([^"\r\n]*)"')
foreach ($match in $localized) {
    $id = $match.Groups[1].Value
    $nodes = $strings.SelectNodes("//string[@id='$id']")
    if ($nodes.Count -ne 1 -or $nodes[0].text -cne $match.Groups[2].Value) {
        throw "Missing, duplicated, or mismatched localization for $id"
    }
}
Check ($localized.Count -ge 30) 'All estate UI and notification IDs have unique matching English text and placeholders.'
Write-Output 'Source/XML contracts only; no live engine UI rendering or campaign behavior is verified.'
