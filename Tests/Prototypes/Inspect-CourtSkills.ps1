param([string]$ModuleData)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../GamePath.ps1"
$ModuleData = Resolve-BellumModuleData $ModuleData
$data = & "$PSScriptRoot/Compare-CourtPersonality.ps1" -ModuleData $ModuleData | ConvertFrom-Json
$owners = ($data.reports | Where-Object candidate -eq 'C_standardized_strengths').owner_scores
function Read-Document($path) {
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $reader = [System.Xml.XmlReader]::Create($path, $settings)
    try { $doc=New-Object System.Xml.XmlDocument; $doc.Load($reader); return ,$doc }
    finally { $reader.Dispose() }
}
$lords = Read-Document (Join-Path $ModuleData 'lords.xml')
$templates = Read-Document (Join-Path $ModuleData 'sandbox_skill_sets.xml')
$characters=@{}; $sets=@{}
foreach ($node in $lords.SelectNodes('/NPCCharacters/NPCCharacter')) { $characters[$node.GetAttribute('id')]=$node }
foreach ($node in $templates.SelectNodes('/SkillSets/SkillSet')) { $sets[$node.GetAttribute('id')]=$node }
$rows=@()
foreach ($owner in $owners) {
    $node=$characters[$owner.hero]; $values=@{}
    $template=$node.GetAttribute('skill_template')
    if ($template) {
        if (-not $template.StartsWith('SkillSet.') -or -not $sets.ContainsKey($template.Substring(9))) { throw "Unresolved template: $template" }
        foreach ($skill in $sets[$template.Substring(9)].SelectNodes('skill')) { $values[$skill.GetAttribute('id')]=[int]$skill.GetAttribute('value') }
    }
    # BasicCharacterObject.Deserialize ignores inline skills when a template resolves.
    $explicit=@($node.SelectNodes('skills/skill | Skills/skill'))
    if (-not $template) {
        foreach ($skill in $explicit) { $values[$skill.GetAttribute('id')]=[int]$skill.GetAttribute('value') }
    }
    $rows += [pscustomobject]@{
        hero=$owner.hero; realm=$owner.realm; template=$template; explicit_count=$explicit.Count
        attribute_elements=$node.SelectNodes('attributes').Count
        skills=$values
    }
}
$stats=@()
foreach ($id in @('OneHanded','TwoHanded','Polearm','Bow','Crossbow','Throwing','Tactics','Leadership','Charm','Steward','Trade','Medicine','Engineering')) {
    $values=@($rows | ForEach-Object { [int]$_.skills[$id] } | Sort-Object)
    $stats += [pscustomobject]@{skill=$id; min=$values[0]; median=$values[[int][Math]::Floor($values.Count/2)]; max=$values[-1]; mean=[Math]::Round(($values | Measure-Object -Average).Average,1); positive=@($values | Where-Object {$_ -gt 0}).Count}
}
[pscustomobject]@{
    scope='SandBox XML template baselines, not runtime skills or attributes. Missing skill entries assumed zero; runtime initialization adds variation and later progression.'
    owners=$rows.Count; with_template=@($rows | Where-Object template).Count
    with_explicit_skills=@($rows | Where-Object {$_.explicit_count -gt 0}).Count
    mixed_sources=@($rows | Where-Object {$_.template -and $_.explicit_count -gt 0}).Count
    with_attribute_elements=@($rows | Where-Object {$_.attribute_elements -gt 0}).Count
    source_hashes=@($data.files) + @([pscustomobject]@{file='sandbox_skill_sets.xml'; sha256=(Get-FileHash -LiteralPath (Join-Path $ModuleData 'sandbox_skill_sets.xml')).Hash})
    templates=@($rows | Group-Object template | Select-Object Name,Count); skill_stats=$stats; owner_skills=$rows
} | ConvertTo-Json -Depth 8
