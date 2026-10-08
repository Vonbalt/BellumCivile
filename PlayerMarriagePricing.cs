using System;
using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;

namespace BellumCivile
{
    internal sealed class PlayerMarriageQuote
    {
        internal PlayerMarriageAgreement Agreement;
        internal int Compensation;
    }

    internal static class PlayerMarriagePricing
    {
        private static readonly ConditionalWeakTable<MarriageBarterable, PlayerMarriageQuote> Quotes
            = new ConditionalWeakTable<MarriageBarterable, PlayerMarriageQuote>();
        [ThreadStatic] internal static bool ReadingNative;

        internal static int Calculate(int nativeValue, int tierGap, float risk, bool npcLeaves,
            bool crownHeir, bool clanHeir, bool continuation)
        {
            double ordinary = Math.Max(0d, -(double)nativeValue) * (1 + .5 * Math.Min(4, Math.Max(0, tierGap)));
            double vulnerability = Math.Max(0, Math.Min(1, risk));
            double price = npcLeaves
                ? ordinary + (crownHeir ? 100000 : clanHeir ? 50000 : 0) + (continuation ? 50000 * vulnerability : 0)
                : ordinary * (continuation ? 1 - .5 * vulnerability : 1);
            return (int)Math.Min(int.MaxValue, Math.Ceiling(price));
        }

        internal static bool TryGet(MarriageBarterable item, out PlayerMarriageQuote quote)
        {
            quote = null;
            if (ReadingNative || item == null) return false;
            if (Quotes.TryGetValue(item, out quote)) return true;
            Hero first = item.HeroBeingProposedTo, second = item.ProposingHero;
            if (first?.Clan == null || second?.Clan == null || first.Clan == second.Clan) return false;
            Hero player = first.Clan == Clan.PlayerClan ? first : second.Clan == Clan.PlayerClan ? second : null;
            if (player == null || TreatyMarriageClanContext.TryResolve(first, second, out _)) return false;
            Hero other = player == first ? second : first;
            Clan destination = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
            return Bind(item, PlayerMarriageAgreement.Create(player, other, destination, true), out quote);
        }

        internal static bool Bind(MarriageBarterable item, PlayerMarriageAgreement agreement, out PlayerMarriageQuote quote)
        {
            if (Quotes.TryGetValue(item, out quote)) return true;
            // Capture once for barter redraws; legality is checked again before any payment.
            var policy = new MarriageHouseholdPolicy();
            bool npcLeaves = agreement.Destination == agreement.PlayerHouse;
            Hero bride = agreement.Player.IsFemale ? agreement.Player : agreement.Other;
            bool continuation = npcLeaves ? policy.IsContinuation(agreement.Other)
                : bride.Age >= SuccessionLawHelper.GetAgeOfMajority() && bride.Age <= 45;
            int native;
            bool previous = ReadingNative;
            try
            {
                ReadingNative = true;
                using (new NpcMarriageClanContext(agreement.Player, agreement.Other, agreement.Destination))
                    native = new MarriageBarterable(item.OriginalOwner, null, agreement.Player, agreement.Other)
                        .GetUnitValueForFaction(agreement.OtherHouse);
            }
            finally { ReadingNative = previous; }
            quote = new PlayerMarriageQuote { Agreement = agreement, Compensation = Calculate(native,
                agreement.OtherHouse.Tier - agreement.PlayerHouse.Tier,
                BellumMarriageStrategyHelper.PlayerMarriageHouseRisk(agreement.OtherHouse), npcLeaves,
                policy.CrownHeirs.Contains(agreement.Other), policy.MustRemain(agreement.Other), continuation) };
            Quotes.Add(item, quote);
            return true;
        }
    }
}
