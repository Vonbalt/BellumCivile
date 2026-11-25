using System;
using System.Collections;
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

internal static class WarTargetPerformanceTests
{
    private sealed class EmptyCampaignBehaviors : ICampaignBehaviorManager
    {
        public T GetBehavior<T>() => default;
        public IEnumerable<T> GetBehaviors<T>() => Enumerable.Empty<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) { }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly Dictionary<Clan, Kingdom> Homes = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<string, Clan> Houses = new Dictionary<string, Clan>();
    private static FeudalTitleBehavior _titles;
    private static IReadOnlyCollection<FeudalTitleRecord> _allTitles;
    private static Clan _source;
    private static Hero _leader;
    private static Type _motiveType;
    private static bool _legacy;
    private static float _day;
    private static int _registryRequests, _claimRequests, _heldTitleRequests, _legacyVisits;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Homes.Keys.ToList()); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms.AsEnumerable().Reverse().ToList()); return false; }
    private static bool Home(Clan __instance, ref Kingdom __result) { __result = Homes[__instance]; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { __result = Rulers[__instance]; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool Enemies(ref MBReadOnlyList<IFaction> __result) { __result = new MBReadOnlyList<IFaction>(new List<IFaction>()); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool IsAtWar(IFaction __0, ref bool __result) { __result = __0?.StringId == "realm_09"; return false; }
    private static bool Neutral(ref float __result) { __result = 0f; return false; }
    private static bool Strength(ref float __result) { __result = 100f; return false; }
    private static bool RealmStrength(Kingdom __instance, ref float __result)
    { __result = __instance.StringId == "realm_07" || __instance.StringId == "realm_08" ? 70f : 100f; return false; }
    private static bool CurrentDay(ref float __result) { __result = _day; return false; }
    private static bool Time(ref CampaignTime __result) { __result = default; return false; }
    private static bool Days(ref double __result) { __result = _day; return false; }
    private static bool Land(string targetKey, ref bool __result) { __result = targetKey.StartsWith("realm:realm_07", StringComparison.Ordinal); return false; }
    private static bool Sea(string targetKey, ref bool __result) { __result = targetKey.StartsWith("realm:realm_08", StringComparison.Ordinal); return false; }
    private static void CountRegistry() => _registryRequests++;
    private static void CountClaims() => _claimRequests++;
    private static void CountHeldTitles() => _heldTitleRequests++;

    // Reference implementation: the former per-realm full scan, injected into the otherwise unchanged ranker.
    private static bool LegacyClaimScore(Kingdom targetKingdom, List<string> reasons, IList motives, ref float __result)
    {
        if (!_legacy) return true;
        var seen = new HashSet<string>();
        float score = 0f;
        bool TargetControls(FeudalTitleRecord title) => title?.IsActive == true
            && Houses.TryGetValue(title.DeFactoHolderClanId, out Clan holder) && holder.Kingdom == targetKingdom;
        void Add(FeudalTitleRecord title, float value, string type, string reason)
        {
            score += value;
            reasons.Add(reason);
            object motive = Activator.CreateInstance(_motiveType, nonPublic: true);
            var motiveType = _motiveType.GetProperty("Type");
            motiveType.SetValue(motive, Enum.Parse(motiveType.PropertyType, type));
            _motiveType.GetProperty("Score").SetValue(motive, value);
            _motiveType.GetProperty("Subject").SetValue(motive, new TextObject(title.Name ?? string.Empty));
            motives.Add(motive);
        }
        foreach (var claim in _titles.GetActiveClaimsByClan(_source))
        {
            var title = _titles.GetTitle(claim.TargetTitleId);
            if (!TargetControls(title) || !seen.Add(title.TitleId)) continue;
            bool strong = claim.Strength == FeudalClaimStrength.Strong;
            float value = strong ? 45f : 25f;
            Add(title, value, strong ? "StrongClaim" : "WeakClaim", $"{claim.Strength.ToString().ToLowerInvariant()} claim to {title.Name} +{value:0}");
        }
        foreach (var title in _allTitles)
        {
            _legacyVisits++;
            if (title?.IsActive == true && title.DeJureHolderClanId == _source.StringId
                && title.DeFactoHolderClanId != _source.StringId && TargetControls(title) && seen.Add(title.TitleId))
                Add(title, 35f, "DeJureReclamation", $"legal title occupied: {title.Name} +{35f:0}");
        }
        __result = Math.Min(100f, score);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.war_target_performance");
        var mod = typeof(FeudalTitleBehavior).Assembly;
        var scorer = mod.GetType("BellumCivile.WarPeace.WarTargetScoringService", throwOnError: true);
        var scoreType = mod.GetType("BellumCivile.WarPeace.WarTargetScore", throwOnError: true);
        _motiveType = mod.GetType("BellumCivile.WarPeace.WarTargetMotive", throwOnError: true);
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(WarTargetPerformanceTests), prefix));
        void Getter(Type type, string name, string prefix) => Patch(AccessTools.PropertyGetter(type, name), prefix);
        void Count(string target, string prefix) => Patch(AccessTools.Method(typeof(FeudalTitleBehavior), target), prefix);
        var setTitles = AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance");
        var setClients = AccessTools.PropertySetter(typeof(ClientKingdomBehavior), "Instance");
        var setCampaign = AccessTools.PropertySetter(typeof(Campaign), "Current");
        var previousTitles = FeudalTitleBehavior.Instance;
        var previousClients = ClientKingdomBehavior.Instance;
        var previousCampaign = Campaign.Current;
        _day = 42f;
        _leader = Blank<Hero>();
        _titles = new FeudalTitleBehavior();
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(_titles);
        var claims = (List<FeudalClaimRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_claims").GetValue(_titles);
        void Reindex() => AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes").Invoke(_titles, new object[] { true, false });
        Clan House(string id, Kingdom realm)
        {
            var clan = Blank<Clan>();
            clan.StringId = id;
            Homes.Add(clan, realm);
            Houses.Add(id, clan);
            return clan;
        }
        FeudalTitleRecord Title(string id, Clan legal, string holder, bool active = true)
        {
            var title = new FeudalTitleRecord(id, id, FeudalTitleType.Barony, legal?.StringId, holder, "", "", Realms[0].StringId, 0, 0, active);
            registry.Add(id, title);
            return title;
        }
        void Claim(string id, string title, FeudalClaimStrength strength, float expiry = -1f)
            => claims.Add(new FeudalClaimRecord(id, _source.StringId, title, strength, "fixture", "", "", 0, expiry));
        object Read(object score, string key) => scoreType.GetProperty(key).GetValue(score);
        Kingdom Target(object score) => (Kingdom)Read(score, "TargetKingdom");
        bool Flag(object score, string key) => (bool)Read(score, key);
        string[] Reasons(object score) => ((IEnumerable<string>)Read(score, "Reasons")).ToArray();
        string Signature(object score)
        {
            var motives = ((IEnumerable)Read(score, "Motives")).Cast<object>().Select(note =>
                string.Join(":", new[] { "Type", "Score", "Subject" }.Select(key => _motiveType.GetProperty(key).GetValue(note)?.ToString())));
            return string.Join("|", new object[] { Target(score).StringId, Read(score, "Score"), Read(score, "DefensivePactPenaltyApplied"),
                Flag(score, "IsClaimTarget"), Flag(score, "IsActiveWar"), Flag(score, "IsLiberationTarget"), Flag(score, "IsNeighboringRealm"),
                Flag(score, "IsMaritimeNeighbor"), Flag(score, "IsLocalLandNeighbor"), Flag(score, "IsLocalMaritimeNeighbor"),
                string.Join(";", Reasons(score)), string.Join(";", motives) });
        }

        try
        {
            var campaign = Blank<Campaign>();
            campaign.AddCampaignBehaviorManager(new EmptyCampaignBehaviors());
            setCampaign.Invoke(null, new object[] { campaign });
            setTitles.Invoke(null, new object[] { _titles });
            setClients.Invoke(null, new object[] { null });
            Getter(typeof(CampaignTime), "Now", nameof(Time));
            Getter(typeof(CampaignTime), "ToDays", nameof(Days));
            Getter(typeof(Clan), "All", nameof(AllClans));
            Getter(typeof(Clan), "Kingdom", nameof(Home));
            Getter(typeof(Clan), "Leader", nameof(Leader));
            Getter(typeof(Kingdom), "All", nameof(AllRealms));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler));
            Getter(typeof(Kingdom), "Name", nameof(Name));
            Getter(typeof(Kingdom), "CurrentTotalStrength", nameof(RealmStrength));
            Getter(typeof(Kingdom), "FactionsAtWarWith", nameof(Enemies));
            Getter(typeof(Hero), "IsDead", nameof(No));
            Getter(typeof(FeudalTitleBehavior), "CurrentDay", nameof(CurrentDay));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(IsAtWar));
            Patch(AccessTools.Method(typeof(Campaign), "GetAverageDistanceBetweenClosestTwoTownsWithNavigationType"), nameof(Strength));
            Patch(AccessTools.Method(scorer, "IsNavalDlcLoaded"), nameof(Yes));
            Patch(AccessTools.Method(scorer, "HasLandFrontier"), nameof(Land));
            Patch(AccessTools.Method(scorer, "HasMaritimeRoute"), nameof(Sea));
            foreach (string name in new[] { "CalculateFrontierPredationWeight", "CalculateRelationScore", "CalculateDiplomaticPactScore", "CalculateCrossRealmTieScore" })
                Patch(AccessTools.Method(scorer, name), nameof(Neutral));
            Patch(AccessTools.Method(scorer, "CalculateClaimScore"), nameof(LegacyClaimScore));
            Count("GetAllTitles", nameof(CountRegistry));
            Count("GetActiveClaimsByClan", nameof(CountClaims));
            Count("GetTitlesHeldByClan", nameof(CountHeldTitles));

            for (int i = 0; i < 55; i++)
            {
                var realm = Blank<Kingdom>();
                realm.StringId = i == 54 ? "bc_feud_fixture" : $"realm_{i:00}";
                Realms.Add(realm);
                Rulers.Add(realm, House($"clan_{i:00}", realm));
            }
            _source = Rulers[Realms[0]];
            var controller = House("occupier", Realms[1]);
            string Holder(int i) => Rulers[Realms[i]].StringId;
            for (int i = 0; i < 3200; i++) Title($"other_{i}", Rulers[Realms[3]], Holder(3 + i % 51));
            var occupied = Title("occupied_before_claim", _source, controller.StringId);
            var contested = Title("distant_claim", _source, controller.StringId);
            Title("another_weak", null, controller.StringId);
            Title("also_occupied", _source, controller.StringId);
            Title("tie_a", null, Holder(4));
            Title("tie_b", null, Holder(5));
            Title("inactive", _source, Holder(2), active: false);
            Title("expired", null, Holder(2));
            Title("fief_already_held", _source, _source.StringId);
            Title("holder_missing", _source, "unknown");
            Title("landless_holder", _source, House("independent_holder", null).StringId);
            Title("feud_title", _source, Holder(54));
            Claim("first", "distant_claim", FeudalClaimStrength.Strong);
            Claim("duplicate", "distant_claim", FeudalClaimStrength.Weak);
            Claim("weak", "another_weak", FeudalClaimStrength.Weak);
            Claim("tie_a", "tie_a", FeudalClaimStrength.Weak);
            Claim("tie_b", "tie_b", FeudalClaimStrength.Weak);
            Claim("inactive", "inactive", FeudalClaimStrength.Strong);
            Claim("expired", "expired", FeudalClaimStrength.Strong, 20);
            Claim("missing", "not_found", FeudalClaimStrength.Strong);
            Reindex();
            _allTitles = _titles.GetAllTitles();

            object service = Activator.CreateInstance(scorer, nonPublic: true);
            var rank = AccessTools.Method(scorer, "RankTargets");
            object[] Rank(bool legacy)
            {
                _legacy = legacy;
                return ((IEnumerable)rank.Invoke(service, new object[] { _source })).Cast<object>().ToArray();
            }
            _registryRequests = _claimRequests = _heldTitleRequests = _legacyVisits = 0;
            var indexed = Rank(false);
            check(_registryRequests == 0 && _claimRequests == 1 && _heldTitleRequests == 1,
                "A complete ranking reads the source clan's claim and legal-holding indexes once each, without requesting all titles");
            var legacy = Rank(true);
            check(indexed.Select(Signature).SequenceEqual(legacy.Select(Signature)), "55-realm ranking preserves every score, reason, motive, flag and tie order");
            check(_legacyVisits == _allTitles.Count * 53, "Legacy scoring visits the entire registry once for every eligible target realm");
            Console.WriteLine($"Large-map fixture (55 realms, {_allTitles.Count} titles): full registry title visits {_legacyVisits} -> 0 per clan ranking.");
            object First(int i) => indexed.Single(score => Target(score) == Realms[i]);
            check(Flag(First(1), "IsClaimTarget") && !Flag(First(1), "IsNeighboringRealm") && (float)Read(First(1), "Score") == 100f
                && Reasons(First(1)).SequenceEqual(new[] { "strong claim to distant_claim +45", "weak claim to another_weak +25",
                    "legal title occupied: occupied_before_claim +35", "legal title occupied: also_occupied +35" }),
                "Distant claims, scoring cap, explicit claim precedence and registry order are preserved");
            var tied = indexed.Where(score => Target(score) == Realms[4] || Target(score) == Realms[5]).ToArray();
            check(tied.Length == 2 && Target(tied[0]) == Realms[4] && (float)Read(tied[0], "Score") == (float)Read(tied[1], "Score"),
                "Equal scores retain name-based ordering even when target enumeration is reversed");
            check(Flag(First(7), "IsNeighboringRealm") && Flag(First(8), "IsMaritimeNeighbor") && Flag(First(9), "IsActiveWar"),
                "Land frontier, maritime frontier and active war still qualify without claims");
            check(indexed.All(score => Target(score) != Realms[2] && Target(score) != Realms[54]),
                "Expired claims, inactive titles and temporary feud realms stay excluded");

            Homes[controller] = Realms[6];
            indexed = Rank(false);
            check(indexed.Select(Signature).SequenceEqual(Rank(true).Select(Signature))
                && indexed.All(score => Target(score) != Realms[1]) && Flag(First(6), "IsClaimTarget"),
                "A controller's defection moves the claims to its new realm on the next evaluation without a title rebuild");
            contested.SetDeFactoHolder(Holder(4));
            Reindex();
            indexed = Rank(false);
            check(indexed.Select(Signature).SequenceEqual(Rank(true).Select(Signature))
                && Reasons(First(4)).First() == "strong claim to distant_claim +45", "A fief transfer is reflected immediately in fresh rankings");
            occupied.SetDeJureHolder(controller.StringId);
            Reindex();
            indexed = Rank(false);
            check(indexed.Select(Signature).SequenceEqual(Rank(true).Select(Signature))
                && !Reasons(First(6)).Any(reason => reason.Contains("occupied_before_claim")), "Transferring legal ownership removes the old owner's reclamation motive");

            var clients = new ClientKingdomBehavior();
            var records = (List<ClientKingdomRecord>)AccessTools.Field(typeof(ClientKingdomBehavior), "_clients").GetValue(clients);
            records.Add(new ClientKingdomRecord(Realms[0].StringId, Realms[4].StringId, 0, false, 0));
            setClients.Invoke(null, new object[] { clients });
            var assessment = new ClientLibertyAssessment { ClientKingdom = Realms[0], SuzerainKingdom = Realms[4] };
            var preview = AccessTools.Method(scorer, "ScoreLiberationTarget");
            _legacy = false;
            object indexedPreview = preview.Invoke(service, new object[] { _source, assessment });
            _legacy = true;
            object legacyPreview = preview.Invoke(service, new object[] { _source, assessment });
            check(Flag(indexedPreview, "IsLiberationTarget") && Flag(indexedPreview, "IsClaimTarget")
                && Signature(indexedPreview) == Signature(legacyPreview), "The single-target liberation preview preserves its claims and other motives");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            setTitles.Invoke(null, new object[] { previousTitles });
            setClients.Invoke(null, new object[] { previousClients });
            setCampaign.Invoke(null, new object[] { previousCampaign });
            Realms.Clear(); Homes.Clear(); Rulers.Clear(); Houses.Clear();
            _titles = null; _allTitles = null; _source = null; _leader = null; _legacy = false;
        }
    }
}
