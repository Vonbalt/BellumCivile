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

        private AsylumVisitRecord ConversationAsylumVisit()
        {
            var npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || !_asylumVisits.TryGetValue(npc.Clan.StringId, out var visit)
                || visit.Guest != npc || visit.State != AsylumVisitRecord.Waiting || !visit.Deadline.IsFuture
                || !AsylumAuthorityValid(visit) || !AsylumGuestValid(visit) || visit.Clan.Kingdom != null
                || visit.Clan.IsAtWarWith(visit.Realm)
                || Hero.MainHero.IsPrisoner || Hero.MainHero.CurrentSettlement != visit.Settlement
                || npc.CurrentSettlement != visit.Settlement || npc.PartyBelongedTo != visit.Party
                || !IsAsylumHost(visit.Settlement, visit.Realm)) return null;
            var context = Campaign.Current.CurrentConversationContext;
            return context == ConversationContext.Default || context == ConversationContext.PartyEncounter ? visit : null;
        }

        private bool CanDiscussAsylum() => ConversationAsylumVisit() != null;

        private bool AsylumOpening()
        {
            var visit = ConversationAsylumVisit();
            if (visit == null) return false;
            StringHelpers.SetCharacterProperties("PLAYER", Hero.MainHero.CharacterObject);
            string title;
            if (!FeudalTitleDisplayHelper.TryGetHighestDisplayTitle(Hero.MainHero, out title) || string.IsNullOrWhiteSpace(title))
                title = new TextObject(Hero.MainHero.IsFemale ? "{=BC_Asylum_AddressLady}lady" : "{=BC_Asylum_AddressLord}lord").ToString();
            MBTextManager.SetTextVariable("BC_ASYLUM_LIEGE_TITLE", title);
            MBTextManager.SetTextVariable("BC_ASYLUM_REALM", visit.Realm.Name);
            MBTextManager.SetTextVariable("BC_ASYLUM_EXPLANATION", AsylumExplanation(visit));
            MBTextManager.SetTextVariable("BC_ASYLUM_DAYS", Math.Max(0, (int)Math.Ceiling(visit.Deadline.ToDays - CampaignTime.Now.ToDays)));
            return true;
        }

        private void BeginAsylumConversation()
        {
            _asylumConversation = ConversationAsylumVisit();
            _asylumOfferedVillage = null;
            _asylumAdmissionSucceeded = false;
            if (_asylumConversation?.Guest.HasMet == false)
            {
                _asylumConversation.Guest.SetHasMet();
                Campaign.Current.ConversationManager.CurrentConversationIsFirst = true;
            }
        }

        private bool CanOfferAsylumEstate(out TextObject reason)
        {
            var visit = ConversationAsylumVisit();
            var grants = LordshipBehavior.Instance;
            bool available = visit != null && grants?.Ready == true && grants.Villages(Clan.PlayerClan)
                .Any(v => grants.CanOfferAsylumGrant(Clan.PlayerClan, v, visit.Guest));
            reason = available ? null : new TextObject("{=BC_Asylum_NoEstates}None of your villages are available for a personal grant to this house.");
            return available;
        }

        private void ListAsylumEstates()
        {
            _asylumOfferedVillage = null;
            var visit = ConversationAsylumVisit();
            var grants = LordshipBehavior.Instance;
            ConversationSentence.SetObjectsToRepeatOver(visit == null || grants?.Ready != true ? new object[0]
                : grants.Villages(Clan.PlayerClan).Where(v => grants.CanOfferAsylumGrant(Clan.PlayerClan, v, visit.Guest))
                    .OrderBy(v => v.Name.ToString()).Cast<object>().ToArray());
        }

        private bool AsylumEstateStillAvailable()
            => _asylumOfferedVillage == null || LordshipBehavior.Instance?.CanOfferAsylumGrant(
                Clan.PlayerClan, _asylumOfferedVillage, _asylumConversation?.Guest) == true;

        private void CompleteAsylumOath()
        {
            _asylumAdmissionSucceeded = false;
            var visit = ConversationAsylumVisit();
            if (visit == null || visit != _asylumConversation || !AsylumEstateStillAvailable()) return;
            visit.PromisedVillage = _asylumOfferedVillage;
            visit.State = AsylumVisitRecord.Accepted;
            try
            {
                _asylumAdmissionSucceeded = CompleteAsylumAdmission(visit);
                if (!_asylumAdmissionSucceeded && visit.Clan.Kingdom == null) visit.State = AsylumVisitRecord.Waiting;
            }
            catch (Exception ex) { BellumCivileLogger.Log($"Asylum oath completion deferred; clan={visit.Clan.StringId}; error={ex}"); }
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
                "{=BC_Asylum_AudienceOpening}My {BC_ASYLUM_LIEGE_TITLE}, {BC_ASYLUM_EXPLANATION} I seek protection for my household and offer my house's service in return.",
                AsylumOpening, BeginAsylumConversation, 240);
            starter.AddPlayerLine("bc_asylum_discuss", "hero_main_options", "bc_asylum_resume",
                "{=BC_Asylum_ResumePetition}Let us return to your petition for asylum.", CanDiscussAsylum, BeginAsylumConversation, 120);
            starter.AddDialogLine("bc_asylum_resume", "bc_asylum_resume", "bc_asylum_choices",
                "{=BC_Asylum_ResumeAnswer}My house still awaits your answer.", AsylumOpening, null);
            starter.AddPlayerLine("bc_asylum_accept", "bc_asylum_choices", "bc_asylum_oath_ready",
                "{=BC_Asylum_AudienceAccept}I will receive your house into my service. Swear homage to me.", CanDiscussAsylum,
                () => _asylumOfferedVillage = null, 120);
            starter.AddPlayerLine("bc_asylum_accept_estate", "bc_asylum_choices", "bc_asylum_estates_prompt",
                "{=BC_Asylum_AudienceAcceptEstate}I will receive your homage and grant you land to maintain your household.", CanDiscussAsylum,
                null, 120, CanOfferAsylumEstate, null);
            starter.AddPlayerLine("bc_asylum_refuse", "bc_asylum_choices", "bc_asylum_refused",
                "{=BC_Asylum_AudienceRefuse}I cannot receive your house into my service. You must seek protection elsewhere.",
                CanDiscussAsylum, RefuseAsylumAudience, 100);
            starter.AddDialogLine("bc_asylum_refused", "bc_asylum_refused", "close_window",
                "{=BC_Asylum_AudienceRefused}Then I shall trouble you no further. I must seek another liege.", null, null);
            starter.AddPlayerLine("bc_asylum_postpone", "bc_asylum_choices", "bc_asylum_postponed",
                "{=BC_Asylum_AudiencePostpone}I must consider your petition.", null, null, 90);
            starter.AddDialogLine("bc_asylum_postponed", "bc_asylum_postponed", "close_window",
                "{=BC_Asylum_AudiencePostponed}I shall wait here for your answer for another {BC_ASYLUM_DAYS} days. After that, I must seek protection elsewhere.",
                AsylumOpening, null);
            starter.AddDialogLine("bc_asylum_estates_prompt", "bc_asylum_estates_prompt", "bc_asylum_estates",
                "{=BC_Asylum_EstatesPrompt}You honor my house. Which estate would you entrust to me?", CanDiscussAsylum, ListAsylumEstates);
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
            starter.AddPlayerLine("bc_asylum_estate_cancel", "bc_asylum_estates", "bc_asylum_resume",
                "{=BC_Asylum_ReconsiderEstate}Let me reconsider the grant.", null, () => _asylumOfferedVillage = null);
            starter.AddDialogLine("bc_asylum_estate_selected", "bc_asylum_estate_selected", "bc_asylum_estate_confirm",
                "{=BC_Asylum_EstateSelected}I would be honored to hold it in your service.", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_estate_confirm", "bc_asylum_estate_confirm", "bc_asylum_oath_ready",
                "{=BC_Asylum_EstatePromise}To help you maintain your household and your men, I shall grant you {BC_ASYLUM_ESTATE}. Protect its people and collect your dues.",
                CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_estate_back", "bc_asylum_estate_confirm", "bc_asylum_estates_prompt",
                "{=BC_Asylum_ReconsiderEstate}Let me reconsider the grant.", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_estate_stale", "bc_asylum_oath_ready", "bc_asylum_choices",
                "{=BC_Asylum_EstateUnavailable}It seems that estate can no longer be granted. May we reconsider the terms?",
                () => CanDiscussAsylum() && !AsylumEstateStillAvailable(), () => _asylumOfferedVillage = null, 200);
            starter.AddDialogLine("bc_asylum_oath_ready", "bc_asylum_oath_ready", "bc_asylum_oath_prompt",
                "{=BC_Asylum_OathReady}I am ready to swear my oath.", AsylumOpening, null, 100);
            starter.AddPlayerLine("bc_asylum_oath_prompt", "bc_asylum_oath_prompt", "bc_asylum_oath_first",
                "{=BC_Asylum_OathPrompt}Then swear homage to me, and pledge your house to the service of {BC_ASYLUM_REALM}.", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_first", "bc_asylum_oath_first", "bc_asylum_oath_bond",
                "{=BC_Asylum_OathFirst}I swear homage to you as lawful {BC_ASYLUM_LIEGE_TITLE}.", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_oath_bond", "bc_asylum_oath_bond", "bc_asylum_oath_second",
                "{=BC_Asylum_OathBond}Will you stand beside me against my enemies and uphold the laws of this realm?", CanDiscussAsylum, null);
            starter.AddDialogLine("bc_asylum_oath_second", "bc_asylum_oath_second", "bc_asylum_oath_accept",
                "{=BC_Asylum_OathSecond}My sword and the strength of my house are yours. I shall stand by your banner and uphold your lawful judgements.", CanDiscussAsylum, null);
            starter.AddPlayerLine("bc_asylum_oath_accept", "bc_asylum_oath_accept", "bc_asylum_oath_result",
                "{=BC_Asylum_OathAccepted}I accept your homage. Your house shall have my protection, as mine shall have your service.", CanDiscussAsylum, CompleteAsylumOath);
            foreach (string token in new[] { "bc_asylum_oath_prompt", "bc_asylum_oath_bond", "bc_asylum_oath_accept" })
                starter.AddPlayerLine(token + "_postpone", token, "bc_asylum_postponed",
                    "{=BC_Asylum_AudiencePostpone}I must consider your petition.", CanDiscussAsylum, null, 90);
            starter.AddDialogLine("bc_asylum_oath_success", "bc_asylum_oath_result", "close_window",
                "{=BC_Asylum_OathThanks}I thank you, my {?PLAYER.GENDER}lady{?}lord{\\?}. May my service repay the trust you have placed in me.",
                () => _asylumAdmissionSucceeded, null, 120);
            starter.AddDialogLine("bc_asylum_oath_changed", "bc_asylum_oath_result", "close_window",
                "{=BC_Asylum_OathChanged}It seems the circumstances have changed. We must settle the remaining formalities another time.", null, null, 100);
            foreach (string token in new[] { "bc_asylum_resume", "bc_asylum_estates_prompt", "bc_asylum_estate_selected",
                "bc_asylum_oath_ready", "bc_asylum_oath_first", "bc_asylum_oath_second", "bc_asylum_postponed" })
                starter.AddDialogLine(token + "_stale", token, "close_window",
                    "{=BC_Asylum_VisitStale}It seems this audience can no longer proceed. We must speak another time.",
                    () => !CanDiscussAsylum(), null, 300);
            foreach (string token in new[] { "bc_asylum_choices", "bc_asylum_estates", "bc_asylum_estate_confirm",
                "bc_asylum_oath_prompt", "bc_asylum_oath_bond", "bc_asylum_oath_accept" })
                starter.AddPlayerLine(token + "_stale", token, "bc_asylum_stale_exit",
                    "{=BC_Asylum_EndChangedAudience}Circumstances have changed. We must leave this for another time.",
                    () => !CanDiscussAsylum(), null, 300);
            starter.AddDialogLine("bc_asylum_stale_exit", "bc_asylum_stale_exit", "close_window",
                "{=BC_Asylum_VisitStale}It seems this audience can no longer proceed. We must speak another time.", null, null);
        }

        private TextObject AsylumExplanation(AsylumVisitRecord visit)
        {
            var origin = ResolveKingdomById(GetDictionaryValue(_trackedOriginKingdomIds, visit.Clan.StringId));
            _trackedExileCauseIds.TryGetValue(visit.Clan.StringId, out int cause);
            TextObject text;
            switch ((ExileCause)cause)
            {
                case ExileCause.RebelIndependence: text = new TextObject("{=BC_Asylum_ExplainIndependence}Our rising for independence from {REALM} failed, and my house was banished."); break;
                case ExileCause.RebelAbdication: text = new TextObject("{=BC_Asylum_ExplainAbdication}We sought to depose the ruler of {REALM}. Our cause failed, and my house was banished."); break;
                case ExileCause.RebelInstallRuler: text = new TextObject("{=BC_Asylum_ExplainClaimant}We pressed a claim to the crown of {REALM}. Our cause failed, and my house was banished."); break;
                case ExileCause.Treason: text = new TextObject("{=BC_Asylum_ExplainTreason}I was condemned for treason in {REALM}. I maintain that the accusation was unjust."); break;
                case ExileCause.LoyalistIndependence: text = new TextObject("{=BC_Asylum_ExplainSecessionLoyalist}We resisted the secession from {REALM}. When our cause fell, we were driven into exile."); break;
                case ExileCause.LoyalistAbdication:
                case ExileCause.LoyalistInstallRuler: text = new TextObject("{=BC_Asylum_ExplainLoyalist}We stood by our former ruler in {REALM}. When our cause fell, we were driven into exile."); break;
                case ExileCause.KingdomDestroyed: text = new TextObject("{=BC_Asylum_ExplainDestroyed}With the fall of {REALM}, my house has been left without a liege."); break;
                case ExileCause.Expulsion: text = new TextObject("{=BC_Asylum_ExplainExpelled}My house was cast out of {REALM}, and we have nowhere left to call our own."); break;
                case ExileCause.VoluntaryDeparture: text = new TextObject("{=BC_Asylum_ExplainDeparture}My house has left the service of {REALM} and seeks a new allegiance."); break;
                default: text = new TextObject("{=BC_Asylum_ExplainUnknown}My house has fallen on hard times and has no lands to call its own."); break;
            }
            return text.SetTextVariable("REALM", origin?.Name ?? new TextObject("{=BC_Asylum_FormerRealm}our former realm"));
        }
    }
}
