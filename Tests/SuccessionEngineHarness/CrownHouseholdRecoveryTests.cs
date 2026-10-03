using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;

internal static class CrownHouseholdRecoveryTests
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

    private static Hero _heir, _predecessor;
    private static Clan _current, _previous, _ruler;
    private static Kingdom _realm;
    private static int _finishes;
    private static bool Alive(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Age(ref float __result) { __result = 30; return false; }
    private static bool House(Hero __instance, ref Clan __result)
    { __result = __instance == _heir ? _current : _previous; return false; }
    private static bool Head(Clan __instance, ref Hero __result)
    { __result = __instance == _current ? _heir : _predecessor; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool ChangeRuler(Kingdom kingdom, Clan clan)
    { _ruler = clan; return false; }
    private static bool Incoming(CrownAccessionRecord record, ref bool __result)
    { __result = record.HeirHouse == _current && !record.IncomingHousePrepared; return false; }
    private static bool Finish(CrownAccessionRecord record, Clan house)
    { _finishes++; record.TitleTransferred = record.Completed = true; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var oldCampaign = Campaign.Current;
        var h = new Harmony("bellum.test.crown_household_recovery");
        void Patch(MethodBase method, string prefix) => h.Patch(method,
            prefix: new HarmonyMethod(typeof(CrownHouseholdRecoveryTests), prefix));
        var behavior = new CrownAccessionBehavior();
        var records = new List<CrownAccessionRecord>();
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior, records);
        _heir = Blank<Hero>(); _heir.StringId = "deven";
        _predecessor = Blank<Hero>(); _predecessor.StringId = "predecessor";
        var spouse = Blank<Hero>(); spouse.StringId = "planned_spouse";
        var unrelated = Blank<Hero>(); unrelated.StringId = "future_heir";
        _previous = Blank<Clan>(); _previous.StringId = "original_house";
        var receiving = Blank<Clan>(); receiving.StringId = "married_house";
        _current = receiving;
        _realm = Blank<Kingdom>(); _realm.StringId = "crown_realm";
        _ruler = _previous;
        var campaign = Blank<Campaign>();
        var manager = new Manager(); manager.AddBehavior(behavior);
        var reconcile = AccessTools.Method(typeof(CrownAccessionBehavior), "ReconcileCrownHousehold");
        bool Recover(CrownAccessionRecord r) => (bool)reconcile.Invoke(behavior, new object[] { r });
        bool Reserved(Hero hero) => (bool)AccessTools.Method(typeof(CrownAccessionBehavior), "IsHouseholdReservedForMarriage")
            .Invoke(behavior, new object[] { hero });
        CrownAccessionRecord Pending() => new CrownAccessionRecord
        {
            Realm = _realm, PreviousHouse = _previous, Predecessor = _predecessor, Heir = _heir,
            HeirHouse = _previous, ForcedAbdication = true, ForcedPrepared = true,
            SettlementRulingHouse = _previous, ForcedCauseId = "original_abdication",
            IncomingHousePrepared = true, IncomingSourceHouse = _previous,
            IncomingSourceHead = _predecessor, IncomingSourceRealm = _realm,
            CreatesCadet = true, CadetId = "unexecuted_plan", CadetName = "Old name",
            Household = new List<string> { _heir.StringId, spouse.StringId },
            EndowmentFiefs = new List<string> { "old_fief" }, EndowmentTitles = new List<string> { "old_title" },
            EndowmentGold = 300, HasLivingEndowment = true, RequiresRegency = true
        };
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));

            var record = Pending(); records.Add(record);
            check(Recover(record), "Unexecuted Crown household recovers after an heir marries away");
            check(record.Heir == _heir && record.Predecessor == _predecessor && record.Realm == _realm
                && record.ForcedCauseId == "original_abdication", "Recovery preserves the lawful heir and original Crown cause");
            check(record.HeirHouse == receiving && !record.IncomingHousePrepared && !record.ForcedPrepared
                && record.IncomingSourceHouse == null && record.IncomingSourceHead == null
                && record.IncomingSourceRealm == null, "Recovery discards stale source-house preparation");
            check(!record.CreatesCadet && !record.HasLivingEndowment && !record.RequiresRegency
                && record.EndowmentGold == 0 && record.EndowmentFiefs.Count == 0
                && record.EndowmentTitles.Count == 0 && record.Household.Count == 0,
                "Unexecuted old-house endowment is not charged to the new household");
            string plan = record.CadetId;
            check(Recover(record) && record.CadetId == plan, "Recovery is idempotent on hourly retries");
            check(behavior.IsPendingCrownHeir(_heir), "Refreshing a plan does not falsely complete its Crown transfer");

            var waiting = Pending(); waiting.IncomingHousePrepared = false; waiting.CreatesCadet = false;
            check(Recover(waiting) && waiting.HeirHouse == receiving && !waiting.ForcedPrepared,
                "Deven's pre-household deferral recovers even before an incoming-house snapshot exists");
            var death = Pending(); death.ForcedAbdication = false;
            check(Recover(death) && !death.IsAbdication && death.Heir == _heir && death.HeirHouse == receiving,
                "Death accession refreshes the household without changing the lawful heir or cause");

            var unchanged = Pending(); _current = _previous;
            check(Recover(unchanged) && unchanged.IncomingHousePrepared && unchanged.EndowmentGold == 300,
                "Unchanged household retains its saved inheritance plan");
            _current = receiving;
            var cadet = Pending(); cadet.Cadet = receiving;
            check(Recover(cadet) && cadet.IncomingHousePrepared,
                "Expected movement into the journaled cadet house does not reset settlement");

            foreach (string receipt in new[] { "Cadet", "CadetInitialized", "CadetAnnounced", "EndowmentSettled",
                "DeliveredFiefs", "DeliveredTitles", "DeliveredGold", "GoldDebited", "GoldCredited", "GoldRecipient",
                "ForeignMovingClan", "ForeignMoveStarted", "ForeignMoveCompleted", "ForeignInfluenceRestored",
                "TitleTransferred", "OutcomeApplied", "Announced", "MandateStarted" })
            {
                var committed = Pending();
                var field = typeof(CrownAccessionRecord).GetField(receipt);
                object value = field.FieldType == typeof(bool) ? (object)true
                    : field.FieldType == typeof(Clan) ? _previous
                    : field.FieldType == typeof(Hero) ? _predecessor
                    : field.FieldType == typeof(int) ? (object)50 : new List<string> { "receipt" };
                field.SetValue(committed, value);
                check(!Recover(committed) && committed.HeirHouse == _previous
                    && committed.EndowmentGold == 300 && Equals(field.GetValue(committed), value),
                    "Recovery preserves committed receipt: " + receipt);
            }
            var union = Pending(); union.Union = new RealmUnionRecord();
            check(Recover(union) && union.HeirHouse == _previous, "Realm union recovery retains ownership of its own journal");
            var elected = Pending(); elected.ElectiveElection = true;
            check(Recover(elected) && elected.HeirHouse == _previous, "Elective candidate validation retains its own recovery path");

            record.Household.Add(spouse.StringId);
            check(Reserved(_heir) && Reserved(spouse) && !Reserved(unrelated) && !Reserved(null),
                "Only the pending heir and journaled household are reserved for marriage");
            var model = new BellumMarriageModel();
            check(!model.IsSuitableForMarriage(_heir) && !model.IsCoupleSuitableForMarriage(_heir, unrelated)
                && !model.IsCoupleSuitableForMarriage(unrelated, _heir),
                "Shared marriage model rejects either reserved spouse before native evaluation");
            var helper = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
            var participant = AccessTools.Method(helper, "GetOfferParticipantRejectionReason");
            check(participant.Invoke(null, new object[] { _heir, true, true }).ToString() == "TemporarilyUnavailable",
                "Army/recovery prospect scoring cannot bypass the Crown household reservation");
            record.Emergency = true;
            check(!Reserved(_heir) && !Reserved(spouse), "Emergency ballot does not reserve a stale incoming household");
            record.Emergency = false; record.Completed = true;
            check(!Reserved(_heir) && !Reserved(spouse), "Completed Crown settlement releases marriage restrictions");
            Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsSuitableForMarriage"), nameof(Alive));
            Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage"), nameof(Alive));
            check(model.IsSuitableForMarriage(_heir) && model.IsCoupleSuitableForMarriage(_heir, unrelated),
                "Completed Crown settlement returns ordinary marriage decisions to the native model");
            record.Completed = false;
            var prospectType = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.MarriageProspectEvaluation");
            using ((IDisposable)Activator.CreateInstance(prospectType, true))
                check(!model.IsSuitableForMarriage(_heir), "Temporary prospect evaluation cannot bypass the marriage-model guard");

            // Run the real hourly retry and abdication validation. Stub only the
            // native world-transfer endpoints that require a running campaign.
            record = Pending(); records.Clear(); records.Add(record); _finishes = 0;
            var partition = new PartitionSuccessionBehavior();
            var estate = new CrossClanEstateRecord { Source = receiving, Deceased = _predecessor };
            var share = new CrossClanEstateShare { Heir = _heir, Fiefs = new List<string> { "already_delivered" },
                DeliveredFiefs = new List<string> { "already_delivered" } };
            var titles = Blank<FeudalTitleBehavior>();
            var settle = AccessTools.Method(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare");
            void Settle() => settle.Invoke(partition, new object[] { estate, share, titles, null });
            Settle();
            check(!share.Completed && share.Status == "awaiting Crown household preparation",
                "Ordinary estate delivery reproduces the pending Crown household blockage");
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "PrepareIncomingCrownHouse"), nameof(Incoming));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "PrepareForeignCrownClan"), nameof(Alive));
            var succession = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.HereditaryRealmSuccession");
            Patch(AccessTools.Method(succession, "CanConsiderClan"), nameof(Alive));
            Patch(AccessTools.Method(typeof(ChangeRulingClanAction), "Apply"), nameof(ChangeRuler));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "Finish"), nameof(Finish));
            var tick = AccessTools.Method(typeof(CrownAccessionBehavior), "ResumePending");
            tick.Invoke(behavior, null);
            check(record.Completed && _finishes == 1 && _ruler == receiving,
                "Hourly retry refreshes the old save and advances the same heir's Crown settlement");
            check(!behavior.IsPendingCrownHeir(_heir) && !Reserved(_heir),
                "Recovered accession releases the estate wait and marriage reservation");
            Settle(); Settle();
            check(share.Completed && share.GoldPayment.GoldCredited && share.DeliveredFiefs.Count == 1,
                "Ordinary estate retry completes after Crown recovery without duplicating an existing delivery");
            tick.Invoke(behavior, null);
            check(_finishes == 1, "Later hourly retry does not replay the repaired Crown settlement");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
