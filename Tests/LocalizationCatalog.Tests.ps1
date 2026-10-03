$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalog = New-Object System.Xml.XmlDocument
$catalog.Load((Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
$entries = @{}
foreach ($node in $catalog.SelectNodes('//string')) {
    if ([string]::IsNullOrWhiteSpace($node.id)) { throw 'Catalog entry has no ID.' }
    if ($entries.ContainsKey($node.id)) { throw "Duplicate localization ID: $($node.id)" }
    if ($node.text.Contains('\n') -or $node.text.Contains('\r')) {
        throw "Literal escaped newline in catalog entry: $($node.id)"
    }
    $entries[$node.id] = $node.text
}

$checked = @{}
function Check-Reference([string]$id, [string]$source) {
    if (!$entries.ContainsKey($id)) { throw "Missing localization ID ${id}: $source" }
    $checked[$id] = $true
}
function Get-Tokens([string]$text) {
    @([regex]::Matches($text.Replace('{newline}', "`n"), '\{[^{}]+\}') |
        ForEach-Object { $_.Value } | Sort-Object -Unique)
}
function Check-Fallback([string]$id, [string]$fallback, [string]$source, [bool]$exact = $false) {
    Check-Reference $id $source
    if (((Get-Tokens $fallback) -join '|') -cne ((Get-Tokens $entries[$id]) -join '|')) {
        throw "Localization variables differ from fallback for ${id}: $source"
    }
    if ($exact -and $entries[$id] -cne $fallback) {
        throw "English localization differs from fallback for ${id}: $source"
    }
}
function Check-KeyedText([string]$text, [string]$source, [bool]$exact = $false) {
    if ($text -match '^\{=(BC_[A-Za-z0-9_]+)\}([\s\S]*)$') {
        Check-Fallback $Matches[1] $Matches[2] $source $exact
    }
}

# Scan production source and data, including the optional naval assembly.
$files = @(
    Get-ChildItem -LiteralPath $root -File
    foreach ($directory in Get-ChildItem -LiteralPath $root -Directory) {
        if ($directory.Name -notin @('Tests', 'docs', 'bin', 'obj') -and !$directory.Name.StartsWith('.')) {
            Get-ChildItem -LiteralPath $directory.FullName -Recurse -File |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        }
    }
) | Where-Object {
    $_.Extension -in @('.cs', '.xml') -and $_.FullName -notmatch '[\\/]ModuleData[\\/]Languages[\\/]'
}
foreach ($file in $files) {
    $source = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, '\{=(BC_[A-Za-z0-9_]+)\}')) {
        Check-Reference $match.Groups[1].Value $file.FullName
    }
    if ($file.Extension -eq '.xml') {
        [xml]$document = $source
        foreach ($attribute in $document.SelectNodes('//@*')) {
            Check-KeyedText $attribute.Value $file.FullName $true
        }
    } else {
        foreach ($match in [regex]::Matches($source, '"((?:\\.|[^"\\])*)"')) {
            if ($match.Groups[1].Value.StartsWith('{=BC_')) {
                Check-KeyedText ([regex]::Unescape($match.Groups[1].Value)) $file.FullName
            }
        }
        foreach ($match in [regex]::Matches($source, 'new TextObject\s*\(\s*"((?:\\.|[^"\\])*)"')) {
            $text = $match.Groups[1].Value
            if ($text -match '[A-Za-z]' -and !$text.StartsWith('{=')) {
                throw "Unkeyed display text in $($file.FullName): $text"
            }
        }
    }
}

