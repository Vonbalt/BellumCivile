$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $name) { if (!$condition) { throw $name }; "PASS: $name" }
$service = Read 'Behaviors/RealmPeaceEnforcementBehavior.cs'
$feud = Read 'Behaviors/ClaimFeudBehavior.cs'
$war = Read 'Behaviors/ClaimFeudWarBehavior.cs'
Check ($service.Contains('!feuds.GetActiveFeuds().Contains(record)')) 'Preview rejects stale and inactive feud records.'
Check ($service.Contains('!BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)')) 'Foreign war excludes temporary realms.'
Check ($feud.Contains('RealmPeaceEnforcementBehavior.ResolveKingdomForFeud(record)')) 'Suppression uses the same parent realm as preview.'
Check ($feud.Contains('return wars.TryEnforceRoyalPeace(record, enforcingClan, out report)')) 'Armed suppression delegates before changing feud state.'
Check ($war.Contains('ClaimFeudWarOutcome.WhitePeace, "peace imposed by the Crown", peaceEnforcer: ruler')) 'Realm enforcement uses existing white-peace settlement in either pricing mode.'
Check ($war.Contains('peaceEnforcer != null ? ClaimFeudState.SuppressedCooldown : ClaimFeudState.Cooldown')) 'Only imposed settlements gain the suppressed cooldown.'
Check ($war.Contains('if (peaceEnforcer != null) parent = originalParent;')) 'Imposed peace returns houses to the authorized parent realm.'
Check ($war.Contains('peaceEnforcer.Influence = paidCrownInfluence') -and $war.IndexOf('peaceEnforcer.Influence = paidCrownInfluence') -gt $war.IndexOf('RestoreInfluenceSnapshot(influenceSnapshot)')) 'Snapshot restoration cannot reimburse enforcement.'
Check ($service.Contains('decree.NoticePublished') -and $service.Contains('"realm_royal_peace"') -and $war.Contains('realmPeaceExecuting ? null') -and $war.Contains('if (!realmPeaceExecuting)')) 'Realm enforcement suppresses individual war results and persists one aggregate result notice.'
Check ($war.Contains('RestoreFiefSnapshot(war.FiefSnapshot, null)') -and $war.Contains('ReturnWarClans(war, parent)') -and $war.Contains('CompleteClaimFeudWar') -and $war.Contains('DestroyTemporaryKingdom(holderKingdom, parent)')) 'Settlement retains fief restoration, return, score cleanup and shell disposal.'
Check ($service.Contains('GetRealmPreview(Kingdom kingdom, Clan actingClan)') -and $service.Contains('TryScheduleRoyalPeace(preview.Kingdom, actingClan, out report)')) 'Legacy feud enforcement schedules the same realm-wide decree.'
Check ($service.Contains('BC_RoyalPeaceUntilByRealm') -and $service.Contains('BellumCivile_TyrantsDebtUntilByClanId') -and $service.Contains('BC_RoyalPeaceDecrees')) 'Realm restriction, clan debt and paid journals persist separately.'
Check ($service.Contains('if (!preview.IsEnabled || !preview.SettlementReady) return false;') -and $service.IndexOf('if (!preview.IsEnabled || !preview.SettlementReady) return false;') -lt $service.IndexOf('NpcInfluenceBudgetService.TrySpend')) 'Safety and eligibility are revalidated before payment.'
Check ($service.IndexOf('decree.StartedFeudId = id;') -lt $service.IndexOf('if (!behavior.TrySuppressByRoyalPeace') -and $service.Contains('!d.Complete && !d.Interrupted')) 'Saved in-flight settlement receipts prevent automatic replay.'
Check ($service.IndexOf('decree.PenalizedClanIds.Add(clan.StringId)') -lt $service.IndexOf('RelationMemoryService.ApplyChange') -and $service.Contains('decree.PenalizedClanIds.Contains(clan.StringId)')) 'Relation penalties are deduplicated and receipted before callbacks.'
Check ($service.Contains('"realm_royal_peace_interrupted"') -and $service.Contains('!d.NoticePublished')) 'Interrupted settlements retain a publishable warning.'
Check ($service.Contains('decree.NoticePublished = notice.Ready') -and $service.IndexOf('decree.NoticePublished = notice.Ready') -gt $service.IndexOf('behavior.Publish(notice, decree.Interrupted')) 'Notice publication is latched only after the notice is ready.'
Check ($war.Contains('peace?.OwnsUnfinishedFeudSettlement(war.FeudRecordId) == true') -and $service.Contains('!d.Complete && d.StartedFeudId == feudId')) 'Orphan repair preserves in-flight realm settlements even when their wars are inactive.'
Check ($war.Contains('party.MapEvent != null || party.SiegeEvent != null') -and $war.Contains('estate.Key.SiegeEvent != null') -and $war.Contains('war.PendingCaptureWinnerClanId')) 'Armed peace protects battles, sieges, estates and pending captures.'
$revocation = Read 'FeudalTitlePlayerActionService.cs'
Check ($feud.Contains('GetRoyalPeaceBlockReason') -and $war.Contains('GetRoyalPeaceBlockReason') -and $revocation.Contains('GetRoyalPeaceBlockReason')) 'Feud, war and claimed-title revocation entry points share the realm restriction.'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($save.Contains('typeof(RoyalPeaceDecreeRecord), 135') -and $save.Contains('typeof(List<RoyalPeaceDecreeRecord>)')) 'Paid realm journal type and collection are registered for saves.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_RoyalPeace_ArmedUnavailable','BC_Result_RoyalPeaceTitle','BC_Result_RoyalPeaceChat','BC_Result_RoyalPeaceBody')) {
    Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Unique English localization: $id"
}
