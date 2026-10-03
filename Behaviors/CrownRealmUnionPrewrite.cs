using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private bool TryValidateRealmUnionBeforeFirstWrite(CrownAccessionRecord accession, out string reason)
        {
            var journal = accession?.Union;
            reason = "union participants changed before the first transfer";
            if (journal == null || HasStartedRealmUnion(journal) || journal.Source == null || journal.Destination == null
                || journal.Source.IsEliminated || journal.Destination.IsEliminated
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Source.RulingClan != journal.PreviousHouse || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.SurvivingHouse?.Kingdom != journal.Destination || journal.Heir?.IsAlive != true
                || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse.Leader) != journal.Heir)
                return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var partition = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (titles == null || factions == null || partition == null
                || partition.IsCrownPromotionRealmProtected(journal.Source) || partition.IsCrownPromotionRealmProtected(journal.Destination))
            { reason = "union services are unavailable or a Crown partition is in progress"; return false; }
            if (factions.GetFactionsInKingdom(journal.Source).Any(f => !f.IsIdeology || CivilWarConflictBehavior.IsFactionTransferPending(f)))
            { reason = "source political conflict appeared before transfer"; return false; }
            if (journal.Clans == null || journal.Clans.Any(c => c?.Clan == null)
                || journal.Clans.Select(c => c.Clan).Distinct().Count() != journal.Clans.Count
                || journal.Clans.Count != journal.Source.Clans.Count
                || journal.Source.Clans.Any(c => !journal.Clans.Any(r => r.Clan == c))
                || Clan.All.Any(c => c.Kingdom == journal.Source && !journal.Clans.Any(r => r.Clan == c)))
            { reason = "source membership changed before transfer"; return false; }
            if (journal.Source.Armies.Any()) { reason = "source army must disband before transfer"; return false; }
            foreach (var receipt in journal.Clans)
            {
                var clan = receipt.Clan;
                if (clan.IsEliminated || clan.IsBanditFaction || clan.Kingdom != journal.Source
                    || !RealmUnionPrewriteRules.SameClan(receipt, RealmUnionSnapshotService.CaptureClan(clan))
                    || clan.Heroes.Any(h => h.PartyBelongedTo?.MapEvent != null || h.PartyBelongedTo?.SiegeEvent != null
                        || h.PartyBelongedTo?.Army != null)
                    || clan.Settlements.Any(s => s.SiegeEvent != null || s.Party?.MapEvent != null))
                { reason = "source house changed or entered active fighting before transfer: " + clan.StringId; return false; }
            }
            if (!RealmUnionCrownTransfer.VerifyTitles(journal, journal.PreviousHouse?.StringId,
                journal.SurvivingHouse.StringId, titles.GetTitle, false, out reason)) return false;
            if (titles.GetKingdomPoliticalTitle(journal.Source)?.TitleId != journal.InheritedCrownId
                || titles.GetKingdomPoliticalTitle(journal.Destination)?.TitleId != journal.PrimaryCrownId)
            { reason = "political Crown identity changed before transfer"; return false; }
            reason = null;
            return true;
        }
    }
}
