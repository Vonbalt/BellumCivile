$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$behavior = Get-Content -Raw (Join-Path $root 'Behaviors/HereditaryLoyaltyBehavior.cs')
$vm = Get-Content -Raw (Join-Path $root 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs')
$crown = Get-Content -Raw (Join-Path $root 'Behaviors/CrownAccessionBehavior.cs')
$court = Get-Content -Raw (Join-Path $root 'Behaviors/CourtAgendaBehavior.cs')
$intent = Get-Content -Raw (Join-Path $root 'RebellionIntentAssessment.cs')
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
Check ($behavior.Contains('store.SyncData("BC_HereditaryLoyalty", ref _memories)') -and
    $behavior.Contains('_cache.Clear(); _population = null; _populationDay = -1;')) 'Saved event memories and disposable derived caches are separate.'
Check ($behavior.Contains('if (memory?.Sovereign == sovereign) return;') -and
    $crown.Contains('if (!record.RegentReplacement) HereditaryLoyaltyBehavior.Instance?.Observe(record.Realm, false);')) 'Same sovereign and regent replacement cannot replay shock.'
Check ($behavior.Contains('IsPending(realm) != true') -and $behavior.Contains('memory.ShockSubjects = subjects;')) 'Observation waits for completed Crown transitions and captures shock recipients.'
Check ($behavior -notmatch 'DailyTickEvent|DailyTickHeroEvent|HourlyTickEvent' -and
    $court.Contains('HereditaryLoyaltyBehavior.Instance?.Maintain(realm)')) 'No independent loyalty scheduler.'
Check ($behavior.Contains('cache.LineInputs.SequenceEqual(input)') -and
    $behavior.Contains('cache.EstateSettings.SequenceEqual(settings)') -and
    $behavior.Contains('previous.Item1.SequenceEqual(input)')) 'Line, estate settings and score inputs each guard cached results.'
Check ($behavior.Contains('FeudalInheritancePlanner.BuildPlan(') -and
    $behavior.Contains('FeudalInheritancePlanner.GetLivingAccessionShare(') -and
    $behavior.Contains('HasInheritanceAdvance(memory.Sovereign, hero)')) 'Inheritance status uses existing packages and excludes recorded advances.'
$read = $behavior.Substring($behavior.IndexOf('public HereditaryLoyaltyAssessment Get('))
Check ($read -notmatch 'Observe\(|_memories.Add|ShockSubjects\s*=' -and
    $vm -notmatch 'HereditaryLoyaltyBehavior.Instance\?\.Observe') 'Assessment and UI reads cannot create accession memories.'
Check ($behavior -notmatch 'DeclareWarAction|ChangeKingdomAction|new FactionObject|MBRandom|ShowInquiry') 'Meter cannot start a challenge, move a house or roll a war.'
Check ($vm.Contains('AddLoyaltyRow(rows,') -and $vm.Contains('BC_Loyalty_Sovereign') -and
    $vm.Contains('BC_Loyalty_Disloyal')) 'Portrait tooltip explains components, lawful sovereign and disposition.'
Check ($behavior.Contains('CharacterRelationManager.GetHeroRelation(hero, memory.Sovereign)') -and
    $behavior.Contains('realm.RulingClan.Fiefs.Count, relation, concession, testTarget, submission };')) 'Visible relation, concession, submission and testing override participate in cache freshness.'
Check ($behavior.Contains('GetTotalControversy(realm)') -and $intent.Contains('GetTotalControversy(clan.Kingdom)') -and
    $vm.Contains('Ruler Controversy')) 'Ruler Controversy uses the same source as rebellious intent.'
$xml = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
$ids = [regex]::Matches($vm, '\{=(BC_Loyalty_[A-Za-z]+)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
Check ($behavior.Contains('if (store.IsLoading) _testLoyalty.Clear();') -and $behavior.Contains('_testLoyalty.Remove(realm);') -and
    $behavior -notmatch 'SyncData\([^\r\n]+ref _testLoyalty') 'Testing overrides are session-only and end on sovereign change; saving does not clear them.'
Check ($behavior.Contains('_cache.Remove(realm);') -and $vm.Contains('BC_Loyalty_Test')) 'Testing overrides invalidate cached assessments and explain their adjustment in the tooltip.'
foreach ($id in $ids) { Check ($xml.SelectNodes("//string[@id='$id']").Count -eq 1) "Localized loyalty label: $id" }
Write-Output 'Source contracts only; save/load, live regency and Gauntlet rendering require campaign verification.'
