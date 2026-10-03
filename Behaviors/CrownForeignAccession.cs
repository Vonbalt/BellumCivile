using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal static bool RequiresRealmUnion(Clan house, Kingdom destination) =>
            house?.Kingdom != null && house.Kingdom != destination && house.Kingdom.RulingClan == house;

        private bool PrepareForeignCrownClan(CrownAccessionRecord record, Clan house)
        {
            if (record.ForeignMovingClan == null && house.Kingdom == record.Realm) return true;
            if (RequiresRealmUnion(house, record.Realm))
            {
                if (!TryPublishRealmUnion(record, out string reason)) return DeferAbdication(record, reason);
                if (!TryAdvanceRealmUnion(record, out reason)) return DeferAbdication(record, reason);
                // Union completion owns the accession. Never run the ordinary ruling-
                // clan change or Finish against the now-retired source kingdom.
                return false;
            }
            if (house.Leader == null || !house.Leader.IsAlive || house.IsEliminated
                || house.IsUnderMercenaryService || house.IsBanditFaction
                || NobleClanEligibilityHelper.IsNonPlayerMinorClan(house))
                return DeferAbdication(record, "the incoming ruling house is unavailable");
            if (!record.ForeignMoveCompleted && (house.Heroes.Any(h => h.IsPrisoner
                || h.PartyBelongedTo?.MapEvent != null || h.PartyBelongedTo?.SiegeEvent != null)
                || house.Settlements.Any(s => s.SiegeEvent != null || s.Party?.MapEvent != null)))
                return DeferAbdication(record, "foreign accession is waiting for captivity or active fighting to end");
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return DeferAbdication(record, "title service unavailable for foreign accession");
            if (record.ForeignMovingClan == null)
            {
                Kingdom origin = house.Kingdom;
                if (origin == null || origin.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(origin))
                    return DeferAbdication(record, "the incoming house must belong to a permanent realm before accession");
                var held = titles.GetTitlesHeldByClan(house, deJure: true)
                    .Concat(titles.GetTitlesHeldByClan(house, deJure: false))
                    .Where(t => t != null && t.IsActive).GroupBy(t => t.TitleId).Select(g => g.First()).ToList();
                record.ForeignHoldings = house.Settlements.Select(s => s.StringId).ToList();
                record.ForeignLegalHolders = held.ToDictionary(t => t.TitleId, t => t.DeJureHolderClanId);
                record.ForeignLegalParents = held.ToDictionary(t => t.TitleId, t => t.ParentTitleId);
                record.ForeignOriginRealm = origin;
                record.ForeignInfluence = house.Influence;
                record.ForeignMovingClan = house;
            }
            if (record.ForeignMovingClan != house)
                return DeferAbdication(record, "the incoming house changed during foreign accession");
            if (!record.ForeignMoveCompleted)
            {
                if (house.Kingdom != record.ForeignOriginRealm)
                    return DeferAbdication(record, "foreign movement was interrupted or allegiance changed; reconciliation required");
                if (!ForeignEstateUnchanged(record, house, titles))
                    return DeferAbdication(record, "the recorded foreign estate changed before allegiance transfer");
                record.ForeignMoveStarted = true;
                // Native JoinKingdom keeps settlements and emits the normal membership
                // callbacks, without LeaveKingdom confiscation or rebellion/defection events.
                ChangeKingdomAction.ApplyByJoinToKingdom(house, record.Realm, showNotification: false);
                if (house.Kingdom != record.Realm)
                    return DeferAbdication(record, "foreign allegiance transfer did not complete");
                record.ForeignMoveCompleted = true;
            }
            if (house.Kingdom != record.Realm)
                return DeferAbdication(record, "the transferred house no longer belongs to the inherited realm");
            if (!record.ForeignInfluenceRestored)
            {
                house.Influence = record.ForeignInfluence;
                record.ForeignInfluenceRestored = true;
            }
            if (!ForeignEstateUnchanged(record, house, titles))
                return DeferAbdication(record, "foreign allegiance callbacks changed legal rights or holdings; reconciliation required");
            return true;
        }

        private static bool ForeignEstateUnchanged(CrownAccessionRecord record, Clan house, FeudalTitleBehavior titles) =>
            record.ForeignHoldings.All(id => Settlement.Find(id)?.OwnerClan == house)
            && ForeignLegalRightsUnchanged(record, record.ForeignLegalHolders.Keys.Select(titles.GetTitle));

        internal static bool ForeignLegalRightsUnchanged(CrownAccessionRecord record, IEnumerable<FeudalTitleRecord> titles)
        {
            var current = titles.Where(t => t != null && t.IsActive).ToDictionary(t => t.TitleId);
            return record.ForeignLegalHolders.All(pair => current.TryGetValue(pair.Key, out var title)
                    && title.DeJureHolderClanId == pair.Value)
                && record.ForeignLegalParents.All(pair => current.TryGetValue(pair.Key, out var title)
                    && title.ParentTitleId == pair.Value);
        }
    }
}
