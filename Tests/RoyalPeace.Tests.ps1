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
Check ($war.Contains('ClaimFeudWarOutcome.WhitePeace, "peace imposed by the Crown during foreign war", peaceEnforcer: ruler')) 'Emergency enforcement uses existing white-peace settlement.'
Check ($war.Contains('peaceEnforcer != null ? ClaimFeudState.SuppressedCooldown : ClaimFeudState.Cooldown')) 'Only imposed settlements gain the suppressed cooldown.'
Check ($war.Contains('if (peaceEnforcer != null) parent = originalParent;')) 'Imposed peace returns houses to the authorized parent realm.'
Check ($war.Contains('peaceEnforcer.Influence = paidCrownInfluence') -and $war.IndexOf('peaceEnforcer.Influence = paidCrownInfluence') -gt $war.IndexOf('RestoreInfluenceSnapshot(influenceSnapshot)')) 'Snapshot restoration cannot reimburse enforcement.'
Check ($service.Contains('if (showNotification && !armed)') -and $war.Contains('resultKind = "feud_royal_peace"')) 'Armed enforcement uses a single distinct persisted result notice.'
Check ($war.Contains('RestoreFiefSnapshot(war.FiefSnapshot, null)') -and $war.Contains('ReturnWarClans(war, parent)') -and $war.Contains('CompleteClaimFeudWar') -and $war.Contains('DestroyTemporaryKingdom(holderKingdom, parent)')) 'Settlement retains fief restoration, return, score cleanup and shell disposal.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_RoyalPeace_ArmedUnavailable','BC_Result_RoyalPeaceTitle','BC_Result_RoyalPeaceChat','BC_Result_RoyalPeaceBody')) {
    Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Unique English localization: $id"
}
