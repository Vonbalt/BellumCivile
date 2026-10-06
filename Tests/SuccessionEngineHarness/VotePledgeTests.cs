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
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class VotePledgeTests
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
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) { Data[key] = data; return true; }
            if (!Data.TryGetValue(key, out var value)) return false;
            data = (T)value; return true;
        }
    }

    private static readonly Assembly Mod = typeof(FiefDeliberationBehavior).Assembly;
    private static readonly Type Service = Mod.GetType("BellumCivile.VotePledgeService", true);
    private static Kingdom _realm, _voterRealm;
    private static Clan _player, _voter, _candidate, _ruler;
    private static Hero _leader, _playerHero;
    private static PerkObject _flexible, _goodNatured;
    private static bool _discount, _refund, _validCouncilCandidate;
    private static float _balance, _merit;
    private static DecisionOutcome _preferred;
    private static int _unfulfilled;
    private static bool _pending = true, _offerAcceptable = true;
    private static int _paid, _cancelled;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(string method, params object[] args) => AccessTools.Method(Service, method).Invoke(null, args);
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _voter ? _voterRealm : _realm; return false; }
    private static bool Head(ref Hero __result) { __result = _leader; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm }); return false; }
    private static bool Balance(ref float __result) { __result = _balance; return false; }
    private static bool Spend(Clan clan, float amount) { _balance += amount; return false; }
    private static bool Flexible(ref PerkObject __result) { __result = _flexible; return false; }
    private static bool GoodNatured(ref PerkObject __result) { __result = _goodNatured; return false; }
    private static bool HasPerk(PerkObject perk, ref bool __result)
    { __result = perk == _flexible ? _discount : perk == _goodNatured && _refund; return false; }
    private static bool Bonus(ref float __result) { __result = -.2f; return false; }
    private static bool Support(DecisionOutcome possibleOutcome, ref float __result)
    { __result = possibleOutcome == _preferred ? _merit : 0; return false; }
    private static bool FindClan(string clanId, ref Clan __result)
    { __result = new[] { _player, _voter, _candidate, _ruler }.FirstOrDefault(c => c.StringId == clanId); return false; }
    private static bool Nomination(Clan candidate, ref object __result)
    {
        __result = _validCouncilCandidate && candidate == _candidate
            ? Activator.CreateInstance(Mod.GetType("BellumCivile.CouncilAppointmentNominationResult"), true) : null;
        return false;
    }
    private static bool Notice() { _unfulfilled++; return false; }
    private static bool MainHero(ref Hero __result) { __result = _playerHero; return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result)
    { __result = __instance == _playerHero ? _player : _voter; return false; }
    private static bool NoParty(ref MobileParty __result) { __result = null; return false; }
    private static bool MapRealm(ref IFaction __result) { __result = _realm; return false; }
    private static bool Pending(ref bool __result) { __result = _pending; return false; }
    private static bool Acceptable(ref bool __result) { __result = _offerAcceptable; return false; }
    private static bool Cancel() { _cancelled++; return false; }
    private static bool Pay(List<Barterable> barters)
    { _paid++; foreach (var item in barters) item.Apply(); return false; }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.vote_pledges");
        var previous = Campaign.Current;
        void Patch(MethodBase target, string prefix) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(VotePledgeTests), prefix));
        Type PatchType(string name) => Mod.GetType("BellumCivile.Patches." + name, true);
        void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        void Decision(KingdomDecision decision) { Set(decision, "_kingdom", _realm); }
        float Reserved(string except = null) => (float)Call("ReservedInfluence", _voter, except);
        bool CanPromise(string key) => (bool)Call("CanPromise", _voter, _realm, key, null);
        _realm = Blank<Kingdom>(); _realm.StringId = "realm"; _voterRealm = _realm;
        _player = Blank<Clan>(); _player.StringId = "player";
        _voter = Blank<Clan>(); _voter.StringId = "voter";
        _candidate = Blank<Clan>(); _candidate.StringId = "candidate";
        _ruler = Blank<Clan>(); _ruler.StringId = "ruler";
        _leader = Blank<Hero>(); _playerHero = Blank<Hero>();
        _flexible = Blank<PerkObject>(); _goodNatured = Blank<PerkObject>();
        _discount = _refund = false; _validCouncilCandidate = true; _unfulfilled = 0;
        var campaign = Blank<Campaign>();
        var manager = new Manager();
        var fiefs = new FiefDeliberationBehavior();
        var policies = new PolicyDeliberationBehavior();
        var expulsions = new ExpulsionDeliberationBehavior();
        var council = new CouncilAppointmentDeliberationBehavior();
        manager.Items.AddRange(new CampaignBehaviorBase[] { fiefs, policies, expulsions, council });
        var settlement = Blank<Settlement>(); settlement.StringId = "castle";
        var policy = Blank<PolicyObject>(); policy.StringId = "policy";
        var fiefDecision = Blank<SettlementClaimantDecision>(); Decision(fiefDecision); Set(fiefDecision, "Settlement", settlement);
        var policyDecision = Blank<KingdomPolicyDecision>(); Decision(policyDecision); Set(policyDecision, "Policy", policy);
        var expulsionDecision = Blank<ExpelClanFromKingdomDecision>(); Decision(expulsionDecision); Set(expulsionDecision, "ClanToExpel", _candidate);
        var councilDecision = Blank<PrivyCouncilAppointmentDecision>(); Decision(councilDecision);
        Set(councilDecision, "_office", PrivyCouncilOffice.Marshal);
        string fiefKey = (string)Call("FiefKey", _realm, settlement, _voter);
        string policyKey = (string)Call("PolicyKey", _realm, policy, _voter);
        string expulsionKey = (string)Call("ExpulsionKey", _realm, _candidate, _voter);
        string councilKey = (string)Call("CouncilKey", _realm, PrivyCouncilOffice.Marshal, _voter);
        void Clear()
        {
            fiefs.ClearBribedVotesForSettlement(_realm, settlement);
            policies.ClearBribedVotesForPolicy(_realm, policy);
            expulsions.ClearBribedVotesForTarget(_realm, _candidate);
            AccessTools.Method(typeof(CouncilAppointmentDeliberationBehavior), "ClearNominationState")
                .Invoke(council, new object[] { _realm.StringId + "|" + (int)PrivyCouncilOffice.Marshal + "|", false });
        }
        void PledgeCouncil() => council.SetCommittedCandidateVote(_realm, PrivyCouncilOffice.Marshal, _voter, _candidate, "persuaded");
        var fiefYes = new SettlementClaimantDecision.ClanAsDecisionOutcome(_candidate);
        var fiefNo = new SettlementClaimantDecision.ClanAsDecisionOutcome(_ruler);
        var policyYes = new KingdomPolicyDecision.PolicyDecisionOutcome(true);
        var policyNo = new KingdomPolicyDecision.PolicyDecisionOutcome(false);
        var expulsionYes = new ExpelClanFromKingdomDecision.ExpelClanDecisionOutcome(true);
        var expulsionNo = new ExpelClanFromKingdomDecision.ExpelClanDecisionOutcome(false);
        var councilYes = new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate);
        var councilNo = new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_ruler);
        var decisions = new KingdomDecision[] { fiefDecision, policyDecision, expulsionDecision, councilDecision };
        var yes = new DecisionOutcome[] { fiefYes, policyYes, expulsionYes, councilYes };
        var no = new DecisionOutcome[] { fiefNo, policyNo, expulsionNo, councilNo };
        var names = new[] { "fief", "policy", "expulsion", "council" };
        Action[] promise = {
            () => fiefs.SetBribedCandidateVote(_realm, settlement, _voter, _candidate),
            () => policies.SetBribedVote(_realm, policy, _voter, 500),
            () => expulsions.SetBribedVote(_realm, _candidate, _voter, 500), PledgeCouncil
        };
        DecisionOutcome Vote(KingdomDecision d, DecisionOutcome a, DecisionOutcome b, out Supporter.SupportWeights weight,
            Clan voter = null) => d.DetermineSupportOption(new Supporter(voter ?? _voter),
                new MBList<DecisionOutcome> { a, b }, out weight, false);
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Balance));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(DefaultPerks.Charm), "FlexibleEthics"), nameof(Flexible));
            Patch(AccessTools.PropertyGetter(typeof(DefaultPerks.Charm), "GoodNatured"), nameof(GoodNatured));
            Patch(AccessTools.PropertyGetter(typeof(PerkObject), "PrimaryBonus"), nameof(Bonus));
            Patch(AccessTools.Method(typeof(Hero), "GetPerkValue"), nameof(HasPerk));
            Patch(AccessTools.Method(typeof(ChangeClanInfluenceAction), "Apply"), nameof(Spend));
            Patch(AccessTools.Method(typeof(CouncilAppointmentDeliberationBehavior), "ResolveClan"), nameof(FindClan));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.CouncilAppointmentNominationHelper"), "ScoreCandidate",
                new[] { typeof(Clan), typeof(Clan), typeof(Kingdom), typeof(PrivyCouncilOffice), typeof(PrivyCouncilBehavior) }), nameof(Nomination));
            Patch(AccessTools.Method(Service, "ReportUnfulfilled"), nameof(Notice));
            foreach (var d in decisions) Patch(AccessTools.Method(d.GetType(), "DetermineSupport"), nameof(Support));
            foreach (string patch in new[] { "NpcKingdomDecisionSupportBudgetPatch", "NpcKingdomDecisionPaymentBudgetPatch",
                "VotePledgePaymentPatch", "InfluenceVotePledgeBarterPatch" }) harmony.CreateClassProcessor(PatchType(patch)).Patch();

            for (int i = 0; i < decisions.Length; i++)
            {
                Clear(); promise[i](); _preferred = yes[i]; _merit = 500;
                foreach (float influence in new[] { 20f, 199, 200, 210 })
                {
                    _balance = influence;
                    check(Vote(decisions[i], yes[i], no[i], out var w) == yes[i] && w == Supporter.SupportWeights.SlightlyFavor,
                        names[i] + " promise casts the minimum paid vote at " + influence + " influence");
                }
                _balance = 19;
                check(Vote(decisions[i], yes[i], no[i], out var low) == null && low == Supporter.SupportWeights.StayNeutral,
                    names[i] + " cannot create a free vote after actual insolvency");
                _balance = 350;
                check(Vote(decisions[i], yes[i], no[i], out var rich) == yes[i] && rich == Supporter.SupportWeights.FullyPush,
                    names[i] + " wealthy pledge keeps its normally affordable larger vote");
                _balance = 210; _preferred = no[i];
                check(Vote(decisions[i], yes[i], no[i], out var redirected) == yes[i]
                    && redirected == Supporter.SupportWeights.SlightlyFavor, names[i] + " pledge wins over contrary natural preference");
                _preferred = yes[i]; Clear();
                check(Vote(decisions[i], yes[i], no[i], out var ordinary) == null && ordinary == Supporter.SupportWeights.StayNeutral,
                    names[i] + " ordinary unpromised vote still respects the reserve");
                promise[i](); _discount = true; _balance = 16;
                check(decisions[i].GetInfluenceCostOfSupport(_voter, Supporter.SupportWeights.SlightlyFavor)
                    == (int)Call("MinimumCost", _voter) && Reserved() == 16
                    && Vote(decisions[i], yes[i], no[i], out _) == yes[i], names[i] + " reservation matches native perk-discounted cost");
                _discount = false;
            }

            Clear(); _balance = 20; _preferred = policyYes;
            foreach (bool repeal in new[] { false, true })
            {
                Set(policyDecision, "_isInvertedDecision", repeal);
                foreach (int score in new[] { -500, 500 })
                {
                    policies.SetBribedVote(_realm, policy, _voter, score);
                    check(Vote(policyDecision, policyYes, policyNo, out _) == (score > 0 ? policyYes : policyNo),
                        "Policy pledge follows the promised motion: repeal=" + repeal + ", score=" + score);
                }
            }
            Clear(); expulsions.SetBribedVote(_realm, _candidate, _voter, -500); _preferred = expulsionYes;
            check(Vote(expulsionDecision, expulsionYes, expulsionNo, out _) == expulsionNo, "Promise to defend a clan votes against expulsion");

            Clear(); _balance = 40; promise[0](); PledgeCouncil();
            check(Reserved() == 40 && Reserved(councilKey) == 20 && Reserved(fiefKey) == 20,
                "Concurrent fief and numeric-office council keys reserve exactly one minimum vote each");
            check(CanPromise(fiefKey) && CanPromise(councilKey) && !CanPromise(policyKey),
                "Replacing an existing promise is affordable, overpromising the same funds is not");
            promise[0](); PledgeCouncil();
            check(Reserved() == 40, "Repeated setters do not duplicate reservations");
            _preferred = councilYes;
            check(Vote(councilDecision, councilYes, councilNo, out _) == councilYes, "Council vote can use its own reserved funds");
            _balance = 39;
            check(Vote(councilDecision, councilYes, councilNo, out _) == null, "A vote cannot consume another promise's minimum funding");
            _voterRealm = null;
            check(Reserved() == 0 && !CanPromise(fiefKey), "Leaving a kingdom immediately drops its cached reservation and eligibility");
            _voterRealm = _realm;
            check(Reserved() == 40, "Returning to the realm rebuilds its outstanding reservation");
            Clear(); check(Reserved() == 0, "Resolution or cancellation releases reservations");
            _balance = 19; check(!CanPromise(fiefKey), "An unfunded new promise is refused before barter payment");
            _balance = 20; check(CanPromise(fiefKey), "A funded promise is permitted below the normal influence reserve");

            promise[0](); _preferred = fiefNo; _merit = 500; _balance = 210;
            check(Vote(fiefDecision, fiefNo, new SettlementClaimantDecision.ClanAsDecisionOutcome(_player), out _) == null,
                "Missing promised fief candidate does not redirect a pledge to another clan");
            Set(fiefDecision, "ClanToExclude", _candidate);
            check(Vote(fiefDecision, fiefYes, fiefNo, out _) == null, "Excluded fief candidate cannot receive a pledged vote");
            Set(fiefDecision, "ClanToExclude", null);
            Clear(); PledgeCouncil(); _validCouncilCandidate = false;
            check(Vote(councilDecision, councilYes, councilNo, out _) == null, "Ineligible council nominee cannot receive a pledged vote");
            _validCouncilCandidate = true;
            _balance = 20; _preferred = policyYes; _merit = 0;
            check(Vote(policyDecision, policyYes, policyNo, out _, _player) == null, "Player vote is not forced by the NPC pledge rule");

            // Exercise native cost handling after the pledge dictionaries have already been cleared.
            var snapshot = AccessTools.Method(PatchType("VotePledgePaymentPatch"), "Prefix");
            var finish = AccessTools.Method(PatchType("VotePledgePaymentPatch"), "Finalizer");
            var payment = AccessTools.Method(typeof(KingdomElection), "HandleInfluenceCosts");
            for (int i = 0; i < decisions.Length; i++)
            {
                foreach (bool won in new[] { true, false })
                {
                    Clear(); promise[i](); _balance = 20; _refund = false;
                    var election = Blank<KingdomElection>();
                    var supporter = new Supporter(_voter) { SupportWeight = Supporter.SupportWeights.SlightlyFavor };
                    yes[i].SupporterList.Clear(); yes[i].SupporterList.Add(supporter);
                    var outcomes = new MBList<DecisionOutcome> { yes[i], no[i] };
                    var supporters = new List<Supporter> { supporter, new Supporter(_ruler) };
                    Set(election, "_decision", decisions[i]); Set(election, "_possibleOutcomes", outcomes);
                    Set(election, "_supporters", supporters); Set(election, "_chosenOutcome", won ? yes[i] : no[i]);
                    snapshot.Invoke(null, new object[] { election, decisions[i], outcomes, supporters });
                    Clear();
                    payment.Invoke(election, null);
                    check(_balance == (won ? 0 : 10), names[i] + " pledged vote charges native " + (won ? "full" : "losing half") + " cost below reserves after cleanup");
                    if (!won)
                    {
                        _balance = 20; _refund = true; payment.Invoke(election, null); _refund = false;
                        check(_balance == 20, names[i] + " retains the native Good Natured losing-vote exemption");
                    }
                    _balance = 20; supporters.RemoveAt(1); payment.Invoke(election, null);
                    check(_balance == 20, names[i] + " retains the native sole-voter exemption");
                    finish.Invoke(null, new object[] { election, null });
                    check(AccessTools.Method(PatchType("VotePledgePaymentPatch"), "GetPaymentKey")
                        .Invoke(null, new object[] { election, _voter }) == null, "Payment snapshot is removed after resolution");
                    yes[i].SupporterList.Clear();
                }
            }
            check(_unfulfilled == 0, "Fulfilled votes do not issue failure notifications");
            Clear(); promise[0]();
            snapshot.Invoke(null, new object[] { Blank<KingdomElection>(), fiefDecision,
                new MBList<DecisionOutcome> { fiefYes, fiefNo }, new List<Supporter> { new Supporter(_voter) } });
            check(_unfulfilled == 1, "An unfulfilled pledge is reported only when the election actually resolves");
            Clear();

            var expense = Mod.GetType("BellumCivile.NpcInfluenceExpenseKind");
            var budget = Mod.GetType("BellumCivile.NpcInfluenceBudgetService");
            float Reserve(string kind) => (float)AccessTools.Method(budget, "GetProtectedReserve")
                .Invoke(null, new[] { (object)_voter, 10f, Enum.Parse(expense, kind) });
            for (int i = 0; i < 12; i++)
            {
                var another = Blank<PolicyObject>(); another.StringId = "policy_" + i;
                policies.SetBribedVote(_realm, another, _voter, 500);
            }
            _balance = 240;
            check(Reserved() == 240 && Reserve("CouncilCommitment") == 240 && Reserve("Discretionary") == 240
                && Reserve("CrownEmergency") == 240 && Reserve("InvoluntaryLoss") == 0,
                "Voluntary expenses protect outstanding minimum votes; involuntary losses remain possible");
            float charged = (float)AccessTools.Method(budget, "SpendPledgedVote").Invoke(null,
                new object[] { _voter, 20f, "policy|realm|policy_0|voter" });
            check(charged == 20 && _balance == 220, "Pledged payment protects the other eleven promises");
            for (int i = 0; i < 12; i++)
            {
                var another = Blank<PolicyObject>(); another.StringId = "policy_" + i;
                policies.ClearBribedVotesForPolicy(_realm, another);
            }
            check(Reserved() == 0 && Reserve("CouncilCommitment") == 200, "Normal role reserve returns after all promises clear");

            foreach (Action commit in promise) commit();
            fiefs.SetBribedVote(_realm, settlement, _voter, (int)FactionType.Glory);
            check(Reserved() == 80, "Legacy faction and explicit candidate pledges for one fief are not double counted");
            var store = new Store();
            foreach (var behavior in manager.Items) behavior.SyncData(store);
            manager.ClearBehaviors(); Call("Invalidate");
            check(Reserved() == 0, "An empty campaign cannot reuse another campaign's pledge index");
            var restored = new CampaignBehaviorBase[] { new FiefDeliberationBehavior(), new PolicyDeliberationBehavior(),
                new ExpulsionDeliberationBehavior(), new CouncilAppointmentDeliberationBehavior() };
            store.IsLoading = true;
            foreach (var behavior in restored) { manager.AddBehavior(behavior); behavior.SyncData(store); }
            check(Reserved() == 80, "All four promise reservations rebuild from existing save records");
            _balance = 80;
            for (int i = 0; i < decisions.Length; i++)
                check(Vote(decisions[i], yes[i], no[i], out _) == yes[i], names[i] + " promise still works after behavior reload");
            manager.ClearBehaviors(); manager.Items.AddRange(new CampaignBehaviorBase[] { fiefs, policies, expulsions, council });
            Call("Invalidate"); Clear();

            // Keep native barter finalization and the production guard; stub only world/UI side effects.
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "PartyBelongedTo"), nameof(NoParty));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "MapFaction"), nameof(MapRealm));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "HasPendingFiefVoteForSettlement"), nameof(Pending));
            Patch(AccessTools.Method(typeof(PolicyDeliberationBehavior), "HasPendingVoteForPolicy"), nameof(Pending));
            Patch(AccessTools.Method(typeof(ExpulsionDeliberationBehavior), "HasPendingExpulsionForTarget"), nameof(Pending));
            Patch(AccessTools.Method(typeof(CouncilAppointmentDeliberationBehavior), "HasPendingAppointment",
                new[] { typeof(Kingdom), typeof(PrivyCouncilOffice) }), nameof(Pending));
            Patch(AccessTools.Method(typeof(BarterManager), "IsOfferAcceptable"), nameof(Acceptable));
            Patch(AccessTools.Method(typeof(BarterManager), "CancelAndFinalizePlayerBarter"), nameof(Cancel));
            Patch(AccessTools.Method(typeof(BarterManager), "ApplyBarterOffer"), nameof(Pay));
            Patch(AccessTools.Method(typeof(BarterManager), "HandleHeroCooldown"), nameof(Skip));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.BellumCivileNotifications"), "ShowPersonal",
                new[] { typeof(TextObject), typeof(Color) }), nameof(Skip));
            var barterManager = Blank<BarterManager>();
            Func<Barterable>[] tokens = {
                () => new FiefVoteBribeBarterable(_voter, _realm, settlement, _candidate, 100, _playerHero),
                () => new PolicyVoteBribeBarterable(_voter, _realm, policy, true, 100, _playerHero),
                () => new ExpulsionVoteBribeBarterable(_voter, _realm, _candidate, true, 100, _playerHero),
                () => new CouncilAppointmentVoteBribeBarterable(_voter, _realm, PrivyCouncilOffice.Marshal, _candidate, 100, _playerHero)
            };
            foreach (var tokenFactory in tokens)
            {
                foreach (string state in new[] { "poor", "stale", "unoffered", "unacceptable", "funded" })
                {
                    Clear(); _paid = _cancelled = 0;
                    _balance = state == "poor" ? 19 : 20;
                    _pending = state != "stale"; _offerAcceptable = state != "unacceptable";
                    var token = tokenFactory(); token.SetIsOffered(state != "unoffered");
                    var data = Blank<BarterData>(); Set(data, "_barterables", new List<Barterable> { token });
                    barterManager.ApplyAndFinalizePlayerBarter(_playerHero, _leader, data);
                    bool funded = state == "funded";
                    check(_paid == (funded ? 1 : 0) && _cancelled == (funded ? 0 : 1)
                        && Reserved() == (funded ? 20 : 0), token.GetType().Name + " validates before payment: " + state);
                    if (funded)
                    {
                        barterManager.ApplyAndFinalizePlayerBarter(_playerHero, _leader, data);
                        check(_paid == 1 && _cancelled == 1 && Reserved() == 20, "Repeated barter confirmation cannot charge twice");
                    }
                }
            }
            Clear();
        }
        finally
        {
            Call("Invalidate");
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
