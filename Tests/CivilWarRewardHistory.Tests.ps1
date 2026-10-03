$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$record = Read 'CivilWarConflictRecord.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$recovery = Read 'Behaviors/CivilWarSuccessionSettlement.cs'
$coordinator = Read 'Behaviors/CivilWarConflictBehavior.cs'
Check ($record.Contains('[SaveableField(8)] public List<Clan> CrownDefeatedHouses')) 'Defeated houses persist with the shared civil-war record.'
Check ($resolution.IndexOf('RecordCrownDefeat(losingClans)') -lt $resolution.IndexOf('CompleteCivilWarTracker(faction, rebelKingdom, "loyalist victory")')) 'Defeat is journaled before peace and household-return callbacks.'
Check ($resolution.Contains('"loyalist victory", loyalistVictory: true') -and $recovery.Contains('history.CanReceiveLoyalistReward(clan)')) 'Normal loyalist rewards consult history without changing influence restoration.'
Check ($recovery.Contains('RecordCrownDefeat(record.OutcomeLosers)') -and $recovery.Contains('loyalistVictory: record.WarOutcome == SuccessionChallengeOutcome.Defeat')) 'Resumed hereditary settlements use the same defeat journal and reward filter.'
Check ($coordinator.Contains('GetRewardHistory(FactionObject faction)') -and $coordinator.Contains('_conflicts.FirstOrDefault(c => c.Sides.Any(s => s.Faction == faction))')) 'Exact faction history remains available after conflict closure.'
'Source contracts only; native save/load and live three-way reward delivery need campaign testing.'
