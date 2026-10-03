$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
$faction = Read 'FactionObject.cs'
$war = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$forced = Read 'Behaviors/CrownForcedAbdication.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$voluntary = Read 'Behaviors/CrownAbdication.cs'
$inheritance = Read 'FeudalInheritancePlanner.cs'
$partition = Read 'Behaviors/PartitionSuccessionBehavior.cs'
$law = Read 'SuccessionLawHelper.cs'
Check ($faction -match 'type == FactionType.Abdication && CrownAccessionBehavior.IsHereditaryRealm' -and $faction -match 'SaveableField\(26\)') 'Existing rebellion type captures hereditary target and realm-specific branch.'
Check ($faction -match 'BeginForcedAbdication\(' -and $faction -match 'TryBeginAcceptedAbdicationVote\(') 'Accepted demands retain separate hereditary and elective routes.'
Check (([regex]::Matches($war, 'BeginForcedAbdication\(')).Count -eq 2) 'Normal and collapsed-realm victories both enter the hereditary coordinator.'
Check ($forced -match 'SuccessionRealmRules.Classify\(laws.SuccessionLaw\) != RealmSuccessionSystem.Hereditary') 'Forced coordinator rejects elective law sets.'
Check ($faction -match 'AbdicationLaws' -and $war -match 'faction.AbdicationLaws, faction.AbdicationCauseId, abdicationHeir') 'Collapsed settlement carries the original law, cause and heir context.'
Check ($war -match 'CrownAccessionBehavior.Instance\?\.IsPending\(rewardKingdom\) == true') 'Tribunals wait for Crown settlement, not merely absence of a ballot.'
Check ($forced -match 'ResolveWhitePeace\(faction, rebel, demandSatisfied: true\)' -and $crown -match 'SettleSatisfiedAbdicationFactions\(\)') 'Completed lawful succession closes remaining target-specific rebellions as a satisfied demand.'
Check ($law -match 'HasInheritanceAdvance' -and $partition -match 'HasInheritanceAdvance' -and $inheritance -match 'HasInheritanceAdvance') 'Law ordering, estate planning and delayed partition consult inheritance advances.'
Check ($voluntary -match 'first.EndowmentGold == second.EndowmentGold' -and $voluntary -match 'Gold: \{GOLD\}') 'Voluntary preview displays and revalidates the gold share.'
Check ($forced.IndexOf('record.GoldCredited = true') -lt $forced.IndexOf('OnHeroOrPartyTradedGold')) 'Gold receipt precedes external trade callbacks.'
Check ($forced -notmatch 'ChangePlayerCharacterAction|ApplyHeirSelectionAction|QueueDeferredSuccessionVote') 'Forced inheritance does not replace player control or queue an ordinary election.'
$strings = [xml](Read 'ModuleData/Languages/EN/strings.xml')
Check ($strings.SelectSingleNode("//string[@id='BC_CrownForcedAccession']")) 'Forced accession narration is localized.'
Write-Output 'Structural contracts only; war resolution, regency and engine save/load still require campaign testing.'
