using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class FactionManagerBehavior
    {
        private Dictionary<string, string> _pendingSuccessionOpportunities = new Dictionary<string, string>();

        private static bool IsWarmingCrownFaction(FactionObject faction) => faction != null && !faction.IsIdeology
            && !faction.HasTrackedRebelKingdom && !faction.IsChallengeStartupPending
            && !faction.IsRebellionCreationPending && !faction.HasStartedCivilWar;

        private void ReconcileWarmingCrownFactions(Kingdom realm, bool rulerChanged = false)
        {
            Clan ruler = realm?.RulingClan;
            if (ruler == null) return;
            foreach (var faction in _activeFactions.Where(f => f.ParentKingdom == realm && IsWarmingCrownFaction(f)).ToList())
            {
                if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) continue;
                if (faction.Leader == ruler || rulerChanged && faction.Type == FactionType.Abdication && !faction.IsHereditaryAbdication)
                {
                    RemoveFaction(faction);
                    NotificationHelper.ShowFactionDisbanded(faction, new TextObject(faction.Leader == ruler
                        ? "{=BC_FactionManager_DisbandReason_LeaderAscended}their leader ascended to the throne"
                        : "{=BC_FactionManager_DisbandReason_OldRuler}the old ruler is no longer in power").ToString());
                }
                else if (faction.Members.Contains(ruler))
                {
                    faction.RemoveMember(ruler);
                    NotificationHelper.ShowClanLeftFaction(ruler, faction,
                        new TextObject("{=BC_FactionManager_LeftReason_Ascended}they ascended to the throne").ToString());
                }
            }
        }

        internal static bool SuccessionOpportunityReady(Kingdom realm) => realm != null && !realm.IsEliminated
            && NobleClanEligibilityHelper.IsValidRulingClan(realm.RulingClan, realm)
            && CrownAccessionBehavior.Instance?.IsPending(realm) != true
            && !CivilWarConflictBehavior.IsRealmTransferPending(realm)
            && ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) == null
            && !realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any();

        private bool IsCurrentSuccessionOpportunity(FactionObject faction, Clan ruler, Hero monarch) =>
            _activeFactions.Contains(faction) && IsWarmingCrownFaction(faction)
            && faction.Type == FactionType.InstallRuler && faction.ParentKingdom?.RulingClan == ruler
            && ruler?.Leader == monarch && SuccessionOpportunityReady(faction.ParentKingdom)
            && faction.CanTriggerUltimatum(out _);

        private void ProcessSuccessionOpportunities()
        {
            foreach (var pending in _pendingSuccessionOpportunities.ToList())
            {
                Kingdom realm = Kingdom.All.FirstOrDefault(k => k.StringId == pending.Key);
                if (realm == null || realm.IsEliminated || realm.RulingClan?.StringId != pending.Value)
                { _pendingSuccessionOpportunities.Remove(pending.Key); continue; }
                if (!SuccessionOpportunityReady(realm)) continue;
                // Consume before notifications or ultimatums can re-enter accession.
                _pendingSuccessionOpportunities.Remove(pending.Key);
                ReconcileWarmingCrownFactions(realm);
                Clan ruler = realm.RulingClan;
                Hero monarch = ruler.Leader;
                foreach (var faction in _activeFactions.Where(f => f.ParentKingdom == realm).ToList())
                {
                    if (!IsCurrentSuccessionOpportunity(faction, ruler, monarch)) continue;
                    float threshold = 0.8f;
                    Hero leader = faction.Leader.Leader;
                    int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
                    int valor = leader.GetTraitLevel(DefaultTraits.Valor);
                    threshold += calculating <= -2 ? -0.2f : calculating == -1 ? -0.1f : calculating == 1 ? 0.1f : calculating >= 2 ? 0.2f : 0;
                    threshold += valor >= 2 ? -0.2f : valor == 1 ? -0.1f : valor == -1 ? 0.1f : valor <= -2 ? 0.2f : 0;
                    if (faction.CalculateFactionPower() < faction.CalculateLoyalistPower() * System.Math.Max(0.4f, threshold)) continue;
                    if (faction.Leader == Clan.PlayerClan)
                        NotificationHelper.ShowPlayerSuccessionOpportunity(() =>
                        {
                            if (IsCurrentSuccessionOpportunity(faction, ruler, monarch)) faction.TriggerUltimatum();
                        });
                    else
                    {
                        TextObject text = new TextObject("{=BC_FactionManager_SuccessionChaos}Seizing the chaos of succession, the {FACTION_NAME} has issued their demands!");
                        text.SetTextVariable("FACTION_NAME", NotificationHelper.GetFactionDisplayName(faction));
                        BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion, primaryKingdom: realm, primaryClan: faction.Leader);
                        if (IsCurrentSuccessionOpportunity(faction, ruler, monarch)) faction.TriggerUltimatum();
                    }
                }
            }
        }
    }
}
