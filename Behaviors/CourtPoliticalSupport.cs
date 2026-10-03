using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private bool ReceivesPoliticalSupport(CourtAgendaRecord agenda, Clan voter, List<Clan> sessionMembers)
        {
            if (agenda?.Faction == null || !Eligible(voter, agenda.Realm)) return false;
            bool ruler = voter == agenda.Realm.RulingClan;
            return CourtAgendaRules.ReceivesPoliticalSupport(voter == Clan.PlayerClan, ruler,
                ruler && GetFavoredBloc(agenda.Realm) == agenda.Faction.Type,
                agenda.Faction.Members.Contains(voter), sessionMembers?.Contains(voter) == true);
        }
    }
}
