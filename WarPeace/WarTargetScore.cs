using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.WarPeace
{
    internal enum WarTargetMotiveType
    {
        Liberation,
        StrongClaim,
        WeakClaim,
        DeJureReclamation,
        LocalLandFrontier,
        RealmLandFrontier,
        LocalMaritimeFrontier,
        RealmMaritimeReach,
        CulturalUnification,
        RichFrontierPrize,
        RealmPlunderOpportunity,
        MilitaryVulnerability,
        HostileRuler,
        ForeignRival
    }

    internal sealed class WarTargetMotive
    {
        public WarTargetMotiveType Type { get; set; }
        public float Score { get; set; }
        public TextObject Subject { get; set; }
    }

    internal sealed class WarTargetScore
    {
        public Kingdom TargetKingdom { get; set; }
        public float Score { get; set; }
        public bool IsNeighboringRealm { get; set; }
        public bool IsMaritimeNeighbor { get; set; }
        public bool IsLocalLandNeighbor { get; set; }
        public bool IsLocalMaritimeNeighbor { get; set; }
        public bool IsClaimTarget { get; set; }
        public bool IsLiberationTarget { get; set; }
        public bool IsActiveWar { get; set; }
        public List<string> Reasons { get; } = new List<string>();
        public List<WarTargetMotive> Motives { get; } = new List<WarTargetMotive>();
    }
}