# These families construct IDs at runtime, so literal-ID scanning cannot cover them.
$activities = [System.IO.File]::ReadAllText((Join-Path $root 'CourtActivityCatalog.cs'))
foreach ($match in [regex]::Matches($activities,
    'new CourtActivityDefinition\("(BC_[^"]+)", FactionType\.\w+, (?:true|false), "([^"]+)"')) {
    Check-Fallback ($match.Groups[1].Value + '_Agenda') $match.Groups[2].Value 'CourtActivityCatalog' $true
}
$reports = [System.IO.File]::ReadAllText((Join-Path $root 'CourtSessionEventReports.cs'))
foreach ($match in [regex]::Matches($reports, 'case "(BC_[^"]+)": text = "([^"]+)";')) {
    Check-Fallback ($match.Groups[1].Value + '_Report') $match.Groups[2].Value 'CourtSessionEventReports' $true
}
$moods = [System.IO.File]::ReadAllText((Join-Path $root 'CourtMoodPresentation.cs'))
foreach ($match in [regex]::Matches($moods, '\["(BC_[^"]+)"\] = \("([^"]*)", "([^"]*)"\)')) {
    Check-Fallback ('BC_MoodName_' + $match.Groups[1].Value) $match.Groups[2].Value 'CourtMoodPresentation' $true
    Check-Fallback ('BC_MoodHint_' + $match.Groups[1].Value) $match.Groups[3].Value 'CourtMoodPresentation' $true
}
$ideology = [System.IO.File]::ReadAllText((Join-Path $root 'Behaviors/IdeologyBehavior.cs'))
$start = $ideology.IndexOf('void Add(string id')
$end = $ideology.IndexOf('private static bool DoGreatHouses', $start)
if ($start -lt 0 -or $end -lt $start) { throw 'Court mood condition source was not found.' }
foreach ($match in [regex]::Matches($ideology.Substring($start, $end - $start), '"(BC_[A-Za-z0-9_]+)"')) {
    Check-Reference $match.Groups[1].Value 'Court mood condition'
    Check-Reference ($match.Groups[1].Value + '_Rule') 'Court mood condition rule'
}

$record = [System.IO.File]::ReadAllText((Join-Path $root 'CourtAgendaRecord.cs'))
$enum = [regex]::Match($record, 'enum CourtAgendaState\s*\{([^}]+)\}')
if (!$enum.Success) { throw 'CourtAgendaState enumeration was not found.' }
$states = @($enum.Groups[1].Value -split ',' | ForEach-Object { $_.Trim() })
$agenda = [System.IO.File]::ReadAllText((Join-Path $root 'Behaviors/CourtAgendaBehavior.cs'))
foreach ($state in $states) {
    $id = 'BC_CourtStatus_' + $state
    $match = [regex]::Match($agenda, 'case CourtAgendaState\.' + [regex]::Escape($state) +
        ': return new TextObject\("\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)"\);')
    if (!$match.Success) { throw "Court agenda state has no explicit localized label: $state" }
    Check-Fallback $id $match.Groups[1].Value 'Court agenda status' $true
}
foreach ($file in @('Behaviors/CourtAgendaBehavior.cs', 'Behaviors/CourtActivityAgendas.cs')) {
    $source = [System.IO.File]::ReadAllText((Join-Path $root $file))
    if ($source.Contains('"{=BC_CourtStatus_" +')) { throw "Dynamic enum label remains in $file" }
    if (!$source.Contains('StatusLabel(agenda.State)')) { throw "Shared localized status label is not used in $file" }
}

foreach ($file in @('BellumCivileSettings.cs', 'CourtInstitutionDisplayHelper.cs', 'DynamicMercenaryNameConfig.cs')) {
    $source = [System.IO.File]::ReadAllText((Join-Path $root $file))
    foreach ($match in [regex]::Matches($source, '"\{=(BC_(?:MCM_CourtTerm\w*|FacName_(?:Glory|Nobility|Liberty)|MercenaryName_\w+))\}([^"\r\n]*)"')) {
        Check-Fallback $match.Groups[1].Value $match.Groups[2].Value $file $true
    }
}
$parley = [System.IO.File]::ReadAllText((Join-Path $root 'UI/Parley/TreatyCouncilMemberVM.cs'))
if ($parley.Contains('{=BC_Parley_Enthusiasm}Enthusiasm"') -or !$parley.Contains('{=BC_Parley_EnthusiasmLabel}Enthusiasm')) {
    throw 'Parley tooltip label reuses the value-bearing enthusiasm string.'
}
$mercenaries = New-Object System.Xml.XmlDocument
$mercenaries.Load((Join-Path $root 'ModuleData/dynamic_mercenary_names.xml'))
foreach ($node in $mercenaries.SelectNodes('//Name')) {
    if (!$node.text.StartsWith('{=BC_MercenaryName_')) { throw "Unkeyed shipped mercenary name: $($node.text)" }
}

Write-Output "PASS: $($files.Count) production files, $($entries.Count) unique catalog entries, $($checked.Count) referenced IDs and $($states.Count) localized agenda states; variables, generated court IDs and XML fallbacks validated."
