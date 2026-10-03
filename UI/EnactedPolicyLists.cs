using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BellumCivile.UI
{
    internal static class EnactedPolicyLists
    {
        internal static void Populate(Kingdom realm, FactionType? alignment, bool crown,
            MBBindingList<AgendaItemVM> support, MBBindingList<AgendaItemVM> oppose)
        {
            support.Clear();
            oppose.Clear();
            if (realm == null) return;
            var policies = realm.ActivePolicies.Where(p => p != null).Distinct().OrderBy(p => p.Name.ToString()).ToList();
            var stances = EnactedPolicyStances.Build(IdeologyPolicyAgendaConfig.Instance, policies.Select(p => p.StringId), alignment, crown);
            foreach (var policy in policies)
            {
                if (!stances.TryGetValue(policy.StringId, out var stance)) continue;
                bool supported = stance == CourtPolicyStance.Support;
                var hint = new TaleWorlds.Localization.TextObject("{=BC_PolicyLastingInterest}This law's lasting effects on the faction's interests determine its contribution to mood. Willingness to grant further royal authority may change with confidence in the Crown; consult the policy card for the current political stance.");
                var item = new AgendaItemVM(policy.Name.ToString(), Color.ConvertStringToColor(supported ? "#82E06AFF" : "#FF6B6BFF"), hint.ToString());
                (supported ? support : oppose).Add(item);
            }
        }
    }
}
