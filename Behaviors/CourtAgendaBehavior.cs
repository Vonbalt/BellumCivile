using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior : CampaignBehaviorBase
    {
        public static CourtAgendaBehavior Current => Campaign.Current?.CampaignBehaviorManager?.GetBehavior<CourtAgendaBehavior>();
        internal CampaignTime NextEvaluation(Kingdom realm)
        {
            if (realm == null) return CampaignTime.Now;
            if (!_nextTerms.TryGetValue(realm.StringId, out var next))
                _nextTerms[realm.StringId] = next = CampaignTime.Days((float)CourtAgendaRules.NextBoundary(
                    CampaignTime.Now.ToDays, BellumCivileOptions.CourtTermDays));
            return next;
        }
        private List<CourtAgendaRecord> _agendas = new List<CourtAgendaRecord>();
        private Dictionary<string, CampaignTime> _nextTerms = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _reconsiderAfter = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _emergencyAfter = new Dictionary<string, CampaignTime>();
        private readonly CourtObjectiveSelector<CourtTermContext, CourtObjectiveOwner> _objectiveSelector = CreateObjectiveSelector();

        private static CourtObjectiveSelector<CourtTermContext, CourtObjectiveOwner> CreateObjectiveSelector()
        {
            var selector = new CourtObjectiveSelector<CourtTermContext, CourtObjectiveOwner>();
            selector.Register(new CourtPolicyObjectiveSource(FilingCost));
            selector.Register(new CourtExecutiveObjectiveSource(CourtExecutiveRules.Treason));
            selector.Register(new CourtExecutiveObjectiveSource(CourtExecutiveRules.Grant));
            selector.Register(new CourtExecutiveObjectiveSource(CourtExecutiveRules.Revoke));
            selector.Register(new CourtCouncilObjectiveSource());
            selector.Register(new CourtMandateObjectiveSource());
            selector.Register(new CourtActivityObjectiveSource());
            selector.Register(new CourtAppeasementObjectiveSource());
            selector.Register(new CourtPeaceObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtCampaignObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtSubjugationObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtClaimObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtDynasticObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtProtectionObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtTradeObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtRallyObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtLiberationObjectiveSource(), "foreign_affairs");
            selector.Register(new CourtTitleGrantObjectiveSource(CourtTitleGrantRules.Grant), "higher_titles");
            selector.Register(new CourtTitleGrantObjectiveSource(CourtTitleGrantRules.Petition), "higher_titles");
            selector.Register(new CourtClientGrantObjectiveSource());
            return selector;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, RegisterMandateDialogues);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, (clan, oldRealm, newRealm, detail, show) =>
            { if (oldRealm != newRealm) ClearMandatePledges(clan, null); });
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (oldLeader, newLeader) => ClearMandatePledges(null, oldLeader));
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, _ => ReconcileCrowns());
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, realm =>
            {
                if (realm == null || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) return;
                // Creation can be announced before the ruling clan is assigned.
                if (!_crownReignStarts.ContainsKey(realm.StringId)) _crownReignStarts[realm.StringId] = CampaignTime.Now;
                if (ValidRealm(realm)) UpdateCrownReign(realm, newRealm: true);
            });
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (oldLeader, newLeader) => ReconcileCrowns());
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (oldLeader, newLeader) => EndChangedRulerAccommodations());
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, (realm, clan) => EndChangedRulerAccommodations());
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => { ReconcileCrowns(); FreezeExistingSchedules(); MigratePlayerCrownBusiness(); EndChangedRulerAccommodations(); });
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Tick);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnCourtPeace);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnCourtCampaignWar);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnRoyalPeaceInvasion);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnRoyalPeaceEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnCourtClaimOwnerChanged);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainClaimObjectives);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainDynasticObjectives);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainProtectionOffers);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainTradeObjectives);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainTitleGrants);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainRallies);
            CampaignEvents.OnTradeAgreementSignedEvent.AddNonSerializedListener(this, OnCourtTradeSigned);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnCourtTradeDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnCourtTradeDecisionConcluded);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnCourtDynasticDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnCourtDynasticDecisionConcluded);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnCourtTradeWar);
            CampaignEvents.OnMarriageOfferCanceledEvent.AddNonSerializedListener(this, OnCourtMarriageOfferCancelled);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, ProcessPlayerInquiry);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_CourtTrade_RepeatUntil", ref _tradeRepeatUntil);
            _tradeRepeatUntil = _tradeRepeatUntil ?? new Dictionary<string, double>();
            foreach (var agenda in _agendas) agenda.GetObjective();
            foreach (var agenda in _agendas.Where(IsMandate))
                if (agenda.Mandate != null && agenda.Mandate.Pledges == null) agenda.Mandate.Pledges = new List<CourtMandatePledge>();
            SyncCrownData(dataStore);
            dataStore.SyncData("BC_CrownActionUntil", ref _crownActionUntil);
            _crownActionUntil = _crownActionUntil ?? new Dictionary<string, CampaignTime>();
            dataStore.SyncData("BC_CourtMandateSettledUntil", ref _mandateSettledUntil);
            _mandateSettledUntil = _mandateSettledUntil ?? new Dictionary<string, CampaignTime>();
            dataStore.SyncData("BC_CourtAgendas", ref _agendas);
            dataStore.SyncData("BC_CourtProtectionOffers", ref _protectionOffers);
            dataStore.SyncData("BC_CourtTitleDeliveries", ref _titleDeliveries);
            dataStore.SyncData("BC_CourtRallies", ref _rallies);
            _rallies = _rallies ?? new List<CourtAgendaRecord>();
            _rallyIndex = null;
            _titleDeliveries = _titleDeliveries ?? new List<CourtTitleGrantRecord>();
            _protectionOffers = _protectionOffers ?? new List<CourtProtectionRecord>();
            dataStore.SyncData("BC_CourtClaimGrace", ref _claimGrace);
            _claimGrace = _claimGrace ?? new List<CourtAgendaRecord>();
            dataStore.SyncData("BC_CourtTerms", ref _nextTerms);
            dataStore.SyncData("BC_CourtReconsiderAfter", ref _reconsiderAfter);
            dataStore.SyncData("BC_CourtEmergencyAfter", ref _emergencyAfter);
            dataStore.SyncData("BC_CourtDecreeCases", ref _decreeCases);
            dataStore.SyncData("BC_CourtRoyalPeaceCases", ref _royalPeaceCases);
            _royalPeaceCases = _royalPeaceCases ?? new List<CourtRoyalPeaceCase>();
            dataStore.SyncData("BC_CourtCouncilSettledUntil", ref _councilSettledUntil);
            dataStore.SyncData("BC_CourtCouncilContestedDay", ref _councilContestedDay);
            _councilSettledUntil = _councilSettledUntil ?? new Dictionary<string, CampaignTime>();
            _councilContestedDay = _councilContestedDay ?? new Dictionary<string, float>();
            _decreeCases = _decreeCases ?? new List<CourtDecreeCase>();
            _agendas = _agendas ?? new List<CourtAgendaRecord>();
            SyncReactionHistory(dataStore);
            _nextTerms = _nextTerms ?? new Dictionary<string, CampaignTime>();
            _reconsiderAfter = _reconsiderAfter ?? new Dictionary<string, CampaignTime>();
            _emergencyAfter = _emergencyAfter ?? new Dictionary<string, CampaignTime>();
            foreach (var agenda in _agendas) agenda.GetObjective();
            foreach (var agenda in _agendas.Where(IsMandate))
                if (agenda.Mandate != null && agenda.Mandate.Pledges == null) agenda.Mandate.Pledges = new List<CourtMandatePledge>();
            RecoverCrownActionCooldowns();
        }

        internal static bool ValidRealm(Kingdom realm) => realm != null && !realm.IsEliminated
            && realm.RulingClan?.Leader != null && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);
        internal static bool Eligible(Clan clan, Kingdom realm) => clan != null && !clan.IsEliminated
            && (!clan.IsMinorFaction || clan == Clan.PlayerClan) && !clan.IsUnderMercenaryService && clan.Kingdom == realm && clan.Leader != null;
        internal static int MemberCount(FactionObject faction) => faction?.Members
            .Where(c => Eligible(c, faction.ParentKingdom) && (!faction.IsIdeology || c != faction.ParentKingdom.RulingClan)).Distinct().Count() ?? 0;
        private static string Key(Kingdom realm, PolicyObject policy) => realm.StringId + "|" + policy.StringId;
        private static PolicyObject Policy(CourtAgendaRecord agenda) => PolicyObject.All.FirstOrDefault(p => p.StringId == agenda.PolicyId);
        private static bool Reached(CourtAgendaRecord agenda, PolicyObject policy) => agenda.Realm.ActivePolicies.Contains(policy) != agenda.Abolish;

        private static int FilingCost(KingdomPolicyDecision decision, FactionObject faction)
        {
            int cost = decision.GetProposalInfluenceCost();
            return faction == null ? cost : Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetFactionMotionInfluenceCost(decision.ProposerClan, cost) ?? cost;
        }

        public bool HasConcluded(Kingdom realm, PolicyObject policy, Clan sponsor) => _agendas.Any(a =>
            a.Realm == realm && a.PolicyId == policy.StringId && a.Sponsor == sponsor && a.ResultApplied);

        public bool CanPropose(Kingdom realm, PolicyObject policy)
        {
            if (!ValidRealm(realm) || policy == null) return false;
            return (!_reconsiderAfter.TryGetValue(Key(realm, policy), out var date) || date.IsPast)
                && !realm.UnresolvedDecisions.OfType<KingdomPolicyDecision>().Any(d => d.Policy == policy)
                && PolicyDeliberationBehavior.Current?.HasPendingVoteForPolicy(realm, policy) != true;
        }

        public CourtAgendaRecord GetAgenda(Kingdom realm, FactionObject faction) =>
            _agendas.LastOrDefault(a => !a.Manual && a.Realm == realm && a.Faction == faction);

        public CampaignTime? GetScheduledVoteDate(Kingdom realm, PolicyObject policy, Clan sponsor) =>
            _agendas.LastOrDefault(a => a.Realm == realm && a.PolicyId == policy?.StringId && a.Sponsor == sponsor
                && a.IsFiled && a.HasScheduleSnapshot)?.VoteDate;

        private void FreezeExistingSchedules()
        {
            // Older saves have no timing snapshot. Preserve any date already in the vote queue.
            foreach (var agenda in _agendas.Where(a => !a.HasScheduleSnapshot))
            {
                var liveVote = agenda.Realm?.UnresolvedDecisions.OfType<KingdomPolicyDecision>()
                    .FirstOrDefault(d => d.Policy?.StringId == agenda.PolicyId && d.ProposerClan == agenda.Sponsor);
                agenda.FreezeSchedule(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays,
                    PolicyDeliberationBehavior.Current?.GetPendingVoteDate(agenda.Realm, Policy(agenda)) ?? liveVote?.TriggerTime);
            }
        }

        public CourtAgendaRecord GetDisplayedAgenda(Kingdom realm, FactionObject faction)
        {
            if (faction == null && ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null) return null;
            if (faction == null && realm?.RulingClan == Clan.PlayerClan)
                return DisplayedPlayerCrownBusiness(realm).FirstOrDefault();
            var agenda = GetAgenda(realm, faction);
            if (faction == null)
                agenda = agenda?.IsPolicy == false || agenda?.PlayerSelectionConfirmed == true || agenda?.State == CourtAgendaState.AwaitingPlayerDecision
                    ? agenda : _agendas.LastOrDefault(a => a.Manual && a.Realm == realm && a.Sponsor == realm?.RulingClan) ?? agenda;
            agenda?.GetObjective();
            return agenda;
        }

        public string DescribeAgenda(Kingdom realm, FactionObject faction, bool compact = false)
        {
            if (faction == null && ElectiveSuccessionBehavior.Instance?.DepositionAgenda(realm) is string election) return election;
            if (faction == null && realm?.RulingClan == Clan.PlayerClan)
            {
                var proceedings = DisplayedPlayerCrownBusiness(realm);
                var lines = proceedings.Select(a => DescribeAgendaEntry(realm, null, compact, a))
                    .Concat(CrownAllocationDescriptions(realm, proceedings)).ToArray();
                if (lines.Length > 0) return string.Join("\n", lines);
            }
            var agenda = GetDisplayedAgenda(realm, faction);
            return DescribeAgendaEntry(realm, faction, compact, agenda);
        }

        private string DescribeAgendaEntry(Kingdom realm, FactionObject faction, bool compact, CourtAgendaRecord agenda)
        {
            if (agenda == null && faction == null && realm?.RulingClan == Clan.PlayerClan)
                return new TextObject("{=BC_CrownNoProceedings}Current agenda: No business before the Crown").ToString();
            var text = new TextObject(compact ? "{=BC_CurrentAgendaCompact}Current agenda: {OBJECTIVE} ({STATUS})"
                : "{=BC_CurrentAgendaDetails}Current agenda: {OBJECTIVE}\n{STATUS}");
            PolicyObject policy = agenda == null ? null : Policy(agenda);
            var objective = new TextObject(agenda?.State == CourtAgendaState.AwaitingNomination
                ? (agenda.NominationKind == CourtCouncilObjectiveSource.CouncilKind
                    ? "{=BC_CourtAwaitingCouncilNomination}Awaiting your council appointment proposal" : "{=BC_CourtAwaitingNomination}Awaiting your policy nomination")
                : agenda?.State == CourtAgendaState.NominationExpired || agenda?.State == CourtAgendaState.Blocked ? "{=BC_CourtNoMotion}No motion proposed"
                : agenda?.State == CourtAgendaState.Crisis
                ? "{=BC_CourtChallenge}Challenge the ruler's authority"
                : policy == null ? "{=BC_CourtNoMotion}No motion proposed" : agenda.Abolish
                    ? "{=BC_CourtRepealObjective}Repeal {POLICY}" : "{=BC_CourtEnactObjective}Enact {POLICY}");
            if (policy != null) objective.SetTextVariable("POLICY", policy.Name);
            if (agenda?.IsPolicy == false && agenda.State != CourtAgendaState.Crisis
                && agenda.State != CourtAgendaState.AwaitingNomination && agenda.State != CourtAgendaState.NominationExpired)
                objective = ExecutiveObjectiveText(agenda);
            text.SetTextVariable("OBJECTIVE", objective);
            TextObject status;
            if (agenda == null) status = new TextObject("{=BC_CourtAwaitingTerm}Awaiting the next term.");
            else if (agenda.CrisisInterventionPending)
                status = new TextObject("{=BC_CourtCrisisDecisionPending}The faction demands a crisis agenda. Awaiting your response.");
            else if (agenda.State == CourtAgendaState.AwaitingPlayerDecision)
                status = new TextObject("{=BC_CourtAgendaDecisionPending}Awaiting your approval, substitution or veto.");
            else if (agenda.State == CourtAgendaState.AwaitingNomination)
            {
                status = new TextObject("{=BC_CourtMandateDates}Mandate expires {DEADLINE}. Deliberation begins when you file your proposal.");
                status.SetTextVariable("DEADLINE", agenda.NominationDeadline.ToString());
                status.SetTextVariable("DATE", agenda.SessionDate.ToString());
            }
            else if (IsCampaignObjective(agenda)) status = CampaignStatus(agenda);
            else if (IsSubjugationObjective(agenda)) status = SubjugationStatus(agenda);
            else if (IsClaimObjective(agenda)) status = ClaimStatus(agenda);
            else if (IsDynastic(agenda)) status = DynasticStatus(agenda);
            else if (IsTrade(agenda)) status = TradeStatus(agenda);
            else if (IsTitleGrant(agenda)) status = TitleGrantStatus(agenda);
            else if (IsClientGrant(agenda)) status = new TextObject("{=BC_ClientGrantStatus}A voluntary grant to a client realm, costing 100 influence. No distribution vote is held. Only the Crown's own rights accompany the holding; higher titles are not granted.");
            else if (IsRally(agenda)) status = RallyStatus(agenda);
            else if (IsLiberation(agenda)) status = new TextObject("{=BC_CourtLiberationStatus}Preparations strengthen the realm's desire for self-rule by +20 during this term. A declaration still requires sufficient readiness, war enthusiasm and the usual vote. Beginning the war completes this objective; it does not guarantee independence.");
            else if (IsProtection(agenda)) status = new TextObject("{=BC_ProtectionStatus}A single appeal for protection this term. Submission requires the protector to join the named war; no faction mood reward or penalty applies.");
            else if (IsPeaceObjective(agenda)) status = PeaceStatus(agenda);
            else if (IsActivity(agenda) || IsAppeasement(agenda)) status = ActivityStatus(agenda);
            else if (IsRoyalPeace(agenda) && agenda.IsUnopened)
                status = new TextObject("{=BC_RoyalPeaceStatus}The Crown has ordered the feuding houses to lay down their private banners on {DATE}.")
                    .SetTextVariable("DATE", agenda.SessionDate.ToString());
            else if (agenda.IsUnopened)
            {
                status = policy == null ? new TextObject("{=BC_CourtSessionDate}Deliberation session begins {DATE}.")
                    : new TextObject("{=BC_CourtSessionAndVoteDates}Deliberations begin {DATE}. Voting is scheduled for {VOTE_DATE}.");
                status.SetTextVariable("DATE", agenda.SessionDate.ToString());
                status.SetTextVariable("VOTE_DATE", agenda.VoteDate.ToString());
                if (agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree)
                {
                    status = new TextObject("{=BC_CourtDecreeDate}Royal judgment is appointed for {DATE}.");
                    status.SetTextVariable("DATE", agenda.SessionDate.ToString());
                }
            }
            else if (agenda.State == CourtAgendaState.Deliberating)
            {
                status = new TextObject("{=BC_CourtScheduledVote}Deliberating; voting is scheduled for {DATE}.");
                status.SetTextVariable("DATE", (agenda.AllocationStage ? agenda.AllocationVoteDate : agenda.VoteDate).ToString());
            }
            else status = new TextObject("{=BC_CourtStatus_" + agenda.State + "}" + StatusLabel(agenda.State));
            if (compact)
                status = new TextObject(CourtAgendaPresentation.Status(agenda?.State, agenda?.CrisisInterventionPending == true,
                    IsCampaignObjective(agenda), IsSubjugationObjective(agenda), IsClaimObjective(agenda), IsDynastic(agenda), IsProtection(agenda), IsTrade(agenda), IsTitleGrant(agenda), IsRally(agenda), IsLiberation(agenda)));
            text.SetTextVariable("STATUS", status);
            if (compact && CourtAgendaPresentation.Deadline(agenda) is var deadline && deadline.HasValue)
            {
                var dateText = new TextObject(deadline.Value.text).SetTextVariable("DATE", deadline.Value.date.ToString());
                text = new TextObject("{=BC_CurrentAgendaCompactDated}{AGENDA}\n{DEADLINE}")
                    .SetTextVariable("AGENDA", text).SetTextVariable("DEADLINE", dateText);
            }
            return text.ToString();
        }

        private static string StatusLabel(CourtAgendaState state)
        {
            switch (state)
            {
                case CourtAgendaState.NotProposed: return "Not brought to a vote.";
                case CourtAgendaState.FulfilledElsewhere: return "Fulfilled elsewhere.";
                case CourtAgendaState.TooWeak: return "Challenge ended: insufficient backing.";
                case CourtAgendaState.Withdrawn: return "Challenge withdrawn.";
                case CourtAgendaState.Ultimatum: return "Coalition ultimatum issued.";
                case CourtAgendaState.Blocked: return "Blocked by the faction leader.";
                case CourtAgendaState.NominationExpired: return "No nomination before the promised deadline.";
                default: return state + ".";
            }
        }

        private void Tick()
        {
            MigratePlayerCrownBusiness();
            _recentResults.RemoveAll(a => a == null || !a.ResultVisibleUntil.IsFuture || !ReactionApproval(a).HasValue);
            FreezeExistingSchedules();
            // Resolve expired objectives before OpenTerm cleans up or selects replacement business.
            MaintainPeaceObjectives();
            MaintainLiberationObjectives();
            MaintainCampaignObjectives();
            MaintainSubjugationObjectives();
            MaintainClaimObjectives();
            MaintainDynasticObjectives();
            MaintainTradeObjectives();
            MaintainTitleGrants();
            MaintainClientGrants();
            MaintainRallies();
            MaintainProtectionOffers();
            ReconcileCrowns();
            MaintainRoyalPeaceCases();
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (manager == null || ideology == null) return;
            foreach (var agenda in _agendas.ToList())
                if (!ValidRealm(agenda.Realm) && (agenda.IsUnopened || agenda.IsFiled)) Cancel(agenda, "realm_invalid");

            foreach (Kingdom realm in Kingdom.All.Where(ValidRealm).ToList())
            {
                ElectiveSuccessionBehavior.Instance?.CheckMandate(realm);
                HereditaryLoyaltyBehavior.Instance?.Maintain(realm);
                ScanDecreeCases(realm, ideology);
                MaintainCouncilVacancyPriority(realm);
                if (!_nextTerms.TryGetValue(realm.StringId, out var next))
                {
                    double interval = BellumCivileOptions.CourtTermDays;
                    _nextTerms[realm.StringId] = CampaignTime.Days((float)CourtAgendaRules.NextBoundary(CampaignTime.Now.ToDays, interval));
                }
                else if (next.IsPast)
                {
                    // Publish the next boundary first; inquiries cannot reopen the same term.
                    _nextTerms[realm.StringId] = CampaignTime.Now + CampaignTime.Days(BellumCivileOptions.CourtTermDays);
                    OpenTerm(realm, manager, ideology);
                }

                foreach (var faction in manager.GetFactionsInKingdom(realm).Where(f => f.IsIdeology).ToList())
                {
                    string emergencyKey = realm.StringId + "|" + (int)faction.Type;
                    if (faction.Mood > -100) { _emergencyAfter.Remove(emergencyKey); continue; }
                    if (MemberCount(faction) < 2 || (_emergencyAfter.TryGetValue(emergencyKey, out var retry) && !retry.IsPast)) continue;
                    _emergencyAfter[emergencyKey] = CampaignTime.Now + CampaignTime.Days(7);
                    if (ideology.TryLaunchCourtChallenge(faction, realm))
                    {
                        var agenda = GetAgenda(realm, faction);
                        if (agenda?.IsUnopened == true)
                        {
                            agenda.CrisisInterventionPending = false;
                            agenda.State = CourtAgendaState.Ultimatum;
                        }
                    }
                }

                foreach (var agenda in _agendas.Where(a => a.Realm == realm && (a.IsUnopened || a.IsFiled)).ToList())
                    Advance(agenda, ideology);
            }
            foreach (string key in _reconsiderAfter.Where(p => p.Value.IsPast).Select(p => p.Key).ToList()) _reconsiderAfter.Remove(key);
            foreach (string key in _mandateSettledUntil.Where(p => p.Value.IsPast).Select(p => p.Key).ToList()) _mandateSettledUntil.Remove(key);
            foreach (string key in _emergencyAfter.Where(p => p.Value.IsPast).Select(p => p.Key).ToList()) _emergencyAfter.Remove(key);
            var livingRealms = new HashSet<string>(Kingdom.All.Where(ValidRealm).Select(k => k.StringId));
            foreach (string key in _nextTerms.Keys.Where(k => !livingRealms.Contains(k)).ToList()) _nextTerms.Remove(key);
            _agendas.RemoveAll(a => !ValidRealm(a.Realm) && !a.IsFiled && !a.IsUnopened);
            SettleClosedExecutiveObjectives();
            foreach (var agenda in _agendas) agenda.GetObjective();
        }

        private void OpenTerm(Kingdom realm, FactionManagerBehavior manager, IdeologyBehavior ideology)
        {
            foreach (var old in _agendas.Where(a => a.Realm == realm && a.State == CourtAgendaState.AwaitingNomination).ToList())
                if (ValidPlayerAgenda(old)) UpdatePlayerAgenda(old);
            ideology.BeginCourtTerm(realm);
            ElectiveSuccessionBehavior.Instance?.Maintain(realm, renewal: true);
            OpenCrownTerm(realm, manager);
            _agendas.RemoveAll(a => a.Realm == realm && !a.IsFiled && !a.IsUnopened && !a.IsOngoingObjective);
            var factions = manager.GetFactionsInKingdom(realm).Where(f => f.IsIdeology && MemberCount(f) >= 2).ToList();
            var context = new CourtTermContext(realm, policy => CanPropose(realm, policy));
            var selecting = new List<CourtAgendaRecord>();
            foreach (FactionObject owner in factions.Concat(new FactionObject[] { null }))
            {
                if (owner == null && realm.RulingClan == Clan.PlayerClan) continue;
                if (owner == null && ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null) continue;
                var existing = GetAgenda(realm, owner);
                if (existing?.IsFiled == true || existing?.IsUnopened == true || existing?.IsOngoingObjective == true) continue;
                if (owner == null && _agendas.Any(a => a.Realm == realm && a.Sponsor == realm.RulingClan && a.IsFiled)) continue;
                Clan sponsor = owner?.Leader ?? realm.RulingClan;
                if (!Eligible(sponsor, realm)) continue;
                int nominationDays = CourtAgendaRules.NominationDays(BellumCivileOptions.CourtTermDays);
                int earliest = nominationDays + 1;
                int latest = Math.Max(earliest, (int)BellumCivileOptions.CourtTermDays - BellumCivileOptions.PoliticalDeliberationDays);
                var agenda = new CourtAgendaRecord { Realm = realm, Faction = owner, Sponsor = sponsor,
                    NominationWindowDays = nominationDays, State = CourtAgendaState.NotProposed,
                    SessionDate = CampaignTime.Now + CampaignTime.Days(earliest + MBRandom.RandomInt(latest - earliest + 1)) };
                agenda.FreezeSchedule(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays);
                agenda.GetObjective().FreezeTerm(CampaignTime.Now.ToDays, _nextTerms[realm.StringId].ToDays);
                _agendas.Add(agenda);
                if (owner == null && AssignPriorityDecree(agenda)) continue;
                if (owner == null && AssignPriorityRoyalPeace(agenda)) continue;
                if (owner == null)
                {
                    MaintainCouncilVacancyPriority(realm);
                    if (IsCouncil(agenda) || GetAgenda(realm, null) != agenda) continue;
                    if (sponsor == Clan.PlayerClan)
                    {
                        agenda.State = CourtAgendaState.AwaitingPlayerDecision;
                        continue;
                    }
                }
                bool playerLed = owner != null && sponsor == Clan.PlayerClan;
                if (!playerLed && owner?.Mood <= -60) { agenda.State = CourtAgendaState.Crisis; continue; }
                if (playerLed) agenda.State = CourtAgendaState.AwaitingPlayerDecision;
                if (playerLed && owner.Mood <= -60) agenda.CrisisInterventionPending = true;
                selecting.Add(agenda);
            }
            var choices = _objectiveSelector.SelectBatch(context,
                selecting.Select(a => new CourtObjectiveOwner(a.Faction, a.Sponsor)).ToList(), new[] { "council_appointment", CourtMandateRules.Kind },
                    () => MBRandom.RandomFloat,
                    (owner, candidate) => BellumCivileDebug.TraceIfEnabled("court-objectives",
                        $"realm={realm.StringId}; owner={owner.Faction?.Type.ToString() ?? "Crown"}; kind={candidate.Kind}; target={candidate.Candidate.TargetId}; action={candidate.Candidate.ActionId}; eligible={candidate.Evaluation.Eligible}; viable={candidate.Evaluation.Viable}; {candidate.Evaluation.Weight}; {candidate.Evaluation.Reason}"));
            for (int i = 0; i < selecting.Count; i++)
            {
                var agenda = selecting[i];
                var owner = agenda.Faction;
                var sponsor = agenda.Sponsor;
                bool playerLed = owner != null && sponsor == Clan.PlayerClan;
                var choice = choices[i];
                if (choice == null) continue;
                ((ICourtAgendaObjectiveSource)_objectiveSelector.FindSource(choice.Kind)).ApplySelection(agenda, choice);
                if (owner == null && sponsor == Clan.PlayerClan && !agenda.IsPolicy)
                { playerLed = true; agenda.State = CourtAgendaState.AwaitingPlayerDecision; }
                if (!playerLed) agenda.State = CourtAgendaState.Announced;
                agenda.GetObjective();
                if (!agenda.IsPolicy) NotifyAgenda(agenda);
                BellumCivileLogger.Log($"Court agenda announced; realm={realm.StringId}; owner={owner?.Type.ToString() ?? "Crown"}; policy={agenda.PolicyId}; repeal={agenda.Abolish}; date={agenda.SessionDate.ToDays}; {choice.Evaluation.Reason}.");
            }
            var message = new TextObject("{=BC_CourtTermOpened}The court of {REALM} has declared its agendas for the new term.");
            message.SetTextVariable("REALM", realm.Name);
            BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: realm);
        }

        private void Advance(CourtAgendaRecord agenda, IdeologyBehavior ideology)
        {
            if (IsRoyalPeace(agenda)) { AdvanceRoyalPeace(agenda); return; }
            if (TryWithdrawChangedPolicyAgenda(agenda)) return;
            if (agenda.State == CourtAgendaState.AwaitingNomination && !UpdatePlayerAgenda(agenda)) return;
            ReviewInheritedFactionAgenda(agenda.Faction);
            if (agenda.Faction != null && (agenda.Faction.ParentKingdom != agenda.Realm || !agenda.Faction.IsIdeology)) { Cancel(agenda, "faction_invalid"); return; }
            if (!Eligible(agenda.Sponsor, agenda.Realm)) { Cancel(agenda, "sponsor_ineligible"); return; }
            if (agenda.IsUnopened)
            {
                if (agenda.Faction != null && MemberCount(agenda.Faction) < 2) { Cancel(agenda, "insufficient_members_before_filing"); return; }
                agenda.Sponsor = agenda.Faction?.Leader ?? agenda.Realm.RulingClan;
                if (!Eligible(agenda.Sponsor, agenda.Realm)) { Cancel(agenda, "replacement_sponsor_ineligible"); return; }
                if (!UpdatePlayerAgenda(agenda)) return;
                if ((agenda.State == CourtAgendaState.Announced || agenda.State == CourtAgendaState.AwaitingNomination
                    || agenda.State == CourtAgendaState.AwaitingPlayerDecision) && agenda.Faction?.Mood <= -60
                    && !agenda.OrdinaryCrisisRestrained)
                {
                    if (agenda.Faction.Leader == Clan.PlayerClan)
                    {
                        agenda.CrisisInterventionPending = true;
                        return;
                    }
                    agenda.State = CourtAgendaState.Crisis;
                    var message = new TextObject("{=BC_CourtAgendaCrisis}The {FACTION} of {REALM} has abandoned its legislative agenda to confront what it regards as abuses of {RULER}'s authority. Its leaders will convene on {DATE} to decide whether to demand their removal.");
                    message.SetTextVariable("FACTION", agenda.Faction.GetDisplayName());
                    message.SetTextVariable("REALM", agenda.Realm.Name);
                    message.SetTextVariable("RULER", agenda.Realm.RulingClan.Leader.Name);
                    message.SetTextVariable("DATE", agenda.SessionDate.ToString());
                    BellumCivileNotifications.Show(message, BellumNotificationColors.Danger, primaryKingdom: agenda.Realm);
                }
                if (agenda.State == CourtAgendaState.AwaitingPlayerDecision || agenda.State == CourtAgendaState.AwaitingNomination) return;
                if (!agenda.SessionDate.IsPast) return;
                if (agenda.State == CourtAgendaState.Crisis)
                {
                    agenda.State = agenda.Faction.Mood >= -20 ? CourtAgendaState.Withdrawn
                        : ideology.TryLaunchCourtChallenge(agenda.Faction, agenda.Realm) ? CourtAgendaState.Ultimatum : CourtAgendaState.TooWeak;
                    return;
                }
                if (!agenda.IsPolicy) { AdvanceExecutive(agenda, ideology); return; }
                PolicyObject policy = Policy(agenda);
                if (policy == null) { Cancel(agenda, "policy_missing"); return; }
                if (Reached(agenda, policy)) { agenda.State = CourtAgendaState.FulfilledElsewhere; return; }
                if (PolicyDeliberationBehavior.Current?.HasPendingVoteForPolicy(agenda.Realm, policy) == true
                    || agenda.Realm.UnresolvedDecisions.OfType<KingdomPolicyDecision>().Any(d => d.Policy == policy)) return;
                var decision = new KingdomPolicyDecision(agenda.Sponsor, policy, agenda.Abolish);
                int cost = FilingCost(decision, agenda.Faction);
                if (!CanPropose(agenda.Realm, policy) || (agenda.Sponsor != Clan.PlayerClan && !CourtPolicyForecast.Calculate(decision, cost).Viable) || !TryPay(agenda, cost))
                { agenda.State = CourtAgendaState.NotProposed; return; }
                agenda.State = CourtAgendaState.Deliberating;
                if (PolicyDeliberationBehavior.Current?.QueuePolicyVote(agenda.Realm, policy, agenda.Abolish, agenda.Faction, agenda.Sponsor, agenda.VoteDate) != true) Cancel(agenda, "queue_rejected_after_payment");
            }
            else
            {
                if (!agenda.IsPolicy) { AdvanceExecutive(agenda, ideology); return; }
                PolicyObject policy = Policy(agenda);
                if (policy == null) { Cancel(agenda, "policy_missing"); return; }
                bool voting = agenda.Realm.UnresolvedDecisions.OfType<KingdomPolicyDecision>().Any(d => d.Policy == policy && d.ProposerClan == agenda.Sponsor);
                if (voting) agenda.State = CourtAgendaState.Voting;
                else if (PolicyDeliberationBehavior.Current?.HasPendingVoteForPolicy(agenda.Realm, policy) != true)
                    Cancel(agenda, "filed_motion_missing_from_queue_and_vote");
            }
        }

        private static bool TryPay(CourtAgendaRecord agenda, int cost, NpcInfluenceExpenseKind expense = NpcInfluenceExpenseKind.Discretionary)
        {
            if (!NpcInfluenceBudgetService.TrySpend(agenda.Sponsor, cost, expense, "court_agenda_filing")) return false;
            agenda.PaidInfluence = cost;
            agenda.PaymentExpenseCode = (int)expense + 1;
            return true;
        }

        public bool TryRegisterManualPayment(Kingdom realm, PolicyObject policy, bool abolish)
        {
            if (!CanPropose(realm, policy)) return false;
            var agenda = new CourtAgendaRecord { Realm = realm, Sponsor = Clan.PlayerClan, PolicyId = policy.StringId,
                Abolish = abolish, Manual = true, State = CourtAgendaState.Deliberating, SessionDate = CampaignTime.Now };
            agenda.FreezeSchedule(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays);
            if (!TryPay(agenda, new KingdomPolicyDecision(Clan.PlayerClan, policy, abolish).GetProposalInfluenceCost())) return false;
            _agendas.Add(agenda);
            return true;
        }

        internal void ReplaceCrownAgendaForElection(Kingdom realm)
        {
            foreach (var agenda in _agendas.Where(a => a.Realm == realm && a.Faction == null
                && (a.IsUnopened || a.IsFiled)).ToList())
            {
                if (agenda.IsPolicy)
                    foreach (var decision in realm.UnresolvedDecisions.OfType<KingdomPolicyDecision>()
                        .Where(d => d.ProposerClan == agenda.Sponsor && d.Policy?.StringId == agenda.PolicyId).ToList())
                        realm.RemoveDecision(decision);
                Cancel(agenda, "crown_agenda_replaced_by_election");
            }
        }

        private void Cancel(CourtAgendaRecord agenda, string reason)
        {
            if (agenda.PaymentSettled || agenda.ResultApplied) return;
            CourtAgendaState previousState = agenda.State;
            bool queued = agenda.IsFiled;
            var expense = agenda.PaymentExpenseCode > 0
                ? (NpcInfluenceExpenseKind)(agenda.PaymentExpenseCode - 1)
                : IsCaptivityAppointment(agenda) ? NpcInfluenceExpenseKind.CrownEmergency : NpcInfluenceExpenseKind.Discretionary;
            int refund = agenda.SettleCancellation(reason);
            if (!agenda.IsPolicy)
            {
                agenda.ObjectiveData.Finish(CourtObjectiveState.Cancelled, CourtObjectiveCredit.None, reason);
                if (queued) CancelExecutiveQueue(agenda);
            }
            if (refund > 0 && agenda.Sponsor != null)
                NpcInfluenceBudgetService.Refund(agenda.Sponsor, refund, expense, "court_agenda_technical_failure");
            if (queued && agenda.Realm != null && Policy(agenda) is PolicyObject policy)
                PolicyDeliberationBehavior.Current?.CancelQueuedPolicyVote(agenda.Realm, policy, agenda.Sponsor?.StringId);
            BellumCivileLogger.Log($"Court agenda cancelled; realm={agenda.Realm?.StringId}; policy={agenda.PolicyId}; sponsor={agenda.Sponsor?.StringId}; reason={reason}; previous_state={previousState}; paid={agenda.PaidInfluence}; refund={(agenda.Sponsor != null ? refund : 0)}.");
        }

        internal void CancelFiledMotion(Kingdom realm, string policyId, string sponsorId, string reason)
        {
            if (realm == null || (string.IsNullOrEmpty(policyId) && string.IsNullOrEmpty(sponsorId))) return;
            var matches = _agendas.Where(a => a.IsPolicy && a.Realm == realm && (string.IsNullOrEmpty(policyId) || a.PolicyId == policyId) && a.IsFiled
                && (string.IsNullOrEmpty(sponsorId) || a.Sponsor?.StringId == sponsorId)).ToList();
            // Missing sponsor data is recoverable only when the payment owner is unambiguous.
            if (matches.Count == 1) Cancel(matches[0], reason);
        }

        internal bool WasMotionCancelled(Kingdom realm, PolicyObject policy, Clan sponsor) =>
            _agendas.LastOrDefault(a => a.Realm == realm && a.PolicyId == policy?.StringId && a.Sponsor == sponsor)?.State == CourtAgendaState.Cancelled;

        internal bool TryWithdrawChangedPolicyMotion(Kingdom realm, PolicyObject policy, Clan sponsor)
        {
            var agenda = _agendas.LastOrDefault(a => a.IsPolicy && a.Realm == realm
                && a.PolicyId == policy?.StringId && a.Sponsor == sponsor && (a.IsUnopened || a.IsFiled));
            return agenda != null && TryWithdrawChangedPolicyAgenda(agenda);
        }

        private bool TryWithdrawChangedPolicyAgenda(CourtAgendaRecord agenda)
        {
            if (agenda == null || agenda.Realm == null || !agenda.IsPolicy || agenda.Faction == null || string.IsNullOrEmpty(agenda.PolicyId)
                || agenda.State == CourtAgendaState.Crisis || agenda.State == CourtAgendaState.AwaitingNomination
                || !(agenda.IsUnopened || agenda.IsFiled)) return false;
            // Existing saves adopt the current stance; never invent an old political commitment.
            agenda.CapturePolicyStance();
            var stance = IdeologyPolicyRoster.GetEffectiveStanceForId(agenda.Faction.Type, agenda.PolicyId, agenda.Faction.Mood);
            if (!agenda.HasLostPolicyMandate(stance)) return false;
            // Once a ballot exists it must resolve normally; Conclude suppresses obsolete mandate blame.
            if (agenda.Realm.UnresolvedDecisions.OfType<KingdomPolicyDecision>()
                .Any(d => d.Policy?.StringId == agenda.PolicyId && d.ProposerClan == agenda.Sponsor)) return false;
            Cancel(agenda, "faction_policy_stance_changed");
            var message = new TextObject("{=BC_CourtPolicyStanceWithdrawn}The {FACTION} of {REALM} no longer stands behind its motion concerning {POLICY}. Its members withdraw the proposal without reproach to their leader.")
                .SetTextVariable("FACTION", agenda.Faction.GetDisplayName()).SetTextVariable("REALM", agenda.Realm.Name)
                .SetTextVariable("POLICY", Policy(agenda)?.Name ?? new TextObject(agenda.PolicyId));
            BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
            return true;
        }

        public void Conclude(Kingdom realm, PolicyObject policy, Clan proposer, bool abolish, bool passed,
            IReadOnlyDictionary<FactionObject, CourtPolicyStance> stances = null)
        {
            var agenda = _agendas.LastOrDefault(a => a.Realm == realm && a.PolicyId == policy.StringId && a.Sponsor == proposer && a.Abolish == abolish && (a.IsFiled || a.ResultApplied));
            if (agenda?.ResultApplied == true) return;
            _reconsiderAfter[Key(realm, policy)] = CampaignTime.Now + CampaignTime.Days(
                agenda?.HasScheduleSnapshot == true ? agenda.TermDays : BellumCivileOptions.CourtTermDays);
            if (agenda == null) return;
            agenda.ResultApplied = true;
            agenda.PaymentSettled = true;
            agenda.State = passed ? CourtAgendaState.Passed : CourtAgendaState.Defeated;
            agenda.GetObjective();
            var currentStance = agenda.Faction == null ? CourtPolicyStance.Neutral
                : stances != null && stances.TryGetValue(agenda.Faction, out var snapshot) ? snapshot
                : IdeologyPolicyRoster.GetEffectiveStance(agenda.Faction, policy);
            bool obsoleteMandate = agenda.HasLostPolicyMandate(currentStance);
            if (agenda.Faction?.ParentKingdom == realm && !obsoleteMandate)
            {
                float shock = passed ? BellumCivileConstants.CourtAgendaSuccessShock : BellumCivileConstants.CourtAgendaFailureShock;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, agenda.Faction.Mood + shock));
                RecordResultHistory(agenda, shock);
            }
            var message = new TextObject(obsoleteMandate
                ? "{=BC_CourtPolicyStanceResolved}The vote concerning {POLICY} in {REALM} has concluded. The {OWNER} no longer stands behind its former motion and attaches neither praise nor reproach to its leader's result."
                : passed ? "{=BC_CourtAgendaPassed}The {OWNER} of {REALM} has carried its motion concerning {POLICY}."
                : "{=BC_CourtAgendaDefeated}The {OWNER} of {REALM} has failed to carry its motion concerning {POLICY}.");
            message.SetTextVariable("OWNER", agenda.Faction?.GetDisplayName() ?? proposer.Name);
            message.SetTextVariable("REALM", realm.Name);
            message.SetTextVariable("POLICY", policy.Name);
            BellumCivileNotifications.Show(message, obsoleteMandate ? BellumNotificationColors.Politics
                : passed ? BellumNotificationColors.Success : BellumNotificationColors.Danger, primaryKingdom: realm);
            BellumCivileLogger.Log($"Court agenda concluded; realm={realm.StringId}; policy={policy.StringId}; result={agenda.State}; paid={agenda.PaidInfluence}.");
        }
    }
}
