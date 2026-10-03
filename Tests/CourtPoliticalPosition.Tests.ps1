$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$constants = Get-Content (Join-Path $root 'BellumCivileConstants.cs') -Raw
$weights = [regex]::Matches($constants, 'public const float Ideology(?:PoliticalComponentCap|EstablishedRealmYears|LegacyRealmYears|FirstSubordinatePull|AdditionalSubordinatePull|MilitaryReferenceFloor|MilitaryMeanMultiplier) = [\d.]+f;')
if ($weights.Count -ne 7) { throw 'Expected seven centralized political-position constants.' }
$socialWeights = [regex]::Matches($constants, 'public const (?:float|int)\s+IdeologyFaction(?:MemberMarriageAllianceBonus|MemberMarriageAllianceCap|FriendshipCap|SocialGravityCap|ImmediateLiegePull|HighLiegePull|FriendshipThreshold|FriendshipScale) = [\d.]+f?;')
if ($socialWeights.Count -ne 8) { throw 'Expected eight social constants.' }
$harness = @'
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using BellumCivile;
using BellumCivile.Behaviors;
namespace TaleWorlds.CampaignSystem {
 public abstract class CampaignBehaviorBase { public abstract void RegisterEvents(); public abstract void SyncData(IDataStore store); }
 public interface IDataStore { void SyncData<T>(string key, ref T value); }
 public class CampaignGameStarter {}
 public class Event<T> { public T Listener; public void AddNonSerializedListener(object owner,T listener) { Listener=listener; } }
 public static class CampaignEvents {
  public static Event<Action<CampaignGameStarter>> OnNewGameCreatedEvent=new Event<Action<CampaignGameStarter>>(), OnSessionLaunchedEvent=new Event<Action<CampaignGameStarter>>();
  public static Event<Action<Clan,Kingdom,Kingdom,ChangeKingdomAction.ChangeKingdomActionDetail,bool>> OnClanChangedKingdomEvent=new Event<Action<Clan,Kingdom,Kingdom,ChangeKingdomAction.ChangeKingdomActionDetail,bool>>();
  public static Event<Action<Clan,bool>> OnClanCreatedEvent=new Event<Action<Clan,bool>>();
  public static Event<Action<Clan>> OnClanDestroyedEvent=new Event<Action<Clan>>();
  public static Event<Action> DailyTickEvent=new Event<Action>();
 }
 public struct CampaignTime {
  public static float Day=100000; public static int DaysInYear=84; public float Value;
  public static CampaignTime Now => new CampaignTime { Value=Day }; public double ToDays => Value;
  public float ElapsedDaysUntilNow => Day-Value;
 }
 public class Hero { public bool IsDead; public Dictionary<Hero,int> Relations=new Dictionary<Hero,int>(); public int GetRelation(Hero other) { return Relations.TryGetValue(other,out var r)?r:0; } }
 public class Clan {
  public static readonly List<Clan> All=new List<Clan>(); public static Clan PlayerClan;
  public string StringId; public Kingdom Kingdom; public Hero Leader=new Hero(); public bool IsEliminated,IsMinorFaction,IsUnderMercenaryService;
  public int Tier=4; public float Strength=600; public float Influence; public CampaignTime LastFactionChangeTime=CampaignTime.Now;
  public List<Town> Fiefs=new List<Town>();
 }
 public class Kingdom { public string StringId; public Clan RulingClan; public bool IsEliminated,Temporary; public Kingdom Parent; public List<Clan> Clans=new List<Clan>(); }
 public class Campaign {
  public static Campaign Current; public Models Models=new Models(); public Dictionary<Type,object> Behaviors=new Dictionary<Type,object>();
  public T GetCampaignBehavior<T>() where T:class { return Behaviors.TryGetValue(typeof(T),out var b)?(T)b:null; }
  public void Add<T>(T behavior) { Behaviors[typeof(T)]=behavior; }
 }
 public class Models { public MapDistanceModel MapDistanceModel=new MapDistanceModel(); }
 public class MapDistanceModel {
  public int Calls; public bool Throw; public Dictionary<Town,List<Settlement>> Neighbors=new Dictionary<Town,List<Settlement>>();
  public List<Settlement> GetNeighborsOfFortification(Town town,MobileParty.NavigationType nav) {
   Calls++; if(nav!=MobileParty.NavigationType.Default)throw new Exception("Expected land neighbors");
   if(Throw)throw new InvalidOperationException("custom map unsupported");
   return Neighbors.TryGetValue(town,out var list)?list:new List<Settlement>();
  }
 }
}
namespace TaleWorlds.CampaignSystem.Actions { public static class ChangeKingdomAction { public enum ChangeKingdomActionDetail { JoinKingdom, CreateKingdom } } }
namespace TaleWorlds.CampaignSystem.Party { public class MobileParty { public enum NavigationType { Default,Naval } } }
namespace TaleWorlds.CampaignSystem.Settlements {
 public class Settlement { public string StringId; public Clan OwnerClan; }
 public class Town { public Settlement Settlement; }
}
namespace BellumCivile {
 public class FactionObject { public bool IsIdeology=true; public Clan Leader; public List<Clan> Members=new List<Clan>(); }
 public static class MarriageAllianceHelper {
  public static HashSet<string> Pairs=new HashSet<string>();
  public static bool HasMarriageAlliance(Clan a,Clan b) { return Pairs.Contains(a.StringId+":"+b.StringId)||Pairs.Contains(b.StringId+":"+a.StringId); }
 }
 public enum FeudalTitleType { Barony,County,Duchy,Kingdom,Empire }
 public enum FeudalHierarchyMode { DeJure,DeFacto }
 public class FeudalTitleRecord { public string TitleId,DeJureHolderClanId,DeFactoHolderClanId,ParentTitleId,DeFactoParentTitleId; public bool IsActive=true; public FeudalTitleType TitleType; }
 public static class BellumKingdomVisibilityHelper { public static bool IsTemporaryBellumKingdom(Kingdom k) { return k.Temporary; } }
 public static class RebellionPowerHelper { public static int Calls; public static float GetClanMilitaryStrength(Clan c) { Calls++;return c.Strength; } }
 public static class BellumCivileLogger { public static int Warnings; public static void Log(string s) { Warnings++; } }
 public static class BellumCivileDebug { public static List<string> Reports=new List<string>(); public static void TraceYearlyReport(string c,string s) { Reports.Add(s); } }
}
namespace BellumCivile.Behaviors {
 public class SuccessionLawBehavior { public Kingdom ResolvePermanentRealm(Kingdom k) { return k.Parent??k; } }
 public class PrivyCouncilBehavior {
  public bool Known=true; public HashSet<string> Holders=new HashSet<string>();
  public bool TryGetExistingOfficeHolders(Kingdom k,HashSet<string> holders) { holders.UnionWith(Holders);return Known; }
 }
 public class FeudalTitleBehavior {
  public int ChildrenReads; public Dictionary<string,FeudalTitleRecord> Titles=new Dictionary<string,FeudalTitleRecord>();
  public IEnumerable<FeudalTitleRecord> GetTitlesHeldByClan(Clan clan,bool deJure) { return Titles.Values.Where(t=>t.IsActive && (deJure?t.DeJureHolderClanId:t.DeFactoHolderClanId)==clan.StringId); }
  public bool TryGetBarony(Settlement s,out FeudalTitleRecord t) { return Titles.TryGetValue(s.StringId,out t); }
  public FeudalTitleRecord GetParentTitle(FeudalTitleRecord t,FeudalHierarchyMode mode) { string id=mode==FeudalHierarchyMode.DeFacto?t.DeFactoParentTitleId:t.ParentTitleId;return id!=null && Titles.TryGetValue(id,out var p)?p:null; }
  public IEnumerable<FeudalTitleRecord> GetChildTitles(FeudalTitleRecord t,FeudalHierarchyMode mode) { ChildrenReads++;return Titles.Values.Where(c=>c.IsActive && c.ParentTitleId==t.TitleId); }
 }
}
public class MemoryStore:IDataStore {
 public bool Loading; public Dictionary<string,object> Data=new Dictionary<string,object>();
 public void SyncData<T>(string key,ref T value) {
  if(Loading) { value=(T)Data[key];return; }
  object o=value;
  Data[key]=o is Dictionary<string,string> strings?(object)new Dictionary<string,string>(strings):new Dictionary<string,float>((Dictionary<string,float>)o);
 }
}
public static class CourtPoliticalTests {
 static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
 static void Equal(float a,float b,string message) { Check(Math.Abs(a-b)<0.001f,message+": "+a+" != "+b); }
 static CourtPoliticalPositionBehavior History;
 static FeudalTitleBehavior Titles;
 static Clan AddClan(string id,Kingdom realm) { var c=new Clan {StringId=id,Kingdom=realm};Clan.All.Add(c);realm?.Clans.Add(c);return c; }
 static void Reset() {
  MarriageAllianceHelper.Pairs.Clear();Clan.All.Clear();Clan.PlayerClan=null;Campaign.Current=new Campaign();CampaignTime.Day=100000;CampaignTime.DaysInYear=84;
  History=new CourtPoliticalPositionBehavior();History.RegisterEvents();Campaign.Current.Add(History);
  Titles=new FeudalTitleBehavior();Campaign.Current.Add(Titles);Campaign.Current.Add(new SuccessionLawBehavior());Campaign.Current.Add(new PrivyCouncilBehavior());
  RebellionPowerHelper.Calls=0;BellumCivileLogger.Warnings=0;BellumCivileDebug.Reports.Clear();
 }
 static void Start(bool fresh=true) { if(fresh)CampaignEvents.OnNewGameCreatedEvent.Listener(null);CampaignEvents.OnSessionLaunchedEvent.Listener(null); }
 static void Move(Clan c,Kingdom next,ChangeKingdomAction.ChangeKingdomActionDetail detail=ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom) {
  Kingdom old=c.Kingdom;old?.Clans.Remove(c);c.Kingdom=next;next?.Clans.Add(c);c.LastFactionChangeTime=CampaignTime.Now;
  CampaignEvents.OnClanChangedKingdomEvent.Listener(c,old,next,detail,false);
 }
 static FeudalTitleRecord Title(string id,Clan legal,Clan actual,FeudalTitleType type,string parent=null) {
  var t=new FeudalTitleRecord {TitleId=id,DeJureHolderClanId=legal?.StringId,DeFactoHolderClanId=actual?.StringId,TitleType=type,ParentTitleId=parent,DeFactoParentTitleId=parent};Titles.Titles[id]=t;return t;
 }
 static Town Fief(string id,Clan legal,Clan actual,string parent) {
  Title(id,legal,actual,FeudalTitleType.Barony,parent);var f=new Town {Settlement=new Settlement {StringId=id,OwnerClan=actual}};actual.Fiefs.Add(f);return f;
 }
 static void Formula() {
  var full=CourtPoliticalPositionScore.Calculate(0,0,40,100,100,100,10000,100,0,6,false,true,false);
  Equal(full.RealmStanding,12,"standing cap");Equal(full.SubordinateHouses,12,"vassal cap");Equal(full.LegalTerritory,12,"legal cap");Equal(full.MilitaryStrength,12,"military cap");Equal(full.LandShortage,12,"landless shortage");
  var ordinary=CourtPoliticalPositionScore.Calculate(2,2,10,1,5,0,600,600,1,4,false,true,false);
  Equal(ordinary.Traditionalists,18,"lawful + ten years");Equal(ordinary.SubordinateHouses,6,"first subordinate");Equal(ordinary.MilitaryStrength,6,"mean force");Equal(ordinary.FrontierExposure,6,"half frontier");Equal(ordinary.LandShortage,6,"structural tier shortage");Equal(ordinary.PoliticalExclusion,3,"diminished exclusion");
  var landless=CourtPoliticalPositionScore.Calculate(0,0,0,0,0,0,10,0,0,0,false,true,false);
  Equal(landless.LawfulTenure,0,"landless not secure");Equal(landless.PoliticalExclusion,12,"landless excluded");Check(landless.MilitaryStrength<1,"weak armies cannot max relative force");
  for(int i=0;i<10000;i++) {
   var r=new Random(i);var s=CourtPoliticalPositionScore.Calculate(r.Next(10),r.Next(10),r.Next(40),r.Next(10),r.Next(20),r.Next(20),r.Next(5000),r.Next(5000),r.Next(10),r.Next(7),false,true,false);
   foreach(float v in new[]{s.Traditionalists,s.Aristocrats,s.Militarists,s.Populists})Check(v>=0&&v<=24,"political score out of range");
  }
  Equal(CourtPoliticalPositionScore.Calculate(0,0,0,0,0,0,0,0,0,4,true,true,false).PoliticalExclusion,0,"ruler not excluded");
  Equal(CourtPoliticalPositionScore.Calculate(0,0,0,0,0,0,0,0,0,4,false,true,true).PoliticalExclusion,0,"councillor not excluded");
  Equal(CourtPoliticalPositionScore.Calculate(0,0,0,0,0,0,0,0,0,4,false,false,false).PoliticalExclusion,0,"unknown council not exclusion");
 }
 static void Tenure() {
  Reset();var realm=new Kingdom {StringId="home"};var other=new Kingdom {StringId="foreign"};var c=AddClan("old",realm);Start();
  Equal(History.GetStandingYears(c,realm),20,"original house established");
  var shell=new Kingdom {StringId="rebels",Temporary=true,Parent=realm};Move(c,shell);CampaignTime.Day+=84;Move(c,realm);Equal(History.GetStandingYears(c,realm),21,"temporary war retained tenure");
  var unknown=new Kingdom {StringId="orphan",Temporary=true};Move(c,unknown);Move(c,realm);Equal(History.GetStandingYears(c,realm),21,"orphan return retained known tenure");
  var successor=new Kingdom {StringId="independent"};
  CourtPoliticalPositionBehavior.MoveToSuccessorRealm(c,realm,successor,()=>Move(c,successor));Equal(History.GetStandingYears(c,successor),21,"independence retained tenure");
  CourtPoliticalPositionBehavior.MoveToSuccessorRealm(c,successor,realm,()=>{});Equal(History.GetStandingYears(c,successor),21,"failed move retained old tenure");
  try {CourtPoliticalPositionBehavior.MoveToSuccessorRealm(c,successor,realm,()=>{throw new Exception("move failed");});}catch(Exception){}
  Equal(History.GetStandingYears(c,successor),21,"exception retained old tenure");
  Move(c,other);Equal(History.GetStandingYears(c,other),0,"real defection reset");CampaignTime.Day+=420;Equal(History.GetStandingYears(c,other),5,"lazy growth");
  var store=new MemoryStore();History.SyncData(store);History=new CourtPoliticalPositionBehavior();store.Loading=true;History.SyncData(store);Campaign.Current.Add(History);History.RegisterEvents();Start(false);Equal(History.GetStandingYears(c,other),5,"saved standing");
  CampaignEvents.OnClanDestroyedEvent.Listener(c);Equal(History.GetStandingYears(c,other),0,"destroyed record cleared");
  var fresh=AddClan("new",other);CampaignEvents.OnClanCreatedEvent.Listener(fresh,false);Equal(History.GetStandingYears(fresh,other),0,"new house starts zero");
  Reset();realm=new Kingdom {StringId="legacy"};c=AddClan("legacy",realm);c.LastFactionChangeTime=new CampaignTime {Value=CampaignTime.Day-30*84};Start(false);Equal(History.GetStandingYears(c,realm),10,"legacy credit capped at half");
  CampaignTime.DaysInYear=24;Reset();CampaignTime.DaysInYear=24;realm=new Kingdom {StringId="fast"};c=AddClan("fast",realm);Start();CampaignTime.Day+=24;Equal(History.GetStandingYears(c,realm),21,"fast calendar years");
 }
 static void Context() {
  Reset();var realm=new Kingdom {StringId="a"};var foreign=new Kingdom {StringId="b"};var ruler=AddClan("r",realm);realm.RulingClan=ruler;
  var count=AddClan("c",realm);var baron=AddClan("b",realm);var landless=AddClan("l",realm);var foreignClan=AddClan("f",foreign);
  Title("kingdom",ruler,ruler,FeudalTitleType.Kingdom);Title("county",count,count,FeudalTitleType.County,"kingdom");
  var a=Fief("a",ruler,ruler,"kingdom");var b=Fief("b",count,count,"county");var c=Fief("c",baron,baron,"county");var d=Fief("d",count,foreignClan,"county");
  // The foreign-held barony is no longer inside the realm's effective tree.
  Titles.Titles["d"].DeFactoParentTitleId=null;
  // Two held layers must not double-count the same subordinate house or territory.
  Title("extra",baron,baron,FeudalTitleType.County,"county");
  Campaign.Current.Models.MapDistanceModel.Neighbors[b]=new List<Settlement>{d.Settlement};Start();
  using(var context=new CourtPoliticalPositionContext(realm)) {
   var s=context.GetScore(count);Equal(s.SubordinateHouses,6,"one subordinate despite duplicate title layers");Equal(s.LegalTerritory,4,"one of three legal baronies outside authority");Equal(s.FrontierExposure,12,"foreign frontier");Equal(s.LawfulTenure,12,"lawful physical estate");
   var r=context.GetScore(ruler);Equal(r.SubordinateHouses,9,"two distinct subordinate houses");Equal(r.LegalTerritory,3,"one lost barony in four");Equal(r.PoliticalExclusion,0,"ruler exclusion");
   int reads=Titles.ChildrenReads;context.GetScore(count);Check(Titles.ChildrenReads==reads,"legal footprint memoization");
   Check(RebellionPowerHelper.Calls==4,"military input read once per eligible house");
  }
  count.Influence=100000;var shell=new Kingdom {StringId="civil",Temporary=true,Parent=realm};foreignClan.Kingdom=shell;
  using(var context=new CourtPoliticalPositionContext(realm)) { var s=context.GetScore(count);Equal(s.FrontierExposure,0,"temporary domestic shell not foreign frontier");Equal(s.MilitaryStrength,6,"influence excluded"); }
  var council=Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();council.Holders.Add(landless.StringId);
  using(var context=new CourtPoliticalPositionContext(realm))Equal(context.GetScore(landless).PoliticalExclusion,0,"office read");
  council.Holders.Clear();council.Known=false;
  using(var context=new CourtPoliticalPositionContext(realm))Equal(context.GetScore(landless).PoliticalExclusion,0,"missing office records neutral");
  Campaign.Current.Models.MapDistanceModel.Throw=true;
  using(var context=new CourtPoliticalPositionContext(realm)) {context.GetScore(count);context.GetScore(ruler);}
  Check(BellumCivileLogger.Warnings==1,"bounded neighbor diagnostic");
  Titles.Titles["county"].ParentTitleId="extra";
  using(var context=new CourtPoliticalPositionContext(realm))Check(!float.IsNaN(context.GetScore(count).Aristocrats),"cyclic legal input bounded");
  History.RecordSwitch();CampaignTime.Day+=84;CampaignEvents.DailyTickEvent.Listener();
  Check(BellumCivileDebug.Reports.Count==1 && BellumCivileDebug.Reports[0].Contains("switches=1") && BellumCivileDebug.Reports[0].Contains("mean_components:"),"aggregate telemetry");
 }
 static void Social() {
  Reset();var realm=new Kingdom {StringId="social"};var c=AddClan("subject",realm);var ruler=AddClan("ruler",realm);var friend=AddClan("friend",realm);var third=AddClan("third",realm);realm.RulingClan=ruler;Start();
  var bloc=new FactionObject {Leader=ruler,Members=new List<Clan>{ruler,friend,third,c}};
  Title("crown",ruler,ruler,FeudalTitleType.Kingdom);Fief("estate",c,c,"crown");
  c.Leader.Relations[ruler.Leader]=60;c.Leader.Relations[friend.Leader]=100;c.Leader.Relations[c.Leader]=100;
  MarriageAllianceHelper.Pairs.Add("subject:ruler");MarriageAllianceHelper.Pairs.Add("subject:friend");MarriageAllianceHelper.Pairs.Add("subject:third");
  var ties=new CourtSocialTieContext(realm,Titles);
  var s=ties.GetScore(c,bloc);Equal(s.Friendship,8,"ordinary friend counts");Equal(s.Marriage,8,"marriage network cap");Equal(s.Hierarchy,8,"ruler liege included");Equal(s.Total,24,"social cap");
  int reads=ties.RelationReads;bloc.Leader=third;bloc.Members.Add(friend);s=ties.GetScore(c,bloc);Equal(s.Total,24,"leadership and duplicate-member invariance");Check(ties.RelationReads==reads && reads==3,"cached pair reads and self exclusion");
  bloc.Members=new List<Clan>{c};Equal(ties.GetScore(c,bloc).Total,0,"own house never attracts itself");
  bloc.Members=new List<Clan>{ruler};Equal(ties.GetScore(c,bloc).Marriage,4,"one allied house including ruler");
  ruler.Leader.Relations[friend.Leader]=100;bloc.Members=new List<Clan>{friend};Equal(ties.GetScore(ruler,bloc).Friendship,8,"ruler evaluates other houses normally");
  Equal(CourtSocialTieContext.FriendshipPull(-100),0,"no negative attraction");Equal(CourtSocialTieContext.FriendshipPull(20),0,"friendship start");
  Equal(CourtSocialTieContext.FriendshipPull(21),0.1f,"smooth threshold");Equal(CourtSocialTieContext.FriendshipPull(60),4,"moderate friendship");Equal(CourtSocialTieContext.FriendshipPull(61),4.1f,"no old cliff");
  Title("legal",friend,friend,FeudalTitleType.County);Titles.Titles["estate"].ParentTitleId="legal";
  ties=new CourtSocialTieContext(realm,Titles);
  bloc.Members=new List<Clan>{ruler};Equal(ties.GetScore(c,bloc).Hierarchy,4,"separate actual chain");
  bloc.Members=new List<Clan>{friend};Equal(ties.GetScore(c,bloc).Hierarchy,4,"separate legal chain");
  bloc.Members=new List<Clan>{friend,ruler};Equal(ties.GetScore(c,bloc).Hierarchy,8,"both lieges in bloc");
  Titles.Titles["legal"].ParentTitleId="crown";Titles.Titles["legal"].DeFactoParentTitleId="crown";Titles.Titles["estate"].DeFactoParentTitleId="legal";
  ties=new CourtSocialTieContext(realm,Titles);bloc.Members=new List<Clan>{ruler};Equal(ties.GetScore(c,bloc).Hierarchy,4,"higher liege");
  Titles.Titles["crown"].ParentTitleId="legal";Titles.Titles["crown"].DeFactoParentTitleId="legal";
  ties=new CourtSocialTieContext(realm,Titles);Check(ties.GetScore(c,bloc).Total<=24,"cyclic hierarchy bounded");
  ruler.IsEliminated=true;Equal(ties.GetScore(c,bloc).Total,0,"invalid house ignored");ruler.IsEliminated=false;
  using(var context=new CourtPoliticalPositionContext(realm)) { context.GetSocialScore(c,bloc);context.GetSocialScore(c,bloc); }
  CampaignTime.Day+=84;CampaignEvents.DailyTickEvent.Listener();Check(BellumCivileDebug.Reports.Any(r=>r.Contains("bloc_evaluations=2")&&r.Contains("relation_reads=1")),"social telemetry reuses reads");
 }
 public static void Run() { Formula();Tenure();Context();Social(); }
}
'@
$harness += "`nnamespace BellumCivile { public static class BellumCivileConstants {" + (($weights.Value + $socialWeights.Value) -join "`n") + '} }'
$sdk = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdk\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumCourtPositionTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temp) | Out-Null
$inputFile = Join-Path $temp 'Harness.cs'
$dll = Join-Path $temp 'Tests.dll'
try {
    [System.IO.File]::WriteAllText($inputFile, $harness)
    & dotnet $compiler /nologo /target:library /langversion:7.3 "/out:$dll" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" (Join-Path $root 'CourtSocialTieContext.cs') (Join-Path $root 'CourtPoliticalPositionScore.cs') (Join-Path $root 'CourtPoliticalPositionContext.cs') (Join-Path $root 'Behaviors/CourtPoliticalPositionBehavior.cs') $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Political position harness compilation failed.' }
    [Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($dll)) | Out-Null
    [CourtPoliticalTests]::Run()
} finally {
    if (Test-Path -LiteralPath $inputFile) { Remove-Item -LiteralPath $inputFile }
    if (Test-Path -LiteralPath $dll) { Remove-Item -LiteralPath $dll }
    Remove-Item -LiteralPath $temp
}
$ideology = Get-Content (Join-Path $root 'Behaviors/IdeologyBehavior.cs') -Raw
if (!$ideology.Contains('OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched)')) { throw 'Initial scores must wait for title initialization.' }
if (!$ideology.Contains('politicalContext = politicalContext ?? new CourtPoliticalPositionContext(kingdom)')) { throw 'Initial context must be lazy.' }
if (([regex]::Matches($ideology, 'CalculateRawIdeologyScores\(clan, factionManager, politicalContext\)')).Count -ne 2) { throw 'Both assignment paths must share position inputs.' }
if ($ideology.Contains('IdeologyRoyalistRulerBonus') -or $ideology.Contains('CalculateIdeologyAristocraticTitlePull')) { throw 'Legacy political bonuses remain.' }
Write-Host 'PASS: 10,000 cap cases, component formulas, indexed hierarchy deduplication, cached frontier, tenure lifecycle/save/load/fast calendar, failure recovery, council neutrality, aggregate telemetry, social caps, ruler/member equality, separate liege chains, cached relation reads and demand-driven wiring.'
