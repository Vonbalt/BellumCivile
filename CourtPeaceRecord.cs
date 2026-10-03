using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtPeaceRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public WarScoreRecord War;
        [SaveableField(3)] public List<Clan> Members = new List<Clan>();
        [SaveableField(4)] public double ActivatedDay;
        [SaveableField(5)] public bool Activated;

        internal float BonusFor(WarScoreRecord war, Clan member, double now, double deadline) =>
            member != null && Members?.Contains(member) == true ? ActiveBonus(war, now, deadline) : 0;

        internal float ActiveBonus(WarScoreRecord war, double now, double deadline) =>
            Activated && War != null && War == war
            && CourtPeaceRules.InWindow(now, ActivatedDay, deadline) ? CourtPeaceRules.AcceptanceBonus : 0;
    }
}
