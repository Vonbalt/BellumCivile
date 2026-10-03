using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        internal const int CrownInitiativeCost = 100;
        private Dictionary<string, CampaignTime> _crownActionUntil = new Dictionary<string, CampaignTime>();

        internal CampaignTime CrownActionUntil(Kingdom realm, string kind) => realm != null
            && _crownActionUntil.TryGetValue(realm.StringId + ":" + kind, out var until) ? until : CampaignTime.Zero;

        internal bool CrownActionCoolingDown(Kingdom realm, string kind) => CrownActionUntil(realm, kind).ToDays > CampaignTime.Now.ToDays;

        private void RecoverCrownActionCooldowns()
        {
            // Older saves have effect receipts but no independent realm cooldowns.
            foreach (var agenda in _agendas)
            {
                CampaignTime until;
                if (agenda.Appeasement?.Applied == true) until = agenda.Appeasement.Until;
                else if (agenda.Liberation?.Activated == true)
                    until = CampaignTime.Days((float)agenda.Liberation.ActivatedDay
                        + (agenda.HasScheduleSnapshot ? agenda.TermDays : BellumCivileOptions.CourtTermDays));
                else continue;
                RememberCrownActionUntil(agenda.Realm, agenda.ObjectiveData?.Kind, until);
            }
            foreach (var offer in _protectionOffers)
                if (offer.ReplyDay > 0)
                    RememberCrownActionUntil(offer.Client, CourtProtectionRules.Kind, CampaignTime.Days((float)offer.TermEnd));
        }

        private void RememberCrownActionUntil(Kingdom realm, string kind, CampaignTime until)
        {
            if (realm != null && kind != null && until.ToDays > CrownActionUntil(realm, kind).ToDays)
                _crownActionUntil[realm.StringId + ":" + kind] = until;
        }

        private void StartCrownActionCooldown(CourtAgendaRecord agenda)
        {
            var days = agenda.HasScheduleSnapshot ? agenda.TermDays : BellumCivileOptions.CourtTermDays;
            _crownActionUntil[agenda.Realm.StringId + ":" + agenda.ObjectiveData.Kind] = CampaignTime.Now + CampaignTime.Days(days);
        }

        private TextObject CrownActionOptionHint(Kingdom realm, string kind, string hint)
        {
            if (CrownActionCoolingDown(realm, kind))
                return new TextObject("{=BC_CrownActionCooldown}This royal initiative may next be undertaken on {DATE}.")
                    .SetTextVariable("DATE", CrownActionUntil(realm, kind).ToString());
            return new TextObject(hint);
        }
    }
}
