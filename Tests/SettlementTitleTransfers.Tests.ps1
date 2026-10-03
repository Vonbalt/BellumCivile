$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs') -Raw
$start = $source.IndexOf('        private bool ShouldLegalTitleTransferToHolder(')
$end = $source.IndexOf('        private static bool IsClaimCurrentlyActive(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Transfer rules not found.' }
$rules = $source.Substring($start, $end - $start)
$harness = @'
using System;
using System.Collections.Generic;
public class Clan { public string StringId; public bool IsEliminated; public Hero Leader = new Hero(); }
public class Hero { public bool IsDead; }
public class FeudalTitleRecord { public string TitleId = "castle"; }
public class FeudalClaimRecord { public string CarrierHeroId = "leader"; }
public enum FeudalClaimStrength { Strong }
public static class ChangeOwnerOfSettlementAction {
 public enum ChangeOwnerOfSettlementDetail { BySiege, ByKingDecision, ByGift, ByBarter }
}
public static class BellumCivileLogger { public static void Log(string message) {} }
public class TransferTests {
 private string _deFactoOnlySettlementTransferTitleId;
 private bool claim;
 private string retained;
 private Dictionary<string, Clan> clans = new Dictionary<string, Clan>();
 private bool HasActiveClaim(Clan clan, FeudalTitleRecord title) { return claim; }
 private Clan ResolveClan(string id) { return clans.ContainsKey(id) ? clans[id] : null; }
 private FeudalClaimRecord RegisterClaim(Clan clan, FeudalTitleRecord title, FeudalClaimStrength strength,
 string source, Hero hero, Clan origin, Hero carrierHero, int generationDepth) {
  retained = clan.StringId; return new FeudalClaimRecord();
 }
 private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
 public static void Run() {
  var t = new TransferTests();
  var title = new FeudalTitleRecord();
  var owner = new Clan { StringId = "owner" };
  var enemy = new Clan { StringId = "enemy" };
  t.clans.Add(owner.StringId, owner); t.clans.Add(enemy.StringId, enemy);
  var siege = ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege;
  var vote = ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision;
  Check(!t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "owner", siege), "Siege stripped legal ownership");
  Check(!t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "occupier", vote), "Enemy allocation stripped disputed legal ownership");
  Check(t.ShouldLegalTitleTransferToHolder(title, owner, "owner", "enemy", vote), "Recapture did not restore full ownership");
  Check(t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "owner", vote), "Undisputed grant rejected");
  t.claim = true;
  Check(t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "occupier", vote), "Claimant acquisition rejected");
  Check(!t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "owner", siege, true), "Pending siege custody promoted a claim into ownership");
  Check(t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "owner", siege, false), "Final claimant acquisition was blocked");
  t.claim = false;
  Check(t.ShouldLegalTitleTransferToHolder(title, owner, "enemy", "enemy", vote), "Custodian-to-vote transfer no longer reproduces");
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "enemy", owner, vote);
  Check(t.retained == "enemy", "Custodian did not receive the reported displaced-owner claim");
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "owner", enemy, vote);
  Check(t.retained == "owner", "Dispossessed legal owner did not retain claim");
  t.retained = null;
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "owner", owner, vote);
  Check(t.retained == null, "Restored owner received self claim");
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "missing", enemy, vote);
  Check(t.retained == null, "Unknown owner received claim");
  owner.IsEliminated = true;
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "owner", enemy, vote);
  Check(t.retained == null, "Eliminated owner received claim");
  owner.IsEliminated = false; owner.Leader.IsDead = true;
  t.RegisterDisplacedLegalOwnerClaimIfNeeded(title, "owner", enemy, vote);
  Check(t.retained == null, "Dead carrier received claim");
  t._deFactoOnlySettlementTransferTitleId = "castle";
  Check(!t.ShouldLegalTitleTransferToHolder(title, enemy, "owner", "owner", vote), "Explicit possession-only transfer ignored");
 }
'@
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumTransferTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $harness + $rules + '}')
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [TransferTests]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
$start = $source.IndexOf('        private void OnSettlementOwnerChanged(')
$end = $source.IndexOf('        private bool TryApplySubinfeudationOwnershipChange(', $start)
$handler = $source.Substring($start, $end - $start)
foreach ($declaration in @('string previousDeJure =', 'string previousDeFacto =')) {
    $index = $handler.IndexOf($declaration)
    if ($index -lt 0 -or $index -gt $handler.IndexOf('FeudalTitleRecord title = EnsureBaronyTitle')) {
        throw "Ownership must be captured before synchronization: $declaration"
    }
}
if ($handler -notmatch 'oldOwner\?\.Clan\?\.StringId \?\? titleBeforeTransfer\?\.DeFactoHolderClanId') {
    throw 'Previous possession must prefer event history over potentially synchronized state.'
}
Write-Host 'PASS: transfer rules, pending siege custody prevention, genuine displaced-owner claims, and ownership wiring.'
