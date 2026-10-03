# Read the approved manuscript without introducing a runtime Markdown dependency.
function Get-BellumConceptArticles {
    param([string]$Root = (Split-Path $PSScriptRoot -Parent))
    $articles = [System.Collections.Generic.List[object]]::new()
    $section = ''
    $article = $null
    foreach ($line in Get-Content (Join-Path $Root 'docs/encyclopedia-articles-draft.md')) {
        if ($line -match '^## (.+)$') { $section = $Matches[1]; $article = $null }
        elseif ($line -match '^### (.+)$') {
            $title = $Matches[1]
            $slug = $title.ToLowerInvariant() -replace '[^a-z0-9 -]', '' -replace ' ', '-'
            $key = $slug.Replace('-', '_')
            $article = [pscustomobject]@{
                Title = $title; Section = $section; Slug = $slug
                Id = 'bc_concept_' + $key
                Link = 'BC_CONCEPT_' + $key.ToUpperInvariant() + '_LINK'
                TitleKey = 'BC_Concept_' + $key + '_Title'
                TextKey = 'BC_Concept_' + $key + '_Text'
                Paragraphs = [System.Collections.Generic.List[string]]::new()
                Text = ''
            }
            $articles.Add($article)
        }
        elseif ($article -and ![string]::IsNullOrWhiteSpace($line)) { $article.Paragraphs.Add($line.Trim()) }
    }
    if ($articles.Count -ne 31) { throw "Expected 31 approved articles, found $($articles.Count)." }
    $bySlug = @{}
    foreach ($item in $articles) {
        if ($bySlug.ContainsKey($item.Slug)) { throw "Duplicate article: $($item.Title)" }
        $bySlug[$item.Slug] = $item
    }
    foreach ($item in $articles) {
        $item.Text = [regex]::Replace(($item.Paragraphs -join '{newline}{newline}'), '\[([^\]]+)\]\(#([^)]+)\)', {
            param($match)
            $target = $bySlug[$match.Groups[2].Value]
            if (!$target) { throw "Unknown article link: $($match.Value)" }
            if ($match.Groups[1].Value -cne $target.Title) { throw "Link label differs from title: $($match.Value)" }
            return '{' + $target.Link + '}'
        })
        if ($item.Slug -ne 'bellum-civile') {
            $item.Text += '{newline}{newline}{' + $bySlug['bellum-civile'].Link + '}'
        }
    }
    $overview = $bySlug['bellum-civile']
    foreach ($group in ($articles | Where-Object { $_ -ne $overview } | Group-Object Section)) {
        $overview.Text += '{newline}{newline}' + $group.Name + ':{newline}' +
            (($group.Group | ForEach-Object { '{' + $_.Link + '}' }) -join '{newline}')
    }
    return $articles
}
