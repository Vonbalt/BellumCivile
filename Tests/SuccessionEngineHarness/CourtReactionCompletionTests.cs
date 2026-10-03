using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;

internal static class CourtReactionCompletionTests
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
    private static Clan _ruling;
    private static bool _legal, _blocked;
    private static IdeologyEventShockBehavior _shocks;
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static bool Legal(ref bool __result) { __result = _legal; return false; }
    private static bool Ruling(ref Clan __result) { __result = _ruling; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool House(Hero __instance, ref Clan __result) { Houses.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !Dead.Contains(__instance); return false; }
    private static bool IsDead(Hero __instance, ref bool __result) { __result = Dead.Contains(__instance); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Kill(Hero __0, Hero __1)
    {
        if (!_blocked)
        {
            Dead.Add(__0);
            AccessTools.Method(typeof(IdeologyEventShockBehavior), "OnHeroKilled").Invoke(_shocks,
                new object[] { __0, __1, KillCharacterAction.KillCharacterActionDetail.Executed, true });
        }
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var oldCampaign = Campaign.Current;
        var h = new Harmony("bellum.test.reaction_completion");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtReactionCompletionTests), name));
        var realm = Blank<Kingdom>(); realm.StringId = "reaction_realm";
        var royal = Blank<Clan>(); royal.StringId = "royal";
        var rival = Blank<Clan>(); rival.StringId = "rival";
        var ruler = Blank<Hero>(); ruler.StringId = "ruler";
        var opponent = Blank<Hero>(); opponent.StringId = "opponent";
        Houses[ruler] = royal; Houses[opponent] = rival;
        Realms[royal] = Realms[rival] = realm;
        _ruling = royal; _legal = true; _blocked = false;
        var manager = new FactionManagerBehavior();
        var factions = (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(manager);
        FactionObject Faction(FactionType bloc, Clan voter)
        {
            var f = Blank<FactionObject>();
            AccessTools.Field(typeof(FactionObject), "_type").SetValue(f, bloc);
            AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(f, realm);
            AccessTools.Field(typeof(FactionObject), "_members").SetValue(f, new List<Clan> { voter });
            factions.Add(f); return f;
        }
        var nobility = Faction(FactionType.Nobility, royal);
        var glory = Faction(FactionType.Glory, rival);
        var liberty = Faction(FactionType.Liberty, Blank<Clan>());
        _shocks = new IdeologyEventShockBehavior();
        var behaviors = new Behaviors(); behaviors.AddBehavior(manager); behaviors.AddBehavior(_shocks);
        var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(behaviors);
        var type = typeof(IdeologyEventShockBehavior);
        bool Call(string name, params object[] args) => (bool)AccessTools.Method(type, name).Invoke(_shocks, args);
        var won = (Dictionary<string, CampaignTime>)AccessTools.Field(type, "_recentCandidateWon").GetValue(_shocks);
        var lost = (Dictionary<string, CampaignTime>)AccessTools.Field(type, "_recentCandidateLost").GetValue(_shocks);
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.Method(type, "IsCompletedLegalAccession"), nameof(Legal));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruling));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(IsDead));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(KillCharacterAction), "ApplyByExecution"), nameof(Kill));

            var ballot = new ElectiveSuccessionRecord { Realm = realm, Winner = ruler, Frozen = true };
            ballot.Votes.Add(new ElectiveCommitment { Clan = royal, Supported = ruler, Weight = 10 });
            ballot.Votes.Add(new ElectiveCommitment { Clan = rival, Supported = opponent, Weight = 10 });
            var election = new CrownAccessionRecord { Realm = realm, Heir = ruler, Predecessor = ruler, ElectiveElection = true };
            nobility.Mood = 7; glory.Mood = 8; liberty.Mood = 9;
            lost["reaction_realm_Nobility"] = default(CampaignTime);
            won["reaction_realm_Liberty"] = default(CampaignTime);
            check(Call("CompleteElectionReactions", election, ballot), "Incumbent ballot aftermath completes");
            check(nobility.Mood == 27 && glory.Mood == -22 && liberty.Mood == 9, "Reelection adds +20/-30 without resetting moods");
            check(!lost.ContainsKey("reaction_realm_Nobility") && !won.ContainsKey("reaction_realm_Liberty"), "New ballot clears superseded history including unaffiliated outcomes");
            Call("CompleteElectionReactions", election, ballot);
            check(nobility.Mood == 27 && election.ElectionReactionsApplied && !election.AccessionReactionsApplied, "Ballot receipt blocks repeat delivery without marking a new reign");
            var restored = new CrownAccessionRecord { Realm = realm, Heir = ruler, Predecessor = ruler, DepositionElection = true };
            var prior = (Dictionary<string, string>)AccessTools.Field(type, "_announcedSovereigns").GetValue(_shocks);
            prior[realm.StringId] = ruler.StringId;
            check(Call("CompleteCrownAccession", realm, ruler, restored) && nobility.Mood == 0, "New restoration episode ignores old same-hero receipt");
            nobility.Mood = 11;
            Call("CompleteCrownAccession", realm, ruler, restored);
            check(nobility.Mood == 11, "Same saved restoration cannot reset mood twice");
            _legal = false;
            check(!Call("CompleteCrownAccession", realm, ruler, new CrownAccessionRecord()), "Unconfirmed legal accession remains deferred");
            _legal = true;
            var invalidBallot = new CrownAccessionRecord { Realm = realm, Heir = opponent, ElectiveElection = true };
            check(!Call("CaptureElectionEndorsements", invalidBallot, ballot) && !invalidBallot.ElectionEndorsementsCaptured, "Wrong winner ballot cannot create a receipt");

            var decision = Blank<KingSelectionKingdomDecision>();
            var otherDecision = Blank<KingSelectionKingdomDecision>();
            var emergency = new CrownAccessionRecord { Realm = realm, Heir = ruler, Emergency = true, ElectiveElection = true, EmergencyElection = decision };
            var profile = new SuccessionElectionProfile { SourceDecision = decision };
            profile.Endorsements[FactionType.Glory] = royal;
            KingSelectionAIPatch.ActiveElections[realm] = profile;
            check(Call("CaptureElectionEndorsements", emergency, null) && emergency.ElectionEndorsements[FactionType.Glory] == royal, "Native emergency takes precedence over obsolete standing ballot");
            profile.Endorsements[FactionType.Glory] = rival;
            KingSelectionAIPatch.ActiveElections.Remove(realm);
            check(emergency.ElectionEndorsements[FactionType.Glory] == royal, "Saved endorsement snapshot is independent of transient native cache");
            Call("CompleteElectionReactions", emergency, null);
            check(glory.Mood == 20, "Native emergency aftermath works with persisted snapshot and empty cache");
            var stale = new CrownAccessionRecord { Realm = realm, Heir = ruler, Emergency = true, EmergencyElection = otherDecision };
            KingSelectionAIPatch.ActiveElections[realm] = profile;
            Call("CaptureElectionEndorsements", stale, null);
            check(stale.ElectionEndorsements.Count == 0, "Unrelated native decision never supplies endorsements");

            var execute = AccessTools.Method(typeof(IdeologyBehavior), "ExecuteTreasonSentence");
            foreach (bool exiled in new[] { false, true })
            {
                var victim = Blank<Hero>(); victim.StringId = exiled ? "player_sentence" : "npc_sentence";
                var clan = Blank<Clan>(); Houses[victim] = clan; Realms[clan] = exiled ? null : realm;
                nobility.Mood = 0;
                execute.Invoke(null, new object[] { realm, victim, ruler });
                AccessTools.Method(type, "RecordRoyalExecution").Invoke(_shocks, new object[] { realm, victim });
                check(nobility.Mood == -30, "Sentence records once, native callback plus fallback; exiled=" + exiled);
            }
            _blocked = true; nobility.Mood = 0;
            var spared = Blank<Hero>(); spared.StringId = "blocked_sentence"; Houses[spared] = rival;
            execute.Invoke(null, new object[] { realm, spared, ruler });
            check(nobility.Mood == 0 && !Dead.Contains(spared), "Blocked execution creates no penalty");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            KingSelectionAIPatch.ActiveElections.Remove(realm);
            Houses.Clear(); Realms.Clear(); Dead.Clear(); _shocks = null;
        }
    }
}
