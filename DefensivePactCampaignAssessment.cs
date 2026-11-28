using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // One candidate pair, with geography supplied by the bounded frontier scheduler.
    internal sealed class DefensivePactCampaignAssessment
    {
        internal Kingdom Member { get; private set; }
        internal Kingdom Target { get; private set; }
        internal double Threat { get; private set; }
        internal int JoiningThreshold { get; private set; }
        internal int Fiefs { get; private set; }
        internal double MemberStrength { get; private set; }
        internal double TargetStrength { get; private set; }
        internal int RulerRelation { get; private set; }
        internal bool AtPeace { get; private set; }
        internal bool Allied { get; private set; }
        internal bool HasPledge { get; private set; }
        internal bool IsTargetClient { get; private set; }
        internal bool IsEligible { get; private set; }

        internal static bool SharesCulture(Kingdom member, Kingdom target) => member?.Culture != null && member.Culture == target?.Culture;

        internal static DefensivePactCampaignAssessment Capture(Kingdom member, Kingdom target, int frontierDistance,
            double now, DefensivePactBehavior pacts, DefensivePactThreatBehavior threatBehavior,
            HostagePactBehavior hostages, ClientKingdomBehavior clients)
        {
            if (!BellumCivileOptions.DefensivePactsActive || member == null || target == null || member == target
                || member.IsEliminated || target.IsEliminated
                || string.IsNullOrWhiteSpace(member.StringId) || string.IsNullOrWhiteSpace(target.StringId)
                || member.RulingClan?.Leader?.IsAlive != true || target.RulingClan?.Leader?.IsAlive != true
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(member)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(target)
                || CivilWarConflictBehavior.IsRealmTransferPending(member) || CivilWarConflictBehavior.IsRealmTransferPending(target)
                || member.Fiefs == null || target.Fiefs == null || member.FactionsAtWarWith == null
                || pacts == null || hostages == null || clients == null || threatBehavior?.IsReady != true
                || frontierDistance < 0 || !DefensivePactThreatRules.Finite(now) || now < 0)
                return null;
            int fiefs = member.Fiefs.Count;
            var result = new DefensivePactCampaignAssessment
            {
                Member = member,
                Target = target,
                Threat = threatBehavior.GetThreat(target.StringId, now),
                JoiningThreshold = DefensivePactEligibilityRules.JoiningThreshold(frontierDistance, fiefs, SharesCulture(member, target)),
                Fiefs = fiefs,
                MemberStrength = member.CurrentTotalStrength,
                TargetStrength = target.CurrentTotalStrength,
                RulerRelation = member.RulingClan.Leader.GetRelation(target.RulingClan.Leader),
                AtPeace = !member.FactionsAtWarWith.OfType<Kingdom>().Any(enemy => enemy != null && !enemy.IsEliminated),
                Allied = member.IsAllyWith(target),
                HasPledge = hostages?.HasPactBetween(member, target) == true,
                IsTargetClient = clients?.IsClientOf(member, target) == true
            };
            result.IsEligible = DefensivePactEligibilityRules.CanJoin(result.Threat, result.JoiningThreshold, result.Fiefs,
                result.MemberStrength, result.TargetStrength, result.RulerRelation, result.AtPeace,
                pacts.HasMembership(member.StringId), result.Allied, result.HasPledge, result.IsTargetClient,
                pacts.IsCooldownActive(target.StringId, now));
            return result;
        }

    }
}
