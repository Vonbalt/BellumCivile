using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        // Capture before native clan succession replaces the deceased leader. Persist until the ballot settles.
        private Dictionary<string, string> _coalitionDeaths = new Dictionary<string, string>();
        private List<string> _coalitionSuccessionNotices = new List<string>();
        private Dictionary<string, string> _coalitionNominees = new Dictionary<string, string>();

        internal static CivilWarResolutionBehavior Current => Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();

        private static FactionObject ElectoralCoalition(Kingdom realm)
        {
            if (realm == null || realm.IsEliminated) return null;
            var faction = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(realm);
            return faction?.Type == FactionType.InstallRuler
                && ElectiveSuccessionBehavior.UsesElection(faction.ParentKingdom) ? faction : null;
        }

        internal void CaptureCoalitionDeath(Hero victim)
        {
            var realm = victim?.Clan?.Kingdom;
            var faction = ElectoralCoalition(realm);
            if (faction == null || (victim != faction.Leader?.Leader
                && !(IsCoalitionSuccession(realm) && victim == realm.Leader))) return;
            _coalitionDeaths[realm.StringId] = victim.StringId;
            _coalitionSuccessionNotices.Remove(realm.StringId);
            Patches.KingSelectionAIPatch.ActiveElections.Remove(realm);
            ClearCoalitionNominees(realm);
            CivilWarTransitionDiagnostics.Log("coalition death captured", faction, realm, $"victim={victim.StringId}");
        }

        internal bool IsCoalitionSuccession(Kingdom realm) => realm != null
            && _coalitionDeaths.ContainsKey(realm.StringId) && ElectoralCoalition(realm) != null;

        internal List<Clan> CoalitionCandidates(Kingdom realm)
        {
            var faction = ElectoralCoalition(realm);
            if (faction == null) return new List<Clan>();
            var laws = SuccessionLawHelper.GetLawsForKingdom(faction.ParentKingdom);
            return realm.Clans.Where(c => NobleClanEligibilityHelper.IsValidRulingClan(c, realm)
                && c.Leader.IsAlive && !c.Leader.IsChild
                && SuccessionLawHelper.IsEligibleUnderGenderLaw(c.Leader, laws)).ToList();
        }

        internal bool KeepCoalitionBallot(KingdomDecision decision) => decision is KingSelectionKingdomDecision
            && IsCoalitionSuccession(decision.Kingdom)
            && decision.Kingdom.UnresolvedDecisions.Contains(decision)
            && NobleClanEligibilityHelper.IsValidRulingClan(decision.ProposerClan, decision.Kingdom)
            && CoalitionCandidates(decision.Kingdom).Count > 1;

        private void ClearCoalitionNominees(Kingdom realm)
        {
            for (int slot = 0; slot < 3; slot++) _coalitionNominees.Remove(realm.StringId + "|" + slot);
        }

        internal List<Hero> PinCoalitionNominees(Kingdom realm, IEnumerable<Hero> proposed)
        {
            if (!_coalitionNominees.ContainsKey(realm.StringId + "|0"))
            {
                int slot = 0;
                foreach (var hero in proposed.Where(h => h != null).Distinct().Take(3))
                    _coalitionNominees[realm.StringId + "|" + slot++] = hero.StringId;
            }
            var result = new List<Hero>();
            for (int slot = 0; slot < 3; slot++)
                if (_coalitionNominees.TryGetValue(realm.StringId + "|" + slot, out var id))
                    result.Add(Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id));
            return result;
        }

        private void ClearCoalitionSuccession(Kingdom realm)
        {
            _coalitionDeaths.Remove(realm.StringId);
            _coalitionSuccessionNotices.Remove(realm.StringId);
            Patches.KingSelectionAIPatch.ActiveElections.Remove(realm);
            ClearCoalitionNominees(realm);
        }

        private void RecoverCoalitionBallots()
        {
            // Adopt outstanding native ballots in older saves, but never infer a new election from an old death.
            foreach (var realm in Kingdom.All.Where(k => ElectoralCoalition(k) != null))
                if (!_coalitionDeaths.ContainsKey(realm.StringId)
                    && realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any())
                    _coalitionDeaths[realm.StringId] = string.Empty;
            ProcessCoalitionSuccessions();
        }

        private void ProcessCoalitionSuccessions()
        {
            foreach (var entry in _coalitionDeaths.ToList())
            {
                var realm = Kingdom.All.FirstOrDefault(k => k.StringId == entry.Key);
                var faction = ElectoralCoalition(realm);
                if (faction == null)
                {
                    _coalitionDeaths.Remove(entry.Key);
                    _coalitionSuccessionNotices.Remove(entry.Key);
                    for (int slot = 0; slot < 3; slot++) _coalitionNominees.Remove(entry.Key + "|" + slot);
                    if (realm != null) Patches.KingSelectionAIPatch.ActiveElections.Remove(realm);
                    continue;
                }
                if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) continue;
                var victim = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == entry.Value);
                if (victim != null && victim.IsAlive) continue;

                var candidates = CoalitionCandidates(realm);
                if (candidates.Count == 0)
                {
                    CivilWarTransitionDiagnostics.Log("coalition succession failed", faction, realm, "no eligible successor");
                    ClearCoalitionSuccession(realm);
                    foreach (var ballot in realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().ToList())
                        realm.RemoveDecision(ballot);
                    ResolveLiegeVictory(faction, realm);
                    continue;
                }
                if (!NobleClanEligibilityHelper.IsValidRulingClan(realm.RulingClan, realm))
                    realm.RulingClan = candidates.OrderByDescending(RebellionPowerHelper.CalculateClanPower).First();

                if (candidates.Count == 1)
                {
                    realm.RulingClan = candidates[0];
                    CompleteCoalitionSuccession(realm, candidates[0]);
                    continue;
                }

                var ballots = realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().ToList();
                var nominees = PinCoalitionNominees(realm, new Hero[0]);
                if (nominees.Any(h => h == null || !candidates.Contains(h.Clan) || h.Clan.Leader != h))
                {
                    foreach (var ballot in ballots) realm.RemoveDecision(ballot);
                    ballots.Clear();
                    ClearCoalitionNominees(realm);
                    Patches.KingSelectionAIPatch.ActiveElections.Remove(realm);
                }
                var keep = ballots.FirstOrDefault(b => NobleClanEligibilityHelper.IsValidRulingClan(b.ProposerClan, realm));
                foreach (var ballot in ballots.Where(b => b != keep)) realm.RemoveDecision(ballot);
                if (keep == null)
                {
                    realm.AddDecision(new KingSelectionKingdomDecision(realm.RulingClan), true);
                    CivilWarTransitionDiagnostics.Log("coalition ballot recovered", faction, realm);
                }
                if (!_coalitionSuccessionNotices.Contains(entry.Key))
                {
                    _coalitionSuccessionNotices.Add(entry.Key);
                    var text = new TextObject("{=BC_Coalition_Succession}The claimant host has lost its leader. Its houses must choose another to lead their cause. Until their decision, {HEIR} commands the host.");
                    text.SetTextVariable("HEIR", realm.Leader?.Name);
                    BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion,
                        primaryKingdom: faction.ParentKingdom, secondaryKingdom: realm, isMajorEvent: true);
                }
            }
        }

        internal void CompleteCoalitionSuccession(Kingdom realm, Clan winner)
        {
            if (!IsCoalitionSuccession(realm) || realm.RulingClan != winner || !CoalitionCandidates(realm).Contains(winner)) return;
            var faction = ElectoralCoalition(realm);
            var formerHouse = faction.Leader;
            faction.Leader = winner;
            foreach (var key in _captureResolutionRebelKingdomIds.Where(p => p.Value == realm.StringId).Select(p => p.Key).ToList())
                _captureResolutionLeaderClanIds[key] = winner.StringId;
            ClearCoalitionSuccession(realm);
            foreach (var ballot in realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().ToList())
                realm.RemoveDecision(ballot);
            CivilWarTransitionDiagnostics.Log("coalition succession completed", faction, realm,
                $"former_house={formerHouse?.StringId}; chosen_house={winner.StringId}; chosen_hero={winner.Leader.StringId}; existing_wars_preserved=True");
            var text = new TextObject("{=BC_Coalition_Successor}The claimant houses have rallied behind {LEADER}. Their struggle for the crown continues.");
            text.SetTextVariable("LEADER", winner.Leader.Name);
            BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion,
                primaryKingdom: faction.ParentKingdom, secondaryKingdom: realm, primaryClan: winner, isMajorEvent: true);
        }
    }
}
