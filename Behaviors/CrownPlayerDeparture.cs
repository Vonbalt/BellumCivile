using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private CrownAccessionRecord _activeCrownDeparture;
        private CrownAccessionRecord _activeCrownDepartureLetter;
        private int _crownDeparturePostLoadTicks;
        private float _crownDepartureRetrySeconds;

        private static void ResetPlayerCrownDeparture(CrownAccessionRecord record)
        {
            record.PlayerDepartureCaptured = false;
            record.PlayerDepartureObserver = record.PlayerDepartureHero = null;
            record.PlayerDepartureWantsDialogue = record.PlayerDepartureDialogueAttempted = record.PlayerDepartureExplained = false;
            record.PlayerDepartureAddress = record.PlayerDepartureHeroName = record.PlayerDepartureSourceName = record.PlayerDepartureRealmName = null;
            record.PlayerDepartureIsSpouse = false;
            record.PlayerDepartureQueuedAt = default;
        }

        private void CapturePlayerCrownDeparture(CrownAccessionRecord record, bool allowDialogue)
        {
            Hero heir = record?.Heir;
            Clan source = record?.EndowmentHouse;
            if (record == null || record.Completed || record.PlayerDepartureCaptured || !record.CreatesCadet
                || record.ChallengeConcession || record.RegentReplacement || record.Emergency
                || Hero.MainHero == null || source == null || source != Clan.PlayerClan || heir == null
                || heir == Hero.MainHero || heir == source.Leader || record.Realm == null
                || (heir.Clan != source && heir.Clan != record.Cadet)) return;

            record.PlayerDepartureObserver = Hero.MainHero;
            record.PlayerDepartureHero = heir;
            record.PlayerDepartureAddress = CrownDepartureAddress(heir, Hero.MainHero).ToString();
            record.PlayerDepartureHeroName = heir.Name?.ToString() ?? "?";
            record.PlayerDepartureSourceName = source.Name?.ToString() ?? "?";
            record.PlayerDepartureRealmName = record.Realm.Name?.ToString() ?? "?";
            record.PlayerDepartureIsSpouse = heir.Spouse == Hero.MainHero;
            record.PlayerDepartureQueuedAt = CampaignTime.Now;
            record.PlayerDepartureWantsDialogue = allowDialogue && record.Cadet == null && !record.CadetInitialized
                && !record.EndowmentSettled && heir.Clan == source && CanSpeakCrownFarewell(heir);
            record.PlayerDepartureCaptured = true;
            var mercenaries = Campaign.Current?.GetCampaignBehavior<DynamicMercenaryBandBehavior>();
            mercenaries?.ClearPendingPlayerDeparture(heir);
            BellumCivileLogger.Log($"Player-family Crown departure recorded; heir={heir.StringId}; realm={record.Realm.StringId}; dialogue={record.PlayerDepartureWantsDialogue}.");
        }

        private static bool CanSpeakCrownFarewell(Hero hero) => hero != null && hero.IsAlive && !hero.IsDisabled
            && !hero.IsPrisoner && !hero.IsTraveling && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
            && MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty;

        private bool WaitForPlayerCrownFarewell(CrownAccessionRecord record)
        {
            if (_activeCrownDeparture == record) return true;
            if (!record.PlayerDepartureWantsDialogue || record.PlayerDepartureExplained) return false;
            if (record.PlayerDepartureObserver != Hero.MainHero || record.Heir != record.PlayerDepartureHero
                || !CanSpeakCrownFarewell(record.PlayerDepartureHero)
                || CampaignTime.Now.ToDays - record.PlayerDepartureQueuedAt.ToDays >= 1d)
            {
                record.PlayerDepartureWantsDialogue = false;
                return false;
            }
            return true;
        }

        private void RegisterCrownDepartureDialogue(CampaignGameStarter starter)
        {
            _crownDeparturePostLoadTicks = 2;
            starter.AddDialogLine("bc_crown_departure_opening", "start", "bc_crown_departure_acknowledge",
                "{=BC_CrownDeparture_Dialogue}{ADDRESS}, the Crown of {REALM} has passed to me. To take my place as sovereign, I must establish a household of my own and leave {SOURCE_CLAN}. {FAMILY_TIES}",
                IsCrownDepartureConversation, null, 260);
            starter.AddPlayerLine("bc_crown_departure_acknowledge", "bc_crown_departure_acknowledge", "close_window",
                "{=BC_CrownDeparture_Acknowledge}I understand. Go and take your rightful place.",
                IsCrownDepartureConversation, AcknowledgeCrownDeparture);
        }

        private bool IsCrownDepartureConversation()
        {
            var record = _activeCrownDeparture;
            if (record == null || !record.PlayerDepartureCaptured || record.PlayerDepartureExplained
                || record.PlayerDepartureObserver != Hero.MainHero || record.PlayerDepartureHero == Hero.MainHero
                || Hero.OneToOneConversationHero != record.PlayerDepartureHero) return false;
            MBTextManager.SetTextVariable("ADDRESS", record.PlayerDepartureAddress ?? "");
            MBTextManager.SetTextVariable("REALM", record.PlayerDepartureRealmName ?? "");
            MBTextManager.SetTextVariable("SOURCE_CLAN", record.PlayerDepartureSourceName ?? "");
            MBTextManager.SetTextVariable("FAMILY_TIES", CrownDepartureFamilyTies(record));
            return true;
        }

        private void AcknowledgeCrownDeparture()
        {
            if (!IsCrownDepartureConversation()) return;
            _activeCrownDeparture.PlayerDepartureExplained = true;
            _activeCrownDeparture.PlayerDepartureWantsDialogue = false;
        }

        private void ProcessPlayerCrownDepartures(float dt)
        {
            if (Campaign.Current == null) return;
            if (_activeCrownDeparture == null && !_accessions.Any(r => r.PlayerDepartureCaptured && !r.PlayerDepartureExplained)) return;
            if (_crownDeparturePostLoadTicks > 0) { _crownDeparturePostLoadTicks--; return; }
            if (_crownDepartureRetrySeconds > 0f)
            {
                _crownDepartureRetrySeconds = Math.Max(0f, _crownDepartureRetrySeconds - dt);
                return;
            }
            if (_activeCrownDeparture != null)
            {
                if (CampaignMission.Current != null
                    || Campaign.Current.ConversationManager?.IsConversationInProgress == true
                    || Campaign.Current.ConversationManager?.IsConversationFlowActive == true
                    || !CanPresentCrownDeparture()) return;
                var record = _activeCrownDeparture;
                _activeCrownDeparture = null;
                // Closing without the acknowledgement, or loading an interrupted conversation,
                // must not trap the Crown behind a presentation requirement.
                record.PlayerDepartureWantsDialogue = false;
                Resolve(record);
            }
            if (!CanPresentCrownDeparture()) return;
            if (_activeCrownDepartureLetter != null) _activeCrownDepartureLetter = null;
            foreach (var record in _accessions.Where(r => r.PlayerDepartureCaptured && !r.PlayerDepartureExplained).ToList())
            {
                if (record.PlayerDepartureObserver != Hero.MainHero || record.PlayerDepartureHero == Hero.MainHero
                    || record.Heir != record.PlayerDepartureHero)
                {
                    record.PlayerDepartureWantsDialogue = false;
                    record.PlayerDepartureExplained = true;
                    if (!record.Completed) Resolve(record);
                    continue;
                }
                if (!record.Completed && record.PlayerDepartureWantsDialogue)
                {
                    if (WaitForPlayerCrownFarewell(record) && !record.PlayerDepartureDialogueAttempted
                        && record.PlayerDepartureHero.Clan == record.EndowmentHouse
                        && CanSpeakCrownFarewell(record.PlayerDepartureHero))
                    {
                        record.PlayerDepartureDialogueAttempted = true;
                        if (OpenCrownDepartureConversation(record)) return;
                    }
                    record.PlayerDepartureWantsDialogue = false;
                    Resolve(record);
                }
                if (!record.Completed) continue;
                if (!record.TitleTransferred || record.Cadet == null)
                {
                    record.PlayerDepartureExplained = true;
                    continue;
                }
                if (!CanPresentCrownDeparture()) return;
                ShowCrownDepartureLetter(record);
                return;
            }
        }

        private bool OpenCrownDepartureConversation(CrownAccessionRecord record)
        {
            if (record.PlayerDepartureHero?.CharacterObject == null) return false;
            _activeCrownDeparture = record;
            try
            {
                Campaign.Current.CurrentConversationContext = ConversationContext.Default;
                if (CampaignMission.OpenConversationMission(
                    new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty),
                    new ConversationCharacterData(record.PlayerDepartureHero.CharacterObject, PartyBase.MainParty)) != null)
                    return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Crown farewell dialogue unavailable; heir={record.PlayerDepartureHero.StringId}; error={ex.GetType().Name}:{ex.Message}; letter_fallback=true.");
            }
            _activeCrownDeparture = null;
            return false;
        }

        private void ShowCrownDepartureLetter(CrownAccessionRecord record)
        {
            _activeCrownDepartureLetter = record;
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    CrownDepartureText(record, "{=BC_CrownDeparture_LetterTitle}A Crown for {HEIR}").ToString(),
                    BuildCrownDepartureLetter(record).ToString(), true, false,
                    new TextObject("{=BC_CrownDeparture_LetterAcknowledge}I understand.").ToString(), null,
                    () => { record.PlayerDepartureExplained = true; _activeCrownDepartureLetter = null; }, null), true, false);
            }
            catch (Exception ex)
            {
                _activeCrownDepartureLetter = null;
                _crownDepartureRetrySeconds = 2f;
                BellumCivileLogger.Log($"Crown departure letter deferred; heir={record.PlayerDepartureHero?.StringId}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }

        private static TextObject BuildCrownDepartureLetter(CrownAccessionRecord record)
        {
            Hero hero = record.PlayerDepartureHero;
            if (hero?.IsAlive != true || hero.IsDisabled || record.Realm?.IsEliminated != false
                || record.Realm.RulingClan != record.Cadet)
                return CrownDepartureText(record, "{=BC_CrownDeparture_LetterHistorical}Word reaches you that {HEIR} left {SOURCE_CLAN} to assume the Crown of {REALM}, establishing a new household, {NEW_CLAN}.");
            if (hero.Age < SuccessionLawHelper.GetAgeOfMajority())
                return CrownDepartureText(record, "{=BC_CrownDeparture_LetterMinor}Word reaches you that {HEIR} has left {SOURCE_CLAN} to assume the Crown of {REALM}. The new household, {NEW_CLAN}, will be led by a regent until {HEIR} comes of age.");
            return CrownDepartureText(record, "{=BC_CrownDeparture_LetterBody}A sealed letter from {HEIR} reaches you.\n\n\"{ADDRESS}, I have left {SOURCE_CLAN} to assume the Crown of {REALM}. I now lead {NEW_CLAN}, a household of my own. {FAMILY_TIES}\"");
        }

        private static TextObject CrownDepartureText(CrownAccessionRecord record, string template) => new TextObject(template)
            .SetTextVariable("HEIR", record.PlayerDepartureHeroName ?? "?")
            .SetTextVariable("ADDRESS", record.PlayerDepartureAddress ?? "")
            .SetTextVariable("SOURCE_CLAN", record.PlayerDepartureSourceName ?? "?")
            .SetTextVariable("REALM", record.PlayerDepartureRealmName ?? "?")
            .SetTextVariable("NEW_CLAN", record.Cadet?.Name?.ToString() ?? record.CadetName ?? "?")
            .SetTextVariable("FAMILY_TIES", CrownDepartureFamilyTies(record));

        private static TextObject CrownDepartureFamilyTies(CrownAccessionRecord record) => new TextObject(record.PlayerDepartureIsSpouse
            ? record.PlayerDepartureHero?.IsFemale == true
                ? "{=BC_CrownDeparture_MarriageWife}Whatever duties this crown brings, I remain your wife."
                : "{=BC_CrownDeparture_MarriageHusband}Whatever duties this crown brings, I remain your husband."
            : "{=BC_CrownDeparture_FamilyUnchanged}My duties may change, but we are still family.");

        private static TextObject CrownDepartureAddress(Hero hero, Hero player)
        {
            if (hero.Spouse == player)
                return new TextObject(player.IsFemale ? "{=BC_CrownDeparture_AddressWife}My wife" : "{=BC_CrownDeparture_AddressHusband}My husband");
            if (hero.Mother == player) return new TextObject("{=BC_DynamicMercenaryDeparture_AddressMother}Mother");
            if (hero.Father == player) return new TextObject("{=BC_DynamicMercenaryDeparture_AddressFather}Father");
            if (player.Mother == hero || player.Father == hero)
                return new TextObject(player.IsFemale ? "{=BC_CrownDeparture_AddressDaughter}My daughter" : "{=BC_CrownDeparture_AddressSon}My son");
            if ((hero.Mother != null && hero.Mother == player.Mother) || (hero.Father != null && hero.Father == player.Father))
                return new TextObject(player.IsFemale ? "{=BC_CrownDeparture_AddressSister}Sister" : "{=BC_CrownDeparture_AddressBrother}Brother");
            return player.Name ?? new TextObject("?");
        }

        private static bool CanPresentCrownDeparture()
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            var party = MobileParty.MainParty;
            return map != null && !map.AtMenu && !map.MapConversationActive && map.NextIncident == null && !map.IsSimulationActive
                && !InformationManager.IsAnyInquiryActive() && Campaign.Current?.CurrentMenuContext == null
                && Campaign.Current?.ConversationManager?.IsConversationInProgress != true
                && Campaign.Current?.ConversationManager?.IsConversationFlowActive != true
                && CampaignMission.Current == null && Hero.MainHero?.IsPrisoner != true && PlayerEncounter.Current == null
                && PartyBase.MainParty != null && party != null && party.MapEvent == null && party.SiegeEvent == null;
        }
    }
}
