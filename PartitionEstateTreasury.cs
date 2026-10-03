using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class PartitionEstateTreasury
    {
        internal static bool TryCapture(Hero donor, int treasury, IReadOnlyCollection<Hero> beneficiaries,
            out List<CrownAccessionRecord> payments, out string reason)
        {
            payments = null;
            reason = "estate treasury requires a donor and distinct secondary beneficiaries";
            if (donor == null || beneficiaries == null || beneficiaries.Count == 0
                || beneficiaries.Any(h => h == null || h == donor || string.IsNullOrWhiteSpace(h.StringId))
                || beneficiaries.Select(h => h.StringId).Distinct().Count() != beneficiaries.Count) return false;
            int share = Math.Max(0, treasury) / (beneficiaries.Count + 1);
            payments = beneficiaries.OrderBy(h => h.StringId, StringComparer.Ordinal).Select(h => new CrownAccessionRecord
            {
                IncomingSourceHead = donor, GoldRecipient = h, EndowmentGold = share
            }).ToList();
            reason = null;
            return true;
        }

        internal static bool TryDeliver(IReadOnlyList<CrownAccessionRecord> payments,
            Func<CrownAccessionRecord, bool> deliver, out string reason)
        {
            reason = "invalid saved estate treasury manifest";
            if (payments == null || payments.Count == 0 || deliver == null
                || payments.Any(p => p == null || p.EndowmentDonor == null || p.GoldRecipient == null
                    || p.EndowmentDonor == p.GoldRecipient || p.EndowmentGold < 0
                    || p.DeliveredGold < 0 || p.DeliveredGold > p.EndowmentGold
                    || p.GoldCredited && !p.GoldDebited || !p.GoldDebited && p.DeliveredGold != 0)
                || payments.Select(p => p.EndowmentDonor).Distinct().Count() != 1
                || payments.Select(p => p.GoldRecipient.StringId).Distinct().Count() != payments.Count
                || payments.Select(p => p.EndowmentGold).Distinct().Count() != 1) return false;
            // A later recipient cannot be paid before an earlier saved payment completes.
            bool unfinished = false;
            foreach (var payment in payments)
            {
                if (unfinished && (payment.GoldDebited || payment.GoldCredited)) return false;
                if (!payment.GoldCredited) unfinished = true;
            }
            try
            {
                foreach (var payment in payments)
                {
                    if (payment.GoldCredited) continue;
                    if (!deliver(payment) || !payment.GoldDebited || !payment.GoldCredited)
                    { reason = "estate gold delivery has not completed: " + payment.GoldRecipient.StringId; return false; }
                }
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "estate gold delivery interrupted: " + ex.Message;
                return false;
            }
        }
    }
}
