using System;
using BellumCivile;
using HarmonyLib;

internal static class MarriageDowrySimulation
{
    internal static void Run(Action<bool, string> check)
    {
        var payments = typeof(PlayerMarriageAgreement).Assembly.GetType("BellumCivile.MarriagePayments");
        var premium = AccessTools.Method(payments, "ApplyPrestigePremium");
        const int ordinary = 50000;
        int Price(int sendingTier, int receivingTier, FeudalTitleType? sendingRank, FeudalTitleType? receivingRank) =>
            (int)premium.Invoke(null, new object[] { ordinary, sendingTier, receivingTier, sendingRank, receivingRank });
        var baron = FeudalTitleType.Barony;
        var king = FeudalTitleType.Kingdom;
        int tierOnly = Price(6, 1, baron, baron);
        int rankOnly = Price(6, 6, king, baron);
        int both = Price(6, 1, king, baron);
        check(tierOnly == 112500 && rankOnly == 102500 && both == 138750,
            "Renown and peerage independently raise the quote; the weaker signal contributes only half");
        check(Price(1, 6, baron, king) == ordinary && Price(6, 6, king, king) == ordinary,
            "Peers and houses marrying down never pay a prestige surcharge");
        check(Price(6, 1, baron, king) == tierOnly && Price(1, 6, king, baron) == rankOnly,
            "Being stronger on one measure does not erase a genuine gap on the other");
        check(Price(6, 0, FeudalTitleType.Empire, null) == 150000, "Even the largest combined gap caps the base dowry at 3x");
        check(Price(4, 4, baron, null) == 67500, "An unlanded house ranks one step below a baron");

        FeudalTitleType? Rank(int rank) => rank < 0 ? (FeudalTitleType?)null : (FeudalTitleType)rank;
        int cases = 0, violations = 0;
        for (int sendingTier = 0; sendingTier <= 6; sendingTier++)
        for (int receivingTier = 0; receivingTier <= 6; receivingTier++)
        for (int sendingRank = -1; sendingRank <= 4; sendingRank++)
        for (int receivingRank = -1; receivingRank <= 4; receivingRank++)
        {
            cases++;
            int price = Price(sendingTier, receivingTier, Rank(sendingRank), Rank(receivingRank));
            if (price < ordinary || price > ordinary * 3
                || (receivingTier < 6 && Price(sendingTier, receivingTier + 1, Rank(sendingRank), Rank(receivingRank)) > price)
                || (receivingRank < 4 && Price(sendingTier, receivingTier, Rank(sendingRank), Rank(receivingRank + 1)) > price))
                violations++;
        }
        check(violations == 0, $"Across {cases:N0} tier/rank combinations, gaining renown or rank never makes the same match more expensive");
        Console.WriteLine($"50,000 base dowry: tier gap only {tierOnly:N0}; peerage gap only {rankOnly:N0}; both {both:N0}; "
            + $"both with the Crown heir premium {both + 45000:N0}.");
    }
}
