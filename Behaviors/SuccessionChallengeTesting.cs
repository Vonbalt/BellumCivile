using System.Linq;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        internal string StartTestChallenge(Kingdom realm, Hero heir)
        {
            ReconcileSovereigns();
            var blocked = _records.LastOrDefault(r => (r.Realm == realm || r.OutcomeRealm == realm)
                && (r.IsOpen || r.RealmBlockedUntil > Day || r.Challenger == heir && r.PersonalBlockedUntil > Day));
            if (blocked != null)
                return $"Challenge blocked: {blocked.Id}, {blocked.Phase}. {blocked.Failure} "
                    + $"Realm cooldown: {System.Math.Max(0, blocked.RealmBlockedUntil - Day):0.#} days; "
                    + $"personal cooldown: {System.Math.Max(0, blocked.PersonalBlockedUntil - Day):0.#} days.";
            var record = TryBegin(realm, heir, MBRandom.RandomFloat);
            if (record == null)
                return "Cannot start: requires an available adult NPC lawful heir with loyalty below 25%, "
                    + "in the royal household or leading their own realm clan, with a valid cadet plan, no pacification, existing rebel faction, active civil war or pending accession. No checks were bypassed.";
            record.ManualTest = true;
            ResolveAppeal(record);
            BellumCivileLogger.Log($"Manual succession challenge {record.Id}: {heir.StringId}, {record.Demand}, {record.Phase}.");
            return $"Challenge {record.Id}: {record.Demand}, {record.Phase}. "
                + "Close the console and resume the campaign. Real support, settlements, war and cooldowns apply; natural initiation is also enabled.";
        }

        private void AdvanceRulerResponses()
        {
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.AwaitingResponse
                && r.Realm.RulingClan != Clan.PlayerClan
                && (!r.ReportPending || r.Realm != Clan.PlayerClan?.Kingdom)).ToList())
            {
                // Persist before validation: temporary unavailability must not buy another roll.
                if (record.ResponseRoll < 0) record.ResponseRoll = MBRandom.RandomFloat;
                RecordNpcResponse(record, record.ResponseRoll);
            }
        }

        private void ShowRulerResponse()
        {
            var record = _records.FirstOrDefault(r => r.Phase == SuccessionChallengePhase.AwaitingResponse
                && r.Realm.RulingClan == Clan.PlayerClan && ValidParticipants(r));
            if (record == null) return;
            RefreshMissingConcession(record);
            var text = DemandMessage(record, true);
            var choices = new List<InquiryElement>();
            if (record.Demand == SuccessionChallengeDemand.InheritanceFirst && record.Estate != null)
                choices.Add(new InquiryElement(SuccessionChallengePhase.LandSettlement,
                    Label("{=BC_Challenge_Grant}Grant lands for a household"), null, CanDeliver(record), EstatePreview(record.Estate)));
            choices.Add(new InquiryElement(SuccessionChallengePhase.CrownSettlement,
                Label("{=BC_Challenge_Yield}Yield the Crown"), null,
                CanSettleChallengedCrown(record) && (record.CrownEstate == null || CrownEstateAvailable(record.CrownEstate)),
                Label("{=BC_Challenge_CrownEstateHeading}If you yield the Crown:") + "\n" + EstatePreview(record.CrownEstate)));
            choices.Add(new InquiryElement(SuccessionChallengePhase.WarRequired,
                Label("{=BC_Challenge_Refuse}Refuse the demands"), null, true,
                Label("{=BC_Challenge_Refusal}The challenger may form a cadet house and seize a portion of the royal domains before civil war. Victory or defeat will be followed by the usual settlement and tribunal.")));
            _inquiry = record;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                Label("{=BC_Challenge_Title}Dynastic Falling-out"), text + "\n\n" + PowerSummary(record)
                    + "\n\n" + DemandEstatePreview(record), choices, false, 1, 1,
                Label("{=BC_Challenge_Confirm}Confirm response"), "",
                selected =>
                {
                    _inquiry = null;
                    if (selected == null || selected.Count != 1 || !(selected[0].Identifier is SuccessionChallengePhase phase)) return;
                    if (!RecordPlayerResponse(record, phase))
                        InformationManager.DisplayMessage(new InformationMessage(Label("{=BC_Challenge_Changed}The challenge conditions changed. No response was recorded.")));
                }, null), true);
        }

        private static string EstatePreview(CrownAccessionRecord estate)
        {
            if (estate == null) return Label("{=BC_Challenge_NoEstate}No royal lands will be transferred.");
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var text = new TextObject("{=BC_Challenge_Estate}New cadet house: {HOUSE}\nFiefs: {FIEFS}\nTitles: {TITLES}\nGold: {GOLD}");
            text.SetTextVariable("HOUSE", estate.CadetName ?? "?");
            text.SetTextVariable("FIEFS", string.Join(", ", estate.EndowmentFiefs.Select(id => Settlement.Find(id)?.Name.ToString() ?? id)));
            text.SetTextVariable("TITLES", string.Join(", ", estate.EndowmentTitles.Select(id => titles?.GetTitle(id)?.Name ?? id)));
            text.SetTextVariable("GOLD", estate.EndowmentGold);
            return text.ToString();
        }

        private static string DemandEstatePreview(SuccessionChallengeRecord record)
        {
            if (record.Demand == SuccessionChallengeDemand.Crown)
                return Label("{=BC_Challenge_CrownEstateHeading}If you yield the Crown:") + "\n" + EstatePreview(record.CrownEstate);
            return record.Estate == null
                ? Label("{=BC_Challenge_NoConcession}No landed grant can presently be offered without violating the settlement's conditions. Your last holding cannot be given away. You may still yield the Crown or refuse the demand.")
                : Label("{=BC_Challenge_InheritanceHeading}To grant a household estate and retain your crown:") + "\n" + EstatePreview(record.Estate);
        }

        private static void AnnounceConcession(SuccessionChallengeRecord record)
        {
            if (record.ConcessionAnnounced) return;
            record.ConcessionAnnounced = true;
            var text = new TextObject("{=BC_Challenge_ConcessionSettled}{RULER} has granted lands to {HEIR}, settling the dynastic challenge in {REALM} without civil war.");
            SetSubjects(text, record);
            BellumCivileNotifications.Show(text, BellumNotificationColors.Inheritance,
                primaryKingdom: record.Realm, primaryClan: record.Challenger.Clan);
        }
    }
}
