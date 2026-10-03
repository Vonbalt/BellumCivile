$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$collapse = Read 'Behaviors/CivilWarCollapseContinuation.cs'
$fiefs = Read 'Behaviors/FiefDeliberationBehavior.cs'
$fiefPatch = Read 'Patches/BlockVanillaFiefVotePatch.cs'
Check ($fiefPatch.IndexOf('IsTemporaryBellumKingdom(kingdomDecision.Kingdom)') -lt $fiefPatch.IndexOf('if (kingdomDecision is SettlementClaimantPreliminaryDecision)') -and
    $fiefPatch.IndexOf('IsTemporaryBellumKingdom(kingdomDecision.Kingdom)') -lt $fiefPatch.IndexOf('IdeologyBehavior.IsModAddingDecision')) 'Temporary fief decisions are blocked before preliminary/mod/immediate/reliability bypasses.'
Check ($collapse.Contains('wars.Count == 0 || wars.Keys.Any(f => f.Type != FactionType.InstallRuler)')) 'One install-ruler side can reserve a collapse; mixed demands remain excluded.'
Check ($collapse.Contains('wars.Count > 1 && nativePairs.Count == 0')) 'Only a sole claimant accepts an empty remaining-pair snapshot.'
Check ($collapse.Contains('if (record.NativePairs.Count > 0)') -and $collapse.Contains('if (record.Transfer != null &&')) 'Sole claimant skips the nonexistent rival transfer without blocking mantle stages.'
Check ($collapse.IndexOf('ExternalEnemies = GetExternalEnemiesToInherit') -lt $collapse.IndexOf('_collapses.Add(record)') -and
    $collapse.Contains('InheritExternalWars(record.Successor, record.ExternalEnemies)')) 'Foreign enemies are captured before destruction and read from the saved record at completion.'
Check ($collapse.IndexOf('Successor mantle has not been transferred') -lt $collapse.IndexOf('record.Stage = 3')) 'Even without a rival transfer, the mantle must commit before old-shell destruction.'
Check ($collapse.Contains('Reserved civil-war collapse;') -and $collapse.Contains('Civil-war collapse completed;')) 'Logs expose reservation, external enemies and completion for the next live test.'
Check ($fiefs.Contains('if (!CanDeliberateFiefs(kingdom) || settlement == null)') -and
    $fiefs.Contains('bool invalid = !CanDeliberateFiefs(kingdom)')) 'New temporary-realm queues are rejected and saved queues are discarded before reliability retries.'
Check ($fiefs.Contains('IsTemporaryBellumKingdom(newKingdom)') -and
    $fiefs.Contains('if (!CanDeliberateFiefs(kingdom) || kingdom != Clan.PlayerClan?.Kingdom)')) 'Temporary captures and missing-vote recovery share the same restriction.'
$cleanup = $fiefs.Substring($fiefs.IndexOf('private void CleanupOrphanedFiefDecisions'))
Check ($cleanup.IndexOf('if (!CanDeliberateFiefs(kingdom))') -lt $cleanup.IndexOf('HasPotentiallyRepairableFiefDecisions')) 'Loaded temporary live decisions are removed before repair can rebuild them.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
Check ($strings.SelectNodes('//string[@id="BC_CivilWar_CollapseSoleClaimant"]').Count -eq 1) 'Single-claimant notification has one English entry.'
'Source/XML contracts only; foreign war declaration callbacks and interrupted collapse require in-game testing.'
