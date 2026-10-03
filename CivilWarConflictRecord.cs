using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarSideRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public FactionObject Faction;
        [SaveableField(3)] public Kingdom Realm;
        [SaveableField(4)] public Clan OriginalHouse;
        [SaveableField(5)] public bool Closed;
    }

    public sealed class CivilWarPairRecord
    {
        internal const string RivalryPrefix = "bc_rival_";
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public string AttackerSideId;
        [SaveableField(3)] public string DefenderSideId;
        [SaveableField(4)] public WarScoreRecord Score;
        [SaveableField(5)] public bool Closed;
        internal bool IsRivalry => AttackerSideId != CivilWarConflictRecord.CrownSide
            && DefenderSideId != CivilWarConflictRecord.CrownSide;
    }

    public sealed class CivilWarConflictRecord
    {
        internal const string CrownSide = "crown";
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public string SovereignTitleId;
        [SaveableField(3)] public Kingdom OriginalRealm;
        [SaveableField(4)] public Kingdom CrownRealm;
        [SaveableField(5)] public List<CivilWarSideRecord> Sides = new List<CivilWarSideRecord>();
        [SaveableField(6)] public List<CivilWarPairRecord> Pairs = new List<CivilWarPairRecord>();
        [SaveableField(7)] public bool Closed;
        [SaveableField(8)] public List<Clan> CrownDefeatedHouses = new List<Clan>();

        internal void RecordCrownDefeat(IEnumerable<Clan> houses)
        {
            // Older saves have no defeat history. New settlements journal it before clans return.
            CrownDefeatedHouses = CrownDefeatedHouses ?? new List<Clan>();
            foreach (var house in houses ?? Enumerable.Empty<Clan>())
                if (house != null && !CrownDefeatedHouses.Contains(house)) CrownDefeatedHouses.Add(house);
        }

        internal bool CanReceiveLoyalistReward(Clan house) => house != null
            && CrownDefeatedHouses?.Contains(house) != true;

        internal CivilWarSideRecord Observe(FactionObject faction, Kingdom realm, WarScoreRecord score)
        {
            if (Closed || faction == null || realm == null) return null;
            var side = Sides.FirstOrDefault(s => s.Faction == faction);
            if (side?.Closed == true) return null;
            if (side == null)
            {
                side = new CivilWarSideRecord { Id = Guid.NewGuid().ToString("N"), Faction = faction,
                    Realm = realm, OriginalHouse = faction.Leader };
                Sides.Add(side);
            }
            if (side.Realm != realm) return null;
            var pair = Pairs.FirstOrDefault(p => p.AttackerSideId == side.Id && p.DefenderSideId == CrownSide);
            if (pair == null)
            {
                pair = new CivilWarPairRecord { Id = Guid.NewGuid().ToString("N"), AttackerSideId = side.Id,
                    DefenderSideId = CrownSide, Score = score };
                Pairs.Add(pair);
            }
            else if (!pair.Closed && pair.Score == null) pair.Score = score;
            return side;
        }

        internal void Complete(FactionObject faction)
        {
            var side = Sides.FirstOrDefault(s => s.Faction == faction);
            if (side == null || side.Closed) return;
            side.Closed = true;
            foreach (var pair in Pairs.Where(p => p.AttackerSideId == side.Id || p.DefenderSideId == side.Id)) pair.Closed = true;
            Closed = Sides.All(s => s.Closed);
        }

        internal CivilWarPairRecord ObserveRivalry(FactionObject first, FactionObject second)
        {
            if (Closed || CrownRealm == null || first == null || second == null || first == second || first.Leader == null || second.Leader == null
                || !CivilWarContinuationRules.MutuallyExclusive(first.Type, second.Type,
                    first.ParentKingdom == CrownRealm && second.ParentKingdom == CrownRealm, first.Leader == second.Leader)) return null;
            var a = Sides.FirstOrDefault(s => !s.Closed && s.Faction == first);
            var b = Sides.FirstOrDefault(s => !s.Closed && s.Faction == second);
            if (a == null || b == null || a.Realm == b.Realm) return null;
            if (string.CompareOrdinal(a.Id, b.Id) > 0) { var swap = a; a = b; b = swap; }
            var pair = Pairs.FirstOrDefault(p => p.AttackerSideId == a.Id && p.DefenderSideId == b.Id);
            if (pair != null) return pair.Closed ? null : pair;
            pair = new CivilWarPairRecord { Id = CivilWarPairRecord.RivalryPrefix + Guid.NewGuid().ToString("N"),
                AttackerSideId = a.Id, DefenderSideId = b.Id };
            Pairs.Add(pair);
            return pair;
        }

        internal bool TryGetRivalSides(WarScoreRecord score, out CivilWarSideRecord attacker, out CivilWarSideRecord defender)
        {
            attacker = defender = null;
            if (Closed || score == null || score.ConflictType != WarScoreConflictType.CivilWar) return false;
            var pair = Pairs.FirstOrDefault(p => !p.Closed && p.IsRivalry && p.Score == score && p.Id == score.ContextId);
            if (pair == null) return false;
            var a = Sides.FirstOrDefault(s => !s.Closed && s.Id == pair.AttackerSideId);
            var b = Sides.FirstOrDefault(s => !s.Closed && s.Id == pair.DefenderSideId);
            if (a?.Realm == null || b?.Realm == null || a.Realm.StringId != score.AttackerKingdomId
                || b.Realm.StringId != score.DefenderKingdomId) return false;
            attacker = a;
            defender = b;
            return true;
        }
    }
}
