using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal struct CourtSocialTieScore
    {
        internal float Friendship, Marriage, Hierarchy;
        internal float Total => Math.Min(C.IdeologyFactionSocialGravityCap, Friendship + Marriage + Hierarchy);
    }

    // Pair reads live only for one affiliation batch, never across campaign ticks.
    internal sealed class CourtSocialTieContext
    {
        private struct Connection
        {
            internal float Friendship;
            internal bool Marriage;
        }

        private sealed class HouseTies
        {
            internal readonly Dictionary<Clan, Connection> Connections = new Dictionary<Clan, Connection>();
            internal readonly Dictionary<string, float> LegalLieges = new Dictionary<string, float>();
            internal readonly Dictionary<string, float> ActualLieges = new Dictionary<string, float>();
        }

        private readonly FeudalTitleBehavior _titles;
        private readonly Kingdom _realm;
        private readonly Dictionary<Clan, HouseTies> _ties = new Dictionary<Clan, HouseTies>();
        internal int RelationReads { get; private set; }

        internal CourtSocialTieContext(Kingdom realm, FeudalTitleBehavior titles)
        {
            _realm = realm;
            _titles = titles;
        }

        internal static float FriendshipPull(int relation) => Math.Min(C.IdeologyFactionFriendshipCap,
            Math.Max(0f, relation - C.IdeologyFactionFriendshipThreshold) * C.IdeologyFactionFriendshipScale);

        internal CourtSocialTieScore GetScore(Clan clan, FactionObject faction)
        {
            if (!IsEligible(clan) || faction == null || !faction.IsIdeology)
                return default;
            if (!_ties.TryGetValue(clan, out HouseTies ties))
            {
                ties = new HouseTies();
                ReadLieges(clan, FeudalHierarchyMode.DeJure, ties.LegalLieges);
                ReadLieges(clan, FeudalHierarchyMode.DeFacto, ties.ActualLieges);
                _ties.Add(clan, ties);
            }

            var score = new CourtSocialTieScore();
            float legalPull = 0f, actualPull = 0f;
            int alliedHouses = 0;
            var seen = new HashSet<Clan>();
            foreach (Clan member in faction.Members)
            {
                if (member == clan || !seen.Add(member) || !IsEligible(member))
                    continue;
                if (!ties.Connections.TryGetValue(member, out Connection connection))
                {
                    RelationReads++;
                    connection = new Connection
                    {
                        Friendship = FriendshipPull(clan.Leader.GetRelation(member.Leader)),
                        Marriage = MarriageAllianceHelper.HasMarriageAlliance(clan, member)
                    };
                    ties.Connections.Add(member, connection);
                }
                score.Friendship = Math.Max(score.Friendship, connection.Friendship);
                if (connection.Marriage) alliedHouses++;
                if (ties.LegalLieges.TryGetValue(member.StringId, out float legal))
                    legalPull = Math.Max(legalPull, legal);
                if (ties.ActualLieges.TryGetValue(member.StringId, out float actual))
                    actualPull = Math.Max(actualPull, actual);
            }
            score.Marriage = Math.Min(C.IdeologyFactionMemberMarriageAllianceCap,
                alliedHouses * C.IdeologyFactionMemberMarriageAllianceBonus);
            score.Hierarchy = (legalPull + actualPull) * 0.5f;
            return score;
        }

        private bool IsEligible(Clan clan) => CourtPoliticalPositionBehavior.IsNoble(clan)
            && clan.Leader != null && !clan.Leader.IsDead
            && (clan.Kingdom == _realm || CourtPoliticalPositionBehavior.ResolveRealm(clan.Kingdom) == _realm);

        private void ReadLieges(Clan clan, FeudalHierarchyMode mode, Dictionary<string, float> lieges)
        {
            if (_titles == null) return;
            foreach (FeudalTitleRecord title in _titles.GetTitlesHeldByClan(clan, deJure: mode == FeudalHierarchyMode.DeJure))
            {
                var visited = new HashSet<string> { title.TitleId };
                FeudalTitleRecord parent = _titles.GetParentTitle(title, mode);
                int depth = 0;
                while (parent != null && parent.IsActive && depth++ < 32 && visited.Add(parent.TitleId))
                {
                    string holder = mode == FeudalHierarchyMode.DeJure ? parent.DeJureHolderClanId : parent.DeFactoHolderClanId;
                    if (!string.IsNullOrEmpty(holder) && holder != clan.StringId)
                    {
                        float pull = depth == 1 ? C.IdeologyFactionImmediateLiegePull : C.IdeologyFactionHighLiegePull;
                        if (!lieges.TryGetValue(holder, out float existing) || pull > existing)
                            lieges[holder] = pull;
                    }
                    parent = _titles.GetParentTitle(parent, mode);
                }
            }
        }
    }
}
