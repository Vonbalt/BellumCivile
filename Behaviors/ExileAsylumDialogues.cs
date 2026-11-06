using System;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class ExiledClanRecoveryBehavior
    {
        private AsylumVisitRecord _asylumConversation;
        private Settlement _asylumOfferedVillage;
        private bool _asylumAdmissionSucceeded;
        private bool _asylumOathSworn;
        private bool _asylumGrantSucceeded;

        private static bool IsAsylumAudiencePresent(AsylumVisitRecord visit, Hero npc)
        {
            if (npc == null || visit?.Guest != npc || !AsylumAuthorityValid(visit) || !AsylumGuestValid(visit)
                || Hero.MainHero.IsPrisoner || Hero.MainHero.CurrentSettlement != visit.Settlement
                || npc.CurrentSettlement != visit.Settlement || npc.PartyBelongedTo != visit.Party
                || !IsAsylumHost(visit.Settlement, visit.Realm)) return false;
            var context = Campaign.Current.CurrentConversationContext;
            return context == ConversationContext.Default || context == ConversationContext.PartyEncounter;
        }

        private AsylumVisitRecord ConversationAsylumVisit()
        {
            var npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || !_asylumVisits.TryGetValue(npc.Clan.StringId, out var visit)
                || visit.State != AsylumVisitRecord.Waiting || !visit.Deadline.IsFuture || visit.Clan.Kingdom != null
                || visit.Clan.IsAtWarWith(visit.Realm) || !IsAsylumAudiencePresent(visit, npc)) return null;
            return visit;
        }

        private bool CanDiscussAsylum() => ConversationAsylumVisit() != null;

        private bool CanConcludeAsylum() => _asylumAdmissionSucceeded && _asylumOathSworn
            && _asylumConversation?.State == AsylumVisitRecord.Accepted
            && _asylumVisits.TryGetValue(_asylumConversation.Clan.StringId, out var current) && current == _asylumConversation
            && _asylumConversation.Clan.Kingdom == _asylumConversation.Realm
            && IsAsylumAudiencePresent(_asylumConversation, Hero.OneToOneConversationHero);

        private bool AsylumOpening()
        {
            var visit = ConversationAsylumVisit();
            if (visit == null) return false;
            StringHelpers.SetCharacterProperties("PLAYER", Hero.MainHero.CharacterObject);
            StringHelpers.SetCharacterProperties("CONVERSATION_NPC", visit.Guest.CharacterObject);
            string title;
            if (!FeudalTitleDisplayHelper.TryGetHighestDisplayTitle(Hero.MainHero, out title) || string.IsNullOrWhiteSpace(title))
                title = new TextObject(Hero.MainHero.IsFemale ? "{=BC_Asylum_AddressLady}lady" : "{=BC_Asylum_AddressLord}lord").ToString();
            MBTextManager.SetTextVariable("BC_ASYLUM_LIEGE_TITLE", title);
            MBTextManager.SetTextVariable("BC_ASYLUM_REALM", visit.Realm.Name);
            MBTextManager.SetTextVariable("BC_ASYLUM_EXPLANATION", AsylumExplanation(visit));
            MBTextManager.SetTextVariable("BC_ASYLUM_DAYS", Math.Max(0, (int)Math.Ceiling(visit.Deadline.ToDays - CampaignTime.Now.ToDays)));
            SetAsylumOathTexts();
            return true;
        }

        private static void SetAsylumOathTexts()
        {
            MBTextManager.SetTextVariable("BC_ASYLUM_OATH_1", new TextObject("{=BC_Asylum_OathFirst}I swear homage to you as lawful {BC_ASYLUM_LIEGE_TITLE}."));
            MBTextManager.SetTextVariable("BC_ASYLUM_OATH_2", new TextObject("{=BC_Asylum_OathSecond}I will be your loyal {?CONVERSATION_NPC.GENDER}follower{?}man{\\?} as long as my breath remains..."));
            MBTextManager.SetTextVariable("BC_ASYLUM_OATH_3", new TextObject("{=BC_Asylum_OathThird}And I will be at your side to fight your enemies, should you need my sword."));
            MBTextManager.SetTextVariable("BC_ASYLUM_OATH_4", new TextObject("{=BC_Asylum_OathFourth}And I shall defend your rights and the rights of your legitimate heirs."));
        }

        private void BeginAsylumConversation()
        {
            _asylumConversation = ConversationAsylumVisit();
            _asylumOfferedVillage = null;
            _asylumAdmissionSucceeded = false;
            _asylumOathSworn = _asylumGrantSucceeded = false;
            if (_asylumConversation?.Guest.HasMet == false)
            {
                _asylumConversation.Guest.SetHasMet();
                Campaign.Current.ConversationManager.CurrentConversationIsFirst = true;
            }
        }

        private bool CanOfferAsylumEstate(out TextObject reason)
        {
            var visit = _asylumConversation;
            var grants = LordshipBehavior.Instance;
            bool available = CanConcludeAsylum() && grants?.Ready == true && grants.Villages(Clan.PlayerClan)
                .Any(v => grants.CanGrantVassal(Clan.PlayerClan, v, visit.Guest));
            reason = available ? null : new TextObject("{=BC_Asylum_NoEstates}You have no vacant estate that can be granted to this house.");
            return available;
        }

        private void ListAsylumEstates()
        {
            _asylumOfferedVillage = null;
            var visit = _asylumConversation;
            var grants = LordshipBehavior.Instance;
            ConversationSentence.SetObjectsToRepeatOver(!CanConcludeAsylum() || grants?.Ready != true ? new object[0]
                : grants.Villages(Clan.PlayerClan).Where(v => grants.CanGrantVassal(Clan.PlayerClan, v, visit.Guest))
                    .OrderBy(v => v.Name.ToString()).Cast<object>().ToArray());
        }

        private bool AsylumEstateStillAvailable()
            => _asylumOfferedVillage != null && LordshipBehavior.Instance?.CanGrantVassal(
                Clan.PlayerClan, _asylumOfferedVillage, _asylumConversation?.Guest) == true;

        private void AcknowledgeAsylumOath()
            => _asylumOathSworn = _asylumConversation != null && ConversationAsylumVisit() == _asylumConversation;

        private void CompleteAsylumOath()
        {
            if (CanConcludeAsylum()) return;
            _asylumAdmissionSucceeded = false;
            var visit = ConversationAsylumVisit();
            if (!_asylumOathSworn || visit == null || visit != _asylumConversation) return;
            // The oath establishes vassalage. An optional estate is a separate, later gift.
            _asylumOfferedVillage = null;
            visit.PromisedVillage = null;
            visit.State = AsylumVisitRecord.Accepted;
            try
            {
                _asylumAdmissionSucceeded = CompleteAsylumAdmission(visit);
                if (!_asylumAdmissionSucceeded && visit.Clan.Kingdom == null) visit.State = AsylumVisitRecord.Waiting;
            }
            catch (Exception ex) { BellumCivileLogger.Log($"Asylum oath completion deferred; clan={visit.Clan.StringId}; error={ex}"); }
        }

        private void CompleteAsylumEstate()
        {
            _asylumGrantSucceeded = false;
            if (!CanConcludeAsylum()) return;
            var visit = _asylumConversation;
            var existing = LordshipBehavior.Instance?.GetGrant(visit.Guest);
            if (visit.GrantAwarded && visit.PromisedVillage == _asylumOfferedVillage
                && existing?.Village == _asylumOfferedVillage && existing?.IsValid == true)
            { _asylumGrantSucceeded = true; return; }
            if (!AsylumEstateStillAvailable()) return;
            visit.PromisedVillage = _asylumOfferedVillage;
            visit.GrantSettled = false;
            try { _asylumGrantSucceeded = CompleteAsylumAdmission(visit) && visit.GrantAwarded; }
            catch (Exception ex) { BellumCivileLogger.Log($"Asylum estate grant deferred; clan={visit.Clan.StringId}; error={ex}"); }
        }

        private void RefuseAsylumAudience()
        {
            var visit = ConversationAsylumVisit();
            if (visit == null) return;
            visit.State = AsylumVisitRecord.Refused;
            ApplyAsylumRelation(visit, -10);
        }

        private void RegisterAsylumDialogues(CampaignGameStarter starter)
        {
            starter.AddDialogLine("bc_asylum_opening", "start", "bc_asylum_choices",
                "{=BC_Asylum_AudienceOpening}My {BC_ASYLUM_LIEGE_TITLE}, {BC_ASYLUM_EXPLANATION} I would pledge allegiance to you and be counted among your loyal followers.",
                AsylumOpening, BeginAsylumConversation, 240);
            starter.AddPlayerLine("bc_asylum_discuss", "hero_main_options", "bc_asylum_resume",
                "{=BC_Asylum_ResumePetition}Let us speak of your request to enter my service.", CanDiscussAsylum, BeginAsylumConversation, 120);
            starter.AddDialogLine("bc_asylum_resume", "bc_asylum_resume", "bc_asylum_choices",
                "{=BC_Asylum_ResumeAnswer}My sword is yours, if you will receive me.", AsylumOpening, null);
            starter.AddPlayerLine("bc_asylum_accept", "bc_asylum_choices", "bc_asylum_oath_ready",
                "{=BC_Asylum_AudienceAccept}I shall be pleased to accept your sword into my service, if you are ready to swear homage to me.", CanDiscussAsylum,
                () => _asylumOfferedVillage = null, 120);
            starter.AddPlayerLine("bc_asylum_accept_estate", "bc_asylum_conclusion", "bc_asylum_estates_prompt",
                "{=BC_Asylum_AudienceAcceptEstate}As a token of my esteem, I would grant you a fief to help maintain your men.", CanConcludeAsylum,
                null, 120, CanOfferAsylumEstate, null);
            starter.AddPlayerLine("bc_asylum_refuse", "bc_asylum_choices", "bc_asylum_refused",
                "{=BC_Asylum_AudienceRefuse}I cannot accept your service. You must seek another liege.",
                CanDiscussAsylum, RefuseAsylumAudience, 100);
            starter.AddDialogLine("bc_asylum_refused", "bc_asylum_refused", "close_window",
                "{=BC_Asylum_AudienceRefused}Then I shall trouble you no further, my {?PLAYER.GENDER}lady{?}lord{\\?}.", null, null);
            starter.AddPlayerLine("bc_asylum_postpone", "bc_asylum_choices", "bc_asylum_postponed",
                "{=BC_Asylum_AudiencePostpone}I must give this more thought.", null, null, 90);
            starter.AddDialogLine("bc_asylum_postponed", "bc_asylum_postponed", "close_window",
                "{=BC_Asylum_AudiencePostponed}As you wish. I can wait here another {BC_ASYLUM_DAYS} days, but then I must seek another liege.",
                AsylumOpening, null);
            starter.AddDialogLine("bc_asylum_estates_prompt", "bc_asylum_estates_prompt", "bc_asylum_estates",
                "{=BC_Asylum_EstatesPrompt}You honor me, my {?PLAYER.GENDER}lady{?}lord{\\?}. Which lands would you have me hold?", CanConcludeAsylum, ListAsylumEstates);
            starter.AddRepeatablePlayerLine("bc_asylum_estate", "bc_asylum_estates", "bc_asylum_estate_selected",
                "{=BC_Asylum_EstateChoice}{BC_ASYLUM_ESTATE}.", "{=BC_Asylum_MoreEstates}I am considering another estate.", "bc_asylum_estates_prompt",
                () =>
                {
                    var village = ConversationSentence.CurrentProcessedRepeatObject as Settlement;
                    if (village == null) return false;
                    ConversationSentence.SelectedRepeatLine.SetTextVariable("BC_ASYLUM_ESTATE", LordshipBehavior.Name(village));
                    return true;
                }, () =>
                {
                    _asylumOfferedVillage = ConversationSentence.SelectedRepeatObject as Settlement;
                    MBTextManager.SetTextVariable("BC_ASYLUM_ESTATE", LordshipBehavior.Name(_asylumOfferedVillage));
                }, 120);
            starter.AddPlayerLine("bc_asylum_estate_cancel", "bc_asylum_estates", "bc_asylum_conclusion_resume",
                "{=BC_Asylum_ReconsiderEstate}Let me reconsider the grant.", null, () => _asylumOfferedVillage = null);
            starter.AddDialogLine("bc_asylum_estate_selected", "bc_asylum_estate_selected", "bc_asylum_estate_confirm",
                "{=BC_Asylum_EstateSelected}I would be honored to hold it in your service.", CanConcludeAsylum, null);
            starter.AddPlayerLine("bc_asylum_estate_confirm", "bc_asylum_estate_confirm", "bc_asylum_estate_result",
                "{=BC_Asylum_EstatePromise}As a token of my esteem and to help you maintain your men, I grant you {BC_ASYLUM_ESTATE} as your fief. Go to them, protect them, and collect your dues.",
                CanConcludeAsylum, CompleteAsylumEstate);
            starter.AddPlayerLine("bc_asylum_estate_back", "bc_asylum_estate_confirm", "bc_asylum_estates_prompt",
                "{=BC_Asylum_ReconsiderEstate}Let me reconsider the grant.", CanConcludeAsylum, null);
            starter.AddDialogLine("bc_asylum_estate_stale", "bc_asylum_estate_selected", "bc_asylum_conclusion",
                "{=BC_Asylum_EstateUnavailable}It seems those lands can no longer be granted. Perhaps another estate?",
                () => CanConcludeAsylum() && !AsylumEstateStillAvailable(), () => _asylumOfferedVillage = null, 200);
            starter.AddDialogLine("bc_asylum_oath_ready", "bc_asylum_oath_ready", "bc_asylum_oath_prompt",
                "{=BC_Asylum_OathReady}I am ready, my {?PLAYER.GENDER}lady{?}lord{\\?}.", AsylumOpening, null, 100);
            starter.AddPlayerLine("bc_asylum_oath_prompt", "bc_asylum_oath_prompt", "bc_asylum_oath_first",
                "{=BC_Asylum_OathPrompt}Good. Then repeat the words of the oath with me: {BC_ASYLUM_OATH_1}", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_first", "bc_asylum_oath_first", "bc_asylum_oath_bond",
                "{=!}{BC_ASYLUM_OATH_1}", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_oath_bond", "bc_asylum_oath_bond", "bc_asylum_oath_second",
                "{=!}{BC_ASYLUM_OATH_2}", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_second", "bc_asylum_oath_second", "bc_asylum_oath_third_prompt",
                "{=!}{BC_ASYLUM_OATH_2}", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_oath_third_prompt", "bc_asylum_oath_third_prompt", "bc_asylum_oath_third",
                "{=!}{BC_ASYLUM_OATH_3}", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_third", "bc_asylum_oath_third", "bc_asylum_oath_fourth_prompt",
                "{=!}{BC_ASYLUM_OATH_3}", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_oath_fourth_prompt", "bc_asylum_oath_fourth_prompt", "bc_asylum_oath_fourth",
                "{=!}{BC_ASYLUM_OATH_4}", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_fourth", "bc_asylum_oath_fourth", "bc_asylum_oath_accept",
                "{=!}{BC_ASYLUM_OATH_4}", CanDiscussAsylum, AcknowledgeAsylumOath);
            starter.AddPlayerLine("bc_asylum_oath_accept", "bc_asylum_oath_accept", "bc_asylum_oath_result",
                "{=BC_Asylum_OathAccepted}Very well. You have given me your solemn oath. From this day forward you are my sworn vassal. I give you my protection, and pledge to uphold your rights according to the laws and customs of the realm.", CanDiscussAsylum, CompleteAsylumOath);
            foreach (string token in new[] { "bc_asylum_oath_prompt", "bc_asylum_oath_bond", "bc_asylum_oath_third_prompt", "bc_asylum_oath_fourth_prompt", "bc_asylum_oath_accept" })
                starter.AddPlayerLine(token + "_postpone", token, "bc_asylum_postponed",
                    "{=BC_Asylum_AudiencePostpone}I must give this more thought.", CanDiscussAsylum, null, 90);
            starter.AddDialogLine("bc_asylum_oath_success", "bc_asylum_oath_result", "bc_asylum_conclusion",
                "{=BC_Asylum_OathThanks}I thank you, my {?PLAYER.GENDER}lady{?}lord{\\?}.", CanConcludeAsylum, null, 120);
            starter.AddPlayerLine("bc_asylum_conclude", "bc_asylum_conclusion", "bc_asylum_service_thanks",
                "{=BC_Asylum_Conclude}I have great hopes for you. Serve me well and I promise you will rise high.", CanConcludeAsylum, null, 100);
            starter.AddDialogLine("bc_asylum_conclusion_resume", "bc_asylum_conclusion_resume", "bc_asylum_conclusion",
                "{=BC_Asylum_AwaitCommands}I await your commands, my {?PLAYER.GENDER}lady{?}lord{\\?}.", CanConcludeAsylum, null);
            starter.AddDialogLine("bc_asylum_service_thanks", "bc_asylum_service_thanks", "close_window",
                "{=BC_Asylum_ServicePledge}I shall serve you faithfully, my {?PLAYER.GENDER}lady{?}lord{\\?}.", CanConcludeAsylum, null);
            starter.AddDialogLine("bc_asylum_estate_success", "bc_asylum_estate_result", "close_window",
                "{=BC_Asylum_ServicePledge}I shall serve you faithfully, my {?PLAYER.GENDER}lady{?}lord{\\?}.", () => _asylumGrantSucceeded, null, 120);
            starter.AddDialogLine("bc_asylum_estate_failed", "bc_asylum_estate_result", "close_window",
                "{=BC_Asylum_GrantUnavailable}It seems that grant can no longer be made. We may speak of it another time.", null, null, 100);
            starter.AddDialogLine("bc_asylum_oath_changed", "bc_asylum_oath_result", "close_window",
                "{=BC_Asylum_OathChanged}It seems matters have changed. We must speak of this another time.", null, null, 100);
            foreach (string token in new[] { "bc_asylum_resume", "bc_asylum_oath_ready", "bc_asylum_oath_first",
                "bc_asylum_oath_second", "bc_asylum_oath_third", "bc_asylum_oath_fourth", "bc_asylum_postponed" })
                starter.AddDialogLine(token + "_stale", token, "close_window",
                    "{=BC_Asylum_VisitStale}This matter will have to wait until another time.",
                    () => !CanDiscussAsylum(), null, 300);
            foreach (string token in new[] { "bc_asylum_choices", "bc_asylum_oath_prompt", "bc_asylum_oath_bond",
                "bc_asylum_oath_third_prompt", "bc_asylum_oath_fourth_prompt", "bc_asylum_oath_accept" })
                starter.AddPlayerLine(token + "_stale", token, "bc_asylum_stale_exit",
                    "{=BC_Asylum_EndChangedAudience}Let us leave this for another time.",
                    () => !CanDiscussAsylum(), null, 300);
            foreach (string token in new[] { "bc_asylum_estates_prompt", "bc_asylum_estate_selected", "bc_asylum_conclusion_resume", "bc_asylum_service_thanks" })
                starter.AddDialogLine(token + "_stale", token, "close_window",
                    "{=BC_Asylum_VisitStale}This matter will have to wait until another time.", () => !CanConcludeAsylum(), null, 300);
            foreach (string token in new[] { "bc_asylum_conclusion", "bc_asylum_estates", "bc_asylum_estate_confirm" })
                starter.AddPlayerLine(token + "_stale", token, "bc_asylum_stale_exit",
                    "{=BC_Asylum_EndChangedAudience}Let us leave this for another time.", () => !CanConcludeAsylum(), null, 300);
            starter.AddDialogLine("bc_asylum_stale_exit", "bc_asylum_stale_exit", "close_window",
                "{=BC_Asylum_VisitStale}This matter will have to wait until another time.", null, null);
        }

        private TextObject AsylumExplanation(AsylumVisitRecord visit)
        {
            var origin = ResolveKingdomById(GetDictionaryValue(_trackedOriginKingdomIds, visit.Clan.StringId));
            _trackedExileCauseIds.TryGetValue(visit.Clan.StringId, out int cause);
            TextObject text;
            switch ((ExileCause)cause)
            {
                case ExileCause.RebelIndependence: text = new TextObject("{=BC_Asylum_ExplainIndependence}We rose against {REALM} to win our independence. We were defeated, and my house was banished."); break;
                case ExileCause.RebelAbdication: text = new TextObject("{=BC_Asylum_ExplainAbdication}We sought to depose the ruler of {REALM}, but were defeated. My house was banished."); break;
                case ExileCause.RebelInstallRuler: text = new TextObject("{=BC_Asylum_ExplainClaimant}We pressed a claim to the crown of {REALM}, but our cause was lost and my house was banished."); break;
                case ExileCause.Treason: text = new TextObject("{=BC_Asylum_ExplainTreason}I was condemned for treason in {REALM}. I say I was unjustly accused."); break;
                case ExileCause.LoyalistIndependence: text = new TextObject("{=BC_Asylum_ExplainSecessionLoyalist}We fought to keep {REALM} together. When we were defeated, my house was driven into exile."); break;
                case ExileCause.LoyalistAbdication:
                case ExileCause.LoyalistInstallRuler: text = new TextObject("{=BC_Asylum_ExplainLoyalist}We stood by our former ruler in {REALM}. With that cause lost, my house was banished."); break;
                case ExileCause.KingdomDestroyed: text = new TextObject("{=BC_Asylum_ExplainDestroyed}With the fall of {REALM}, my house has been left without a liege."); break;
                case ExileCause.Expulsion: text = new TextObject("{=BC_Asylum_ExplainExpelled}My house was driven out of {REALM}. We have no lands left to call our own."); break;
                case ExileCause.VoluntaryDeparture: text = new TextObject("{=BC_Asylum_ExplainDeparture}My house has left the service of {REALM}. We seek a new liege."); break;
                default: text = new TextObject("{=BC_Asylum_ExplainUnknown}My house has fallen on hard times. We have no lands left to call our own."); break;
            }
            return text.SetTextVariable("REALM", origin?.Name ?? new TextObject("{=BC_Asylum_FormerRealm}our former realm"));
        }
    }
}
