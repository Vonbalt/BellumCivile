using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ClientKingdomBehavior
    {
        internal const int VoluntaryReleaseGratitude = 40;
        internal const float VoluntaryReleaseMemoryYears = 15;

        internal sealed class ReleaseOffer
        {
            internal Hero Player, Recipient;
            internal Kingdom Suzerain, Client;
            internal ClientKingdomRecord Record;
        }

        private ReleaseOffer _releaseOffer;
        private bool _releaseSucceeded;

        internal ReleaseOffer CreateReleaseOffer(Hero player, Hero recipient)
        {
            var offer = new ReleaseOffer { Player = player, Recipient = recipient,
                Suzerain = player?.Clan?.Kingdom, Client = recipient?.Clan?.Kingdom };
            offer.Record = GetClientRecord(offer.Client);
            return CanVoluntarilyRelease(offer, out _) ? offer : null;
        }

        internal bool CanVoluntarilyRelease(ReleaseOffer offer, out TextObject reason)
        {
            reason = new TextObject("{=BC_ClientRelease_Changed}The circumstances have changed. We must discuss your realm's independence another time.");
            if (_startingClientagePending || offer?.Player == null || offer.Recipient == null || offer.Record == null
                || offer.Player != Hero.MainHero || offer.Player.Clan != Clan.PlayerClan
                || !IsValidPermanentRealm(offer.Suzerain) || !IsValidPermanentRealm(offer.Client) || offer.Suzerain == offer.Client
                || offer.Player.Clan?.Kingdom != offer.Suzerain || offer.Recipient.Clan?.Kingdom != offer.Client
                || offer.Suzerain.RulingClan != offer.Player.Clan || offer.Player.Clan.Leader != offer.Player
                || offer.Client.RulingClan != offer.Recipient.Clan || offer.Recipient.Clan.Leader != offer.Recipient
                || offer.Player.Clan.IsEliminated || offer.Recipient.Clan.IsEliminated || IsClientKingdom(offer.Suzerain)
                || !offer.Player.IsAlive || !offer.Recipient.IsAlive || !offer.Player.IsActive || !offer.Recipient.IsActive
                || offer.Player.IsPrisoner || offer.Recipient.IsPrisoner || PlayerEncounter.Current?.IsJoinedBattle == true
                || offer.Player.PartyBelongedTo?.MapEvent != null || offer.Recipient.PartyBelongedTo?.MapEvent != null
                || GetClientRecord(offer.Client) != offer.Record || offer.Record.SuzerainKingdomId != offer.Suzerain.StringId
                || offer.Client.IsAtWarWith(offer.Suzerain)
                || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(offer.Suzerain) == true
                || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(offer.Client) == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(offer.Suzerain) != null
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(offer.Client) != null) return false;
            reason = null;
            return true;
        }

        internal bool TryVoluntarilyRelease(ReleaseOffer offer, out TextObject reason)
        {
            if (!CanVoluntarilyRelease(offer, out reason)) return false;
            reason = new TextObject("{=BC_ClientRelease_AgreementsUnavailable}Our existing agreements must be settled before I can release your realm.");
            if (!ClientReleaseAgreements.TryPrepare(offer.Suzerain, offer.Client, out var agreements)) return false;
            bool committed = false;
            try
            {
                RunDiplomaticSync(() =>
                {
                    if (!CanVoluntarilyRelease(offer, out _) || !agreements.TryApply()) return;
                    if (!_clients.Remove(offer.Record)) { agreements.Restore(); return; }
                    AdvanceRuntimeRevision();
                    committed = true;
                });
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Friendly client release could not be completed: " + ex);
                return false;
            }
            if (!committed) return false;
            reason = null;
            BellumCivileLogger.Log($"Client voluntarily released; client={offer.Client.StringId}; suzerain={offer.Suzerain.StringId}; ruler={offer.Player.StringId}; recipient={offer.Recipient.StringId}.");
            try
            {
                if (DynamicRelationBehavior.Instance?.HasVoluntaryReleaseGratitude(offer.Player, offer.Recipient) != true)
                    RelationMemoryService.ApplyChange(offer.Player, offer.Recipient, VoluntaryReleaseGratitude, true,
                        RelationMemorySources.GrantedRealmIndependence, VoluntaryReleaseMemoryYears, RelationMemoryScope.Personal);
            }
            catch (Exception ex) { BellumCivileLogger.Log("Friendly client release gratitude failed after independence was granted: " + ex); }
            return true;
        }

        private bool CanDiscussVoluntaryRelease()
        {
            var context = Campaign.Current?.CurrentConversationContext;
            return (context == ConversationContext.Default || context == ConversationContext.PartyEncounter)
                && PlayerEncounter.Current?.IsJoinedBattle != true
                && CreateReleaseOffer(Hero.MainHero, Hero.OneToOneConversationHero) != null;
        }

        private void ClearReleaseOffer() { _releaseOffer = null; _releaseSucceeded = false; }

        private bool ReleaseConfirmationHint(out TextObject reason)
        {
            if (_releaseOffer == null || Hero.OneToOneConversationHero != _releaseOffer.Recipient || !CanDiscussVoluntaryRelease())
            {
                reason = new TextObject("{=BC_ClientRelease_Changed}The circumstances have changed. We must discuss your realm's independence another time.");
                return false;
            }
            if (!CanVoluntarilyRelease(_releaseOffer, out reason)) return false;
            if (!ClientReleaseAgreements.TryPrepare(_releaseOffer.Suzerain, _releaseOffer.Client, out _))
            {
                reason = new TextObject("{=BC_ClientRelease_AgreementsUnavailable}Our existing agreements must be settled before I can release your realm.");
                return false;
            }
            reason = new TextObject("{=BC_ClientRelease_Hint}Grants independence without payment. The alliance and trade agreement continue on ordinary terms. Existing wars and alliance obligations remain unchanged. Grants +{RELATION} personal relations for {YEARS} years; an active gratitude memory cannot stack or be renewed.")
                .SetTextVariable("RELATION", VoluntaryReleaseGratitude)
                .SetTextVariable("YEARS", (VoluntaryReleaseMemoryYears * BellumCivileOptions.RelationMemoryDurationMultiplier).ToString("0.##"));
            return true;
        }

        private void AddVoluntaryReleaseDialogues(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_client_release_open", "lord_talk_speak_diplomacy_2", "bc_client_release_opening",
                "{=BC_ClientRelease_Open}I would speak of your realm's obligations to my crown.", CanDiscussVoluntaryRelease,
                () => { ClearReleaseOffer(); _releaseOffer = CreateReleaseOffer(Hero.MainHero, Hero.OneToOneConversationHero);
                    MBTextManager.SetTextVariable("BC_RELEASE_CLIENT", _releaseOffer?.Client.Name ?? TextObject.GetEmpty()); }, 120);
            starter.AddDialogLine("bc_client_release_stale", "bc_client_release_opening", "lord_talk_speak_diplomacy_2",
                "{=BC_ClientRelease_Changed}The circumstances have changed. We must discuss your realm's independence another time.",
                () => !CanVoluntarilyRelease(_releaseOffer, out _), ClearReleaseOffer, 200);
            starter.AddDialogLine("bc_client_release_question", "bc_client_release_opening", "bc_client_release_proposal",
                "{=BC_ClientRelease_Question}What would you ask of us?", null, null);
            starter.AddPlayerLine("bc_client_release_propose", "bc_client_release_proposal", "bc_client_release_response",
                "{=BC_ClientRelease_Proposal}I would release {BC_RELEASE_CLIENT} from its oath of obedience. Our realms would remain allies, but yours would answer to no overlord.", null, null, 120);
            starter.AddDialogLine("bc_client_release_response", "bc_client_release_response", "bc_client_release_confirmation",
                "{=BC_ClientRelease_Response}You would grant us our independence, and ask nothing in return?", null, null);
            starter.AddPlayerLine("bc_client_release_confirm", "bc_client_release_confirmation", "bc_client_release_result",
                "{=BC_ClientRelease_Confirm}That is my will. Let there be friendship between our realms.", null,
                () => { if (_releaseSucceeded) return;
                    _releaseSucceeded = ReleaseConfirmationHint(out var reason) && TryVoluntarilyRelease(_releaseOffer, out reason);
                    MBTextManager.SetTextVariable("BC_RELEASE_FAILURE", reason ?? TextObject.GetEmpty()); }, 120, ReleaseConfirmationHint);
            foreach (var state in new[] { "bc_client_release_proposal", "bc_client_release_confirmation" })
                starter.AddPlayerLine(state + "_cancel", state, "lord_talk_speak_diplomacy_2",
                    "{=BC_ClientRelease_Cancel}I must give this more thought.", null, ClearReleaseOffer, 100);
            starter.AddDialogLine("bc_client_release_thanks", "bc_client_release_result", "lord_talk_speak_diplomacy_2",
                "{=BC_ClientRelease_Thanks}You do us a great honor. I shall not forget this generosity. May our realms remain friends for many years to come.",
                () => _releaseSucceeded, ClearReleaseOffer, 120);
            starter.AddDialogLine("bc_client_release_failed", "bc_client_release_result", "lord_talk_speak_diplomacy_2",
                "{=BC_ClientRelease_Failure}{BC_RELEASE_FAILURE}", () => !_releaseSucceeded, ClearReleaseOffer, 100);
        }
    }
}
