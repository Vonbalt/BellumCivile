using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class PlayerMarriageAgreementBehavior : CampaignBehaviorBase
    {
        private List<PlayerMarriageAgreement> _agreements = new List<PlayerMarriageAgreement>();
        internal static PlayerMarriageAgreementBehavior Instance => Campaign.Current?.CampaignBehaviorManager?.GetBehavior<PlayerMarriageAgreementBehavior>();
        internal static MarriageOfferCampaignBehavior Native => Campaign.Current?.CampaignBehaviorManager?.GetBehavior<MarriageOfferCampaignBehavior>();
        private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedPlayerClanHero");
        private static readonly FieldInfo OtherField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedOtherClanHero");
        private static readonly FieldInfo WaitingField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_acceptedMarriageOffersThatWaitingForAvailability");
        internal static Hero ActivePlayer => Native == null ? null : (Hero)PlayerField.GetValue(Native);
        internal static Hero ActiveOther => Native == null ? null : (Hero)OtherField.GetValue(Native);
        internal static Dictionary<Hero, Hero> Waiting => Native == null ? null : (Dictionary<Hero, Hero>)WaitingField.GetValue(Native);
        internal PlayerMarriageAgreement Find(Hero a, Hero b) => _agreements.FirstOrDefault(r => r != null && r.Matches(a, b));
        internal PlayerMarriageAgreement Active => Find(ActivePlayer, ActiveOther);
        internal void Remove(PlayerMarriageAgreement record) => _agreements.Remove(record);
        public override void RegisterEvents() { }
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bc_playerMarriageAgreements", ref _agreements);
            if (_agreements == null) _agreements = new List<PlayerMarriageAgreement>();
        }

        internal bool Capture(Hero player, Hero other)
        {
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic) return true;
            BeforeHourlyTick();
            if (player?.Clan != Clan.PlayerClan || other?.Clan == null || Native == null) return false;
            if (ActivePlayer != null || Native.IsHeroEngaged(player) || Native.IsHeroEngaged(other)) return false;
            var policy = new MarriageHouseholdPolicy();
            Clan ordinary = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(player, other);
            if (!policy.TryChoosePlayerOffer(player, other, ordinary, out Clan destination)) return false;
            _agreements.Add(PlayerMarriageAgreement.Create(player, other, destination, false));
            return true;
        }

        internal static bool IsOwnReservation(Hero hero)
        {
            var record = PlayerMarriageValidationScope.Current;
            var waiting = Waiting;
            return record != null && (hero == record.Player || hero == record.Other)
                && waiting != null && waiting.TryGetValue(record.Player, out Hero partner) && partner == record.Other
                && !waiting.Any(p => p.Key != record.Player && (p.Key == hero || p.Value == hero));
        }

        internal static bool Ready(PlayerMarriageAgreement record, out TextObject reason)
        {
            if (!record.Validate(out reason)) return false;
            if (record.Negotiated && (StrategicMarriageBehavior.HasMarriageOfferFor(record.Player)
                || StrategicMarriageBehavior.HasMarriageOfferFor(record.Other)))
            { reason = new TextObject("{=BC_Marriage_AlreadyPromised}One of the betrothed is already promised in another marriage offer."); return false; }
            using (new PlayerMarriageValidationScope(record))
            using (new NpcMarriageClanContext(record.Player, record.Other, record.Destination))
            {
                var model = Campaign.Current.Models.MarriageModel;
                if (model.GetClanAfterMarriage(record.Player, record.Other) == record.Destination
                    && model.IsCoupleSuitableForMarriage(record.Player, record.Other)) return true;
            }
            reason = new TextObject("{=BC_Marriage_NotReady}The betrothed cannot marry at present. The marriage terms must be discussed again when both are available.");
            return false;
        }

        internal static bool StillLegal(PlayerMarriageAgreement record, out TextObject reason)
        {
            if (!record.Validate(out reason)) return false;
            using (new PlayerMarriageValidationScope(record))
            using (new MarriageProspectEvaluation())
            {
                var model = Campaign.Current.Models.MarriageModel;
                if (model.IsCoupleSuitableForMarriage(record.Player, record.Other)
                    && (record.Negotiated || model.ShouldNpcMarriageBetweenClansBeAllowed(record.PlayerHouse, record.OtherHouse)))
                    return true;
            }
            reason = new TextObject("{=BC_Marriage_AgreementChanged}This marriage can no longer proceed on the agreed terms.");
            return false;
        }

        internal void BeforeHourlyTick()
        {
            // Only our new agreements are inspected; native pre-update reservations stay untouched.
            foreach (var record in _agreements.ToArray())
            {
                if (record?.Player == null || record.Other == null) { Remove(record); continue; }
                bool queued = Waiting?.TryGetValue(record.Player, out Hero partner) == true && partner == record.Other;
                bool active = record.Matches(ActivePlayer, ActiveOther);
                if (!queued && !active) { Remove(record); continue; }
                if (StillLegal(record, out TextObject reason)) continue;
                if (queued) Waiting.Remove(record.Player);
                if (active) Native.OnMarriageOfferDeclinedOnPopUp();
                Remove(record);
                Notify(reason);
            }
        }

        internal static void Notify(TextObject reason)
        { if (reason != null) InformationManager.DisplayMessage(new InformationMessage(reason.ToString())); }

        internal static void ConfirmDeparture(PlayerMarriageAgreement record, System.Action accept, System.Action cancel)
        {
            var policy = new MarriageHouseholdPolicy();
            if (record.Departing != record.Player || !policy.MustRemain(record.Player)) { accept(); return; }
            TextObject warning = new TextObject("{=BC_Marriage_ConfirmHeirDeparture}{TERMS} This is an heir of your house. Do you agree to this departure?")
                .SetTextVariable("TERMS", record.HouseholdText());
            if (policy.CrownHeirs.Contains(record.Player)) warning = new TextObject("{=BC_Marriage_HeirDepartureWithCrown}{WARNING}\n\n{CROWN}")
                .SetTextVariable("WARNING", warning).SetTextVariable("CROWN", record.CrownWarning());
            InformationManager.ShowInquiry(new InquiryData(record.FormText().ToString(), warning.ToString(), true, true,
                GameTexts.FindText("str_accept").ToString(), GameTexts.FindText("str_cancel").ToString(), accept, cancel));
        }
    }
}
