$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read-Source($path) { Get-Content (Join-Path $root $path) -Raw }
function Extract-Block($text, $startMarker, $endMarker) {
    $start = $text.IndexOf($startMarker)
    $end = $text.IndexOf($endMarker, $start + 1)
    if ($start -lt 0 -or $end -le $start) { throw "Missing source block: $startMarker" }
    $text.Substring($start, $end - $start)
}
$faction = Read-Source 'FactionObject.cs'
$normalize = Extract-Block $faction '        internal void NormalizeCourtFactionLegacyState()' '        public FactionObject('
$mark = Extract-Block $faction '        public void MarkAsGrandCoalition(' '        public TextObject GetDisplayName()'
$legacy = Extract-Block $faction '        private static bool IsLegacyGrandCoalitionName(' '        public Kingdom GetRebelKingdom()'
$power = Extract-Block (Read-Source 'RebellionPowerHelper.cs') '        public static float CalculateLoyalistContributionMultiplier(' '        public static float CalculateClanPower('
$sovereign = Extract-Block (Read-Source 'Patches/KingSelectionAIPatch.cs') '        private static Hero ResolveLivingRightfulSovereign(' '        private static Hero ResolveBestKingdomTitleClaimCandidate('
$harness = @'
using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem {
 public class Hero { public Clan Clan; public bool IsDead; }
 public class Kingdom { public Clan RulingClan; }
 public class Clan {
  public static List<Clan> All = new List<Clan>(); public Hero Leader;
  public string StringId; public bool IsEliminated, IsUnderMercenaryService, IsMinorFaction;
  public Kingdom Kingdom;
 }
 public class Campaign {
  public static Campaign Current = new Campaign();
  public static Dictionary<Type, object> Behaviors = new Dictionary<Type, object>();
  public T GetCampaignBehavior<T>() where T:class { object v; return Behaviors.TryGetValue(typeof(T), out v) ? (T)v : null; }
 }
}
namespace BellumCivile.Behaviors {
 public class FactionManagerBehavior {}
 public class DynasticClaimBehavior {
  public bool Active, Ally;
  public bool HasActiveClaim(Clan clan, Kingdom kingdom) { return Active; }
  public bool IsDynasticClaimAlly(Clan a, Clan b, Kingdom k) { return Ally; }
 }
 public class FeudalTitleBehavior {
  public FeudalTitleRecord Title = new FeudalTitleRecord();
  public Dictionary<Clan, List<FeudalClaimRecord>> Claims = new Dictionary<Clan, List<FeudalClaimRecord>>();
  public static string BuildKingdomTitleId(Kingdom k) { return "crown"; }
  public FeudalTitleRecord GetTitle(string id) { return Title; }
  public FeudalTitleRecord GetKingdomPoliticalTitle(Kingdom kingdom) { return Title; }
  public IEnumerable<FeudalClaimRecord> GetActiveClaims(Clan c, FeudalTitleRecord t) {
   List<FeudalClaimRecord> result; return Claims.TryGetValue(c, out result) ? result : new List<FeudalClaimRecord>();
  }
  public bool HasActiveClaim(Clan c, FeudalTitleRecord t, FeudalClaimStrength? s = null) {
   return GetActiveClaims(c,t).Any(x => !s.HasValue || x.Strength == s.Value);
  }
 }
}
namespace BellumCivile {
 public enum FactionType { Independence = 0, Abdication = 1, InstallRuler = 2, Royalists = 4, Glory = 5, Nobility = 6, Liberty = 7 }
 public enum FeudalClaimStrength { Weak, Strong }
 public class FeudalTitleRecord { public string DeJureHolderClanId; }
 public class FeudalClaimRecord { public string OriginClanId; public FeudalClaimStrength Strength; }
 public static class C {
  public const float ArmyMoodContent=60, MoodThresholdHappy=20, MoodThresholdUnhappy=-20, GrandCoalitionJoinMoodThreshold=-60;
 }
 public class FactionObject {
  private Clan _loyalClan;
  private bool _isGrandCoalition, _hasGrandCoalitionSourceIdeology;
  private FactionType _grandCoalitionSourceIdeology;
  public FactionType Type; public string Name; public float Mood;
  public bool IsGrandCoalition { get { return _isGrandCoalition || IsLegacyGrandCoalitionName(Name); } }
  public static void Run() {
   var f = new FactionObject { Type=FactionType.Royalists, _loyalClan=new Clan() };
   f.NormalizeCourtFactionLegacyState(); Check(f._loyalClan == null && f.Type == FactionType.Royalists, "Clear old allegiance only");
   f = new FactionObject { Type=FactionType.InstallRuler, Name="Saved restoration" };
   f.MarkAsGrandCoalition(FactionType.Royalists); f.NormalizeCourtFactionLegacyState();
   Check(f.Type == FactionType.Abdication && f.IsGrandCoalition && f.Name == "Traditionalists Grand Coalition", "Tagged restoration conversion");
   f.NormalizeCourtFactionLegacyState(); Check(f.Type == FactionType.Abdication, "Idempotent conversion");
   f = new FactionObject { Type=FactionType.InstallRuler, Name="Vlandia Grand Restoration" };
   f.NormalizeCourtFactionLegacyState(); Check(f.Type == FactionType.Abdication, "Legacy name conversion");
   f = new FactionObject { Type=FactionType.InstallRuler, Name="Meroc Claimants" };
   f.NormalizeCourtFactionLegacyState(); Check(f.Type == FactionType.InstallRuler && !f.IsGrandCoalition, "Ordinary claimants unchanged");
   f = new FactionObject { Type=FactionType.Abdication, Name="Existing coalition" };
   f.MarkAsGrandCoalition(FactionType.Glory); f.NormalizeCourtFactionLegacyState();
   Check(f.Type == FactionType.Abdication && f._grandCoalitionSourceIdeology == FactionType.Glory, "Other coalitions unchanged");
   var kingdom = new Kingdom { RulingClan=new Clan() }; var member=new Clan { Kingdom=kingdom };
   foreach (float mood in new float[] {-100,-60,-20,0,21,61,100}) {
    f.Type=FactionType.Royalists; f.Mood=mood;
    float actual=CalculateLoyalistContributionMultiplier(kingdom,member,f);
    foreach (FactionType type in new[] {FactionType.Nobility,FactionType.Glory,FactionType.Liberty}) {
     f.Type=type; Check(actual == CalculateLoyalistContributionMultiplier(kingdom,member,f), "Equal commitment for mood " + mood);
    }
   }
   f.Mood=-100; Check(CalculateLoyalistContributionMultiplier(kingdom,kingdom.RulingClan,f)==1, "Ruler remains loyalist");
   var titles=new Behaviors.FeudalTitleBehavior(); var dynasty=new Behaviors.DynasticClaimBehavior();
   Campaign.Behaviors[typeof(Behaviors.FeudalTitleBehavior)]=titles;
   Campaign.Behaviors[typeof(Behaviors.DynasticClaimBehavior)]=dynasty;
   var claimant=new Clan {StringId="claimant",Kingdom=kingdom}; var supporter=new Clan {StringId="supporter",Kingdom=kingdom};
   FeudalClaimStrength strength;
   Check(!RoyalistClaimHelper.TryGetThroneClaimStrength(claimant,kingdom,null,out strength), "No allegiance-only claim");
   titles.Claims[claimant]=new List<FeudalClaimRecord> {new FeudalClaimRecord {Strength=FeudalClaimStrength.Weak,OriginClanId="origin"}};
   Check(RoyalistClaimHelper.TryGetThroneClaimStrength(claimant,kingdom,null,out strength) && strength==FeudalClaimStrength.Weak, "Weak claim preserved");
   titles.Claims[claimant].Add(new FeudalClaimRecord {Strength=FeudalClaimStrength.Strong,OriginClanId="origin"});
   Check(RoyalistClaimHelper.TryGetThroneClaimStrength(claimant,kingdom,null,out strength) && strength==FeudalClaimStrength.Strong, "Strong claim preferred");
   Check(!RoyalistClaimHelper.IsRoyalistSupporterOfClaimant(supporter,claimant,kingdom,null), "No automatic faction support");
   titles.Claims[supporter]=new List<FeudalClaimRecord> {new FeudalClaimRecord {OriginClanId="origin"}};
   Check(RoyalistClaimHelper.IsRoyalistSupporterOfClaimant(supporter,claimant,kingdom,null), "Shared claim origin preserved");
   titles.Claims.Clear(); dynasty.Active=true; dynasty.Ally=true;
   Check(RoyalistClaimHelper.TryGetThroneClaimStrength(claimant,kingdom,null,out strength), "Dynastic claim preserved");
   Check(RoyalistClaimHelper.IsRoyalistSupporterOfClaimant(supporter,claimant,kingdom,null), "Dynastic allies preserved");
   claimant.Kingdom=new Kingdom(); Check(!RoyalistClaimHelper.HasRestorationClaim(claimant,kingdom,null), "Foreign claimant rejected");
   claimant.Kingdom=kingdom; claimant.Leader=new Hero {Clan=claimant}; Clan.All.Add(claimant);
   var heir=new Hero {Clan=claimant}; titles.Title.DeJureHolderClanId=claimant.StringId;
   Check(ResolveLivingRightfulSovereign(heir,null,kingdom)==claimant.Leader, "Legal holder replaces faction allegiance");
   Check(ResolveLivingRightfulSovereign(heir,claimant,kingdom)==null, "Excluded ruler not nominated");
   Check(ResolveLivingRightfulSovereign(claimant.Leader,null,kingdom)==null, "Heir not confused with reigning sovereign");
   titles.Title.DeJureHolderClanId=null;
   Check(ResolveLivingRightfulSovereign(heir,null,kingdom)==null, "Vacant title invents no rightful ruler");
  }
  private static bool IsEligibleElectionClan(Clan clan,Kingdom kingdom) { return clan.Kingdom==kingdom && !clan.IsEliminated; }
  private static bool IsElectableRulerHero(Hero hero) { return hero!=null && !hero.IsDead; }
  private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
'@
# Compile the full claim helper against game doubles with the same SDK as the mod.
$sdk = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdk\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumTraditionalistTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $harness + $normalize + $mark + $legacy + $power + $sovereign + '}}')
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" (Join-Path $root 'RoyalistClaimHelper.cs') $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [BellumCivile.FactionObject]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
$ideology = Read-Source 'Behaviors/IdeologyBehavior.cs'
$trigger = Extract-Block $ideology '        private bool TriggerGrandCoalition(' '        private HashSet<Clan> BuildGrandCoalitionMembers('
if (!$trigger.Contains('FactionType blueprint = FactionType.Abdication;') -or $trigger.Contains('InstallRuler')) { throw 'Wrong coalition demand' }
foreach ($path in @('Behaviors/IdeologyBehavior.cs','Behaviors/DynasticHeirBehavior.cs','Patches/KingSelectionAIPatch.cs','UI/FactionItemVM.cs','DynamicArmyManagementModel.cs','RebellionPowerHelper.cs','RoyalistClaimHelper.cs')) {
    if ((Read-Source $path) -match 'LoyalClan|RecognizeCurrentRulingDynasty|RoyalistClaimSwitch|IsCrownAlignedRoyalists') { throw "Live loyalty dependency: $path" }
}
$election = Read-Source 'Patches/KingSelectionAIPatch.cs'
if (!$election.Contains('heirBehavior.GetDynasticSuccessionCandidate(kingdom)') -or !$election.Contains('SuccessionLawHelper.GetOrderedSuccessionLine')) { throw 'Shared lawful succession wiring lost' }
if ($election.Contains('KingSelectRoyalistRestorationBonus') -or $election.Contains('KingSelectRoyalistAbdicationProposerPenalty') -or $election.Contains('affinity -= 0.35f')) { throw 'Restoration election bias remains' }
$panel = Read-Source 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
$null = [xml]$panel
if ($panel -match 'LoyaltyHint|HasLoyaltyText') { throw 'Loyalty panel remains' }
$strings = [xml](Read-Source 'ModuleData/Languages/EN/strings.xml')
if ($strings.SelectSingleNode('//string[@id="BC_FacName_Royalists"]').text -ne 'Traditionalists') { throw 'Localization mismatch' }
foreach ($preset in @('anglicized','immersive','bannerkings')) {
    $presetText = Read-Source "ModuleData/bellum_title_styles_$preset.xml"
    $null = [xml]$presetText
    # These preset entries are commented examples; the default display helper supplies the live name.
    if (!$presetText.Contains('royalists="Traditionalists"')) { throw "Preset example mismatch: $preset" }
}
Write-Host 'PASS: legacy conversion, equal mood commitment, real claim eligibility/support, coalition/election wiring, panel and localized presets.'
