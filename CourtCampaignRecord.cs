using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtCampaignRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public List<Clan> Members = new List<Clan>();
        [SaveableField(3)] public double ActivatedDay;
        [SaveableField(4)] public bool Activated;

        internal float BonusFor(Kingdom target, Clan member, double now, double deadline) =>
            member != null && Members?.Contains(member) == true ? ActiveBonus(target, now, deadline) : 0;

        internal float ActiveBonus(Kingdom target, double now, double deadline) =>
            Activated && Target != null && Target == target
            && CourtAgendaRules.ObjectiveInWindow(now, ActivatedDay, deadline) ? CourtCampaignRules.SupportBonus : 0;
    }
}
