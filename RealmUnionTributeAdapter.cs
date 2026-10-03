using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class RealmUnionTributePlan
    {
        internal StanceLink Source;
        internal StanceLink Destination;
        internal Kingdom SourceRealm;
        internal Kingdom DestinationRealm;
        internal Kingdom Partner;
        internal int SourceRate;
        internal int SourcePaid;
        internal int Installments;
        internal int DestinationRate;
        internal int DestinationPaid;
        internal int DestinationInstallments;
        internal CampaignTime SourcePeaceDate;
        internal CampaignTime DestinationPeaceDate;
        internal int ExpectedRate;
        internal int ExpectedPaid;
        internal int RemainingPayments;
    }

    internal static class RealmUnionTributeAdapter
    {
        private static readonly System.Reflection.FieldInfo Rate = AccessTools.Field(typeof(StanceLink), "_dailyTributeFrom1To2");

        internal static bool TryPrepare(StanceLink source, StanceLink destination, Kingdom sourceRealm,
            Kingdom destinationRealm, Kingdom partner, out RealmUnionTributePlan plan, out string reason)
        {
            plan = null;
            reason = "tribute inheritance needs distinct peaceful participants and native tribute storage";
            if (Rate?.FieldType != typeof(int) || source == null || destination == null || source == destination
                || sourceRealm == null || destinationRealm == null || partner == null || sourceRealm == destinationRealm
                || sourceRealm == partner || destinationRealm == partner || source.IsAtWar || destination.IsAtWar
                || !Pair(source, sourceRealm, partner) || !Pair(destination, destinationRealm, partner)) return false;
            int rate = (int)Rate.GetValue(source);
            int paid = source.TotalTributePaidFrom1To2;
            int installments = source.DailyTributeInstallments;
            int oldRate = (int)Rate.GetValue(destination);
            int oldPaid = destination.TotalTributePaidFrom1To2;
            // Native remaining-payment arithmetic uses Int32 multiplication/subtraction.
            // Reject malformed ledgers before invoking that calculation or reversing signs.
            long total = (long)rate * installments;
            long outstanding = total - paid;
            if (rate == 0 || rate == int.MinValue || paid == int.MinValue || installments <= 0
                || total < int.MinValue || total > int.MaxValue || outstanding < int.MinValue || outstanding > int.MaxValue
                || paid != 0 && Math.Sign(paid) != Math.Sign(rate)
                || Math.Abs((long)paid) > Math.Abs(total) || outstanding / rate <= 0)
            { reason = "source tribute is exhausted or has an inconsistent native ledger"; return false; }
            if (oldRate != 0 || oldPaid != 0 || destination.DailyTributeInstallments != 0)
            { reason = "destination already has a tribute ledger with this partner; separate reconciliation is required"; return false; }
            int signedRate = source.Faction1 == sourceRealm ? rate : -rate;
            int signedPaid = source.Faction1 == sourceRealm ? paid : -paid;
            plan = new RealmUnionTributePlan {
                Source = source, Destination = destination, SourceRealm = sourceRealm, DestinationRealm = destinationRealm, Partner = partner,
                SourceRate = rate, SourcePaid = paid, Installments = installments,
                DestinationRate = oldRate, DestinationPaid = oldPaid, DestinationInstallments = destination.DailyTributeInstallments,
                SourcePeaceDate = source.PeaceDeclarationDate, DestinationPeaceDate = destination.PeaceDeclarationDate,
                ExpectedRate = destination.Faction1 == destinationRealm ? signedRate : -signedRate,
                ExpectedPaid = destination.Faction1 == destinationRealm ? signedPaid : -signedPaid,
                RemainingPayments = (int)(outstanding / rate) };
            reason = null;
            return true;
        }

        internal static bool TryApply(RealmUnionTributePlan plan, Action beforeWrite, Action returned, out string reason)
        {
            reason = "native tribute state changed after capture";
            if (plan == null || beforeWrite == null || returned == null
                || !TryPrepare(plan.Source, plan.Destination, plan.SourceRealm, plan.DestinationRealm, plan.Partner,
                    out var current, out reason)) return false;
            if (current.SourceRate != plan.SourceRate || current.SourcePaid != plan.SourcePaid || current.Installments != plan.Installments
                || current.SourcePeaceDate != plan.SourcePeaceDate || current.DestinationPeaceDate != plan.DestinationPeaceDate
                || current.ExpectedRate != plan.ExpectedRate || current.ExpectedPaid != plan.ExpectedPaid
                || current.RemainingPayments != plan.RemainingPayments)
            { reason = "tribute changed after capture; current payments will not be overwritten"; return false; }
            beforeWrite();
            plan.Destination.TotalTributePaidFrom1To2 = plan.ExpectedPaid;
            plan.Destination.SetDailyTributePaid(plan.Destination.Faction1, plan.ExpectedRate, plan.Installments);
            plan.Source.SetDailyTributePaid(plan.SourceRealm, 0, 0);
            if ((int)Rate.GetValue(plan.Source) != 0 || plan.Source.GetRemainingTributePaymentCount() != 0
                || plan.Source.TotalTributePaidFrom1To2 != plan.SourcePaid
                || (int)Rate.GetValue(plan.Destination) != plan.ExpectedRate
                || plan.Destination.TotalTributePaidFrom1To2 != plan.ExpectedPaid
                || plan.Destination.GetRemainingTributePaymentCount() != plan.RemainingPayments
                || plan.Source.PeaceDeclarationDate != plan.SourcePeaceDate || plan.Destination.PeaceDeclarationDate != plan.DestinationPeaceDate)
            { reason = "inherited tribute ledger does not verify"; return false; }
            returned();
            reason = null;
            return true;
        }

        private static bool Pair(StanceLink stance, Kingdom first, Kingdom second) =>
            stance.Faction1 == first && stance.Faction2 == second || stance.Faction1 == second && stance.Faction2 == first;
    }
}
