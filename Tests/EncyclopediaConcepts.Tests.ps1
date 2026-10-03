$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ConceptArticleSource.ps1')
$expected = @(Get-BellumConceptArticles -Root $root)
[xml]$concepts = Get-Content (Join-Path $root 'ModuleData/bellum_concepts.xml') -Raw
[xml]$strings = Get-Content (Join-Path $root 'ModuleData/Languages/EN/strings.xml') -Raw
[xml]$module = Get-Content (Join-Path $root '_Module/SubModule.xml') -Raw
[xml]$project = Get-Content (Join-Path $root 'BellumCivile.csproj') -Raw
$nodes = @($concepts.Concepts.Concept)
if ($nodes.Count -ne 31) { throw 'Expected 31 shipped articles.' }
foreach ($attribute in @('id', 'link_id')) {
    if (@($nodes | ForEach-Object { $_.$attribute } | Select-Object -Unique).Count -ne 31) {
        throw "Duplicate $attribute."
    }
}
$links = @($nodes | ForEach-Object { $_.link_id })
foreach ($article in $expected) {
    $node = @($nodes | Where-Object { $_.id -ceq $article.Id })
    if ($node.Count -ne 1 -or $node[0].group -cne 'BellumCivile' -or $node[0].link_id -cne $article.Link) {
        throw "Invalid article identity/group: $($article.Id)"
    }
    foreach ($pair in @(@('title', $article.TitleKey, $article.Title), @('text', $article.TextKey, $article.Text))) {
        $field = $pair[0]
        if ($node[0].$field -cne ('{=' + $pair[1] + '}' + $pair[2])) { throw "Draft mismatch: $($article.Id)/$field" }
        $entry = @($strings.SelectNodes('//string') | Where-Object { $_.id -ceq $pair[1] })
        if ($entry.Count -ne 1 -or $entry[0].text -cne $pair[2]) { throw "Missing/mismatched localization: $($pair[1])" }
    }
    if ($article.Text -match '\]\(#|^###|\*\*') { throw "Unconverted Markdown: $($article.Id)" }
    foreach ($token in [regex]::Matches($article.Text, '\{([^}]+)\}')) {
        if ($token.Groups[1].Value -cne 'newline' -and $token.Groups[1].Value -cnotin $links) {
            throw "Unresolved text variable: $($token.Value)"
        }
    }
}
$overview = $nodes | Where-Object { $_.id -ceq 'bc_concept_bellum_civile' }
foreach ($node in $nodes | Where-Object { $_ -ne $overview }) {
    if (!$overview.text.Contains('{' + $node.link_id + '}')) { throw "Missing index entry: $($node.id)" }
    if (!$node.text.Contains('{' + $overview.link_id + '}')) { throw "Missing return link: $($node.id)" }
}
$filter = @($strings.SelectNodes('//string') | Where-Object { $_.id -ceq 'BC_Concept_Filter' })
if ($filter.Count -ne 1 -or $filter[0].text -cne 'Bellum Civile') { throw 'Invalid filter localization.' }
$registration = @($module.Module.Xmls.XmlNode | Where-Object { $_.XmlName.id -ceq 'Concepts' -and $_.XmlName.path -ceq 'bellum_concepts' })
if ($registration.Count -ne 1) { throw 'Concepts module registration missing or duplicated.' }
$modes = @($registration[0].IncludedGameTypes.GameType | ForEach-Object { $_.value })
if ('Campaign' -cnotin $modes -or 'CampaignStoryMode' -cnotin $modes) { throw 'Both campaign modes must load concepts.' }
if (!($project.SelectNodes('//Content') | Where-Object { $_.Include -eq 'ModuleData\bellum_concepts.xml' })) {
    throw 'Concept file missing from project content.'
}
Write-Output 'PASS: 31 articles match the approved draft; 63 localized strings; all links, overview entries, return links and both campaign registrations validated.'
