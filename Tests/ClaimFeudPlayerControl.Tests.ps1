$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Behaviors/ClaimFeudBehavior.cs') -Raw
$start = $source.IndexOf('        private bool EvaluateClanForFeud(')
$end = $source.IndexOf('            _yearlyEvaluations++;', $start)
if ($start -lt 0 -or $end -le $start) { throw 'AI evaluation guard not found.' }
$guard = $source.Substring($start, $end - $start)
$harness = @'
using System;
public class Hero { public static Hero MainHero = new Hero(); }
public class Clan { public static Clan PlayerClan; public Hero Leader; }
public class FeudalTitleBehavior {}
public class PlayerControlTests {
 public static void Run() {
  var tests = new PlayerControlTests(); bool pressable;
  Clan.PlayerClan = new Clan { Leader = Hero.MainHero };
  Check(!tests.EvaluateClanForFeud(null, Clan.PlayerClan, out pressable) && !pressable, "Player clan reached AI");
  Clan.PlayerClan.Leader = new Hero();
  Check(!tests.EvaluateClanForFeud(null, Clan.PlayerClan, out pressable), "Player clan with another leader reached AI");
  Check(!tests.EvaluateClanForFeud(null, new Clan { Leader = Hero.MainHero }, out pressable), "Player-led clan reached AI");
  Check(!tests.EvaluateClanForFeud(null, null, out pressable), "Null clan reached AI");
  Check(tests.EvaluateClanForFeud(null, new Clan { Leader = new Hero() }, out pressable), "NPC clan incorrectly excluded");
 }
 private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
'@
Add-Type -TypeDefinition ($harness + $guard + 'return true; }}')
[PlayerControlTests]::Run()
$start = $source.IndexOf('        public bool TryRunAutonomousLandedAmbitionEvaluationForClan(')
$end = $source.IndexOf('        private bool EvaluateClanForClaimedRevocation(', $start)
$autonomous = $source.Substring($start, $end - $start)
if ($autonomous.IndexOf('clan == Clan.PlayerClan') -lt 0 -or
    $autonomous.IndexOf('clan == Clan.PlayerClan') -gt $autonomous.IndexOf('EvaluateClanForClaimedRevocation(titleBehavior')) {
    throw 'Autonomous entry does not exclude player before selecting actions.'
}
if ($source -notmatch 'record = StartFeud\(candidate, "player_action"\)') {
    throw 'Manual launch path changed.'
}
$start = $source.IndexOf('        private ClaimFeudRecord StartFeud(')
$end = $source.IndexOf('        private bool TryFindBestFeudCandidate(', $start)
if ($source.Substring($start, $end - $start) -match 'Clan.PlayerClan|Hero.MainHero') {
    throw 'Shared launch path must not reject player actions or NPC targets.'
}
Write-Host 'PASS: five AI eligibility cases, autonomous entry guard, and shared/manual launch wiring.'
