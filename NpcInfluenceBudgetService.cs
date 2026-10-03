using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal enum NpcInfluenceExpenseKind
    {
        Discretionary,
        CouncilCommitment,
        CrownEmergency,
        InvoluntaryLoss
    }

    internal sealed class NpcInfluenceBudgetAssessment
    {
        public Clan Clan { get; }
        public float CurrentInfluence { get; }
        public float RequestedCost { get; }
        public float ProtectedReserve { get; }
        public float RequiredInfluence => RequestedCost + ProtectedReserve;
        public float SpendableInfluence => Math.Max(0f, CurrentInfluence - ProtectedReserve);
        public bool CanAfford => RequestedCost <= 0f || CurrentInfluence + 0.001f >= RequiredInfluence;

        public NpcInfluenceBudgetAssessment(Clan clan, float currentInfluence, float requestedCost, float protectedReserve)
        {
            Clan = clan;
            CurrentInfluence = Math.Max(0f, currentInfluence);
            RequestedCost = Math.Max(0f, requestedCost);
            ProtectedReserve = Math.Max(0f, protectedReserve);
        }
    }

    /// <summary>
    /// Central affordability policy for influence spent by NPC clans. Player
    /// costs retain their existing immediate-balance behavior.
    /// </summary>
    internal static class NpcInfluenceBudgetService
    {
        public static NpcInfluenceBudgetAssessment Assess(
            Clan clan,
            float cost,
            NpcInfluenceExpenseKind kind,
            float? balanceOverride = null)
        {
            float current = Math.Max(0f, balanceOverride ?? clan?.Influence ?? 0f);
            float normalizedCost = Math.Max(0f, cost);
            float reserve = GetProtectedReserve(clan, normalizedCost, kind);
            return new NpcInfluenceBudgetAssessment(clan, current, normalizedCost, reserve);
        }

        public static bool CanAfford(
            Clan clan,
            float cost,
            NpcInfluenceExpenseKind kind,
            float? balanceOverride = null)
        {
            return clan != null && Assess(clan, cost, kind, balanceOverride).CanAfford;
        }

        public static float GetSpendableInfluence(
            Clan clan,
            NpcInfluenceExpenseKind kind,
            float? balanceOverride = null)
        {
            return clan == null ? 0f : Assess(clan, 0f, kind, balanceOverride).SpendableInfluence;
        }

        public static float GetRoleReserve(Clan clan)
        {
            if (!IsNpcClan(clan))
                return 0f;

            float reserve = clan.Kingdom?.RulingClan == clan
                ? C.NpcInfluenceRulerReserve
                : C.NpcInfluenceClanReserve;

            if (IsAtForeignWar(clan.Kingdom) && (clan.Kingdom?.RulingClan == clan || IsLeadingArmy(clan)))
                reserve += C.NpcInfluenceForeignWarReserveBonus;

            return reserve;
        }

        public static bool TrySpend(
            Clan clan,
            float cost,
            NpcInfluenceExpenseKind kind,
            string source)
        {
            if (clan == null)
                return false;

            float normalizedCost = Math.Max(0f, cost);
            NpcInfluenceBudgetAssessment assessment = Assess(clan, normalizedCost, kind);
            if (!assessment.CanAfford)
            {
                RecordBlocked(clan, normalizedCost, kind, source, assessment.ProtectedReserve);
                return false;
            }

            if (normalizedCost > 0f)
                ChangeClanInfluenceAction.Apply(clan, -normalizedCost);

            GetTelemetry()?.RecordSpend(
                clan,
                source,
                kind,
                normalizedCost,
                normalizedCost,
                blocked: false,
                assessment.ProtectedReserve);
            return true;
        }

        public static float SpendUpToReserve(
            Clan clan,
            float requestedCost,
            NpcInfluenceExpenseKind kind,
            string source,
            params float[] allowedTiers)
        {
            if (clan == null)
                return 0f;

            float normalizedRequest = Math.Max(0f, requestedCost);
            NpcInfluenceBudgetAssessment assessment = Assess(clan, 0f, kind);
            float spendable = assessment.SpendableInfluence;
            float amount = Math.Min(normalizedRequest, spendable);

            if (allowedTiers != null && allowedTiers.Length > 0)
            {
                amount = allowedTiers
                    .Where(tier => tier > 0f && tier <= normalizedRequest + 0.001f && tier <= spendable + 0.001f)
                    .DefaultIfEmpty(0f)
                    .Max();
            }

            if (amount > 0f)
                ChangeClanInfluenceAction.Apply(clan, -amount);

            bool blocked = normalizedRequest > 0f && amount <= 0f;
            GetTelemetry()?.RecordSpend(
                clan,
                source,
                kind,
                normalizedRequest,
                amount,
                blocked,
                assessment.ProtectedReserve);
            return amount;
        }

        public static float ApplyClampedLoss(
            Clan clan,
            float requestedLoss,
            string source,
            float protectedFloor = 0f,
            bool hostile = false)
        {
            if (clan == null)
                return 0f;

            float normalizedRequest = Math.Max(0f, requestedLoss);
            float floor = IsNpcClan(clan) ? Math.Max(0f, protectedFloor) : 0f;
            float applied = Math.Min(normalizedRequest, Math.Max(0f, clan.Influence - floor));
            if (applied > 0f)
                ChangeClanInfluenceAction.Apply(clan, -applied);

            GetTelemetry()?.RecordLoss(clan, source, normalizedRequest, applied, hostile);
            return applied;
        }

        public static void Refund(Clan clan, float amount, NpcInfluenceExpenseKind kind, string source)
        {
            if (clan == null || amount <= 0f)
                return;

            ChangeClanInfluenceAction.Apply(clan, amount);
            GetTelemetry()?.RecordRefund(clan, source, kind, amount);
        }

        public static void RecordBlocked(
            Clan clan,
            float cost,
            NpcInfluenceExpenseKind kind,
            string source,
            float? protectedReserve = null)
        {
            if (!IsNpcClan(clan))
                return;

            float reserve = protectedReserve ?? Assess(clan, cost, kind).ProtectedReserve;
            GetTelemetry()?.RecordBlocked(clan, source, kind, Math.Max(0f, cost), reserve);
        }

        private static float GetProtectedReserve(Clan clan, float cost, NpcInfluenceExpenseKind kind)
        {
            if (!IsNpcClan(clan))
                return 0f;

            switch (kind)
            {
                case NpcInfluenceExpenseKind.Discretionary:
                    return Math.Max(GetRoleReserve(clan), cost);
                case NpcInfluenceExpenseKind.CouncilCommitment:
                    return GetRoleReserve(clan);
                case NpcInfluenceExpenseKind.CrownEmergency:
                    return C.NpcInfluenceEmergencyFloor;
                case NpcInfluenceExpenseKind.InvoluntaryLoss:
                default:
                    return 0f;
            }
        }

        private static bool IsNpcClan(Clan clan)
        {
            return clan != null && clan != Clan.PlayerClan;
        }

        private static bool IsAtForeignWar(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return false;

            return Kingdom.All.Any(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && !other.IsMinorFaction
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)
                && kingdom.IsAtWarWith(other));
        }

        private static bool IsLeadingArmy(Clan clan)
        {
            return clan?.WarPartyComponents.Any(component =>
            {
                var party = component?.MobileParty;
                return party?.Army?.LeaderParty == party;
            }) == true;
        }

        private static InfluenceBudgetTelemetryBehavior GetTelemetry()
        {
            return Campaign.Current?.GetCampaignBehavior<InfluenceBudgetTelemetryBehavior>();
        }
    }
}
