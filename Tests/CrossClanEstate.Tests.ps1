$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content (Join-Path $root $path) -Raw }
function Check($condition, $message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}
$estate = Read 'Behaviors/PartitionCrossClanInheritance.cs'
$partition = Read 'Behaviors/PartitionSuccessionBehavior.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$titles = Read 'Behaviors/FeudalTitleBehavior.cs'
Check ($estate -match 'Fiefs = SnapshotEstateFiefs\(package.Fiefs\)' -and $estate -match 'fiefs.Select\(f => f.Settlement.StringId\)') 'New death snapshots persist settlement IDs, not Town component IDs.'
Check ($estate -match 'RepairEstateFiefIds\(record\)' -and $estate -match 'ToDictionary\(s => s.Town.StringId, s => s.StringId') 'Pending component IDs are repaired from actual registered settlement objects.'
Check ($estate -match 'share.DeliveredFiefs = CanonicalizeEstateFiefIds' -and $estate -match 'if \(record.Completed\) record.Failure = null;') 'Repair preserves delivery receipts and skipped retries retain their failure deduplication.'
Check ((Read 'Patches/HereditaryDeathAccessionPatch.cs') -match 'CaptureCrossClanEstate\(victim\)') 'Death snapshot records external shares before household succession.'
Check ($estate -match 'household.Concat\(descendants\)' -and $estate -match 'SuccessionLawHelper.GetLawsForClan\(source\)') 'Estate order extends the donor household laws to descendants across clans.'
Check ($estate -notmatch 't.TitleType < FeudalTitleType.Kingdom' -and $estate -match 'GetMainHeirReservedFiefs\(\), primarySovereign\?\.TitleId') 'Planning keeps the full title hierarchy and original preferred sovereign anchor.'
Check ($estate -match 'RequiresSovereignEstateExecutor\(record, titles, t\)' -and $estate -match 'SupersedeEstateTitle\(share, record.RealmCrownTitleId\)') 'Crown execution is routed after receipt reconciliation; unsupported sovereign packages remain pending.'
Check ($estate -match 'share.LandedSettled = true' -and $estate -match 'NeedsLandedSettlement\(s, heir\)') 'Land completion releases Crown accession without falsely completing its Crown-title receipt.'
Check ($partition -match 'if \(HasCrossClanEstate\(victim\)\) return;') 'A captured cross-clan estate cannot also run the legacy partition.'
$laws = Read 'Behaviors/SuccessionLawBehavior.cs'
Check ($laws -match 'HasCrossClanEstate\(victim\)' -and $laws -match 'HasCrossClanEstate\(triggerHero\)') 'Neither a new nor a pending escheat can confiscate an already assigned cross-clan death estate.'
Check ($partition -match 'OnHourlyTick\(\)\s*\{\s*ProcessCrossClanEstates\(\);') 'Existing partition tick resumes saved estates even when new partitions are disabled.'
Check ((Read 'Behaviors/CrownIncomingHouse.cs') -match '!partition.HasCrossClanShare\(heir\)') 'A pending parent death estate does not trigger an unrelated living-house advance.'
Check ($crown.IndexOf('SettleCrownDeathEstate(heir)') -lt $crown.IndexOf('PrepareForeignCrownClan(record, house)')) 'Death estates settle into the Crown heir household before foreign movement.'
Check ($estate -match 'share.CadetPlan != null \|\|' -and $estate -match 'DeliveredFiefs.Add' -and $estate -match 'DeliveredTitles.Add') 'Partial cadets and title/land receipts are resumed rather than replanned.'
Check ($titles -match 'if \(title.DeJureHolderClanId != source.StringId\)\s*_deFactoOnlySettlementTransferTitleId' -and $estate -match 'preserveDeFacto:') 'Possession inheritance preserves third-party legal ownership and third-party possession.'
Check ($estate -match 'GoldPrepared' -and $estate -match 'GoldPayment == null' -and $estate -match 'DeliverAbdicationGold\(share.GoldPayment\)') 'Estate gold uses frozen amounts and the existing saved debit/credit delivery.'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($save -match 'typeof\(CrossClanEstateRecord\), 63' -and $save -match 'typeof\(CrossClanEstateShare\), 64' -and $save -match 'List<CrossClanEstateShare>') 'Estate journals and their containers are registered for saves.'
$ids = [regex]::Matches($save, 'Add(?:Class|Enum)Definition\(typeof\([^)]+\),\s*(\d+)\)') | ForEach-Object { $_.Groups[1].Value }
Check (($ids | Select-Object -Unique).Count -eq $ids.Count) 'Save type IDs do not collide.'
$xml = [xml](Read 'ModuleData/Languages/EN/strings.xml')
Check ($xml.SelectSingleNode("//string[@id='BC_CrossClanEstateInherited']")) 'Cross-clan estate narration is localized.'
Write-Output 'Source contracts only; death event ordering, estate callbacks and save/load need campaign validation.'
