$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$catalog = Get-Content (Join-Path $root 'ModuleData/Languages/EN/strings.xml') -Raw
$entries = @{}
foreach ($node in $catalog.SelectNodes('//string')) {
    $entries[$node.id] = @($entries[$node.id]) + @($node.text)
}
$checked = @{}
function Check-Text([string]$id, [string]$fallback) {
    $values = @($entries[$id] | Where-Object { $null -ne $_ })
    if ($values.Count -ne 1) { throw "Expected one catalog entry for $id; found $($values.Count)." }
    if ($values[0] -cne $fallback) { throw "Catalog/fallback mismatch for $id." }
    $checked[$id] = $true
}

# Include shared keys (title grants and endorsements), not only Relation-prefixed IDs.
foreach ($file in @('RelationMemoryService.cs', 'DynamicRelationBaselineHelper.cs', 'Behaviors/DynamicRelationBehavior.cs')) {
    $source = Get-Content (Join-Path $root $file) -Raw
    foreach ($match in [regex]::Matches($source, '"\{=(BC_[^}]+)\}([^"\r\n]*)"')) {
        Check-Text $match.Groups[1].Value $match.Groups[2].Value
    }
    foreach ($match in [regex]::Matches($source, 'new TextObject\("([^"\r\n]+)"')) {
        if (!$match.Groups[1].Value.StartsWith('{=')) { throw "Unkeyed display text in ${file}: $($match.Value)" }
    }
}

$service = Get-Content (Join-Path $root 'RelationMemoryService.cs') -Raw
$sources = [regex]::Matches($service, 'public const string (\w+) = "[^"]+";')
foreach ($source in $sources) {
    $name = $source.Groups[1].Value
    if ($service -notmatch ('case ' + [regex]::Escape($name) + ':')) {
        throw "Memory source $name lacks an explicit display mapping."
    }
}

# Court-event memory context uses the same translated label as the agenda.
$events = Get-Content (Join-Path $root 'CourtSessionEvents.cs') -Raw
if ($events -notmatch 'd\.AgendaText\.ToString\(\)' -or $events.Contains('new TextObject("{=" + d.Id + "}" + d.Label)')) {
    throw 'Court memory context no longer uses the registered activity label.'
}
$activities = Get-Content (Join-Path $root 'CourtActivityCatalog.cs') -Raw
$contexts = [regex]::Matches($activities,
    'new CourtActivityDefinition\("(BC_[^"]+)", FactionType\.\w+, (?:true|false), "([^"]+)", CourtActivityEffect\.(?:Peers|Notables)\b')
if ($contexts.Count -eq 0) { throw 'No relation-changing court activity contexts discovered.' }
foreach ($context in $contexts) {
    Check-Text ($context.Groups[1].Value + '_Agenda') $context.Groups[2].Value
}
Write-Output "PASS: $($sources.Count) memory-source mappings, $($checked.Count) unique localized strings, $($contexts.Count) court memory contexts."
