using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    internal static class RefugeSelectionHelper
    {
        private const float EnemyOfOriginBonus = 100f;
        private const float SameCultureBonus = 60f;
        private const float RulerRelationWeight = 0.75f;
        private const float AverageCourtRelationWeight = 0.40f;
        private const float FriendlyCourtCap = 30f;
        private const float HostileCourtCap = 30f;
        private const float CloseCandidateRange = 15f;

        public static Kingdom FindBestRefuge(Clan clan, Kingdom originKingdom, params Kingdom[] additionalExcludedKingdoms)
        {
            if (clan == null) return null;

            HashSet<Kingdom> excluded = new HashSet<Kingdom>();
            if (originKingdom != null) excluded.Add(originKingdom);

            if (additionalExcludedKingdoms != null)
            {
                foreach (Kingdom excludedKingdom in additionalExcludedKingdoms)
                {
                    if (excludedKingdom != null)
                        excluded.Add(excludedKingdom);
                }
            }

            List<Kingdom> validRefuges = Kingdom.All
                .Where(k => IsValidRefuge(k, excluded))
                .ToList();

            if (validRefuges.Count == 0)
                return null;

            if (originKingdom != null && !originKingdom.IsEliminated)
            {
                List<Kingdom> enemyRefuges = validRefuges
                    .Where(k => k.IsAtWarWith(originKingdom))
                    .ToList();

                Kingdom enemyRefuge = PickBestRefuge(clan, originKingdom, enemyRefuges);
                if (enemyRefuge != null)
                    return enemyRefuge;
            }

            Kingdom culturalRefuge = PickBestRefuge(clan, originKingdom, validRefuges.Where(k => k.Culture == clan.Culture).ToList());
            return culturalRefuge ?? PickBestRefuge(clan, originKingdom, validRefuges);
        }

        private static bool IsValidRefuge(Kingdom kingdom, HashSet<Kingdom> excluded)
        {
            if (kingdom == null || excluded.Contains(kingdom) || kingdom.IsEliminated)
                return false;

            if (kingdom.RulingClan?.Leader == null)
                return false;

            if (!kingdom.Settlements.Any(s => s.IsTown || s.IsCastle))
                return false;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager?.GetFactionByRebelKingdom(kingdom) != null)
                return false;

            return true;
        }

        private static Kingdom PickBestRefuge(Clan clan, Kingdom originKingdom, List<Kingdom> kingdoms)
        {
            if (kingdoms == null || kingdoms.Count == 0)
                return null;

            List<(Kingdom Kingdom, float Score)> scored = kingdoms
                .Select(k => (Kingdom: k, Score: CalculateRefugeScore(clan, originKingdom, k)))
                .OrderByDescending(k => k.Score)
                .ToList();

            float bestScore = scored[0].Score;
            List<Kingdom> closeCandidates = scored
                .Where(k => bestScore - k.Score <= CloseCandidateRange)
                .Select(k => k.Kingdom)
                .ToList();

            return closeCandidates[MBRandom.RandomInt(closeCandidates.Count)];
        }

        private static float CalculateRefugeScore(Clan clan, Kingdom originKingdom, Kingdom targetKingdom)
        {
            if (clan?.Leader == null || targetKingdom == null)
                return float.MinValue;

            float score = MBRandom.RandomFloat * 5f;

            if (originKingdom != null && targetKingdom.IsAtWarWith(originKingdom))
                score += EnemyOfOriginBonus;

            if (targetKingdom.Culture == clan.Culture)
                score += SameCultureBonus;

            Hero exileLeader = clan.Leader;
            Hero targetRuler = targetKingdom.RulingClan?.Leader;
            if (targetRuler != null)
                score += ClampRelation(exileLeader.GetRelation(targetRuler)) * RulerRelationWeight;

            List<Clan> targetClans = targetKingdom.Clans
                .Where(c => c != null && c != clan && !c.IsEliminated && !c.IsMinorFaction && c.Leader != null)
                .ToList();

            if (targetClans.Count > 0)
            {
                float totalRelation = 0f;
                int friendlyCourtiers = 0;
                int hostileCourtiers = 0;

                foreach (Clan targetClan in targetClans)
                {
                    int relation = ClampRelation(exileLeader.GetRelation(targetClan.Leader));
                    totalRelation += relation;

                    if (relation >= 30)
                        friendlyCourtiers++;
                    else if (relation <= -30)
                        hostileCourtiers++;
                }

                score += (totalRelation / targetClans.Count) * AverageCourtRelationWeight;
                score += TaleWorlds.Library.MathF.Min(FriendlyCourtCap, friendlyCourtiers * 10f);
                score -= TaleWorlds.Library.MathF.Min(HostileCourtCap, hostileCourtiers * 10f);
            }

            return score;
        }

        private static int ClampRelation(int relation)
        {
            if (relation > 100) return 100;
            if (relation < -100) return -100;
            return relation;
        }
    }
}
