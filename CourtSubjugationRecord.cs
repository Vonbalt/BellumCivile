using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtSubjugationRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public List<Clan> Members = new List<Clan>();
        [SaveableField(3)] public double ActivatedDay;
        [SaveableField(4)] public bool Activated;

        internal float ActiveBonus(Kingdom target, double now, double deadline) =>
            Activated && Target != null && Target == target
            && CourtAgendaRules.ObjectiveInWindow(now, ActivatedDay, deadline) ? CourtSubjugationRules.SupportBonus : 0;
    }
}
