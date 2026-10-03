using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal readonly struct TreatyWarScoreSummary
    {
        public int StrategicDemandCost { get; }
        public int ExchangeDemandValue { get; }
        public int ReciprocalExchangeValue { get; }
        public int AppliedReciprocalExchangeCredit { get; }
        public int UnmatchedReciprocalExchangeValue => Math.Max(0, ReciprocalExchangeValue - ExchangeDemandValue);
        public int DemandCost => StrategicDemandCost + ExchangeDemandValue;
        public int OfferingValue { get; }
        public int RawOfferingCredit { get; }
        public int AppliedOfferingCredit { get; }
        public int OfferingCreditCap { get; }
        public int UsedWarScore => DemandCost - AppliedReciprocalExchangeCredit - AppliedOfferingCredit;

        public TreatyWarScoreSummary(
            int strategicDemandCost,
            int exchangeDemandValue,
            int reciprocalExchangeValue,
            int offeringValue,
            int rawOfferingCredit,
            int appliedOfferingCredit,
            int offeringCreditCap)
        {
            StrategicDemandCost = Math.Max(0, strategicDemandCost);
            ExchangeDemandValue = Math.Max(0, exchangeDemandValue);
            ReciprocalExchangeValue = Math.Max(0, reciprocalExchangeValue);
            AppliedReciprocalExchangeCredit = Math.Min(ExchangeDemandValue, ReciprocalExchangeValue);
            OfferingValue = Math.Max(0, offeringValue);
            RawOfferingCredit = Math.Max(0, rawOfferingCredit);
            AppliedOfferingCredit = Math.Max(0, appliedOfferingCredit);
            OfferingCreditCap = Math.Max(0, offeringCreditCap);
        }
    }

    internal static class TreatyWarScoreAccounting
    {
        public static TreatyWarScoreSummary Calculate(
            IEnumerable<TreatyTermRecord> terms,
            string winnerKingdomId,
            int warScoreBudget)
        {
            int strategicDemandCost = 0;
            int exchangeDemandValue = 0;
            int reciprocalExchangeValue = 0;
            int offeringValue = 0;
            foreach (TreatyTermRecord term in terms ?? Enumerable.Empty<TreatyTermRecord>())
            {
                if (term == null || term.Type == TreatyTermType.WhitePeace)
                    continue;

                int value = Math.Max(0, term.WarScoreCost);
                if (term.ToKingdomId == winnerKingdomId)
                {
                    if (IsReciprocalExchangeTerm(term.Type))
                        exchangeDemandValue += value;
                    else
                        strategicDemandCost += value;
                }
                else if (term.FromKingdomId == winnerKingdomId && term.WasVoluntaryOffering)
                    offeringValue += value;
                else if (term.FromKingdomId == winnerKingdomId && IsReciprocalExchangeTerm(term.Type))
                    reciprocalExchangeValue += value;
            }

            int rawCredit = (int)Math.Floor(
                offeringValue * BellumCivileConstants.TreatyOfferingCreditMultiplier);
            int proportionalCap = (int)Math.Floor(
                Math.Max(0, warScoreBudget) * BellumCivileConstants.TreatyOfferingCreditBudgetCap);
            int creditCap = Math.Min(
                BellumCivileConstants.TreatyOfferingCreditAbsoluteCap,
                proportionalCap);
            int appliedCredit = Math.Min(rawCredit, creditCap);
            return new TreatyWarScoreSummary(
                strategicDemandCost,
                exchangeDemandValue,
                reciprocalExchangeValue,
                offeringValue,
                rawCredit,
                appliedCredit,
                creditCap);
        }

        public static IReadOnlyList<int> GetEffectiveTermImpacts(
            IEnumerable<TreatyTermRecord> terms,
            string winnerKingdomId,
            int warScoreBudget)
        {
            List<TreatyTermRecord> termList = (terms ?? Enumerable.Empty<TreatyTermRecord>()).ToList();
            TreatyWarScoreSummary summary = Calculate(termList, winnerKingdomId, warScoreBudget);
            int cumulativeOfferingValue = 0;
            int allocatedCredit = 0;
            List<int> impacts = new List<int>(termList.Count);
            foreach (TreatyTermRecord term in termList)
            {
                if (term == null || term.Type == TreatyTermType.WhitePeace)
                {
                    impacts.Add(0);
                    continue;
                }

                if (term.ToKingdomId == winnerKingdomId)
                {
                    impacts.Add(Math.Max(0, term.WarScoreCost));
                    continue;
                }

                if (term.FromKingdomId == winnerKingdomId && term.WasVoluntaryOffering)
                {
                    cumulativeOfferingValue += Math.Max(0, term.WarScoreCost);
                    int nextAllocatedCredit = summary.OfferingValue <= 0
                        ? 0
                        : (int)Math.Floor(
                            cumulativeOfferingValue
                            * summary.AppliedOfferingCredit
                            / (float)summary.OfferingValue);
                    impacts.Add(-(nextAllocatedCredit - allocatedCredit));
                    allocatedCredit = nextAllocatedCredit;
                    continue;
                }

                if (term.FromKingdomId == winnerKingdomId && IsReciprocalExchangeTerm(term.Type))
                {
                    impacts.Add(-Math.Max(0, term.WarScoreCost));
                    continue;
                }

                impacts.Add(0);
            }

            return impacts;
        }

        public static bool IsReciprocalExchangeTerm(TreatyTermType type)
        {
            return type == TreatyTermType.ReleasePrisoner
                || type == TreatyTermType.Reparations
                || type == TreatyTermType.Tribute;
        }
    }
}
