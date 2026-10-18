using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class RealmRoyalPeaceEngineTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
            {
                object value = data;
                if (value is Dictionary<string, float> timer) value = new Dictionary<string, float>(timer);
                else if (value is List<RoyalPeaceDecreeRecord> records)
                    value = records.Select(Clone).ToList();
                Data[key] = value;
            }
            else data = Data.TryGetValue(key, out var saved) ? (T)saved : default;
            return true;
        }
        private static RoyalPeaceDecreeRecord Clone(RoyalPeaceDecreeRecord record)
        {
            var copy = new RoyalPeaceDecreeRecord();
            foreach (var field in typeof(RoyalPeaceDecreeRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(record);
                field.SetValue(copy, value is List<string> list ? new List<string>(list) : value);
            }
            return copy;
        }
    }

    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly List<Clan> Houses = new List<Clan>();
    private static readonly List<Clan> RealmHouses = new List<Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly List<string> Settlements = new List<string>();
    private static readonly Dictionary<Hero, List<int>> Penalties = new Dictionary<Hero, List<int>>();
    private static RealmPeaceEnforcementBehavior _service;
    private static List<ClaimFeudRecord> _feuds;
    private static Kingdom _realm, _other, _home, _eliminated;
    private static Clan _ruler, _payer, _minor, _mercenary, _dead;
    private static float _day, _balance;
    private static bool _wartime, _ready, _temporary, _denySpend, _throwSuppression;
    private static int _payments, _notices, _attempts;
    private static string _deferId;
    private static bool _throwNotice, _noticeReady;
    private static ConflictOutcomeNotice _notice;
    private static readonly List<string> NoticeIds = new List<string>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static List<RoyalPeaceDecreeRecord> Journal => (List<RoyalPeaceDecreeRecord>)AccessTools.Field(typeof(RealmPeaceEnforcementBehavior), "_decrees").GetValue(_service);
    private static bool Today(ref float __result) { __result = _day; return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Houses); return false; }
    private static bool ResolveRealm(ClaimFeudRecord record, ref Kingdom __result)
    { __result = Realms.FirstOrDefault(r => r.StringId == record?.ParentKingdomId); return false; }
    private static bool ResolveHouse(string id, ref Clan __result)
    { __result = Houses.FirstOrDefault(c => c.StringId == id); return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(RealmHouses); return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Player(ref Clan __result) { __result = _payer; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Home(ref Kingdom __result) { __result = _home; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Fixture realm"); return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = __instance == _eliminated; return false; }
    private static bool Dead(Clan __instance, ref bool __result) { __result = __instance == _dead; return false; }
    private static bool Minor(Clan __instance, ref bool __result) { __result = __instance == _minor; return false; }
    private static bool Mercenary(Clan __instance, ref bool __result) { __result = __instance == _mercenary; return false; }
    private static bool Temporary(ref bool __result) { __result = _temporary; return false; }
    private static bool Wartime(ref bool __result) { __result = _wartime; return false; }
    private static bool Ready(ref bool __result) { __result = _ready; return false; }
    private static bool Afford(float cost, ref bool __result) { __result = _balance >= cost; return false; }
    private static bool Spend(Clan clan, float cost, string source, ref bool __result)
    {
        __result = !_denySpend && _balance >= cost;
        if (__result)
        {
            if (clan != _payer || source != "royal_peace") throw new Exception("Wrong payment owner or source");
            _payments++; _balance -= cost;
        }
        return false;
    }
    private static bool Accession(ref CrownAccessionBehavior __result) { __result = null; return false; }
    private static bool Elective(ref ElectiveSuccessionBehavior __result) { __result = null; return false; }
    private static bool Titles(ref FeudalTitleBehavior __result) { __result = null; return false; }
    private static bool Suppress(string recordId, ref bool __result)
    {
        _attempts++;
        var decree = Journal.Single();
        if (!_service.IsExecutingRealmPeace(_realm) || decree.StartedFeudId != recordId || _payments != 1)
            throw new Exception("Settlement started before payment, realm guard or saved receipt");
        if (_throwSuppression) throw new InvalidOperationException("injected suppression failure");
        __result = recordId != _deferId;
        if (__result)
        {
            Settlements.Add(recordId);
            _feuds.Single(f => f.RecordId == recordId).SetState(ClaimFeudState.SuppressedCooldown);
        }
        return false;
    }
    private static bool Relation(Hero firstHero, Hero secondHero, int relationChange)
    {
        var clan = Leaders.Single(p => p.Value == firstHero).Key;
        if (secondHero != Leaders[_payer] || !Journal.Single().PenalizedClanIds.Contains(clan.StringId))
            throw new Exception("Relation change lacks the original enforcer or pre-dispatch receipt");
        if (!Penalties.TryGetValue(firstHero, out var changes)) Penalties[firstHero] = changes = new List<int>();
        changes.Add(relationChange);
        return false;
    }
    private static bool Publish(RoyalPeaceDecreeRecord decree)
    {
        if (!decree.NoticePublished) { decree.NoticePublished = true; _notices++; }
        return false;
    }
    private static bool BeginNotice(string id, ref ConflictOutcomeNotice __result)
    {
        NoticeIds.Add(id);
        __result = _notice ?? (_notice = new ConflictOutcomeNotice { Id = id });
        return false;
    }
    private static bool DeliverNotice(ConflictOutcomeNotice notice)
    {
        if (_throwNotice) throw new InvalidOperationException("injected presentation failure");
        if (_noticeReady) { notice.Ready = true; _notices++; }
        return false;
    }
    private static bool DateText(ref string __result) { __result = "fixture date"; return false; }
    private static bool RunOriginal() => true;

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.realm_royal_peace");
        var previous = Campaign.Current;
        var type = typeof(RealmPeaceEnforcementBehavior);
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(RealmRoyalPeaceEngineTests), name));
        Clan House(string id) { var clan = Blank<Clan>(); clan.StringId = id; Houses.Add(clan); Leaders[clan] = Blank<Hero>(); return clan; }
        Kingdom Realm(string id) { var realm = Blank<Kingdom>(); realm.StringId = id; Realms.Add(realm); return realm; }
        try
        {
            Realms.Clear(); Houses.Clear(); Leaders.Clear(); RealmHouses.Clear();
            _realm = Realm("realm"); _other = Realm("other"); _home = _realm;
            _payer = _ruler = House("crown");
            var claimant = House("claimant"); var holder = House("holder"); var bystander = House("bystander");
            _minor = House("minor"); _mercenary = House("mercenary"); _dead = House("dead");
            // Claimant appears both in the realm and in multiple feuds; holder is still outside in a shell.
            RealmHouses.AddRange(new[] { _payer, claimant, bystander, _minor, _mercenary, _dead });
            var manager = new Manager();
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            var feuds = new ClaimFeudBehavior(); var wars = new ClaimFeudWarBehavior();
            manager.Items.Add(feuds); manager.Items.Add(wars);
            _feuds = (List<ClaimFeudRecord>)AccessTools.Field(typeof(ClaimFeudBehavior), "_feuds").GetValue(feuds);
            Patch(AccessTools.PropertyGetter(type, "CurrentDay"), nameof(Today));
            Patch(AccessTools.Method(type, "ResolveKingdomForFeud"), nameof(ResolveRealm));
            Patch(AccessTools.Method(type, "ResolveClan"), nameof(ResolveHouse));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(AllRealms));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Home));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(Dead));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(Minor));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(Mercenary));
            Patch(AccessTools.PropertyGetter(typeof(CrownAccessionBehavior), "Instance"), nameof(Accession));
            Patch(AccessTools.PropertyGetter(typeof(ElectiveSuccessionBehavior), "Instance"), nameof(Elective));
            Patch(AccessTools.PropertyGetter(typeof(FeudalTitleBehavior), "Instance"), nameof(Titles));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(Temporary));
            Patch(AccessTools.Method(type, "KingdomHasExternalWar"), nameof(Wartime));
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "CanEnforceRoyalPeace"), nameof(Ready));
            Patch(AccessTools.Method(typeof(ClaimFeudBehavior), "TrySuppressByRoyalPeace"), nameof(Suppress));
            var budget = type.Assembly.GetType("BellumCivile.NpcInfluenceBudgetService");
            Patch(AccessTools.Method(budget, "CanAfford"), nameof(Afford));
            Patch(AccessTools.Method(budget, "TrySpend"), nameof(Spend));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.RelationMemoryService"), "ApplyChange"), nameof(Relation));
            Patch(AccessTools.Method(type, "PublishDecree"), nameof(Publish));
            // Earlier fixtures re-JIT these methods on unpatch; refresh after installing native stubs.
            Patch(AccessTools.Method(type, "GetRealmPreview"), nameof(RunOriginal));
            Patch(AccessTools.Method(type, "TryEnforceRealmPeace"), nameof(RunOriginal));

            ClaimFeudRecord AddFeud(string id, Kingdom realm, bool armed = false)
            {
                var feud = new ClaimFeudRecord(id, realm.StringId, claimant.StringId, holder.StringId, "title_" + id,
                    FeudalClaimStrength.Strong, 100, 1, "claim", "fixture");
                if (armed) feud.SetState(ClaimFeudState.WarActive);
                _feuds.Add(feud); return feud;
            }
            void Reset(bool wartime = true)
            {
                manager.Items.Remove(_service); _service = new RealmPeaceEnforcementBehavior(); manager.Items.Add(_service);
                _feuds.Clear(); Settlements.Clear(); Penalties.Clear();
                _day = 10; _balance = 2000; _payments = _notices = _attempts = 0;
                _wartime = wartime; _ready = true; _temporary = _denySpend = _throwSuppression = false;
                _deferId = null; _eliminated = null; _ruler = _payer; _home = _realm;
            }
            bool Execute() => (bool)Call(_service, "TryEnforceRealmPeace", _realm, _ruler, null);
            void Resume() => Call(_service, "ResumeDecrees");
            RoyalPeacePreview Preview() => _service.GetRealmPreview(_realm, _ruler);

            Reset(); var first = AddFeud("unarmed", _realm); AddFeud("armed", _realm, true); AddFeud("foreign", _other, true);
            var preview = Preview();
            check(preview.IsEnabled && preview.SettlementReady && preview.FeudCount == 1 && preview.WarCount == 1
                && preview.InfluenceCost == 150 && preview.Wartime,
                $"Realm preview combines only its own armed and unarmed conflicts (enabled={preview.IsEnabled}; ready={preview.SettlementReady}; feuds={preview.FeudCount}; wars={preview.WarCount}; cost={preview.InfluenceCost}; wartime={preview.Wartime}; reason={preview.DisabledReason})");
            check(preview.InvolvedClans.Count == 2 && preview.InvolvedClans.Contains(holder), "Realm preview deduplicates involved houses and retains shell participants");
            check(_service.GetClaimFeudPreview(first, _ruler).InfluenceCost == 150, "Legacy feud preview quotes the entire realm");
            var stale = new ClaimFeudRecord("unarmed", "realm", "claimant", "holder", "title", FeudalClaimStrength.Strong, 100, 1, "claim", "fixture");
            check(!_service.GetClaimFeudPreview(stale, _ruler).IsEnabled && !_service.GetClaimFeudPreview(null, _ruler).IsEnabled,
                "Legacy preview rejects stale identities and missing feuds");
            check(!_service.GetRealmPreview(null, _ruler).IsEnabled && !_service.GetRealmPreview(_realm, null).IsEnabled
                && !_service.GetRealmPreview(_realm, claimant).IsEnabled, "Realm preview rejects missing realm, missing Crown and non-rulers");
            _temporary = true; check(!Preview().IsEnabled, "Temporary realms cannot issue Royal Peace"); _temporary = false;
            _eliminated = _realm; check(!Preview().IsEnabled, "Destroyed realms cannot issue Royal Peace"); _eliminated = null;
            var head = Leaders[_payer]; Leaders[_payer] = null;
            check(!Preview().IsEnabled, "Leaderless Crowns cannot issue Royal Peace"); Leaders[_payer] = head;
            _wartime = false; check(Preview().InfluenceCost == 450 && Preview().IsEnabled, "Peacetime is enabled at triple the realm price");
            _balance = 449; check(!Preview().IsEnabled && !Execute() && _payments == 0, "Insufficient influence cannot create a paid decree");
            _balance = 2000; _ready = false;
            check(Preview().IsEnabled && !Preview().SettlementReady && !Execute() && Journal.Count == 0 && _payments == 0,
                "Unsafe armed settlements may be announced but cannot be charged");
            _ready = true; _denySpend = true;
            check(!Execute() && !_service.HasRoyalPeace(_realm) && !_service.HasTyrantsDebt(_payer) && Journal.Count == 0,
                "Rejected payment leaves neither timer nor journal receipt");

            foreach (bool wartime in new[] { true, false })
            {
                Reset(wartime); AddFeud("one", _realm); AddFeud("two", _realm, true);
                check(Execute(), "Realm executor accepts a safe affordable decree");
                int cost = wartime ? 150 : 450; int scale = wartime ? 1 : 2;
                var decree = Journal.Single();
                check(_payments == 1 && _balance == 2000 - cost && decree.InfluenceCost == cost && decree.Wartime == wartime,
                    "The whole realm pays once at the current mode's price");
                check(decree.Complete && decree.SettledFeudIds.Count == 2 && Settlements.SequenceEqual(new[] { "one", "two" })
                    && decree.StartedFeudId == null && !_service.IsExecutingRealmPeace(_realm), "Each captured feud settles once with cleared in-flight receipt");
                check(Penalties.Count == 3 && Penalties[Leaders[claimant]].SequenceEqual(new[] { -15 * scale })
                    && Penalties[Leaders[holder]].SequenceEqual(new[] { -15 * scale })
                    && Penalties[Leaders[bystander]].SequenceEqual(new[] { -5 * scale }) && decree.PenalizedClanIds.Count == 3,
                    "Real penalty dispatcher applies each house once, including shell houses, excluding Crown, mercenaries, minor and dead clans");
                check(decree.PlayerInvolved && _notices == 1, "Realm journal captures player involvement and dispatches one aggregate notice");
                // Repeating the penalty stage after a save must honor its per-house receipts.
                Call(_service, "ApplyRelationPenalties", decree); Resume(); Resume();
                check(!Execute() && _payments == 1 && Settlements.Count == 2 && Penalties.Values.All(p => p.Count == 1) && _notices == 1,
                    "Duplicate execution and hourly retries cannot repay, resettle, repenalize or redispatch");

                var store = new Store(); _service.SyncData(store);
                check(store.Data.ContainsKey("BC_RoyalPeaceUntilByRealm") && store.Data.ContainsKey("BellumCivile_TyrantsDebtUntilByClanId")
                    && store.Data.ContainsKey("BC_RoyalPeaceDecrees"), "Realm expiry, clan debt and settlement journal use separate saved keys");
                store.IsLoading = true; var loaded = new RealmPeaceEnforcementBehavior(); loaded.SyncData(store);
                manager.Items.Remove(_service); _service = loaded; manager.Items.Add(loaded);
                check(Journal.Single() != decree && Journal.Single().PenalizedClanIds.Count == 3 && Journal.Single().Complete,
                    "Saved journal retains independent conflict and relation receipts");
                Resume(); check(_payments == 1 && Penalties.Values.All(p => p.Count == 1), "Loaded completed decree does not replay payment or penalties");
                int year = Math.Max(1, CampaignTime.DaysInYear);
                check(_service.GetRoyalPeaceRemainingDays(_realm) == year && _service.GetTyrantsDebtRemainingDays(_payer) == year,
                    "Both timers last one campaign year");
                _ruler = bystander; _home = _other;
                check(_service.HasRoyalPeace(_realm) && !_service.HasRoyalPeace(_other)
                    && _service.HasTyrantsDebt(_payer) && !_service.HasTyrantsDebt(_ruler), "Realm peace survives ruler or clan migration while debt stays with the paying house");
                check(_service.GetRoyalPeaceBlockReason(_realm) != null && _service.GetRoyalPeaceBlockReason(_other) == null,
                    "Only the protected realm supplies a conflict block reason");
                _day = 10 + year - .25f;
                check(_service.GetRoyalPeaceRemainingDays(_realm) == 1, "A fractional final day rounds up");
                _day = 10 + year;
                check(!_service.HasRoyalPeace(_realm) && _service.GetRoyalPeaceBlockReason(_realm) == null
                    && !_service.HasTyrantsDebt(_payer), "Realm ban and debt expire at their saved deadline");
                _day++; Resume(); check(Journal.Count == 0, "Expired completed journals are pruned without new side effects");
            }

            Reset(); AddFeud("first", _realm); AddFeud("deferred", _realm); _deferId = "deferred";
            check(Execute() && !Journal.Single().Complete && Journal.Single().StartedFeudId == null && _payments == 1,
                "A cleanly deferred settlement keeps its paid journal without an interrupted receipt");
            _deferId = null; Resume(); Resume();
            check(Journal.Single().Complete && _payments == 1 && Settlements.SequenceEqual(new[] { "first", "deferred" })
                && Penalties.Values.All(p => p.Count == 1), "Resuming settles only outstanding feuds and applies penalties once");

            Reset(); AddFeud("interrupted", _realm); _throwSuppression = true;
            check(Execute() && Journal.Single().Interrupted && Journal.Single().StartedFeudId == "interrupted"
                && !_service.IsExecutingRealmPeace(_realm), "Throwing settlement preserves the in-flight receipt and clears the execution guard");
            check((bool)Call(_service, "OwnsUnfinishedFeudSettlement", "interrupted")
                && !(bool)Call(_service, "OwnsUnfinishedFeudSettlement", "other")
                && !(bool)Call(_service, "OwnsUnfinishedFeudSettlement", new object[] { null }),
                "An interrupted in-flight receipt owns only its named feud for orphan-repair exclusion");
            Resume(); Resume();
            check(_attempts == 1 && _payments == 1 && Penalties.Count == 0 && _notices == 1,
                "Interrupted journals do not replay settlement or charge and dispatch an interruption notice");
            _day += Math.Max(1, CampaignTime.DaysInYear);
            check(Preview().IsEnabled, "An interrupted journal does not permanently block a new decree after realm peace expires");

            Reset(); AddFeud("saved_inflight", _realm);
            Journal.Add(new RoyalPeaceDecreeRecord { Id = "loaded", Realm = _realm, Enforcer = _payer,
                StartedFeudId = "saved_inflight", FeudIds = new List<string> { "saved_inflight" } });
            Resume();
            check(Journal.Single().Interrupted && _attempts == 0 && _payments == 0,
                "A loaded in-flight journal is interrupted rather than replayed");

            // Exercise the real publisher while replacing only the native notice sink.
            h.Unpatch(AccessTools.Method(type, "PublishDecree"), HarmonyPatchType.Prefix, h.Id);
            manager.Items.Add(new ConflictOutcomeBehavior());
            Patch(AccessTools.Method(typeof(ConflictOutcomeBehavior), "Begin"), nameof(BeginNotice));
            Patch(AccessTools.Method(typeof(ConflictOutcomeBehavior), "Publish"), nameof(DeliverNotice));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(CampaignTime), "ToString", Type.EmptyTypes), nameof(DateText));
            Reset(); AddFeud("notice_retry", _realm); _notice = null; NoticeIds.Clear();
            _throwNotice = true; _noticeReady = false;
            check(Execute() && Journal.Single().Complete && !Journal.Single().Interrupted && !Journal.Single().NoticePublished,
                "Presentation failure does not mark a completed settlement interrupted or latch its notice");
            _throwNotice = false; Resume();
            check(!Journal.Single().NoticePublished && _notices == 0, "A not-ready notice remains eligible for hourly presentation retry");
            _noticeReady = true; Resume(); Resume();
            check(Journal.Single().NoticePublished && _notices == 1 && _payments == 1 && Settlements.Count == 1
                && Penalties.Values.All(p => p.Count == 1) && NoticeIds.Count == 3 && NoticeIds.Distinct().Count() == 1,
                "Hourly presentation retries reuse the persisted notice identity without repeating settlement, payment or penalties");
            check(!(bool)Call(_service, "OwnsUnfinishedFeudSettlement", "notice_retry"), "Completed settlement releases orphan-repair ownership");

            Reset(); var inactive = AddFeud("finished", _realm); inactive.SetState(ClaimFeudState.SuppressedCooldown);
            check(!Preview().IsEnabled && !_service.GetClaimFeudPreview(inactive, _ruler).IsEnabled, "Finished feuds cannot create another decree");
            var legacy = new Store { IsLoading = true };
            legacy.Data["BellumCivile_TyrantsDebtUntilByClanId"] = new Dictionary<string, float> { [_payer.StringId] = 20 };
            _service.SyncData(legacy);
            check(_service.HasTyrantsDebt(_payer) && !_service.HasRoyalPeace(_realm) && Journal.Count == 0,
                "Legacy clan debt loads without inventing a realm ban or decree");
            check(!_service.HasRoyalPeace(null) && _service.GetRoyalPeaceRemainingDays(null) == 0, "Missing realms have no peace timer");
            var fields = typeof(RoyalPeaceDecreeRecord).GetFields(BindingFlags.Public | BindingFlags.Instance);
            check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), "Every journal receipt field is saved");
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value));
            check(ids.Distinct().Count() == fields.Length, "Journal save IDs remain unique");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
            Realms.Clear(); Houses.Clear(); RealmHouses.Clear(); Leaders.Clear(); Penalties.Clear(); Settlements.Clear();
        }
    }
}
