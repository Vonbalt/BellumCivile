using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public partial class ExiledClanRecoveryBehavior
    {
        internal string CreateAsylumTest(string clanId)
        {
            var player = Hero.MainHero;
            var realm = Clan.PlayerClan?.Kingdom;
            if (player?.IsAlive != true || player.IsDisabled || Clan.PlayerClan?.Leader != player
                || realm?.Leader != player || !IsPlayerRuledValidRefuge(realm) || IsScriptedDeparture(realm))
                return "Error: You must rule an active, landed realm outside a scripted realm transfer.";
            if (!CanPresentAsylumNotice())
                return "Error: Return to the campaign map, outside encounters, menus, conversations and other inquiries, before testing asylum.";
            if (FindAsylumSettlement(realm) == null)
                return "Error: No safe keep is available for an asylum audience. Player holdings take priority over vassal holdings.";

            EnsureCollectionsInitialized();
            Clan clan;
            if (!string.IsNullOrWhiteSpace(clanId))
            {
                clan = Clan.All.FirstOrDefault(c => c != null && string.Equals(c.StringId, clanId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (clan == null) return $"Error: No clan has ID '{clanId}'. Omit the ID for random selection.";
                string rejection = AsylumTestRejection(clan, realm);
                if (rejection != null) return "Error: " + rejection;
            }
            else
            {
                var eligible = Clan.All.Where(c => AsylumTestRejection(c, realm) == null).ToList();
                if (eligible.Count == 0)
                    return "Error: No eligible foreign landless clan is available. Rulers, mercenaries, existing petitioners, hostile realms and unsafe parties are excluded.";
                clan = eligible[MBRandom.RandomInt(eligible.Count)];
            }

            var origin = clan.Kingdom;
            try
            {
                // Force only the refuge destination. Keep normal exile history, native departure,
                // title confiscation and the saved audience lifecycle, without granting membership.
                RecordExileContext(clan, origin, ExileCause.Expulsion);
                RecordDeparture(clan, origin, ExileCause.Expulsion);
                (FeudalTitleBehavior.Instance ?? Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>())
                    ?.ConfiscateNonBaronyTitlesForExile(clan, origin.RulingClan, origin, "asylum test expulsion");
                var result = QueuePlayerAsylumRequest(clan, origin, new[] { origin }, realm);
                if (_asylumVisits.TryGetValue(clan.StringId, out var visit) && visit.State == AsylumVisitRecord.Waiting)
                {
                    string report = $"Success! {clan.Name} ({clan.StringId}) exiled from {origin.Name} ({origin.StringId}). "
                        + $"{visit.Guest.Name} ({visit.Guest.StringId}) awaits you at {visit.Settlement.Name} ({visit.Settlement.StringId}) "
                        + $"for {Math.Max(0, (int)Math.Ceiling(visit.Deadline.RemainingDaysFromNow))} days "
                        + $"(deadline campaign day {visit.Deadline.ToDays:0.00}). The clan has not joined your realm. Use a separate test save.";
                    BellumCivileLogger.Log("Asylum test: " + report);
                    return report;
                }
                if (result == ExileResolutionResult.PendingPlayerAsylum && _pendingAsylumClanIds.Contains(clan.StringId))
                    return $"Queued {clan.Name} ({clan.StringId}) from {origin.Name} for asylum. Arrival is deferred; watch for the normal keep and deadline notice. This has changed the campaign; use a test save.";
                return "Error: The test exile could not arrange its audience. The campaign may already have changed; reload your test save before retrying.";
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Asylum test interrupted; clan={clan.StringId}; origin={origin.StringId}; error={ex}");
                return "Error: The asylum test was interrupted; details are in Bellum's log. The campaign may already have changed; reload your test save before retrying.";
            }
        }

        private string AsylumTestRejection(Clan clan, Kingdom realm)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId) || clan.IsEliminated || !clan.IsNoble
                || clan.IsMinorFaction || clan.IsUnderMercenaryService)
                return "Select a living noble clan, not a minor faction or mercenary.";
            if (_pendingAsylumClanIds.Contains(clan.StringId) || _asylumVisits.ContainsKey(clan.StringId))
                return "This clan already has an asylum request or visit. Its deadline will not be reset.";
            var origin = clan.Kingdom;
            if (clan == Clan.PlayerClan || origin == null || origin == realm)
                return "Select a clan that currently belongs to another realm.";
            if (origin.IsEliminated || origin.RulingClan?.Leader?.IsAlive != true || origin.RulingClan.Kingdom != origin
                || IsScriptedDeparture(origin))
                return "The source realm is eliminated, leaderless, temporary, or undergoing a scripted transfer.";
            if (origin.RulingClan == clan) return "Ruling clans cannot be selected for an asylum test.";
            if (!IsLandlessForExile(clan)) return "This clan still owns a town or castle. Test asylum does not confiscate settlements.";
            if (origin.IsAtWarWith(realm) || clan.IsAtWarWith(realm))
                return "The selected clan or its realm is at war with you. The test does not override hostility.";
            if (clan.Leader?.Clan != clan || !CanClanBeRelocated(clan) || !CanPrepareAsylumGuest(clan.Leader)
                || clan.WarPartyComponents.Any(c => c?.MobileParty != null
                    && (c.MobileParty.ActualClan != clan || !CanPrepareAsylumGuest(c.MobileParty.LeaderHero))))
                return "The clan leader or a clan party is unavailable: captivity, governorship, travel, battle, siege, army, quest, naval travel or scripted AI orders.";
            return null;
        }
    }
}
