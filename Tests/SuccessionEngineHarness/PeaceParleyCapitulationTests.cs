using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PeaceParleyCapitulationTests
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
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsLoading) { if (Values.TryGetValue(key, out var value)) data = (T)value; }
            else Values[key] = data;
            return true;
        }
    }
    private static Campaign _campaign;
    private static Kingdom _winner, _loser, _playerRealm, _draftingRealm;
    private static Clan _player, _winnerRuler, _loserRuler, _npc;
    private static TreatyProposalRecord _proposal;
    private static int _evaluations, _applied, _spends, _drafts, _closed;
    private static bool _badDraft;
    private static int _driftValidation;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _playerRealm; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { __result = __instance == _winner ? _winnerRuler : _loserRuler; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _winner, _loser }); return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool RealmTitle(Kingdom kingdom, ref string __result) { __result = kingdom?.StringId ?? ""; return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result) { __result = kingdomId == "victor" ? _winner : kingdomId == "defeated" ? _loser : null; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Gold(ref int __result) { __result = int.MaxValue; return false; }
    private static void Drift(bool repairStateDrift, ref IEnumerable<TreatyTermRecord> requestedTerms)
    {
        if (repairStateDrift && _driftValidation > 0 && --_driftValidation == 0)
            requestedTerms = new[] { Payment(101) };
    }
    private static bool Council(TreatyProposalRecord proposal, ref TreatyCouncilEvaluation __result)
    {
        _evaluations++;
        __result = new TreatyCouncilEvaluation(0, 0, 0, 0, 0, null,
            isSoleRulerDecision: true, soleRulerAccepts: true, isBudgetBlocked: proposal.IsOverBudget);
        return false;
    }
    private static bool EmptyCandidates(MethodBase __originalMethod, ref object __result)
    {
        Type item = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        __result = Activator.CreateInstance(typeof(List<>).MakeGenericType(item)); return false;
    }
    private static bool Apply(WarScoreRecord war, TreatyProposalRecord proposal, ref bool __result)
    { _applied++; proposal.SetState(TreatyProposalState.Applied, "test settlement"); war.EndParley(); __result = true; return false; }
    private static bool Spend() { _spends++; return false; }
    private static bool Draft(TreatyProposalRecord proposal, Kingdom drafter)
    {
        _drafts++; _draftingRealm = drafter;
        proposal.ReplaceTerms(new[] { Payment(_badDraft ? 101 : 10) }); return false;
    }
    private static TreatyTermRecord Payment(int cost) => new TreatyTermRecord(TreatyTermType.Reparations, cost,
        fromKingdomId: "defeated", toKingdomId: "victor");

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.parley_capitulation");
        var assembly = typeof(ForeignTreatyBehavior).Assembly;
        var manager = new Manager(); _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(manager);
        var treaties = new ForeignTreatyBehavior(); var scores = new WarScoreBehavior();
        manager.Items.AddRange(new CampaignBehaviorBase[] { treaties, scores });
        var proposals = (List<TreatyProposalRecord>)AccessTools.Field(typeof(ForeignTreatyBehavior), "_proposals").GetValue(treaties);
        var wars = (List<WarScoreRecord>)AccessTools.Field(typeof(WarScoreBehavior), "_wars").GetValue(scores);
        _winner = Blank<Kingdom>(); _winner.StringId = "victor"; _loser = Blank<Kingdom>(); _loser.StringId = "defeated";
        _player = Blank<Clan>(); _player.StringId = "player"; _npc = Blank<Clan>(); _npc.StringId = "npc";
        void Patch(MethodBase method, string name)
        {
            if (method == null) throw new MissingMethodException(name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeaceParleyCapitulationTests), name));
        }
        void Setup(bool forced = true, bool wins = false, bool ruler = true)
        {
            _playerRealm = wins ? _winner : _loser;
            _winnerRuler = wins && ruler ? _player : _npc; _loserRuler = !wins && ruler ? _player : _npc;
            _evaluations = _applied = _spends = _drafts = _closed = 0; _badDraft = false; _draftingRealm = null;
            _driftValidation = 0;
            var war = new WarScoreRecord("victor|defeated", "victor", "defeated", 0, null);
            AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, forced ? 100f : 40f);
            war.BeginParley(forced, 0); wars.Clear(); wars.Add(war);
            _proposal = new TreatyProposalRecord(war.WarKey, "victor", "defeated", "victor", 0, forced ? 100 : 40, forced);
            _proposal.AddTerm(Payment(20)); proposals.Clear(); proposals.Add(_proposal);
        }
        PeaceParleyVM View() => new PeaceParleyVM(_proposal, () => _closed++);
        void Call(PeaceParleyVM view, string method, params object[] args) => AccessTools.Method(typeof(PeaceParleyVM), method).Invoke(view, args);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Current));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "GetPlayerPoliticalKingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "IsTreatyRealmAvailable"), nameof(Yes));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "EvaluateCouncil"), nameof(Council));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyTreatyCore"), nameof(Apply));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "DraftAiTerms"), nameof(Draft));
            foreach (string name in new[] { "SpendCouncilCommitments", "SpendRulerOverride" })
                Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), name), nameof(Spend));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyFinalCouncilConsequences"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.StorylineWarProtectionHelper"), "IsPeaceBlocked"), nameof(No));
            var draft = assembly.GetType("BellumCivile.TreatyDraftService");
            Patch(AccessTools.Method(draft, "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(draft, "CalculateCollectiveAvailableGold"), nameof(Gold));
            Patch(AccessTools.Method(draft, "TryValidateAndNormalize"), nameof(Drift));
            foreach (var type in new[] { draft, assembly.GetType("BellumCivile.TreatyRealmTransitionService"),
                assembly.GetType("BellumCivile.TreatyRoyalMarriageService"), assembly.GetType("BellumCivile.ClientWarTerritory") })
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => m.Name.StartsWith("GetAvailable") || m.Name == "GetReleaseCandidates" || m.Name == "GetCandidates" || m.Name == "Candidates"))
                    Patch(method, nameof(EmptyCandidates));
            Patch(AccessTools.Method(draft, "TryGetClientKingdomCandidate"), nameof(No));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.TreatyRealmTransitionService"), "TryGetForceVassalizationCandidate"), nameof(No));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "ResolveRealmTitle"), nameof(RealmTitle));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "AddHostageOption"), nameof(Skip));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "ShowTreatyOutcomeInquiry"), nameof(Skip));

            Setup(); var original = _proposal.Terms.Single(); int revision = _proposal.DraftRevision;
            check(!treaties.TryReplacePlayerDraft(_proposal.ProposalId, Array.Empty<TreatyTermRecord>(), out string reason)
                && _proposal.Terms.Single() == original && _proposal.DraftRevision == revision && _evaluations == 0,
                "A capitulated ruler cannot erase the victor's demands through the backend");
            check(reason.Contains("capitulated"), "Backend refusal explains the capitulation restriction");
            var view = View();
            check(view.IsPlayerRuler && view.IsDraftEditingDisabled && view.IsRejectDisabled && !view.IsSignDisabled,
                "Defeated sovereign may review and sign but cannot edit or reject the imposed settlement");
            check(view.DraftTabDisabledHint.HintText.ToString().Contains("Your realm has capitulated")
                && view.ResetTreatyHint.HintText.ToString().Contains("victorious ruler"), "Disabled controls explain who dictates the peace");
            foreach (string command in new[] { "ExecuteResetDraft", "ExecuteIncreaseReparations", "ExecuteDecreaseReparations",
                "ExecuteIncreaseTribute", "ExecuteDecreaseTribute", "ExecuteIncreaseHostageDuration", "ExecuteDecreaseHostageDuration" })
            {
                view.GetType().GetMethod(command).Invoke(view, null);
                check(_proposal.Terms.Single() == original && _proposal.DraftRevision == revision,
                    "Read-only capitulation command preserves the draft: " + command);
            }
            foreach (var action in new[] { ("ToggleFiefTerm", "castle"), ("TogglePrisonerTerm", "hero"),
                ("ToggleClaimTerm", "claim"), ("ToggleNewPoliticalTerm", "concede_defeat"), ("ToggleHostageTerm", "hostage_peace") })
            {
                Call(view, action.Item1, action.Item2);
                check(_proposal.Terms.Single() == original && _proposal.DraftRevision == revision, "Locked clause cannot toggle: " + action.Item1);
            }
            view.ExecuteSignTreaty();
            check(_applied == 1 && _closed == 1 && _proposal.State == TreatyProposalState.Applied && _proposal.Terms.Any(t => t.Type == TreatyTermType.Reparations),
                "Signing through the real view model concludes capitulation with the victor's demands intact");

            foreach (bool wins in new[] { false, true })
            {
                Setup(false, wins); view = View();
                check(!view.IsDraftEditingDisabled && treaties.TryReplacePlayerDraft(_proposal.ProposalId, Array.Empty<TreatyTermRecord>(), out _),
                    "Ordinary peace negotiations retain ruler editing: playerWins=" + wins);
            }
            Setup(true, true); view = View();
            check(!view.IsDraftEditingDisabled && treaties.TryReplacePlayerDraft(_proposal.ProposalId, Array.Empty<TreatyTermRecord>(), out _)
                && treaties.TrySignPlayerParley(_proposal.ProposalId, out _) && _applied == 1,
                "A victorious player ruler may deliberately grant a termless capitulation");
            Setup(true, false, false); view = View();
            check(view.IsPlayerVassal && view.IsDraftEditingDisabled && view.IsSignDisabled,
                "Player vassals retain their existing vote-before-signing workflow");

            Setup(); var saved = new Store(); treaties.SyncData(saved); saved.IsLoading = true;
            manager.Items.Remove(treaties); treaties = new ForeignTreatyBehavior(); treaties.SyncData(saved); manager.Items.Add(treaties);
            check(!treaties.TryReplacePlayerDraft(_proposal.ProposalId, Array.Empty<TreatyTermRecord>(), out _),
                "Existing saved pending capitulations receive the same backend authority restriction");

            Setup(); _proposal.ReplaceTerms(new[] { Payment(101) }); view = View();
            check(view.IsDraftEditingDisabled && !view.IsSignDisabled, "An obsolete over-budget NPC draft cannot strand a read-only defeated ruler");
            view.ExecuteSignTreaty();
            check(!_proposal.IsOverBudget && _applied == 0 && _spends == 0 && _drafts == 1 && _draftingRealm == _winner
                && view.DraftTerms.Count == 1 && _proposal.State == TreatyProposalState.ParleyPending,
                "Acknowledging an invalid capitulation refreshes the victor's terms for review before applying or spending anything");
            view.ExecuteSignTreaty();
            check(_applied == 1 && _closed == 1 && _drafts == 1, "The reviewed repaired capitulation can then be signed normally");
            foreach (int validation in new[] { 1, 2 })
            {
                Setup(); _driftValidation = validation; view = View(); view.ExecuteSignTreaty();
                check(!_proposal.IsOverBudget && _applied == 0 && _spends == 0 && _closed == 0 && _drafts == 1
                    && _proposal.State == TreatyProposalState.ParleyPending,
                    "Terms that exceed budget during validation must also be reviewed: boundary=" + validation);
                view.ExecuteSignTreaty();
                check(_applied == 1 && _closed == 1, "Reviewed state-drift repair signs normally: boundary=" + validation);
            }
            Setup(); _proposal.ReplaceTerms(new[] { Payment(101) }); _badDraft = true; view = View(); view.ExecuteSignTreaty();
            check(_proposal.State == TreatyProposalState.Cancelled && _closed == 1 && _applied == 0 && _spends == 0 && wars.Single().IsActive,
                "An unrecoverable forced draft closes without applying terms, charges or a menu softlock");
        }
        finally { harmony.UnpatchAll(harmony.Id); _campaign = null; }
    }
}
