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
using TaleWorlds.Library;

internal static class CivilWarRealmIdentityTests
{
    private sealed class Behaviors : ICampaignBehaviorManager
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

    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>();
    private static readonly HashSet<Kingdom> Eliminated = new HashSet<Kingdom>();
    private static bool _atWar;
    private static int _defeatPasses, _peacePasses;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    private static object Call(object obj, string method, params object[] args) => AccessTools.Method(obj.GetType(), method).Invoke(obj, args);
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool Dead(Kingdom __instance, ref bool __result) { __result = Eliminated.Contains(__instance); return false; }
    private static bool War(ref bool __result) { __result = _atWar; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool Defeat(ref bool __result) { _defeatPasses++; __result = true; return false; }
    private static bool Peace() { _peacePasses++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var h = new Harmony("bellum.tests.civil_war_realm_identity");
        var mod = typeof(FactionObject).Assembly;
        void Patch(MethodBase method, string name) => h.Patch(method,
            prefix: new HarmonyMethod(typeof(CivilWarRealmIdentityTests), name));
        var campaign = Blank<Campaign>();
        var behaviors = new Behaviors();
        var manager = new FactionManagerBehavior();
        var resolver = new CivilWarResolutionBehavior();
        behaviors.AddBehavior(manager); behaviors.AddBehavior(resolver);
        campaign.AddCampaignBehaviorManager(behaviors);
        var factions = (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(manager);
        Clan House(string id) { var c = Blank<Clan>(); c.StringId = id; return c; }
        Kingdom RealmObject(string id, Clan ruler)
        {
            var k = Blank<Kingdom>(); k.StringId = id;
            Kingdoms.Add(k); Rulers[k] = ruler; return k;
        }
        FactionObject Faction(Kingdom parent, Clan leader)
        {
            var f = Blank<FactionObject>();
            Set(f, "_name", leader.StringId); Set(f, "_parentKingdom", parent);
            Set(f, "_leader", leader); Set(f, "_members", new List<Clan> { leader });
            factions.Add(f); return f;
        }
        void OldReference(FactionObject f, Kingdom k, bool started = false)
        {
            Set(f, "_rebelKingdom", k); Set(f, "_rebelKingdomStringId", k.StringId);
            Set(f, "_civilWarOriginShellPrefix", null); Set(f, "_rebellionCreationStarted", started);
        }
        Kingdom Resolve(FactionObject f, Kingdom preferred, bool eliminated = false) =>
            (Kingdom)AccessTools.Method(typeof(CivilWarResolutionBehavior), "ResolveTrackedRebelKingdom")
                .Invoke(null, new object[] { f, preferred, eliminated });
        bool Matches(FactionObject f, string id) => (bool)Call(f, "MatchesRebelKingdomId", id);

        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Dead));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(War));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileLogger", true), "Log", new[] { typeof(string) }), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.CivilWarTransitionDiagnostics", true), "Log"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "QueueSettlementRefresh"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "RepairReferencedKingdomRulerState"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.NobleClanEligibilityHelper", true), "IsLiveNobleClan"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ResolveLiegeVictoryInternal"), nameof(Defeat));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ResolveWhitePeace"), nameof(Peace));
            _atWar = true; _defeatPasses = _peacePasses = 0;

            var crown = House("crown"); var founder = House("coalition"); var member = House("town_S2_rebel_clan");
            var parent = RealmObject("empire", crown);
            var shell = RealmObject("empire_rebels_coalition", founder);
            Realms[crown] = parent; Realms[founder] = Realms[member] = shell;
            var coalition = Faction(parent, founder); coalition.SetRebelKingdom(shell); coalition.Members.Add(member);
            var secessionists = Faction(parent, member);
            check(secessionists.GetRebelKingdom() == null && !secessionists.TryBackfillRebelKingdomFromLeaderKingdom(),
                "An unrelated faction cannot claim the coalition its leader merely joined");
            check(manager.GetFactionByRebelKingdom(shell) == coalition && Resolve(secessionists, shell) == null,
                "Faction lookup and settlement both retain the true coalition owner");

            Call(manager, "OnClanChangedKingdom", member, parent, shell, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
            check(!factions.Contains(secessionists) && coalition.Members.Contains(member) && coalition.IsTrackedRebelKingdom(shell),
                "Joining another rebellion removes old political membership but preserves the actual coalition");
            Call(manager, "OnClanChangedKingdom", member, shell, parent, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
            check(coalition.Members.Contains(member), "Returning from the correct rebel realm preserves its settlement roster");

            secessionists = Faction(parent, member); OldReference(secessionists, shell);
            check(secessionists.HasTrackedRebelKingdom && secessionists.GetRebelKingdom() == null
                && secessionists.GetTrackedRebelKingdomIncludingEliminated() == null
                && !secessionists.IsTrackedRebelKingdom(shell) && !secessionists.IsTrackedRebelKingdomId(shell.StringId),
                "Invalid legacy backfill is rejected by live, terminal, and id-based lookups");
            Call(manager, "ReconcileTrackedRebelKingdoms");
            check(!secessionists.HasTrackedRebelKingdom && !factions.Contains(secessionists) && coalition.GetRebelKingdom() == shell,
                "Daily recovery discards the false association without disturbing the real war");

            // Reproduce the log sequence: the real coalition settles, then the other
            // faction tries to resolve that same eliminated shell as a loyalist victory.
            manager.RemoveFaction(coalition); Eliminated.Add(shell); Realms[member] = Realms[founder] = parent;
            secessionists = Faction(parent, member); OldReference(secessionists, shell);
            resolver.ResolveUnscriptedPeace(secessionists, null);
            resolver.ResolveUnscriptedPeace(secessionists, shell);
            resolver.ResolveUnscriptedPeace(coalition, shell);
            check(_defeatPasses == 0 && _peacePasses == 0 && !secessionists.HasTrackedRebelKingdom,
                "Settled coalition cannot trigger a second defeat, tribunal, reward, or peace pass through a stale faction");
            check(((Dictionary<Clan, CampaignTime>)AccessTools.Field(typeof(FactionManagerBehavior), "_pacifiedClans")
                .GetValue(manager)).Count == 0, "Discarding a false war does not pacify unrelated political factions");

            var legacyLeader = House("legacy"); var legacy = Faction(parent, legacyLeader);
            var legacyShell = RealmObject("empire_rebels_legacy_2", legacyLeader); Realms[legacyLeader] = legacyShell;
            check(legacy.GetRebelKingdom() == null && legacy.TryBackfillRebelKingdomFromLeaderKingdom()
                && legacy.GetRebelKingdom() == legacyShell, "Exact legacy identity with numeric collision suffix can be recovered explicitly");
            var replacement = House("replacement"); Realms[replacement] = legacyShell;
            legacy.Leader = replacement; Rulers[legacyShell] = replacement;
            check(legacy.GetRebelKingdom() == legacyShell, "Recovered shell identity survives replacement of its founding clan");
            var successor = RealmObject("successor", crown);
            check((bool)Call(legacy, "RetargetCivilWarParent", parent, successor) && legacy.GetRebelKingdom() == legacyShell
                && Matches(legacy, legacyShell.StringId), "Crown transfer keeps the original rebel identity against a new parent realm");
            Eliminated.Add(legacyShell); Realms[replacement] = successor;
            check(legacy.GetRebelKingdom() == null && legacy.GetTrackedRebelKingdomIncludingEliminated() == legacyShell
                && Resolve(legacy, successor, true) == legacyShell, "Eliminated true shell remains recoverable without adopting the leader's new realm");
            resolver.ResolveUnscriptedPeace(legacy, null);
            check(_defeatPasses == 1, "Genuine eliminated rebel shell still reaches the normal terminal settlement path");

            var original = House("original"); var changed = Faction(parent, original);
            var originalShell = RealmObject("empire_rebels_original", original);
            OldReference(changed, originalShell, started: true); changed.Leader = replacement;
            check(changed.GetRebelKingdom() == originalShell, "Old creation receipts preserve genuine wars after leadership changes");
            Set(changed, "_rebelKingdom", null);
            check(changed.GetRebelKingdom() == originalShell, "Id-only saved references recover their original realm");
            Set(changed, "_rebelKingdomStringId", shell.StringId);
            check(changed.GetRebelKingdom() == null, "Conflicting saved reference and string id are not resolved as a war");
            OldReference(changed, originalShell, started: true); Set(changed, "_rebellionCreationCompleted", true);
            Eliminated.Add(originalShell);
            var newerShell = RealmObject("empire_rebels_replacement", replacement); Realms[replacement] = newerShell;
            var reconciliation = new object[] { changed, null, null };
            check(!(bool)AccessTools.Method(typeof(FactionManagerBehavior), "TryReconcileRebelKingdom")
                .Invoke(manager, reconciliation) && changed.GetTrackedRebelKingdomIncludingEliminated() == originalShell,
                "Daily recovery cannot replace an eliminated saved shell with the leader's newer rebellion");

            var freshLeader = House("fresh"); var fresh = Faction(parent, freshLeader);
            var freshShell = RealmObject("empire_rebels_fresh", freshLeader); Realms[freshLeader] = freshShell;
            _atWar = false;
            check(!fresh.TryBackfillRebelKingdomFromLeaderKingdom(), "Matching identity at peace cannot start a recovered war");
            _atWar = true; Rulers[freshShell] = founder;
            check(!fresh.TryBackfillRebelKingdomFromLeaderKingdom(), "Being a member is not enough to backfill a rebel realm");
            Rulers[freshShell] = freshLeader; Eliminated.Add(freshShell);
            check(!fresh.TryBackfillRebelKingdomFromLeaderKingdom(), "Destroyed untracked realm cannot be newly adopted");
            Eliminated.Remove(freshShell);
            check(Matches(fresh, "empire_rebels_fresh") && Matches(fresh, "empire_rebels_fresh_12")
                && !Matches(fresh, "empire_rebels_fresh_other") && !Matches(fresh, "empire_rebels_fresh_")
                && !Matches(fresh, "foreign_rebels_fresh"), "Legacy matching accepts only the exact identity or its numeric suffix");
            var owner = Faction(parent, freshLeader); owner.SetRebelKingdom(freshShell);
            check(!fresh.TryBackfillRebelKingdomFromLeaderKingdom() && manager.GetFactionByRebelKingdom(freshShell) == owner,
                "Another faction with the same leader cannot steal a realm already claimed by its owner");
            var adoption = new object[] { freshShell, null };
            check(!(bool)AccessTools.Method(typeof(FactionObject), "TryAdoptExistingRebelKingdom").Invoke(fresh, adoption),
                "Direct shell adoption also respects exclusive ownership");

            var challenge = Faction(parent, House("challenger")); Set(challenge, "_successionChallengeId", "contest_123");
            Set(challenge, "_type", FactionType.InstallRuler);
            check(Matches(challenge, "empire_rebels_challenge_contest_123")
                && Matches(challenge, "empire_rebels_challenge_contest_123_2")
                && !Matches(challenge, "empire_rebels_challenger"), "Succession challenge recovery uses its own saved challenge identity");
            var foreign = RealmObject("foreign", freshLeader); Realms[freshLeader] = foreign;
            fresh.ClearRebelKingdom();
            check(manager.GetFactionByRebelKingdom(foreign) == null && fresh.GetRebelKingdom() == null
                && Resolve(fresh, foreign) == null, "Foreign membership cannot become a rebellion through any fallback lookup");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
            Realms.Clear(); Rulers.Clear(); Kingdoms.Clear(); Eliminated.Clear();
        }
    }
}
