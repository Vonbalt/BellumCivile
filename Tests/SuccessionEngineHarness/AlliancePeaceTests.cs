using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class AlliancePeaceTests
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

    private static Campaign _campaign;
    private static Kingdom _first, _second, _caller, _other;
    private static Clan _player, _npc, _ruler;
    private static bool _enabled, _playerRules;
    private static int _effects, _removed, _messages;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _first; return false; }
    private static bool ClanRealm(ref Kingdom __result) { __result = _first; return false; }
    private static bool Ruler(ref Clan __result) { __result = _playerRules ? _player : _ruler; return false; }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = new[] { _first, _second, _caller, _other }.FirstOrDefault(k => k.StringId == kingdomId); return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result)
    { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _first, _second, _caller, _other }); return false; }
    private static bool SideEffect()
    { _effects++; throw new InvalidOperationException("An alliance-restricted treaty reached a side effect"); }
    private static bool Remove() { _removed++; return false; }
    private static bool Message() { _messages++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.alliance_peace");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(AlliancePeaceTests), prefix));
        var assembly = typeof(ForeignTreatyBehavior).Assembly;
        var notificationType = Type.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes.KingdomVoteNotificationItemVM, TaleWorlds.CampaignSystem.ViewModelCollection", true);
        var guard = assembly.GetType("BellumCivile.AlliancePeaceRestriction", true);
        var blocked = AccessTools.Method(guard, "IsPeaceBlocked");
        bool Blocked(Kingdom first, Kingdom second) => (bool)blocked.Invoke(null, new object[] { first, second });
        object Call(object target, string method, params object[] args)
            => AccessTools.Method(target.GetType(), method).Invoke(target, args);
        _first = Blank<Kingdom>(); _first.StringId = "first";
        _second = Blank<Kingdom>(); _second.StringId = "second";
        _caller = Blank<Kingdom>(); _caller.StringId = "caller";
        _other = Blank<Kingdom>(); _other.StringId = "other";
        _player = Blank<Clan>(); _npc = Blank<Clan>(); _ruler = Blank<Clan>();
        _enabled = true; _playerRules = false;
        var scores = new WarScoreBehavior();
        var treaties = new ForeignTreatyBehavior();
        var native = Blank<AllianceCampaignBehavior>();
        var manager = new Manager();
        manager.Items.AddRange(new CampaignBehaviorBase[] { native, scores, treaties });
        _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(manager);
        var entry = typeof(AllianceCampaignBehavior).GetNestedType("CallToWarAgreement", BindingFlags.Public | BindingFlags.NonPublic);
        var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entry));
        AccessTools.Field(typeof(AllianceCampaignBehavior), "_callToWarAgreements").SetValue(native, entries);
        var constructor = AccessTools.Constructor(entry, new[] { typeof(Kingdom), typeof(Kingdom), typeof(Kingdom), typeof(CampaignTime) });
        void Obligation(Kingdom called, Kingdom enemy)
        {
            entries.Clear();
            entries.Add(constructor.Invoke(new object[] { _caller, called, enemy, CampaignTime.Days(142) }));
        }
        var wars = (List<WarScoreRecord>)AccessTools.Field(typeof(WarScoreBehavior), "_wars").GetValue(scores);
        var proposals = (List<TreatyProposalRecord>)AccessTools.Field(typeof(ForeignTreatyBehavior), "_proposals").GetValue(treaties);
        WarScoreRecord War(bool forced = false, bool pending = false)
        {
            var war = new WarScoreRecord("first|second", "first", "second", 0, null);
            AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, forced ? 100f : 20f);
            if (pending) war.BeginParley(forced, 0);
            wars.Clear(); wars.Add(war); proposals.Clear(); _effects = 0;
            return war;
        }
        TreatyProposalRecord Pending(bool forced = false)
        {
            var war = War(forced, true);
            var proposal = new TreatyProposalRecord(war.WarKey, "first", "second", "second", 0, forced ? 100 : 20, forced);
            proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
            proposal.SetPlayerVote(TreatyCouncilVoteStance.Yay, 75);
            proposals.Add(proposal);
            return proposal;
        }
        bool Cancelled(TreatyProposalRecord p) => p.State == TreatyProposalState.Cancelled
            && p.ResolutionNote == "an active call-to-arms agreement prevents a separate peace"
            && !p.PlayerInfluenceSpent && !wars.Single().ParleyPending && wars.Single().IsActive && _effects == 0;
        MakePeaceKingdomDecision Decision()
        {
            var decision = Blank<MakePeaceKingdomDecision>();
            AccessTools.Field(typeof(KingdomDecision), "_kingdom").SetValue(decision, _first);
            AccessTools.Field(typeof(KingdomDecision), "<ProposerClan>k__BackingField").SetValue(decision, _npc);
            AccessTools.Field(typeof(MakePeaceKingdomDecision), "FactionToMakePeaceWith").SetValue(decision, _second);
            return decision;
        }
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object oldTicks = ticks.GetValue(null);
        try
        {
            ticks.SetValue(null, 1000L);
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(ClanRealm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "IsRevampEnabled"), nameof(Enabled));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "GetPlayerPoliticalKingdom"), nameof(Realm));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "IsMutualWhitePeaceEligible"), nameof(No));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "IsStorylineProtectedForeignWar"), nameof(No));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "ShouldProcessWar"), nameof(Yes));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "TryCloseOrphanedForeignWarTracker"), nameof(No));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.StorylineWarProtectionHelper", true), "IsPeaceBlocked"), nameof(No));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(Yes));
            Patch(AccessTools.Method(typeof(Kingdom), "RemoveDecision"), nameof(Remove));
            Patch(AccessTools.Method(notificationType.BaseType, "ExecuteRemove"), nameof(Remove));
            Patch(AccessTools.Method(typeof(InformationManager), "DisplayMessage", new[] { typeof(InformationMessage) }), nameof(Message));
            foreach (string name in new[] { "DraftAiTerms", "SpendCouncilCommitments", "SpendRulerOverride", "ApplyTerritorialTerms", "ApplyRoyalMarriageTerms", "ApplyFinalCouncilConsequences" })
                Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), name), nameof(SideEffect));
            h.Patch(AccessTools.Method(typeof(Kingdom), "AddDecision"), prefix: new HarmonyMethod(typeof(KingdomPeaceDecisionAddedParleyCleanupPatch), "Prefix"));
            h.Patch(AccessTools.Method(notificationType, "OnInspect"), prefix: new HarmonyMethod(typeof(KingdomPeaceVoteNotificationParleyPatch), "Prefix"));

            check(!Blocked(_first, _second) && !Blocked(null, _second) && !Blocked(_first, _first),
                "No obligation, null endpoints and identical endpoints do not block peace");
            Obligation(_other, _second);
            check(!Blocked(_first, _second), "An unrelated call-to-arms agreement does not block this war");
            foreach (bool reverse in new[] { false, true })
            {
                Obligation(reverse ? _second : _first, reverse ? _first : _second);
                check(Blocked(_first, _second) && Blocked(_second, _first),
                    "Native agreement blocks either endpoint regardless of argument order: reverse=" + reverse);
                check(!Blocked(_caller, reverse ? _first : _second), "Calling ally is free to negotiate its own war");
                var reasonArgs = new object[] { _first, _second, null };
                check((bool)AccessTools.Method(guard, "TryGetPeaceBlock").Invoke(null, reasonArgs)
                    && reasonArgs[2].ToString().Contains("caller") && reasonArgs[2].ToString().Contains("separate peace"),
                    "Restriction explains the native call-to-arms obligation");

                foreach (bool forced in new[] { false, true })
                foreach (bool aiOnly in new[] { false, true })
                {
                    var war = War(forced);
                    check(!(bool)Call(treaties, "TryQueueParley", war, forced, true, "test", aiOnly, aiOnly, _first, null)
                        && proposals.Count == 0 && !war.ParleyPending && _effects == 0,
                        "Admission blocks before drafting, notifications or costs: forced=" + forced + "; AI=" + aiOnly);
                }
                War();
                var candidate = new object[] { _npc, null, false, null, false };
                check(!(bool)Call(scores, "TryGetForeignParleyProposal", candidate),
                    "AI candidate selection omits alliance-restricted wars");
                var terminal = War(true);
                Call(terminal, "QueueTerminalResolution", "fixture");
                check(!(bool)Call(scores, "ResolveQueuedTerminalWarScore", terminal)
                    && !terminal.TerminalResolutionQueued && !terminal.ResolutionPending
                    && terminal.IsActive && proposals.Count == 0 && _effects == 0,
                    "Terminal war score cannot fall through to a separate peace while the obligation is active");
                var emptyPending = Pending();
                emptyPending.ReplaceTerms(new TreatyTermRecord[0]);
                check(!treaties.TryOpenPlayerParley(wars.Single(), false, true, "fixture", out _)
                    && Cancelled(emptyPending), "Reopening a saved empty draft cancels before AI drafting or UI creation");

                foreach (bool ruler in new[] { false, true })
                foreach (bool forced in new[] { false, true })
                {
                    _playerRules = ruler;
                    var p = Pending(forced);
                    check(treaties.TrySignPlayerParley(p.ProposalId, out _) && Cancelled(p),
                        "Signing closes stale treaty before voting effects: ruler=" + ruler + "; forced=" + forced);
                    var vm = Blank<PeaceParleyVM>();
                    AccessTools.Field(typeof(PeaceParleyVM), "_proposal").SetValue(vm, p);
                    var outcome = Call(vm, "BuildTreatyOutcomeInquiry", false);
                    string body = (string)AccessTools.Property(outcome.GetType(), "Description").GetValue(outcome);
                    check(body.Contains("call-to-arms") && body.Contains("without spending influence") && !body.Contains("ceased to exist"),
                        "Cancelled parley reports the alliance obligation instead of a rejection or extinct realm");
                }
                _playerRules = true;
                var rejected = Pending(true);
                check(treaties.TryRejectPlayerParley(rejected.ProposalId, out _) && Cancelled(rejected),
                    "Ruler can exit a restricted forced parley without an override cost or refusal penalty");
                foreach (string method in new[] { "ResolvePendingParley", "ApplyTreaty", "ApplyTreatyCore", "TryShowForcedPlayerParleyInquiry" })
                {
                    var p = Pending(true);
                    object[] args = method == "ResolvePendingParley" ? new object[] { wars.Single(), p, true }
                        : method == "TryShowForcedPlayerParleyInquiry" ? new object[] { wars.Single(), p }
                        : method == "ApplyTreaty" ? new object[] { wars.Single(), p, _first, _second, false }
                        : new object[] { wars.Single(), p, _first, _second };
                    Call(treaties, method, args);
                    check(Cancelled(p), "Restricted pending treaty cannot pass " + method);
                }
                var loaded = Pending(true);
                AccessTools.Field(typeof(ForeignTreatyBehavior), "_pendingPlayerParleyOpenProposalId").SetValue(treaties, loaded.ProposalId);
                AccessTools.Field(typeof(ForeignTreatyBehavior), "_forcedPlayerParleyInquiryProposalId").SetValue(treaties, loaded.ProposalId);
                Call(treaties, "OnSessionLaunched", new object[] { null });
                check(Cancelled(loaded)
                    && AccessTools.Field(typeof(ForeignTreatyBehavior), "_pendingPlayerParleyOpenProposalId").GetValue(treaties) == null
                    && AccessTools.Field(typeof(ForeignTreatyBehavior), "_forcedPlayerParleyInquiryProposalId").GetValue(treaties) == null,
                    "Loading clears prohibited pending treaties and their deferred or forced UI requests");

                War();
                var decision = Decision();
                check(!(bool)AccessTools.Method(typeof(KingdomPeaceDecisionAddedParleyCleanupPatch), "Prefix")
                    .Invoke(null, new object[] { decision }), "Decision insertion is blocked before native notification and influence cost");
                _first.AddDecision(decision);
                check(proposals.Count == 0 && _effects == 0,
                    "Installed Harmony insertion guard skips native decision effects");
                _removed = 0;
                AccessTools.Method(typeof(KingdomPeaceVoteNotificationQueuedParleyPatch), "Postfix")
                    .Invoke(null, new object[] { decision });
                check(_removed == 1 && proposals.Count == 0, "Loaded notification cannot queue a prohibited proposal");
                _removed = _messages = 0;
                var notice = FormatterServices.GetUninitializedObject(notificationType);
                AccessTools.Field(notificationType, "_decision").SetValue(notice, decision);
                Call(notice, "OnInspect");
                check(_removed == 2 && _messages == 1 && proposals.Count == 0,
                    "Old notification is dismissed with an explanation instead of opening a forbidden parley");

                entries.Clear();
                check(!Blocked(_first, _second), "Native termination or expiry cleanup immediately lifts the restriction");
                candidate = new object[] { _npc, null, false, null, false };
                check((bool)Call(scores, "TryGetForeignParleyProposal", candidate),
                    "Eligible war returns to Bellum's unchanged candidate selection after obligation ends");
                check((bool)AccessTools.Method(typeof(KingdomPeaceDecisionAddedParleyCleanupPatch), "Prefix")
                    .Invoke(null, new object[] { decision }), "Ordinary peace decisions remain available without an obligation");
            }
            Obligation(_first, _second);
            War(); _enabled = false;
            check((bool)AccessTools.Method(typeof(KingdomPeaceDecisionAddedParleyCleanupPatch), "Prefix")
                .Invoke(null, new object[] { Decision() }),
                "Disabled revamp leaves native decision insertion untouched");
            _enabled = true;
            AccessTools.Field(typeof(WarScoreRecord), "_conflictType").SetValue(wars.Single(), WarScoreConflictType.ClaimFeud);
            check((bool)AccessTools.Method(typeof(KingdomPeaceDecisionAddedParleyCleanupPatch), "Prefix")
                .Invoke(null, new object[] { Decision() }),
                "Foreign peace notification guard does not take over internal conflict decisions");
            manager.Items.Remove(native);
            check(!Blocked(_first, _second), "Missing native alliance behavior does not prevent ordinary negotiations");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); _campaign = null; }
    }
}
