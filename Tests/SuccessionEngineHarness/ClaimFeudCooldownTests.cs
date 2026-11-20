using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class ClaimFeudCooldownTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Store : IDataStore
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
            {
                object value = data;
                if (value is List<ClaimFeudRecord> records)
                    value = records.Select(Clone).ToList();
                _data[key] = value;
            }
            else if (_data.TryGetValue(key, out object value)) data = (T)value;
            return true;
        }
        private static ClaimFeudRecord Clone(ClaimFeudRecord record)
        {
            var copy = Blank<ClaimFeudRecord>();
            foreach (var field in typeof(ClaimFeudRecord).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute"))
                    field.SetValue(copy, field.GetValue(record));
            }
            return copy;
        }
    }

    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly List<Clan> Houses = new List<Clan>();
    private static readonly HashSet<Kingdom> WarEnemies = new HashSet<Kingdom>();
    private static Campaign _campaign;
    private static Manager _manager;
    private static Kingdom _realm;
    private static Clan _crown, _claimant, _holder, _other, _player;
    private static FeudalTitleBehavior _titles;
    private static ClaimFeudBehavior _feuds;
    private static RealmPeaceEnforcementBehavior _peace;
    private static FeudalTitleRecord _title;
    private static float _day, _influence;
    private static int _spends;
    private static int _refunds, _starts, _accepted;
    private static bool _allowSpend, _allowStart, _unsafeUltimatum, _playerWasInLaunch, _extraPlayerCall;
    private static Action _acceptUltimatum, _refuseUltimatum;
    private static FactionObject _launched;
    private static FeudalTitleRecord _sovereign, _barony;
    private static readonly Dictionary<Clan, Kingdom> RealmOverrides = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static int _titleSettlementTransfers, _civilTrackers, _tribunals;
    private static bool _forceTitleCollapse;
    private static InquiryData _inquiry;
    private static float _playerInfluenceSpent;
    private static bool CaptureInquiry(InquiryData data) { _inquiry = data; return false; }
    private static bool CapturePlayerInfluence(float amount) { _playerInfluenceSpent -= amount; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static T Field<T>(object o, string name) => (T)AccessTools.Field(o.GetType(), name).GetValue(o);
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static List<ClaimFeudRecord> Records => Field<List<ClaimFeudRecord>>(_feuds, "_feuds");
    private static Dictionary<string, float> PeaceTimers => Field<Dictionary<string, float>>(_peace, "_royalPeaceUntilByRealmId");
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool ZeroFloat(ref float __result) { __result = 0; return false; }
    private static bool ZeroInt(ref int __result) { __result = 0; return false; }
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = RealmOverrides.TryGetValue(__instance, out Kingdom realm) ? realm : _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _crown; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Houses); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(new[] { _realm }.Concat(WarEnemies).ToList()); return false; }
    private static bool AtWar(Kingdom __instance, IFaction __0, ref bool __result)
    { __result = __instance == _realm && __0 is Kingdom other && WarEnemies.Contains(other); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test realm"); return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Year(ref int __result) { __result = 84; return false; }
    private static bool Influence(ref float __result) { __result = _influence; return false; }
    private static bool TitleName(ref string __result) { __result = "Test county"; return false; }
    private static bool Defiance(ref float __5, ref float __6, ref bool __result)
    { __5 = __6 = 100; __result = false; return false; }
    private static bool Spend(ref bool __result) { _spends++; __result = _allowSpend; return false; }
    private static bool Refund() { _refunds++; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Roster(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Houses.Where(c => c.Kingdom == __instance).ToList()); return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool RulingHead(ref Hero __result) { __result = Heads[_crown]; return false; }
    private static bool UnsafeUltimatum(ref bool __result) { __result = _unsafeUltimatum; return false; }
    private static bool ShowUltimatum(Action onAccept, Action onRefuse)
    { _acceptUltimatum = onAccept; _refuseUltimatum = onRefuse; return false; }
    private static bool LaunchResistance(FactionObject __instance, object __0, ref bool __result)
    {
        _starts++; _launched = __instance; _playerWasInLaunch = __instance.Members.Contains(_player);
        _extraPlayerCall = __0 != null; __result = _allowStart; return false;
    }
    private static bool AcceptUltimatum(FactionObject __instance, ref UltimatumResolution __result)
    {
        _accepted++;
        var manager = _manager.GetBehavior<FactionManagerBehavior>();
        manager.RemoveFaction(__instance);
        __result = UltimatumResolution.DemandsAccepted;
        return false;
    }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Sovereign(ref FeudalTitleRecord __result) { __result = _sovereign; return false; }
    private static bool ResolveHolding(string id, ref Settlement __result)
    { __result = Owners.Keys.FirstOrDefault(s => s.StringId == id); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool Holds(Kingdom __instance, ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(Owners.Where(p => p.Value.Kingdom == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Strongholds(Kingdom kingdom, ref int __result)
    { __result = _forceTitleCollapse && kingdom == _realm && _claimant.Kingdom != _realm ? 0 : 1; return false; }
    private static bool TransferPossession(Clan source, Clan recipient, Settlement holding, ref bool __result)
    {
        _titleSettlementTransfers++;
        Owners[holding] = recipient;
        if (_barony?.CapitalSettlementId == holding.StringId) _barony.SetDeFactoHolder(recipient.StringId);
        __result = true; return false;
    }
    private static bool LiveShell(FactionObject faction, Kingdom preferredKingdom, ref Kingdom __result)
    { __result = preferredKingdom ?? (Kingdom)Call(faction, "GetTrackedRebelKingdomIncludingEliminated"); return false; }
    private static bool TransferHouse(Clan clan, Kingdom targetKingdom, ref bool __result)
    { RealmOverrides[clan] = targetKingdom; __result = true; return false; }
    private static bool CountTracker() { _civilTrackers++; return false; }
    private static bool CountTribunal() { _tribunals++; return false; }

    private static Clan House(string id)
    {
        var clan = Blank<Clan>(); clan.StringId = id;
        var hero = Blank<Hero>(); hero.StringId = id + "_head";
        Heads[clan] = hero; Houses.Add(clan); return clan;
    }
    private static void Reset(FeudalClaimStrength strength = FeudalClaimStrength.Strong)
    {
        Houses.Clear(); Heads.Clear(); WarEnemies.Clear(); _manager.Items.Clear(); _day = 20; _influence = 1000; _spends = 0;
        _realm = Blank<Kingdom>(); _realm.StringId = "realm";
        _crown = House("crown"); _player = _claimant = House("claimant");
        _holder = House("holder"); _other = House("other");
        _titles = new FeudalTitleBehavior(); _feuds = new ClaimFeudBehavior(); _peace = new RealmPeaceEnforcementBehavior();
        _manager.Items.AddRange(new CampaignBehaviorBase[] { _titles, _feuds, _peace });
        var parent = new FeudalTitleRecord("parent", "Parent", FeudalTitleType.Duchy,
            _claimant.StringId, _claimant.StringId, "", "", _realm.StringId, 0, 0);
        _title = new FeudalTitleRecord("county", "County", FeudalTitleType.County,
            _holder.StringId, _holder.StringId, parent.TitleId, "", _realm.StringId, 0, 0);
        var registry = Field<Dictionary<string, FeudalTitleRecord>>(_titles, "_titlesById");
        registry.Add(parent.TitleId, parent); registry.Add(_title.TitleId, _title);
        Call(_titles, "RebuildRuntimeIndexes", true, true);
        _titles.RegisterClaim(_claimant, _title, strength, "fixture");
    }
    private static ClaimFeudRecord Record(Clan claimant = null, Clan holder = null, string titleId = null)
    {
        var record = new ClaimFeudRecord("feud_" + Records.Count, _realm.StringId,
            (claimant ?? _claimant).StringId, (holder ?? _holder).StringId, titleId ?? _title.TitleId,
            _titles.GetStrongestActiveClaim(_claimant, _title)?.Strength ?? FeudalClaimStrength.Strong,
            100, _day, "fixture", "fixture");
        record.SetState(ClaimFeudState.AwaitingPlayerResponse);
        Records.Add(record); return record;
    }
    private static void Rule(ClaimFeudRecord record, ClaimFeudJudgment judgment, ClaimFeudResponse response = ClaimFeudResponse.Accept)
        => Call(_feuds, "ApplyRulingOutcome", _titles, record, _realm, Heads[_crown], _claimant, _holder, _title,
            judgment, response, ClaimFeudResponse.Accept);
    private static TextObject Cooldown(Clan claimant = null, Clan holder = null, FeudalTitleRecord title = null)
        => (TextObject)Call(_feuds, "GetDisputeCooldownBlockReason", claimant ?? _claimant, holder ?? _holder, title ?? _title);
    private static bool ImmediateWar(out string reason)
        => _feuds.TryStartImmediateRevocationWar(_titles, _claimant, _holder, _title,
            FeudalClaimStrength.Strong, out _, out reason);
    private static TextObject Hint(FeudalRevocationPreview preview)
    {
        var vm = typeof(ClaimFeudBehavior).Assembly.GetType("BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy.HierarchyTitleNodeVM", true);
        return (TextObject)AccessTools.Method(vm, "BuildRevocationHint").Invoke(FormatterServices.GetUninitializedObject(vm), new object[] { preview });
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.claim_feud_cooldowns");
        _manager = new Manager(); _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(_manager);
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new Exception("Missing cooldown fixture method: " + prefix);
            h.Patch(method, prefix: new HarmonyMethod(typeof(ClaimFeudCooldownTests), prefix));
        }
        void Getter(Type type, string property, string prefix) => Patch(AccessTools.PropertyGetter(type, property), prefix);
        var mod = typeof(ClaimFeudBehavior).Assembly;
        try
        {
            Getter(typeof(Campaign), "Current", nameof(Current));
            Getter(typeof(CampaignTime), "DaysInYear", nameof(Year));
            Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Clan), "Leader", nameof(Head));
            Getter(typeof(Clan), "Kingdom", nameof(Realm));
            Getter(typeof(Clan), "All", nameof(AllClans));
            Getter(typeof(Clan), "Influence", nameof(Influence));
            foreach (string flag in new[] { "IsEliminated", "IsUnderMercenaryService", "IsMinorFaction" })
                Getter(typeof(Clan), flag, nameof(No));
            Getter(typeof(Hero), "IsDead", nameof(No));
            Getter(typeof(Kingdom), "IsEliminated", nameof(No));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler));
            Getter(typeof(Kingdom), "All", nameof(AllRealms));
            Getter(typeof(Kingdom), "Name", nameof(Name));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(AtWar));
            foreach (var type in new[] { typeof(ClaimFeudBehavior), typeof(FeudalTitleBehavior), typeof(RealmPeaceEnforcementBehavior) })
                Getter(type, "CurrentDay", nameof(Day));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "ApplyRulingRelations"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "WouldHolderDefyRevocation"), nameof(Defiance));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "QueueServiceReviewForTitleAndChildren"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumIntegrationBehavior), "IsBarredId"), nameof(No));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileLogger"), "Log"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.FeudalTitleDisplayHelper"), "FormatTitleName",
                new[] { typeof(FeudalTitleRecord), typeof(Clan) }), nameof(TitleName));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.NpcInfluenceBudgetService"), "TrySpend"), nameof(Spend));
            RunCases(check);
            RunPendingWarCases(check);
            Patch(AccessTools.Method(mod.GetType("BellumCivile.ClanFiefDesireHelper"), "CalculateRebelliousFiefDesirePressure"), nameof(ZeroFloat));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.ClanFiefDesireHelper"), "CalculateFiefDesirePressure"), nameof(ZeroFloat));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "CalculatePowerAdvantageScore"), nameof(ZeroFloat));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "CalculatePersonalityScore"), nameof(ZeroFloat));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(ZeroInt));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.MarriageAllianceHelper"), "HasMarriageAlliance"), nameof(No));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "AreCloseDynasticKin"), nameof(No));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.NotificationHelper"), "ShowClaimFeudStarted"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileDebug"), "Trace"), nameof(Skip));
            Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry", new[] { typeof(InquiryData), typeof(bool), typeof(bool) }), nameof(CaptureInquiry));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileNotifications"), "ShowPersonal", new[] { typeof(TextObject), typeof(Color) }), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionsWindowVM), "RefreshAvailableFactionTypes"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionsWindowVM), "RefreshButtonStates"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionsWindowVM), "RefreshFactionList"), nameof(Skip));
            Patch(AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.ChangeClanInfluenceAction), "Apply"), nameof(CapturePlayerInfluence));
            RunSelectionCases(check);
            Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(Decisions));
            Getter(typeof(Kingdom), "Leader", nameof(RulingHead));
            Getter(typeof(Kingdom), "Clans", nameof(Roster));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "IsHereditaryRealm"), nameof(No));
            Patch(AccessTools.Method(typeof(FactionObject), "ShouldDelayUltimatumForUnsafeLeaderLocation"), nameof(UnsafeUltimatum));
            Patch(AccessTools.Method(typeof(FactionObject), "StartOpenRebellion"), nameof(LaunchResistance));
            Patch(AccessTools.Method(typeof(FactionObject), "AcceptDemands"), nameof(AcceptUltimatum));
            Patch(AccessTools.Method(typeof(NotificationHelper), "ShowUltimatumToPlayer"), nameof(ShowUltimatum));
            Patch(AccessTools.Method(typeof(NotificationHelper), "ShowFeudalTitleRevocationDefied",
                new[] { typeof(Clan), typeof(Clan), typeof(FeudalTitleRecord), typeof(bool) }), nameof(Skip));
            Patch(AccessTools.Method(typeof(NotificationHelper), "ShowFeudalTitleRevoked"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.RebellionPowerHelper"), "CalculateClanPower"), nameof(ZeroFloat));
            Patch(AccessTools.Method(typeof(FeudalTitlePlayerActionService), "ApplyRevocationRelationPenalty"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.NpcInfluenceBudgetService"), "Refund"), nameof(Refund));
            RunRevocationResistanceCases(check);
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(Sovereign));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetKingdomPoliticalTitle"), nameof(Sovereign));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(No));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "FindCrownClaimSettlement"), nameof(ResolveHolding));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "TransferChallengePossession"), nameof(TransferPossession));
            Getter(typeof(Settlement), "OwnerClan", nameof(Owner));
            Getter(typeof(Kingdom), "Settlements", nameof(Holds));
            var resolver = typeof(CivilWarResolutionBehavior);
            Patch(AccessTools.Method(resolver, "CountStrongholds"), nameof(Strongholds));
            Patch(AccessTools.Method(resolver, "ResolveTrackedRebelKingdom"), nameof(LiveShell));
            Patch(AccessTools.Method(resolver, "PrepareMercenarySettlement"), nameof(Yes));
            Patch(AccessTools.Method(resolver, "TransferCivilWarClan"), nameof(TransferHouse));
            Patch(AccessTools.Method(resolver, "DrainAndDestroyRebelKingdom"), nameof(Yes));
            foreach (string method in new[] { "CaptureCurrentCivilWarFiefSnapshot", "ApplyCivilWarPeaceIfNeeded", "ApplyCivilWarVictoryInfluenceRewards", "PacifyIdeologies" })
                Patch(AccessTools.Method(resolver, method), nameof(Skip));
            Patch(AccessTools.Method(resolver, "CompleteCivilWarTracker"), nameof(CountTracker));
            Patch(AccessTools.Method(resolver, "QueuePlayerTribunal"), nameof(CountTribunal));
            Patch(AccessTools.Method(resolver, "ApplyPostWarConsequences"), nameof(CountTribunal));
            RunCrownTitleClaimCases(check);
        }
        finally { h.UnpatchAll(h.Id); _campaign = null; _allowSpend = false; RealmOverrides.Clear(); Owners.Clear(); }
    }

    private static void RunRevocationResistanceCases(Action<bool, string> check)
    {
        FactionManagerBehavior Setup(bool asCrown = true)
        {
            Reset();
            _allowSpend = _allowStart = true; _unsafeUltimatum = false;
            _starts = _refunds = _accepted = 0; _launched = null;
            _acceptUltimatum = _refuseUltimatum = null;
            if (asCrown) _crown = _claimant;
            var manager = new FactionManagerBehavior();
            _manager.Items.Add(manager);
            return manager;
        }
        bool Execute(out bool defied, out string reason) =>
            FeudalTitlePlayerActionService.TryExecuteRevocationWithResponse(_claimant, _title, true, out defied, out reason);
        bool HoldsTitle() => _title.DeFactoHolderClanId == _holder.StringId && _title.DeJureHolderClanId == _holder.StringId;
        string Consequence() => ((TextObject)AccessTools.Method(typeof(FeudalTitlePlayerActionService), "GetRevocationWarConsequence")
            .Invoke(null, new object[] { _claimant, _holder })).ToString();
        FactionObject Existing(FactionManagerBehavior manager)
        {
            var faction = new FactionObject("Test faction", _realm, _other, FactionType.Independence);
            faction.Members.Add(_holder);
            manager.RegisterNewFaction(faction);
            return faction;
        }

        var factions = Setup();
        check(!ImmediateWar(out string reason) && reason.Contains("ruling clan") && Records.Count == 0,
            "The private-war entry point cannot register a defied Crown revocation");
        check(Consequence().Contains("abdication") && Execute(out bool defied, out _) && defied
            && _starts == 1 && _launched.Type == FactionType.Abdication && _launched.Leader == _holder
            && _acceptUltimatum == null && HoldsTitle() && Records.Count == 0 && _spends == 1 && _refunds == 0,
            "Without an existing faction, refusal starts an abdication war directly and keeps the title");

        factions = Setup();
        Field<Dictionary<Kingdom, CampaignTime>>(factions, "_initialCivilWarGrace")[_realm] = CampaignTime.Days(30);
        check(Execute(out _, out _) && _starts == 1 && Field<bool>(_launched, "_judgmentResistance"),
            "Refusing a Crown decree uses the existing judgment-resistance exception to civil-war grace");
        factions = Setup();
        PeaceTimers[_realm.StringId] = _day + 20;
        check(!Execute(out _, out _) && _spends == 0 && _starts == 0 && HoldsTitle(),
            "Royal Peace still prevents claimed revocation from being issued or starting resistance");

        factions = Setup();
        var existing = Existing(factions);
        check(Consequence().Contains("ultimatum") && Execute(out defied, out _) && defied
            && existing.IsUltimatumPending && _starts == 0 && _refuseUltimatum != null,
            "A holder who is only a member triggers their existing faction's ultimatum");
        _refuseUltimatum();
        check(_starts == 1 && _launched == existing && existing.Type == FactionType.Independence
            && existing.Members.Contains(_holder) && HoldsTitle() && Records.Count == 0 && _refunds == 0,
            "Refusing that ultimatum launches the original rebellion rather than a second abdication faction");

        factions = Setup();
        existing = Existing(factions);
        check(Execute(out _, out _) && existing.IsUltimatumPending, "The Crown can choose to accept the faction's ultimatum");
        _acceptUltimatum();
        check(_accepted == 1 && _starts == 0 && _refunds == 0 && HoldsTitle() && Records.Count == 0,
            "Accepting the existing demands avoids war without taking the refused title");

        factions = Setup();
        existing = Existing(factions);
        check(Execute(out _, out _) && !Execute(out _, out _) && _refunds == 1 && existing.IsUltimatumPending,
            "A second demand does not replace or bypass an already pending ultimatum");
        _title.SetActive(false);
        _refuseUltimatum();
        check(_starts == 0 && !existing.IsUltimatumPending && _refunds == 2 && Records.Count == 0,
            "A stale demand clears the deferred ultimatum and refunds its original cost");
        check(!Field<bool>(existing, "_judgmentResistance") && factions.GetRebelFaction(_holder) == existing,
            "A cancelled confrontation returns the existing faction to its original state");

        factions = Setup(); _unsafeUltimatum = true;
        check(!Execute(out defied, out _) && !defied && _starts == 0 && _refunds == 1
            && factions.GetRebelFaction(_holder) == null && HoldsTitle() && Records.Count == 0,
            "An unsafe fallback is refunded and leaves no stray faction or private dispute");
        factions = Setup(); _allowStart = false;
        check(!Execute(out _, out _) && _starts == 1 && _refunds == 1
            && factions.GetRebelFaction(_holder) == null && HoldsTitle() && Records.Count == 0,
            "A clean failure to create the fallback war leaves the title and removes the new faction");

        factions = Setup(); existing = Existing(factions); _player = _holder;
        var result = (UltimatumResolution)Call(existing, "RefuseDemandsAndRebel", null, false, _holder);
        check(result == UltimatumResolution.RebellionStarted && _playerWasInLaunch && !_extraPlayerCall && existing.Members.Contains(_holder),
            "A player who has chosen to defy as a member stays with the faction without a second side-choice prompt");

        factions = Setup();
        check(FeudalTitlePlayerActionService.TryExecuteRevocationWithResponse(_claimant, _title, false, out defied, out _)
            && !defied && _starts == 0 && factions.GetRebelFaction(_holder) == null && Records.Count == 0
            && _title.DeFactoHolderClanId == _claimant.StringId && _title.DeJureHolderClanId == _claimant.StringId,
            "A vassal who submits still surrenders their title without any civil war");

        Setup(asCrown: false);
        check(Consequence().Contains("private war") && Execute(out defied, out _) && defied && _starts == 0
            && Records.Single().State == ClaimFeudState.DefiedPendingWar && HoldsTitle(),
            "Revocation between two non-ruling houses keeps the existing private-war path");
    }

    private static void RunCrownTitleClaimCases(Action<bool, string> check)
    {
        var singleton = AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance");
        var previous = FeudalTitleBehavior.Instance;
        FactionManagerBehavior Setup(bool claimantOwnsLegally, bool barony)
        {
            Reset();
            RealmOverrides.Clear(); Owners.Clear(); _barony = null;
            _titleSettlementTransfers = _civilTrackers = _tribunals = 0;
            _forceTitleCollapse = false;
            singleton.Invoke(null, new object[] { _titles });
            var manager = new FactionManagerBehavior();
            _manager.Items.Add(manager);
            _sovereign = new FeudalTitleRecord("sovereign", "Kingdom", FeudalTitleType.Kingdom,
                _crown.StringId, _crown.StringId, "", "", _realm.StringId, 0, 0);
            var registry = Field<Dictionary<string, FeudalTitleRecord>>(_titles, "_titlesById");
            if (barony)
            {
                _barony = _title = new FeudalTitleRecord("barony", "Calioc", FeudalTitleType.Barony, _crown.StringId,
                    _crown.StringId, "", "calioc", _realm.StringId, 0, 0);
                registry.Add(_title.TitleId, _title);
                var holding = Blank<Settlement>(); holding.StringId = _title.CapitalSettlementId;
                Owners[holding] = _crown;
            }
            _title.SetDeJureHolder(claimantOwnsLegally ? _claimant.StringId : _crown.StringId);
            _title.SetDeFactoHolder(_crown.StringId);
            Call(_titles, "RebuildRuntimeIndexes", true, true);
            if (barony && !claimantOwnsLegally) _titles.RegisterClaim(_claimant, _title, FeudalClaimStrength.Weak, "fixture");
            return manager;
        }
        FactionObject Found(FactionManagerBehavior manager, string crownId = null)
            => (FactionObject)Call(manager, "TryFoundCrownClaim", _claimant, _title, crownId ?? _crown.StringId, null);
        string State(FactionObject faction, Kingdom rebel = null)
            => Call(faction, "CrownClaimState", new object[] { rebel }).ToString();
        bool Accepted(FactionObject faction) => (bool)Call(faction, "TrySettleAcceptedCrownClaim");
        try
        {
            var manager = Setup(claimantOwnsLegally: false, barony: false);
            var faction = Found(manager);
            check(faction != null && faction.Type == FactionType.CrownTitleClaim && State(faction) == "Valid",
                "An active claim to a subordinate title held by the Crown can found one distinct faction");
            check(Found(manager) == null && manager.GetRebelFaction(_claimant) == faction,
                "Founding revalidates the faction and does not duplicate it");
            check(Accepted(faction) && _title.DeJureHolderClanId == _claimant.StringId && _title.DeFactoHolderClanId == _claimant.StringId
                && _realm.RulingClan == _crown && _titles.GetStrongestActiveClaim(_crown, _title)?.Strength == FeudalClaimStrength.Weak,
                "Crown concession awards both rights without changing the ruler or inventing other awards");

            manager = Setup(claimantOwnsLegally: true, barony: true);
            faction = Found(manager);
            check(faction != null && _titles.GetStrongestActiveClaim(_claimant, _title) == null && Accepted(faction)
                && _titleSettlementTransfers == 1 && Owners.Single().Value == _claimant
                && _title.DeJureHolderClanId == _claimant.StringId && _titles.GetStrongestActiveClaim(_crown, _title) == null,
                "For an occupied barony, legal tenure alone suffices and a concession restores possession without rewriting law");

            manager = Setup(claimantOwnsLegally: false, barony: false);
            _title.SetDeJureHolder(_other.StringId);
            check(Found(manager) == null, "A Crown occupation cannot be used to take a third house's legal title");
            _title.SetDeJureHolder(_crown.StringId);
            check(Found(manager, "displaced") == null, "A founding choice recorded under an earlier Crown is rejected");
            var original = _title; _title = _sovereign;
            _titles.RegisterClaim(_claimant, _title, FeudalClaimStrength.Strong, "fixture");
            check(Found(manager) == null, "The realm's sovereign title is reserved for throne claimant factions");
            _title = original;
            faction = Found(manager);
            faction.Members.Add(_other);
            faction.RemoveMember(_claimant);
            check(faction.Leader == _claimant && State(faction) == "Invalid",
                "The saved claimant cannot be replaced by a supporter inheriting the demand");

            manager = Setup(claimantOwnsLegally: true, barony: true);
            faction = Found(manager);
            var resolver = new CivilWarResolutionBehavior();
            _manager.Items.Add(resolver);
            var rebel = Blank<Kingdom>(); rebel.StringId = "realm_rebels_claimant";
            faction.SetRebelKingdom(rebel);
            RealmOverrides[_claimant] = rebel;
            var holding = Owners.Single().Key;
            var otherHolding = Blank<Settlement>(); otherHolding.StringId = "internal_occupation"; Owners[otherHolding] = _other;
            var foreignHolding = Blank<Settlement>(); foreignHolding.StringId = "foreign_occupation"; Owners[foreignHolding] = _crown;
            AccessTools.Field(typeof(FactionObject), "_crownClaimStartingFiefs").SetValue(faction,
                Owners.Select(pair => new WarScoreFiefSnapshotRecord(pair.Key.StringId, _realm.StringId, pair.Value.StringId, true, false)).ToList());
            RealmOverrides[_holder] = Blank<Kingdom>();
            Call(faction, "ObserveCrownClaimOccupation", foreignHolding, _crown, _holder,
                ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege, rebel);
            Owners[foreignHolding] = _holder;
            foreach (var captured in new[] { holding, otherHolding })
            {
                Clan former = Owners[captured];
                Owners[captured] = _claimant;
                Call(faction, "ObserveCrownClaimOccupation", captured, former, _claimant,
                    ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege, rebel);
            }
            _title.SetDeFactoHolder(_claimant.StringId);
            check(State(faction, rebel) == "Valid", "Occupation by the lawful claimant is not itself a Crown concession");
            _forceTitleCollapse = true;
            resolver.HandleCollapsedParentKingdom(_realm);
            check(_civilTrackers == 1 && _tribunals == 0 && _realm.RulingClan == _crown && _claimant.Kingdom == _realm
                && Owners[holding] == _claimant && Owners[otherHolding] == _other && Owners[foreignHolding] == _holder,
                "The claimant taking the last loyalist stronghold awards only the contested barony and preserves the realm and its Crown");
            check(manager.GetRebelFaction(_claimant) == null && _title.DeJureHolderClanId == _claimant.StringId,
                "A successful title war reunifies and removes the rebellion without a Crown transfer");

            manager = Setup(claimantOwnsLegally: false, barony: false);
            faction = Found(manager);
            faction.SetRebelKingdom(rebel);
            RealmOverrides[_claimant] = rebel;
            resolver.ResolveLiegeVictory(faction, rebel);
            check(_tribunals == 1 && _civilTrackers == 1 && _title.DeFactoHolderClanId == _crown.StringId
                && _titles.GetStrongestActiveClaim(_claimant, _title)?.Strength == FeudalClaimStrength.Weak,
                "A victorious Crown retains the titled estate, can judge the rebellion and weakens a strong claim");

            manager = Setup(claimantOwnsLegally: true, barony: true);
            faction = Found(manager);
            faction.SetRebelKingdom(rebel);
            RealmOverrides[_claimant] = rebel;
            Call(resolver, "ResolveWhitePeace", faction, rebel, false, null);
            check(_tribunals == 0 && _civilTrackers == 1 && _title.DeFactoHolderClanId == _crown.StringId
                && _title.DeJureHolderClanId == _claimant.StringId && _claimant.Kingdom == _realm,
                "White peace reunites the houses without settling the claim or transferring legal rights");
        }
        finally { singleton.Invoke(null, new object[] { previous }); RealmOverrides.Clear(); Owners.Clear(); }
    }

    private static void RunPendingWarCases(Action<bool, string> check)
    {
        Reset();
        var first = Blank<Kingdom>(); first.StringId = "foreign_a";
        var last = Blank<Kingdom>(); last.StringId = "foreign_b";
        var feudRealm = Blank<Kingdom>(); feudRealm.StringId = "bc_feud_other_holder";
        WarEnemies.UnionWith(new[] { first, last, feudRealm });
        var record = Record();
        record.SetState(ClaimFeudState.PetitionReady);
        record.SetPressure(82f);

        var vm = Blank<ClaimFeudItemVM>();
        AccessTools.Field(typeof(ClaimFeudItemVM), "_record").SetValue(vm, record);
        void Refresh() => Call(vm, "RefreshPetitionState", _claimant);
        string HintText(object hint) => Field<TextObject>(hint, "HintText").ToString();
        bool PausedByConflict()
        {
            object[] args = { record, _claimant, _holder, null };
            return (bool)AccessTools.Method(typeof(ClaimFeudBehavior), "TryGetTemporaryPauseReason").Invoke(_feuds, args);
        }
        Refresh();
        check(vm.CanPetition && !vm.HasPendingWarStatus && !PausedByConflict()
            && HintText(vm.PetitionHint).Contains("The ruler can hear this petition"),
            "Foreign war still allows a court petition and explains the consequence of defiance");

        Rule(record, ClaimFeudJudgment.UpholdHolder, ClaimFeudResponse.Defy);
        Refresh();
        check(!vm.CanPetition && vm.PendingWarStatusText.Contains("the realm is at war abroad") && record.Pressure == 82f
            && HintText(vm.PendingWarStatusHint).Contains("automatically"),
            "Pending defiance below full intensity shows the foreign-war delay");
        var war = new ClaimFeudWarBehavior();
        check(!(bool)Call(war, "TryStartFeudWar", record) && record.State == ClaimFeudState.Paused
            && record.ResumeState == ClaimFeudState.DefiedPendingWar && !war.HasActiveWarForFeud(record.RecordId),
            "The war launcher still pauses pending defiance without creating a feud war");
        check(!_feuds.TryPetitionFeud(record.RecordId, _claimant, out string petitionReport)
            && petitionReport.Contains("awaiting private war") && record.State == ClaimFeudState.Paused,
            "A stale petition cannot reopen a ruling while pending defiance is paused");

        var notifications = typeof(ClaimFeudBehavior).Assembly.GetType("BellumCivile.NotificationHelper");
        var response = AccessTools.Method(notifications, "GetClaimFeudResponseText");
        string RulingText(ClaimFeudResponse claimantResponse, ClaimFeudResponse holderResponse) =>
            response.Invoke(null, new object[] { _realm, ClaimFeudJudgment.UpholdHolder, null, null, claimantResponse, holderResponse }).ToString();
        check(RulingText(ClaimFeudResponse.Defy, ClaimFeudResponse.Accept).Contains("Private banners cannot be raised")
            && RulingText(ClaimFeudResponse.Defy, ClaimFeudResponse.Defy).Contains("Both houses delivered letters")
            && !RulingText(ClaimFeudResponse.Accept, ClaimFeudResponse.Accept).Contains("banners"),
            "Ruling messages distinguish one or both defiant houses from peaceful acceptance during foreign war");

        WarEnemies.Remove(first);
        Refresh();
        check(PausedByConflict() && vm.PendingWarStatusText.Contains("at war abroad"),
            "Ending only one of multiple foreign wars does not lift the delay");
        WarEnemies.Remove(last);
        Refresh();
        check(!PausedByConflict() && vm.PendingWarStatusText == "Private war pending"
            && !RulingText(ClaimFeudResponse.Defy, ClaimFeudResponse.Accept).Contains("cannot be raised"),
            "Ending the last foreign war clears that blocker even if an internal feud realm is still hostile");
        record.SetState(ClaimFeudState.WarActive);
        Refresh();
        check(!vm.HasPendingWarStatus && !vm.CanPetition, "The pending banner disappears once a private war is active");

        record.Pause(ClaimFeudState.PetitionReady, "fixture", _day);
        var window = Blank<FactionsWindowVM>();
        Call(window, "TryApplySelectedClaimFeudButtonState", record);
        check(window.CreateDestroyText == "Abandon" && !window.IsCreateDestroyEnabled,
            "An ordinary paused feud does not offer an unusable Abandon action");
        WarEnemies.Add(last);
        record.Pause(ClaimFeudState.DefiedPendingWar, "foreign war", _day);
        Call(window, "TryApplySelectedClaimFeudButtonState", record);
        check(window.CreateDestroyText == "Surrender" && window.IsCreateDestroyEnabled,
            "The claimant can choose Surrender while pending defiance is paused");
        check(_feuds.TrySurrenderPlayerFeud(record.RecordId, _claimant, out _) && record.State == ClaimFeudState.Settled
            && record.CooldownUntilDay > _day && _title.DeJureHolderClanId == _holder.StringId,
            "Surrender during the delay upholds the holder and starts the existing cooldown before any banners are raised");
    }

    private static void RunSelectionCases(Action<bool, string> check)
    {
        DirectoryInfo root = null;
        foreach (string searchRoot in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
        {
            root = new DirectoryInfo(searchRoot);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "BellumCivile.csproj"))) root = root.Parent;
            if (root != null) break;
        }
        check(root != null, "Repository found for faction-selection prefab checks");
        var prefab = new XmlDocument();
        prefab.Load(Path.Combine(root.FullName, "GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml"));
        var confirm = (XmlElement)prefab.SelectSingleNode("//ButtonWidget[@Command.Click='ExecuteConfirmCreate']");
        check(confirm != null && confirm.GetAttribute("IsEnabled") == "@IsCreationChoiceSelected",
            "The founding confirmation button is disabled until an explicit selection exists");
        var row = (XmlElement)prefab.SelectSingleNode("//*[@Id='BellumTypeList']//ButtonWidget[@Command.Click='ExecuteSelect']");
        check(row != null && row.GetAttribute("IsSelected") == "@IsSelected" && row.GetAttribute("DoNotPassEventsToChildren") == "true",
            "The selected row is visibly highlighted and receives clicks over its label");
        var strings = new XmlDocument(); strings.Load(Path.Combine(root.FullName, "ModuleData/Languages/EN/strings.xml"));
        foreach (string id in new[] { "Title", "Body", "Accept", "Back", "Changed", "Unavailable", "HolderChanged", "Started" })
            check(strings.SelectNodes("//string[@id='BC_FeudConfirm_" + id + "']").Count == 1,
                "The feud confirmation text has one English localization entry: " + id);
        Reset();
        var window = Blank<FactionsWindowVM>();
        window.AvailableFactionTypes = new MBBindingList<PoliticalActionVM>();
        PoliticalActionVM Choice(string title, string holder, string name, string house, bool enabled = true)
            => PoliticalActionVM.ForClaimFeud(new ClaimFeudActionPreview
            {
                TitleId = title, TitleName = name, HolderClanId = holder, HolderName = house, IsEnabled = enabled
            }, null, "Start feud over " + name, "");
        var pravend = Choice("town_V3", "dey_tihr", "Barony of Pravend", "dey Tihr");
        var ormanfard = Choice("castle_V4", "dey_folcun", "Barony of Ormanfard", "dey Folcun");
        var disabled = Choice("blocked", "other", "Blocked", "Other", false);
        window.AvailableFactionTypes.Add(ormanfard);
        window.AvailableFactionTypes.Add(pravend);
        window.AvailableFactionTypes.Add(disabled);
        window.SelectedFactionType = ormanfard; ormanfard.IsSelected = true;
        Call(window, "OpenCreationSelector");
        check(window.IsCreatingFaction && window.SelectedFactionType == null && !window.IsCreationChoiceSelected && !ormanfard.IsSelected,
            "Opening the founding menu clears the old selection instead of choosing the first feud");
        Call(window, "OnFactionTypeSelected", disabled);
        Call(window, "OnFactionTypeSelected", Choice("stale", "other", "Stale", "Other"));
        check(window.SelectedFactionType == null, "Disabled or stale rows cannot become the selected action");
        Call(window, "OnFactionTypeSelected", pravend);
        check(window.SelectedFactionType == pravend && pravend.IsSelected && window.IsCreationChoiceSelected && !ormanfard.IsSelected,
            "The explicitly clicked claim, not the first claim, becomes selected");
        _inquiry = null; window.ExecuteConfirmCreate();
        check(_inquiry != null && _inquiry.Text.Contains("Barony of Pravend") && _inquiry.Text.Contains("dey Tihr")
            && _inquiry.Text.Contains("100") && Records.Count == 0,
            "Feud confirmation names the selected title, holder and cost before creating a dispute");
        var confirmation = _inquiry;
        Call(window, "OnFactionTypeSelected", ormanfard);
        confirmation.AffirmativeAction();
        check(Records.Count == 0 && window.SelectedFactionType == ormanfard,
            "Changing selection after confirmation prevents the old feud from starting");
        Call(window, "OnFactionTypeSelected", pravend); _inquiry = null; window.ExecuteConfirmCreate();
        _influence = 0; _inquiry.AffirmativeAction();
        check(Records.Count == 0 && window.IsCreatingFaction, "Influence lost before confirmation prevents the feud without closing the menu");
        _influence = 1000;
        window.ExecuteCancelCreate();
        check(!window.IsCreatingFaction && window.SelectedFactionType == null && !pravend.IsSelected,
            "Cancelling clears the selected action before the next opening");

        var start = AccessTools.Method(typeof(ClaimFeudBehavior), "TryStartPlayerFeudForHolder");
        var changed = new object[] { _claimant, _title.TitleId, _other.StringId, null, null };
        check(!(bool)start.Invoke(_feuds, changed) && Records.Count == 0
            && ((string)changed[4]).Contains("holder has changed"),
            "The real feud backend rejects the selected title when its current holder differs from the confirmed house");
        var matching = new object[] { _claimant, _title.TitleId, _holder.StringId, null, null };
        bool started = (bool)start.Invoke(_feuds, matching);
        var record = matching[3] as ClaimFeudRecord;
        check(started && record != null && record.TargetTitleId == _title.TitleId && record.HolderClanId == _holder.StringId,
            "The real feud backend records the exact confirmed title and holder when both remain eligible");

        Reset(); _playerInfluenceSpent = 0;
        window.AvailableFactionTypes.Clear();
        var real = Choice(_title.TitleId, _holder.StringId, "County", "Holder");
        window.AvailableFactionTypes.Add(real);
        Call(window, "OpenCreationSelector");
        Call(window, "OnFactionTypeSelected", real);
        _inquiry = null; window.ExecuteConfirmCreate();
        check(_inquiry != null && Records.Count == 0 && _playerInfluenceSpent == 0,
            "An unaccepted confirmation does not start a feud or spend influence");
        _inquiry.AffirmativeAction();
        check(Records.Count == 1 && Records[0].TargetTitleId == _title.TitleId
            && Records[0].HolderClanId == _holder.StringId && _playerInfluenceSpent == 100
            && !window.IsCreatingFaction && window.SelectedFactionType == null,
            "Accepting a valid confirmation starts exactly the selected feud and charges once");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCases(Action<bool, string> check)
    {
        foreach (var strength in new[] { FeudalClaimStrength.Strong, FeudalClaimStrength.Weak })
        {
            Reset(strength); var record = Record();
            Rule(record, ClaimFeudJudgment.UpholdHolder);
            check(record.State == ClaimFeudState.Settled && record.CooldownUntilDay == _day + CampaignTime.DaysInYear,
                $"Accepted holder judgment starts one campaign year of cooldown: {strength}; state={record.State}; until={record.CooldownUntilDay}; day={_day}; year={CampaignTime.DaysInYear}");
            check(_titles.GetStrongestActiveClaim(_claimant, _title)?.Strength ==
                (strength == FeudalClaimStrength.Strong ? (FeudalClaimStrength?)FeudalClaimStrength.Weak : null),
                "Existing strong-to-weak / weak-to-extinguished consequences are preserved: " + strength);
            check(_title.DeFactoHolderClanId == _holder.StringId && _title.DeJureHolderClanId == _holder.StringId,
                "Holder judgment preserves the holder's title");
        }

        Reset(); var claimantWin = Record(); Rule(claimantWin, ClaimFeudJudgment.UpholdClaimant);
        check(claimantWin.State == ClaimFeudState.Settled && claimantWin.CooldownUntilDay == _day + CampaignTime.DaysInYear,
            "Accepted claimant judgment also starts one campaign year");
        check(_title.DeFactoHolderClanId == _claimant.StringId && _title.DeJureHolderClanId == _claimant.StringId
            && _titles.GetStrongestActiveClaim(_holder, _title)?.Strength == FeudalClaimStrength.Weak,
            "Claimant receives the title and former holder retains a weak counterclaim");
        check(Cooldown(_holder, _claimant) != null, "Cooldown also bars the dispossessed house's reversed counterclaim");

        Reset(); var abstain = Record(); Rule(abstain, ClaimFeudJudgment.Abstain);
        check(abstain.State == ClaimFeudState.Settled && abstain.CooldownUntilDay == _day + CampaignTime.DaysInYear
            && _titles.GetStrongestActiveClaim(_claimant, _title)?.Strength == FeudalClaimStrength.Strong,
            "Accepted abstention retains its existing cooldown without weakening the claim");
        Reset(); var defied = Record(); Rule(defied, ClaimFeudJudgment.UpholdHolder, ClaimFeudResponse.Defy);
        check(defied.State == ClaimFeudState.DefiedPendingWar && defied.CooldownUntilDay < 0,
            "Defied judgments remain eligible for war instead of receiving a premature settlement cooldown");
        Reset(); var failed = Record(); _title.SetActive(false); Rule(failed, ClaimFeudJudgment.UpholdClaimant);
        check(failed.State == ClaimFeudState.Resolved && failed.CooldownUntilDay < 0,
            "Failed title settlement does not start a cooldown");

        Reset(); var settled = Record(); Rule(settled, ClaimFeudJudgment.UpholdHolder);
        float expiry = settled.CooldownUntilDay;
        _day += .25f;
        check(Cooldown().ToString().Contains(CampaignTime.DaysInYear + " more days"), "Remaining days round up");
        check(Cooldown(_holder, _claimant).ToString() == Cooldown().ToString(), "The same houses share one timer in either direction");
        check(Cooldown(_other, _holder) == null, "Another house is not barred by this pair's cooldown");
        var different = new FeudalTitleRecord("other_county", "Other", FeudalTitleType.County, "holder", "holder", "", "", "realm", 0, 0);
        check(Cooldown(title: different) == null, "An unrelated title is not barred");
        var duplicate = Record(_holder, _claimant); duplicate.SetState(ClaimFeudState.Settled); duplicate.SetCooldownUntilDay(expiry + 5);
        check(Cooldown().ToString().Contains((CampaignTime.DaysInYear + 5) + " more days"), "Multiple receipts use the latest expiry");
        Records.Remove(duplicate);

        foreach (Clan actor in new[] { _claimant, _other })
        {
            _player = actor;
            var preview = FeudalTitlePlayerActionService.GetRevocationPreview(_claimant, _title);
            check(!preview.CanRevoke && preview.RestrictionReason != null && preview.Reason == Cooldown().ToString(),
                "Player and NPC revocation previews share the cooldown restriction");
            check(ReferenceEquals(Hint(preview), preview.RestrictionReason), "Hierarchy uses the actual localized cooldown hint");
            check(!FeudalTitlePlayerActionService.TryExecuteRevocationWithResponse(_claimant, _title, false, out _, out string reason)
                && reason == preview.Reason && _spends == 0, "Accepted revocation cannot bypass cooldown or spend influence");
            check(!FeudalTitlePlayerActionService.TryExecuteRevocationWithResponse(_claimant, _title, true, out _, out reason)
                && reason == preview.Reason && _spends == 0, "Defied revocation cannot bypass cooldown or spend influence");
        }
        _player = _claimant;
        check(((IList)Call(_feuds, "BuildRevocationCandidates", _titles, _claimant)).Count == 0,
            "Political scheduler excludes cooled-down revocations");
        int count = Records.Count;
        check(!ImmediateWar(out string blocked) && blocked == Cooldown().ToString() && Records.Count == count,
            "Direct revocation-war entry is blocked before creating a feud or calling allies");
        check(!_feuds.TryStartPlayerFeud(_claimant, _title.TitleId, out _, out _) && Records.Count == count,
            "Ordinary player feuds remain blocked by the same timer");
        var disabled = _feuds.GetPressableFeudPreviews(_claimant).Single(p => p.TitleId == _title.TitleId);
        check(!disabled.IsEnabled && disabled.DisabledReason == Cooldown().ToString(), "Claim selection retains its cooldown tooltip");
        var candidateType = typeof(ClaimFeudBehavior).GetNestedType("ClaimFeudCandidate", BindingFlags.NonPublic);
        var candidate = Activator.CreateInstance(candidateType, _claimant, _holder, _title,
            FeudalClaimStrength.Weak, "fixture", 100f, new List<string>());
        check(Call(_feuds, "StartFeud", candidate, "fixture") == null && Records.Count == count,
            "Stale ordinary feud candidates cannot reactivate a cooled-down dispute");

        var stale = new FeudalRevocationPreview { CanRevoke = true, HolderClan = _holder, InfluenceCost = 100 };
        foreach (bool defy in new[] { false, true })
        {
            var args = new object[] { _claimant, _title, stale, defy, false, null };
            bool executed = (bool)AccessTools.Method(typeof(FeudalTitlePlayerActionService), "TryExecuteRevocationInternal").Invoke(null, args);
            check(!executed && (string)args[5] == Cooldown().ToString() && _spends == 0
                && _title.DeFactoHolderClanId == _holder.StringId, "A stale confirmation is rechecked before any side effects: " + defy);
        }

        var store = new Store(); _feuds.SyncData(store); _manager.Items.Remove(_feuds);
        _feuds = new ClaimFeudBehavior(); _manager.Items.Add(_feuds); store.IsLoading = true; _feuds.SyncData(store);
        check(!ReferenceEquals(Records.Single(), settled) && Records.Single().CooldownUntilDay == expiry && Cooldown() != null,
            "Existing saved record fields preserve the cooldown after reload");
        _day = expiry - .01f; check(Cooldown().ToString().Contains("1 more days"), "Last partial day remains blocked");
        _day = expiry; check(Cooldown() == null, "Cooldown expires at the exact saved deadline");
        check(FeudalTitlePlayerActionService.GetRevocationPreview(_claimant, _title).CanRevoke,
            "Otherwise-valid revocation becomes available again when cooldown expires");

        Reset(); var prior = Record(); Rule(prior, ClaimFeudJudgment.UpholdHolder);
        PeaceTimers[_realm.StringId] = _day + 12.2f; _influence = 0;
        var royal = FeudalTitlePlayerActionService.GetRevocationPreview(_claimant, _title);
        string expected = _peace.GetRoyalPeaceBlockReason(_realm).ToString();
        check(!royal.CanRevoke && royal.Reason == expected && expected.Contains("13 days") && expected.Contains("claim is preserved"),
            "Royal Peace takes priority over cooldown and insufficient influence, with rounded remaining days");
        check(ReferenceEquals(Hint(royal), royal.RestrictionReason), "Hierarchy shows the exact standard Royal Peace explanation");
        check(!ImmediateWar(out string royalReason) && royalReason == expected, "Direct war entry preserves the standard Royal Peace reason");
        check(!FeudalTitlePlayerActionService.TryExecuteRevocation(_claimant, _title, out _, out royalReason)
            && royalReason == expected && _spends == 0, "Royal Peace prevents revocation without spending influence");
        Records.Clear();
        check(!FeudalTitlePlayerActionService.GetRevocationPreview(_claimant, _title).CanRevoke,
            "Royal Peace bars revocation even with no previous dispute between the houses");
        _day += 13; _influence = 1000;
        check(FeudalTitlePlayerActionService.GetRevocationPreview(_claimant, _title).CanRevoke,
            "Expired Royal Peace no longer bars an otherwise-valid revocation");
    }
}
