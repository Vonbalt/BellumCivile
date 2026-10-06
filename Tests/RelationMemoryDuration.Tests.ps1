$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$behavior = Get-Content (Join-Path $root 'Behaviors/DynamicRelationBehavior.cs') -Raw
$constants = Get-Content (Join-Path $root 'BellumCivileConstants.cs') -Raw
$donation = Get-Content (Join-Path $root 'Patches/PrisonerDonationRelationMemoryPatch.cs') -Raw
if ($behavior -notmatch 'ApplyDirectMemory\(enemy, killer, 5, RelationMemorySources\.KilledEnemy, 10f,\s*RelationMemoryScope\.Personal') {
    throw 'Killed-enemy memory must remain personal and grant +5 for ten years.'
}
if ($constants -notmatch 'const float PrisonerDonationMemoryYears = 2f;' -or
    $constants -notmatch 'const int PrisonerDonationRelationCap = 20;') {
    throw 'Prisoner donation memories must last two years with the existing +20 cap.'
}
if ($donation -notmatch 'RelationMemoryService\.Begin\(RelationMemorySources\.DeliveredNoblePrisoners,\s*BellumCivileConstants\.PrisonerDonationMemoryYears, RelationMemoryScope\.House\)') {
    throw 'Prisoner donation must use the configured base duration and house scope.'
}
$start = $behavior.IndexOf('        private void RefreshMemoryDurationMultiplier()')
$end = $behavior.IndexOf('        private void EnsureCollectionsInitialized()', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Duration refresh method missing.' }
$refresh = $behavior.Substring($start, $end - $start)
$start = $behavior.IndexOf('        private void ClearVisibleRelationCache()')
$end = $behavior.IndexOf('        internal int LimitClientGrantGain(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Visible cache reset method missing.' }
$clear = $behavior.Substring($start, $end - $start)
$harness = @'
using System;
using System.Collections.Generic;
namespace TaleWorlds.SaveSystem {
 [AttributeUsage(AttributeTargets.Field)] public class SaveableFieldAttribute : Attribute { public SaveableFieldAttribute(int id) {} }
}
namespace BellumCivile {
 public static class BellumCivileOptions { public static float RelationMemoryDurationMultiplier = 1f; }
 public static class BellumCivileLogger { public static void Log(string value) {} }
 public class DurationTests {
  private bool _memoryDurationReady = true;
  private float _appliedMemoryDurationMultiplier = 1f;
  private int _memoryRevision;
  private float _nextMemoryPruneDay = 999f;
  private float CurrentDay = 25f;
  private List<RelationMemoryRecord> _relationMemories = new List<RelationMemoryRecord>();
  private Dictionary<int, int> _visibleRelationCache = new Dictionary<int, int>();
  private Queue<int> _visiblePruneQueue = new Queue<int>();
  private HashSet<int> _queuedVisibleKeys = new HashSet<int>();
  private static RelationMemoryRecord Make(float end = 100f, int value = 10, float decay = 0f) {
   return new RelationMemoryRecord(RelationMemoryScope.Personal, "a", "b", "test", "", value, 0f, end, decay);
  }
  private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
  private static void Near(float a, float b, string message) { Check(Math.Abs(a - b) < 0.001f, message); }
  public static void Run() {
   var memory = Make(); memory.RescaleRemainingDuration(25, 2);
   Near(memory.ExpiryDay, 175, "Double remaining duration");
   Check(memory.GetCurrentValue(25) == 10, "Value changed");
   memory.RescaleRemainingDuration(25, 0.5f); Near(memory.ExpiryDay, 100, "Reversal compounded");
   var negative = Make(100, -25); negative.RescaleRemainingDuration(25, 0.25f);
   Near(negative.ExpiryDay, 43.75f, "Negative memory not shortened");
   Check(negative.GetCurrentValue(43) == -25 && negative.IsExpired(43.75f), "Scaled expiry mismatch");
   var permanent = Make(-1); permanent.RescaleRemainingDuration(25, 5); Near(permanent.ExpiryDay, -1, "Permanent scaled");
   var expired = Make(20); expired.RescaleRemainingDuration(25, 5); Near(expired.ExpiryDay, 20, "Expired revived");
   var faded = Make(100, 1, 7); faded.RescaleRemainingDuration(25, 5); Near(faded.ExpiryDay, 100, "Faded revived");
   foreach (int sign in new[] { -1, 1 }) {
    var legacy = Make(70, 20 * sign, 2);
    int current = legacy.GetCurrentValue(14);
    legacy.RescaleRemainingDuration(14, 2);
    Check(legacy.GetCurrentValue(14) == current, "Legacy value jumped");
    Near(legacy.LegacyWeeklyDecay, 1, "Legacy rate not scaled");
    Near(legacy.ExpiryDay, 126, "Legacy expiry not scaled");
    Check(legacy.GetCurrentValue(28) == 14 * sign, "Legacy future fading incorrect");
   }
   Near(RelationMemoryRecord.NormalizeDurationMultiplier(0), 1, "Old save default");
   Near(RelationMemoryRecord.NormalizeDurationMultiplier(float.NaN), 1, "NaN setting");
   Near(RelationMemoryRecord.NormalizeDurationMultiplier(10), 5, "Upper clamp");
   Near(RelationMemoryRecord.NormalizeDurationMultiplier(0.1f), 0.25f, "Lower clamp");
   var test = new DurationTests(); test._relationMemories.Add(Make()); test._visibleRelationCache.Add(1, 1);
   test._visiblePruneQueue.Enqueue(1); test._queuedVisibleKeys.Add(1);
   BellumCivileOptions.RelationMemoryDurationMultiplier = 2;
   test.RefreshMemoryDurationMultiplier();
   Near(test._relationMemories[0].ExpiryDay, 175, "Behavior failed to scale");
   Check(test._memoryRevision == 1 && test._visibleRelationCache.Count == 0 && test._nextMemoryPruneDay == 0
    && test._visiblePruneQueue.Count == 0 && test._queuedVisibleKeys.Count == 0,
    "Expiry and visible caches not invalidated");
   test.RefreshMemoryDurationMultiplier();
   Near(test._relationMemories[0].ExpiryDay, 175, "Repeated read scaled twice");
   var loaded = new DurationTests(); loaded._appliedMemoryDurationMultiplier = test._appliedMemoryDurationMultiplier;
   loaded._relationMemories.Add(test._relationMemories[0]); loaded.RefreshMemoryDurationMultiplier();
   Near(loaded._relationMemories[0].ExpiryDay, 175, "Reload scaled twice");
   loaded.CurrentDay = 50; BellumCivileOptions.RelationMemoryDurationMultiplier = 1;
   loaded.RefreshMemoryDurationMultiplier(); Near(loaded._relationMemories[0].ExpiryDay, 112.5f, "Remaining ratio after time elapsed");
   var oldSave = new DurationTests(); oldSave._appliedMemoryDurationMultiplier = 0;
   oldSave._relationMemories.Add(Make()); BellumCivileOptions.RelationMemoryDurationMultiplier = 3.5f;
   oldSave.RefreshMemoryDurationMultiplier(); Near(oldSave._relationMemories[0].ExpiryDay, 287.5f, "Old save conversion");
   var fresh = Make(240); fresh.RescaleRemainingDuration(0, 3.5f); Near(fresh.ExpiryDay, 840, "Fast calendar compensation");
  }
'@
$sdk = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdk\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumMemoryTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $harness + $refresh + $clear + '}}')
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" (Join-Path $root 'RelationMemoryRecord.cs') (Join-Path $root 'RelationMemoryScope.cs') $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [BellumCivile.DurationTests]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
if ($behavior -notmatch 'SyncData\("BellumCivile_MemoryDurationMultiplier", ref _appliedMemoryDurationMultiplier\)') {
    throw 'Applied multiplier is not saved.'
}
if ($behavior -notmatch 'record.RescaleRemainingDuration\(CurrentDay, _appliedMemoryDurationMultiplier\)') {
    throw 'New memories bypass duration scaling.'
}
if ($behavior -notmatch 'remainingDays = Math.Max\(0f, memory.ExpiryDay - CurrentDay\)') {
    throw 'Tooltip no longer reads actual expiry.'
}
Write-Host 'PASS: killed-enemy/donation base durations, unchanged gains and scopes, memory scaling, legacy fading, cache refresh, reload idempotence, and tooltip/save wiring.'
