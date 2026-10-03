using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private bool TryReadPartitionAgreements(Kingdom realm, out Dictionary<string, string> agreements, out string reason)
        {
            agreements = null;
            reason = "diplomatic agreement services unavailable";
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            if (realm == null || alliances == null || trade == null || clients == null) return false;
            if (!CanPrepareCrownPartitionDiplomacy(realm, null, out reason)) return false;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var other in Kingdom.All.Where(k => k != null && !k.IsEliminated && k != realm))
            {
                if (alliances.IsAllyWithKingdom(realm, other))
                    result.Add("alliance:" + other.StringId, alliances.GetAllianceEndDate(realm, other).ToDays.ToString("R", CultureInfo.InvariantCulture));
                if (trade.HasTradeAgreement(realm, other, out var end))
                    result.Add("trade:" + other.StringId, end.EndTime.ToDays.ToString("R", CultureInfo.InvariantCulture));
                var stance = realm.GetStanceWith(other);
                int tribute = stance?.GetDailyTributeToPay(realm) ?? 0;
                if (tribute != 0)
                {
                    result.Add("tribute:" + other.StringId, tribute.ToString(CultureInfo.InvariantCulture));
                    result.Add("tribute-date:" + other.StringId, stance.PeaceDeclarationDate.ToDays.ToString("R", CultureInfo.InvariantCulture));
                    result.Add("tribute-installments:" + other.StringId, stance.DailyTributeInstallments.ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (var client in clients.GetClients(realm)) result.Add("client:" + client.StringId, realm.StringId);
            agreements = result;
            reason = null;
            return true;
        }

        internal bool TryCaptureCrownBatchObligations(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "obligations require a registered unstarted Crown batch";
            if (pending?.CrownBatch == null || _pendingPartitions?.Contains(pending) != true || pending.CrownPromotions == null) return false;
            if (pending.CrownBatch.OriginalAgreements != null) { reason = null; return true; }
            if (pending.CrownPromotions.Any(p => p == null || p.FounderCreationStarted)
                || pending.EstateShares?.Any(s => s == null || s.PartitionFounderStarted) == true) return false;
            if (!TryReadPartitionAgreements(ResolveKingdom(pending.KingdomId), out var agreements, out reason)) return false;
            pending.CrownBatch.OriginalAgreements = agreements;
            return true;
        }

        internal bool TryVerifyCrownBatchObligations(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "original diplomatic obligations are not recorded";
            var batch = pending?.CrownBatch;
            if (batch?.OriginalAgreements == null) return false;
            batch.ObligationsVerified = false;
            if (!TryReadPartitionAgreements(ResolveKingdom(pending.KingdomId), out var current, out reason)) return false;
            if (current.Count != batch.OriginalAgreements.Count || current.Any(p =>
                !batch.OriginalAgreements.TryGetValue(p.Key, out string value) || value != p.Value))
            { reason = "primary realm agreements changed since partition capture; reconciliation required"; return false; }
            foreach (var promotion in pending.CrownPromotions)
            {
                if (!TryReadPartitionAgreements(promotion.Successor, out var inherited, out reason)) return false;
                if (inherited.Count != 0)
                { reason = "a successor has agreements that must remain with the primary realm"; return false; }
                var enemies = Kingdom.All.Where(k => k != null && !k.IsEliminated && k != promotion.Successor
                    && promotion.Successor.IsAtWarWith(k)).Select(k => k.StringId);
                if (promotion.InheritedEnemyIds == null || !new HashSet<string>(promotion.InheritedEnemyIds).SetEquals(enemies))
                { reason = "successor foreign wars differ from the recorded inheritance"; return false; }
            }
            batch.ObligationsVerified = true;
            reason = null;
            return true;
        }
    }
}
