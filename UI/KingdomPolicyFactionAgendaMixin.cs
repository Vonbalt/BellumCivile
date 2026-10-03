using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.Library;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class KingdomPolicyFactionAgendaMixin : BaseViewModelMixin<KingdomPolicyItemVM>
    {
        private bool _isPlayerFactionAgendaPolicy;
        private bool _isPlayerFactionOpposedPolicy;

        public KingdomPolicyFactionAgendaMixin(KingdomPolicyItemVM vm) : base(vm)
        {
            RefreshFactionAgendaState();
        }

        [DataSourceProperty]
        public bool IsPlayerFactionAgendaPolicy
        {
            get => _isPlayerFactionAgendaPolicy;
            private set
            {
                if (value == _isPlayerFactionAgendaPolicy)
                    return;

                _isPlayerFactionAgendaPolicy = value;
                ViewModel.OnPropertyChangedWithValue(value, nameof(IsPlayerFactionAgendaPolicy));
            }
        }

        public override void OnRefresh() => RefreshFactionAgendaState();

        [DataSourceProperty]
        public bool IsPlayerFactionOpposedPolicy
        {
            get => _isPlayerFactionOpposedPolicy;
            private set
            {
                if (value == _isPlayerFactionOpposedPolicy) return;
                _isPlayerFactionOpposedPolicy = value;
                ViewModel.OnPropertyChangedWithValue(value, nameof(IsPlayerFactionOpposedPolicy));
            }
        }

        internal static CourtPolicyStance PolicyStance(PolicyObject policy, bool crown, FactionType? faction, float mood = 0) => crown
            ? (IdeologyPolicyRoster.IsCrownPolicy(policy) ? CourtPolicyStance.Support : CourtPolicyStance.Neutral)
            : faction.HasValue ? IdeologyPolicyRoster.GetEffectiveStance(faction.Value, policy, mood) : CourtPolicyStance.Neutral;

        private HintViewModel _stanceHint;

        [DataSourceMethod]
        public void ExecuteBeginPolicyStanceHint()
        {
            RefreshFactionAgendaState();
            _stanceHint?.ExecuteBeginHint();
        }

        [DataSourceMethod]
        public void ExecuteEndPolicyStanceHint() => _stanceHint?.ExecuteEndHint();

        private void RefreshFactionAgendaState()
        {
            Clan playerClan = Clan.PlayerClan;
            FactionObject playerFaction = playerClan == null
                ? null
                : Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(playerClan);

            bool crown = CourtMembershipEligibility.IsRuler(playerClan);
            var stance = PolicyStance(ViewModel.Policy, crown, playerFaction?.Type, playerFaction?.Mood ?? 0);
            IsPlayerFactionAgendaPolicy = stance == CourtPolicyStance.Support;
            IsPlayerFactionOpposedPolicy = stance == CourtPolicyStance.Oppose;
            var text = new TextObject(stance == CourtPolicyStance.Support
                ? "{=BC_PolicyStanceSupport}{FACTION} supports this law."
                : stance == CourtPolicyStance.Oppose ? "{=BC_PolicyStanceOppose}{FACTION} opposes this law."
                : "{=BC_PolicyStanceNeutral}{FACTION} takes no collective position on this law.")
                .SetTextVariable("FACTION", crown ? new TextObject("{=BC_CrownOwner}The Crown")
                    : playerFaction?.GetDisplayName() ?? new TextObject("{=BC_PolicyNoFaction}Your house"));
            string detail = text.ToString();
            if (!crown && playerFaction != null && IdeologyPolicyRoster.IsCrownPolicy(ViewModel.Policy))
                detail += "\n\n" + new TextObject("{=BC_PolicyCrownTrust}Further royal authority must rest upon our confidence in the Crown. At mood {LOW} or below we oppose it; above {LOW} we withhold opposition; at {HIGH} or above we lend our support. Current mood: {MOOD}.\n\nThis political stance does not change the law's lasting effects on our interests or the burden of centralization.")
                    .SetTextVariable("LOW", CourtPolicyStanceRules.OpposeThrough)
                    .SetTextVariable("HIGH", CourtPolicyStanceRules.SupportFrom)
                    .SetTextVariable("MOOD", playerFaction.Mood.ToString("0.##"));
            _stanceHint = new HintViewModel(new TextObject(detail));
        }
    }
}
