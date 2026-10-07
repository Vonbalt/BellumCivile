using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    internal struct RefugeConsiderations
    {
        public int RulerRelation, ExileRelation, FriendlyCourtiers, HostileCourtiers;
        public int ClanTier, Strongholds, NobleHouses, Mercy, Generosity;
        public float AverageCourtRelation, ExileDays, ReturnDistrust;
        public bool SameCulture, EnemyOfOrigin, FamilyTie;
    }

    internal struct RefugeAssessment
    {
        public float Preference, Acceptance;
        public bool ExileWilling, RulerWilling;
    }

    internal static class RefugeSelectionHelper
    {
        internal const int HostileRelationCutoff = -60;
        private const float CloseCandidateRange = 15f;

        public static Kingdom FindBestRefuge(Clan clan, Kingdom originKingdom, params Kingdom[] additionalExcludedKingdoms)
        {
            if (clan?.Leader == null) return null;
            var recovery = Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
            var excluded = new HashSet<Kingdom>(additionalExcludedKingdoms ?? new Kingdom[0]);
            if (originKingdom != null && recovery?.HasDepartureRecord(clan, originKingdom) != true)
                excluded.Add(originKingdom);
            var scored = new List<(Kingdom Kingdom, float Score)>();
            int unavailable = 0, unwilling = 0, refused = 0;
            foreach (Kingdom realm in Kingdom.All)
            {
                if (excluded.Contains(realm) || !CanConsiderRefuge(clan, realm, recovery))
                { unavailable++; continue; }
                RefugeAssessment assessment = Assess(clan, realm, originKingdom, recovery);
                if (realm.RulingClan != Clan.PlayerClan && !assessment.RulerWilling) { refused++; continue; }
                if (!assessment.ExileWilling) { unwilling++; continue; }
                scored.Add((realm, assessment.Preference + MBRandom.RandomFloat * 5f));
            }
            if (scored.Count == 0)
            {
                BellumCivileLogger.Log($"Exile refuge deferred; clan={clan.StringId}; unavailable={unavailable}; exile_unwilling={unwilling}; ruler_refused={refused}.");
                return null;
            }
            float best = scored.Max(candidate => candidate.Score);
            var close = scored.Where(candidate => best - candidate.Score <= CloseCandidateRange).ToList();
            return close[MBRandom.RandomInt(close.Count)].Kingdom;
        }

        internal static bool IsValidRefuge(Kingdom realm) => realm != null && !realm.IsEliminated
            && realm.RulingClan != null && !realm.RulingClan.IsEliminated
            && realm.RulingClan.Leader?.IsAlive == true && !realm.RulingClan.Leader.IsDisabled
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
            && realm.Settlements.Any(s => s != null && (s.IsTown || s.IsCastle))
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(realm) == null;

        internal static bool CanConsiderRefuge(Clan clan, Kingdom realm, ExiledClanRecoveryBehavior recovery)
            => clan?.Leader?.IsAlive == true && !clan.Leader.IsDisabled && !clan.Leader.IsChild
                && IsValidRefuge(realm) && recovery?.IsExcludedRefuge(clan, realm) != true;

        internal static bool CanRecruitLandlessClan(Clan clan, Kingdom realm)
        {
            if (clan == null || realm == null || clan.Kingdom != null || clan.IsMinorFaction
                || clan.IsUnderMercenaryService || clan == Clan.PlayerClan || !clan.IsNoble
                || !ExiledClanRecoveryBehavior.IsLandlessForExile(clan)) return true;
            if (!IsValidRefuge(realm)) return false;
            // A player ruler's invitation is a deliberate pardon, not automatic asylum.
            if (realm.RulingClan == Clan.PlayerClan) return true;
            var recovery = Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
            if (!CanConsiderRefuge(clan, realm, recovery)) return false;
            return Assess(clan, realm, null, recovery).RulerWilling;
        }

        private static RefugeAssessment Assess(Clan clan, Kingdom realm, Kingdom origin, ExiledClanRecoveryBehavior recovery)
        {
            Hero exile = clan.Leader, ruler = realm.RulingClan.Leader;
            var input = new RefugeConsiderations
            {
                RulerRelation = ruler.GetRelation(exile), ExileRelation = exile.GetRelation(ruler),
                SameCulture = clan.Culture != null && realm.Culture == clan.Culture,
                EnemyOfOrigin = origin != null && !origin.IsEliminated && realm.IsAtWarWith(origin),
                FamilyTie = PersonalKinshipHelper.GetBond(exile, ruler) != PersonalBloodBond.None,
                ClanTier = clan.Tier, Strongholds = realm.Fiefs.Count,
                Mercy = ruler.GetTraitLevel(DefaultTraits.Mercy), Generosity = ruler.GetTraitLevel(DefaultTraits.Generosity),
                ExileDays = recovery?.GetExileDays(clan) ?? 0f, ReturnDistrust = recovery?.GetReturnDistrust(clan, realm) ?? 0f
            };
            float total = 0f;
            foreach (Clan courtier in realm.Clans)
            {
                if (courtier == null || courtier == clan || courtier.IsEliminated || courtier.IsMinorFaction
                    || courtier.IsUnderMercenaryService || courtier.Leader?.IsAlive != true) continue;
                int relation = ClampRelation(exile.GetRelation(courtier.Leader));
                input.NobleHouses++;
                total += relation;
                if (relation >= 30) input.FriendlyCourtiers++;
                if (relation <= -30) input.HostileCourtiers++;
            }
            input.AverageCourtRelation = input.NobleHouses > 0 ? total / input.NobleHouses : 0f;
            return Evaluate(input);
        }

        internal static RefugeAssessment Evaluate(RefugeConsiderations input)
        {
            float preference = 20f + ClampRelation(input.ExileRelation) * 0.75f + input.AverageCourtRelation * 0.4f
                + Math.Min(30, input.FriendlyCourtiers * 10) - Math.Min(30, input.HostileCourtiers * 10)
                + (input.SameCulture ? 20f : 0f) + (input.EnemyOfOrigin ? 25f : 0f) + (input.FamilyTie ? 15f : 0f)
                + Math.Min(20f, Math.Max(0f, input.ExileDays) / 7f) - input.ReturnDistrust;
            float capacity = Math.Max(-15f, Math.Min(10f, (input.Strongholds - input.NobleHouses) * 3f));
            float acceptance = 20f + ClampRelation(input.RulerRelation) * 0.75f + input.AverageCourtRelation * 0.25f
                + Math.Min(15, input.FriendlyCourtiers * 5) - Math.Min(15, input.HostileCourtiers * 5)
                + (input.SameCulture ? 10f : 0f) + (input.EnemyOfOrigin ? 15f : 0f) + (input.FamilyTie ? 15f : 0f)
                + Math.Min(10, Math.Max(0, input.ClanTier) * 2) + capacity + input.Mercy * 4f + input.Generosity * 3f
                - input.ReturnDistrust;
            return new RefugeAssessment
            {
                Preference = preference, Acceptance = acceptance,
                ExileWilling = input.ExileRelation > HostileRelationCutoff && preference >= 0f,
                RulerWilling = input.RulerRelation > HostileRelationCutoff && acceptance >= 0f
            };
        }

        private static int ClampRelation(int relation) => Math.Max(-100, Math.Min(100, relation));
    }
}
