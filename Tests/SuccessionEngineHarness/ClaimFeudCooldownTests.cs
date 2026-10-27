using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
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
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static T Field<T>(object o, string name) => (T)AccessTools.Field(o.GetType(), name).GetValue(o);
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static List<ClaimFeudRecord> Records => Field<List<ClaimFeudRecord>>(_feuds, "_feuds");
    private static Dictionary<string, float> PeaceTimers => Field<Dictionary<string, float>>(_peace, "_royalPeaceUntilByRealmId");
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _crown; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Houses); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(new[] { _realm }); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test realm"); return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Year(ref int __result) { __result = 84; return false; }
    private static bool Influence(ref float __result) { __result = _influence; return false; }
    private static bool TitleName(ref string __result) { __result = "Test county"; return false; }
    private static bool Defiance(ref float __5, ref float __6, ref bool __result)
    { __5 = __6 = 100; __result = false; return false; }
    private static bool Spend(ref bool __result) { _spends++; __result = false; return false; }

    private static Clan House(string id)
    {
        var clan = Blank<Clan>(); clan.StringId = id;
        var hero = Blank<Hero>(); hero.StringId = id + "_head";
        Heads[clan] = hero; Houses.Add(clan); return clan;
    }
    private static void Reset(FeudalClaimStrength strength = FeudalClaimStrength.Strong)
    {
        Houses.Clear(); Heads.Clear(); _manager.Items.Clear(); _day = 20; _influence = 1000; _spends = 0;
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
        }
        finally { h.UnpatchAll(h.Id); _campaign = null; }
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
