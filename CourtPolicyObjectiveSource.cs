using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal sealed class CourtObjectiveOwner
    {
        internal FactionObject Faction { get; }
        internal Clan Sponsor { get; }
        internal CourtObjectiveOwner(FactionObject faction, Clan sponsor) { Faction = faction; Sponsor = sponsor; }
    }

    // One disposable selection-pass context per realm. Never retained across ticks or serialized.
    internal sealed class CourtTermContext
    {
        internal Kingdom Realm { get; }
        internal CourtAgendaRecord PlayerAgenda { get; }
        internal bool ManualSelection => PlayerAgenda != null;
        internal IReadOnlyList<PolicyObject> Policies { get; }
        internal HashSet<PolicyObject> Enacted { get; }
        internal IReadOnlyList<Clan> Voters { get; }
        internal IReadOnlyList<Settlement> Settlements { get; }
        private CourtCouncilSelectionContext _council;
        private CourtLiberationAssessment _liberation;
        internal CourtLiberationAssessment Liberation => _liberation ?? (_liberation = new CourtLiberationAssessment(Realm));
        internal CourtCouncilSelectionContext Council => _council ?? (_council = new CourtCouncilSelectionContext(Realm));
        private readonly Dictionary<FactionObject, bool> _activityAdmissions = new Dictionary<FactionObject, bool>();
        private bool? _clientGrantAdmitted;
        internal bool ClientGrantAdmitted => ManualSelection || (_clientGrantAdmitted ?? (_clientGrantAdmitted =
            TaleWorlds.Core.MBRandom.RandomFloat < CourtClientGrantRules.Admission(
                Realm.Leader.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Generosity)))).Value;
        private readonly Dictionary<Clan, IReadOnlyList<WarTargetScore>> _campaignTargets = new Dictionary<Clan, IReadOnlyList<WarTargetScore>>();
        private readonly Dictionary<Kingdom, bool> _campaignPermissions = new Dictionary<Kingdom, bool>();
        private readonly Dictionary<Kingdom, bool> _clientageCandidates = new Dictionary<Kingdom, bool>();
        private readonly Dictionary<FactionObject, IReadOnlyList<CourtObjectiveCandidate>> _claimCandidates = new Dictionary<FactionObject, IReadOnlyList<CourtObjectiveCandidate>>();
        private IReadOnlyList<CourtObjectiveCandidate> _dynasticCandidates;
        private readonly Dictionary<FactionObject,IReadOnlyList<CourtObjectiveCandidate>> _rallyCandidates = new Dictionary<FactionObject,IReadOnlyList<CourtObjectiveCandidate>>();
        internal IReadOnlyList<CourtObjectiveCandidate> RallyCandidates(FactionObject faction)
        {
            if (!_rallyCandidates.TryGetValue(faction,out var candidates)) _rallyCandidates[faction]=candidates=CourtRallyObjectiveSource.Discover(this,faction);
            return candidates;
        }
        private IReadOnlyList<CourtObjectiveCandidate> _titleGrantCandidates;
        internal IReadOnlyList<CourtObjectiveCandidate> TitleGrantCandidates => _titleGrantCandidates ?? (_titleGrantCandidates = CourtTitleGrantObjectiveSource.Discover(this));
        private IReadOnlyDictionary<Kingdom, float> _tradeCandidates;
        internal IReadOnlyDictionary<Kingdom, float> TradeCandidates => _tradeCandidates ?? (_tradeCandidates = CourtTradeObjectiveSource.Discover(this));
        private IReadOnlyList<CourtProtectionCandidate> _protectionCandidates;
        internal IReadOnlyList<CourtProtectionCandidate> ProtectionCandidates => _protectionCandidates ?? (_protectionCandidates = CourtProtectionObjectiveSource.Discover(this));
        internal IReadOnlyList<CourtObjectiveCandidate> DynasticCandidates => _dynasticCandidates ?? (_dynasticCandidates = CourtDynasticObjectiveSource.Discover(this));
        internal IReadOnlyList<CourtObjectiveCandidate> ClaimCandidates(FactionObject faction)
        {
            if (!_claimCandidates.TryGetValue(faction, out var candidates))
                _claimCandidates[faction] = candidates = CourtClaimObjectiveSource.Discover(this, faction);
            return candidates;
        }
        internal bool CanSeekClientage(Kingdom target)
        {
            if (target == null) return false;
            if (!_clientageCandidates.TryGetValue(target, out bool eligible))
                _clientageCandidates[target] = eligible = CourtSubjugationObjectiveSource.ValidPair(Realm, target)
                    && TreatyDraftService.TryGetClientKingdomCandidate(Realm, target, out var candidate, out _)
                    && CourtSubjugationRules.CanSelect(candidate.FiefCount, candidate.WarScoreCost,
                        Realm.CurrentTotalStrength, target.CurrentTotalStrength);
            return eligible;
        }
        internal IReadOnlyList<WarTargetScore> CampaignTargets(Clan clan)
        {
            if (!_campaignTargets.TryGetValue(clan, out var targets))
                _campaignTargets[clan] = targets = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?
                    .GetRankedTargets(clan, forceRefresh: true).ToList() ?? new List<WarTargetScore>();
            return targets;
        }
        internal bool CanSupportCampaign(Kingdom target)
        {
            if (target == null) return false;
            if (!_campaignPermissions.TryGetValue(target, out bool allowed))
                _campaignPermissions[target] = allowed = WarPeaceRevampBehavior.CanSupportCourtCampaign(Realm, target);
            return allowed;
        }
        internal bool ActivityAdmitted(FactionObject faction)
        {
            if (ManualSelection) return true;
            if (!_activityAdmissions.TryGetValue(faction, out bool admitted))
                _activityAdmissions[faction] = admitted = CourtActivityRules.Admit(faction.Mood, TaleWorlds.Core.MBRandom.RandomFloat);
            return admitted;
        }
        private readonly Dictionary<string, Clan> _clansById;
        private readonly Dictionary<string, Settlement> _settlementsById;
        private readonly Dictionary<Settlement, IReadOnlyList<FeudalClaimRecord>> _claims = new Dictionary<Settlement, IReadOnlyList<FeudalClaimRecord>>();
        private readonly Dictionary<string, PolicyObject> _policiesById;
        private readonly HashSet<PolicyObject> _proposable;
        private readonly Dictionary<Tuple<Clan, PolicyObject, bool, int>, CourtPolicyForecast> _forecasts
            = new Dictionary<Tuple<Clan, PolicyObject, bool, int>, CourtPolicyForecast>();

        internal CourtTermContext(Kingdom realm, Func<PolicyObject, bool> canPropose, CourtAgendaRecord playerAgenda = null)
        {
            Realm = realm;
            PlayerAgenda = playerAgenda;
            Policies = PolicyObject.All.ToList();
            _policiesById = Policies.ToDictionary(p => p.StringId, StringComparer.Ordinal);
            Enacted = new HashSet<PolicyObject>(realm.ActivePolicies);
            Voters = realm.Clans.Where(c => CourtAgendaBehavior.Eligible(c, realm)).ToList();
            _clansById = Voters.ToDictionary(c => c.StringId, StringComparer.Ordinal);
            Settlements = realm.Fiefs.Select(f => f.Settlement).ToList();
            _settlementsById = Settlements.ToDictionary(s => s.StringId, StringComparer.Ordinal);
            _proposable = new HashSet<PolicyObject>(Policies.Where(canPropose));
        }

        internal bool CanPropose(PolicyObject policy) => _proposable.Contains(policy);
        internal Clan FindClan(string id) => _clansById.TryGetValue(id, out var clan) ? clan : null;
        internal Settlement FindSettlement(string id) => _settlementsById.TryGetValue(id, out var settlement) ? settlement : null;
        internal IReadOnlyList<FeudalClaimRecord> Claims(Settlement settlement)
        {
            if (!_claims.TryGetValue(settlement, out var claims))
                _claims.Add(settlement, claims = CourtExecutiveObjectiveSource.Claims(Realm, settlement).ToList());
            return claims;
        }
        internal PolicyObject FindPolicy(string id) => _policiesById.TryGetValue(id, out var policy) ? policy : null;
        internal CourtPolicyForecast Forecast(KingdomPolicyDecision decision, bool abolish, int cost)
        {
            var key = Tuple.Create(decision.ProposerClan, decision.Policy, abolish, cost);
            if (!_forecasts.TryGetValue(key, out var forecast))
                _forecasts.Add(key, forecast = CourtPolicyForecast.Calculate(decision, cost, Voters));
            return forecast;
        }
    }

    internal interface ICourtAgendaObjectiveSource : ICourtObjectiveSource<CourtTermContext, CourtObjectiveOwner>
    {
        void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice);
    }

    internal sealed class CourtPolicyObjectiveSource : ICourtAgendaObjectiveSource
    {
        internal const string PolicyKind = "policy";
        private readonly Func<KingdomPolicyDecision, FactionObject, int> _filingCost;
        public string Kind => PolicyKind;

        internal CourtPolicyObjectiveSource(Func<KingdomPolicyDecision, FactionObject, int> filingCost)
        { _filingCost = filingCost; }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            agenda.PolicyId = choice.Candidate.TargetId;
            agenda.Abolish = choice.Candidate.ActionId == "repeal";
            agenda.CapturePolicyStance();
            agenda.GetObjective();
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (owner.Faction == null && owner.Sponsor == Clan.PlayerClan && !context.ManualSelection) yield break;
            foreach (var policy in context.Policies)
            {
                var stance = Stance(owner, policy);
                if (context.ManualSelection && owner.Faction == null)
                {
                    yield return new CourtObjectiveCandidate(policy.StringId, context.Enacted.Contains(policy) ? "repeal" : "enact");
                    continue;
                }
                if (stance != CourtPolicyStance.Neutral)
                    yield return new CourtObjectiveCandidate(policy.StringId, stance == CourtPolicyStance.Oppose ? "repeal" : "enact");
            }
        }

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var policy = context.FindPolicy(candidate.TargetId);
            bool abolish = candidate.ActionId == "repeal";
            if (policy == null || (candidate.ActionId != "enact" && !abolish)) return Reject("invalid_target");
            var stance = Stance(owner, policy);
            if (!(context.ManualSelection && owner.Faction == null)
                && (stance == CourtPolicyStance.Neutral || (stance == CourtPolicyStance.Oppose) != abolish)) return Reject("unaligned");
            if (context.Enacted.Contains(policy) != abolish) return Reject("already_satisfied");
            if (!context.CanPropose(policy)) return Reject("cooldown_or_pending_motion");
            if (context.ManualSelection) return new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "player_eligible");
            var decision = new KingdomPolicyDecision(owner.Sponsor, policy, abolish);
            var forecast = context.Forecast(decision, abolish, _filingCost(decision, owner.Faction));
            return new CourtObjectiveEvaluation(true, forecast.Viable, new CourtObjectiveWeight(1),
                $"support={forecast.For}/{forecast.For + forecast.Against}; houses={forecast.SupportingHouses}; affordable={forecast.Affordable}; sponsor_supports={forecast.SponsorSupports}; sponsor_preference={forecast.SponsorPreference}");
        }

        private static CourtObjectiveEvaluation Reject(string reason) =>
            new CourtObjectiveEvaluation(false, false, new CourtObjectiveWeight(1), reason);

        private static CourtPolicyStance Stance(CourtObjectiveOwner owner, PolicyObject policy) => owner.Faction == null
            ? (IdeologyPolicyRoster.IsCrownPolicy(policy) ? CourtPolicyStance.Support : CourtPolicyStance.Neutral)
            : IdeologyPolicyRoster.GetEffectiveStance(owner.Faction, policy);
    }
}
