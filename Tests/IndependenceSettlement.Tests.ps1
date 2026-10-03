$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Behaviors/CivilWarResolutionBehavior.cs') -Raw
$start = $source.IndexOf('        private static void ApplyIndependenceMemories(')
$end = $source.IndexOf('        private void ResolveRecognizedIndependence(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Independence memory helper missing.' }
$helper = $source.Substring($start, $end - $start)
$harness = @'
using System;
using System.Linq;
using System.Collections.Generic;
public class Hero { public bool IsAlive = true; }
public class Clan { public Hero Leader = new Hero(); public bool IsEliminated; public bool Minor; }
public static class NobleClanEligibilityHelper {
 public static bool IsLiveNobleClan(Clan clan) { return clan != null && !clan.IsEliminated && !clan.Minor; }
}
public enum RelationMemoryScope { House }
public static class RelationMemorySources { public const string WarOfIndependence = "war_of_independence"; }
public static class RelationMemoryService {
 public static List<Hero> Recipients = new List<Hero>();
 public static void ApplyChange(Hero first, Hero second, int value, bool notify, string source, float years, RelationMemoryScope scope) {
  if (value != -25 || years != 15 || source != RelationMemorySources.WarOfIndependence || scope != RelationMemoryScope.House)
   throw new Exception("Wrong memory effect");
  Recipients.Add(second);
 }
}
public class IndependenceTests {
 public static void Run() {
  Clan ruler = new Clan(), a = new Clan(), b = new Clan();
  ApplyIndependenceMemories(ruler, new[] { a, a, b, ruler, null, new Clan { IsEliminated = true }, new Clan { Minor = true }, new Clan { Leader = null } });
  Check(RelationMemoryService.Recipients.Count == 2, "Duplicate, self, or invalid house memory");
  Check(RelationMemoryService.Recipients.Contains(a.Leader) && RelationMemoryService.Recipients.Contains(b.Leader), "Departing house missing");
  RelationMemoryService.Recipients.Clear();
  ApplyIndependenceMemories(null, new[] { a });
  ApplyIndependenceMemories(new Clan { IsEliminated = true }, new[] { a });
  ApplyIndependenceMemories(new Clan { Leader = null }, new[] { a });
  ruler.Leader.IsAlive = false;
  ApplyIndependenceMemories(ruler, new[] { a });
  Check(RelationMemoryService.Recipients.Count == 0, "Invalid former ruler received memory");
 }
 private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
'@
Add-Type -TypeDefinition ($harness + $helper + '}')
[IndependenceTests]::Run()
$start = $source.IndexOf('                case FactionType.Independence:', $source.IndexOf('bool tribunalDeferredUntilSuccession'))
$end = $source.IndexOf('                case FactionType.Abdication:', $start)
$victory = $source.Substring($start, $end - $start)
if ($victory -match 'QueuePlayerTribunal|ApplyPostWarConsequences' -or !$victory.Contains('ApplyIndependenceMemories')) {
    throw 'Successful independence still judges the former ruler or lacks memory.'
}
if (!$source.Contains('ApplyIndependenceMemories(formerRulingHouse, independentClans)')) { throw 'Negotiated independence lacks memory.' }
if (!$source.Contains('QueuePlayerTribunal(faction.ParentKingdom, faction.ParentKingdom, losingClans')) { throw 'Loyalist victory tribunal changed.' }
$start = $source.IndexOf('        private void PurgeSuccessfulIndependenceJudgments()')
$end = $source.IndexOf('        private ', $start + 20)
$purge = $source.Substring($start, $end - $start)
if ($purge -match 'ExileCause.RebelIndependence' -or
    ([regex]::Matches($purge, '== ExileCause.LoyalistIndependence')).Count -ne 3) {
    throw 'Cleanup must target only successful-independence queues.'
}
foreach ($call in @('FinalizeTribunalGroup(groupId, applyShocks: false)', 'RemovePendingSuccessionTribunal(groupId)', 'RemovePendingPostWarExecution(executionId)')) {
    if (!$purge.Contains($call)) { throw "Missing cleanup: $call" }
}
[xml]$strings = Get-Content (Join-Path $root 'ModuleData/Languages/EN/strings.xml')
if (@($strings.base.strings.string | Where-Object { $_.id -eq 'BC_RelationMemory_WarOfIndependence' }).Count -ne 1) { throw 'Memory localization missing or duplicated.' }
Write-Host 'PASS: independence memories, victory/peace wiring, loyalist tribunal preservation, and old-queue cleanup checks.'
