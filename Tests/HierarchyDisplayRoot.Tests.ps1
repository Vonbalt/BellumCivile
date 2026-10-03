$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'UI/VanillaTabs/Kingdoms/Hierarchy/KingdomHierarchyTabVM.cs') -Raw
$start = $source.IndexOf('        private static FeudalTitleRecord ResolveDisplayRoot(')
$end = $source.IndexOf('        private bool HasDeJureHierarchy(Kingdom kingdom)', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Display root helper missing.' }
$helper = $source.Substring($start, $end - $start)
$harness = @'
using System;
public enum FeudalHierarchyMode { DeFacto, DeJure }
public class Kingdom { public bool IsEliminated; public bool Temporary; }
public class FeudalTitleRecord { public bool IsActive = true; }
public static class BellumKingdomVisibilityHelper {
 public static bool IsTemporaryBellumKingdom(Kingdom kingdom) { return kingdom.Temporary; }
}
public class FeudalTitleBehavior {
 public FeudalTitleRecord Possessed;
 public FeudalTitleRecord Lawful;
 public FeudalTitleRecord Political;
 public FeudalTitleRecord GetRealmSovereignTitle(Kingdom kingdom, FeudalHierarchyMode mode) {
  return mode == FeudalHierarchyMode.DeFacto ? Possessed : Lawful;
 }
 public FeudalTitleRecord GetKingdomPoliticalTitle(Kingdom kingdom) { return Political; }
}
public class DisplayRootTests {
 private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
 public static void Run() {
  var realm = new Kingdom(); var crown = new FeudalTitleRecord();
  var behavior = new FeudalTitleBehavior { Possessed = crown, Political = crown };
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == crown, "Disputed crown hidden");
  behavior.Lawful = new FeudalTitleRecord();
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == crown, "Lesser lawful title replaced realm crown");
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeFacto) == crown, "Possession view changed");
  behavior.Possessed = null;
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == crown, "Accession fallback missing");
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeFacto) == null, "Fallback invented possession");
  crown.IsActive = false;
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == null, "Inactive crown exposed");
  crown.IsActive = true; realm.Temporary = true;
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == null, "Temporary shell exposed");
  realm.Temporary = false; realm.IsEliminated = true;
  Check(ResolveDisplayRoot(behavior, realm, FeudalHierarchyMode.DeJure) == null, "Historical realm used live resolver");
  Check(ResolveDisplayRoot(null, new Kingdom(), FeudalHierarchyMode.DeJure) == null, "Null behavior");
  Check(ResolveDisplayRoot(behavior, null, FeudalHierarchyMode.DeJure) == null, "Null realm");
 }
'@
Add-Type -TypeDefinition ($harness + $helper + '}')
[DisplayRootTests]::Run()
foreach ($call in @('ResolveDisplayRoot(titleBehavior, kingdom, GetDisplayModeForKingdom(kingdom))',
    'ResolveDisplayRoot(titleBehavior, kingdom, displayMode)',
    'ResolveDisplayRoot(titleBehavior, kingdom, FeudalHierarchyMode.DeJure)')) {
    if (!$source.Contains($call)) { throw "Missing shared display root wiring: $call" }
}
Write-Host 'PASS: ten hierarchy root cases and list/tree/toggle wiring.'
