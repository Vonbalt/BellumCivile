using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Presents call-to-arms replies one at a time when the player leads the conflict.
    /// The queue is intentionally transient: the underlying faction and feud records remain
    /// authoritative, while these inquiries are only a more visible account of their outcome.
    /// </summary>
    public sealed class ConflictCallResponseBehavior : CampaignBehaviorBase
    {
        private readonly Queue<PendingResponse> _pendingResponses = new Queue<PendingResponse>();
        private readonly HashSet<string> _queuedResponseKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _isShowingResponse;

        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        internal void QueueCivilWarResponse(
            FactionObject rebelFaction,
            Clan responder,
            Clan sponsor,
            CivilWarSolidarityReason reason,
            bool accepted)
        {
            if (rebelFaction?.Leader != Clan.PlayerClan || responder?.Leader == null || responder == Clan.PlayerClan)
                return;

            TextObject cause = BuildCivilWarCause(rebelFaction);
            string key = $"civil:{rebelFaction.ParentKingdom?.StringId}:{rebelFaction.Leader?.StringId}:{rebelFaction.CreationDate.ToDays:0.000}:{responder.StringId}:{accepted}";
            QueueResponse(key, responder, sponsor, reason, accepted, cause, BuildRefusalReason(responder, sponsor, rebelFaction.ParentKingdom));
        }

        internal void QueueTreasonResponse(
            FactionObject rebelFaction,
            Clan responder,
            Clan accusedClan,
            CivilWarSolidarityReason reason)
        {
            if (rebelFaction?.Leader != Clan.PlayerClan || accusedClan != Clan.PlayerClan || responder?.Leader == null)
                return;

            TextObject cause = new TextObject("{=BC_CallResponse_Cause_Treason}your resistance to the crown's indictment for treason");
            string key = $"treason:{rebelFaction.ParentKingdom?.StringId}:{accusedClan.StringId}:{responder.StringId}:accept";
            QueueResponse(key, responder, accusedClan, reason, true, cause, null);
        }

        internal void QueueFeudResponse(
            ClaimFeudRecord record,
            Clan responder,
            Clan sideLeader,
            Clan opponent,
            CivilWarSolidarityReason reason,
            bool accepted,
            FeudalTitleRecord title,
            bool defending)
        {
            if (record == null || sideLeader != Clan.PlayerClan || responder?.Leader == null || responder == Clan.PlayerClan)
                return;

            TextObject titleName = new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title));
            TextObject cause = defending
                ? new TextObject("{=BC_CallResponse_Cause_DefendTitle}your defense of the {TITLE_NAME}")
                : new TextObject("{=BC_CallResponse_Cause_ClaimTitle}your claim to the {TITLE_NAME}");
            cause.SetTextVariable("TITLE_NAME", titleName);

            string key = $"feud:{record.RecordId}:{sideLeader.StringId}:{responder.StringId}:{accepted}";
            QueueResponse(key, responder, sideLeader, reason, accepted, cause, BuildFeudRefusalReason(responder, opponent));
        }

        internal void QueueLateDefection(FactionObject rebelFaction, Clan responder)
        {
            if (rebelFaction?.Leader != Clan.PlayerClan || responder?.Leader == null || responder == Clan.PlayerClan)
                return;

            TextObject title = new TextObject("{=BC_CallResponse_Title}A Call to Arms");
            TextObject body = new TextObject(
                "{=BC_CallResponse_Defection}A messenger arrives bearing the seal of {LORD_NAME}.\n\n\"The crown has lost my loyalty. The {CLAN_NAME} casts down its old allegiance and will join your cause before all the realm.\"");
            body.SetTextVariable("LORD_NAME", responder.Leader.Name);
            body.SetTextVariable("CLAN_NAME", responder.Name);
            Enqueue($"defection:{rebelFaction.ParentKingdom?.StringId}:{rebelFaction.Leader?.StringId}:{responder.StringId}", title, body);
        }

        private void QueueResponse(
            string key,
            Clan responder,
            Clan sponsor,
            CivilWarSolidarityReason reason,
            bool accepted,
            TextObject cause,
            TextObject refusalReason)
        {
            TextObject title = new TextObject("{=BC_CallResponse_Title}A Call to Arms");
            TextObject introduction = BuildIntroduction(responder, sponsor, reason);
            TextObject resolvedCause = cause ?? new TextObject("{=BC_CallResponse_Cause_Generic}your present struggle");
            TextObject resolvedRefusal = refusalReason ?? new TextObject("{=BC_CallResponse_Refusal_Risk}I will not risk the lives of my people over a vain dispute.");
            TextObject letter = BuildLetter(responder, sponsor, reason, accepted, resolvedCause, resolvedRefusal);
            TextObject body = accepted
                ? new TextObject("{=BC_CallResponse_Body_Accepted}{INTRODUCTION}\n\n\"{LETTER}\"")
                : new TextObject("{=BC_CallResponse_Body_Refused}{INTRODUCTION}\n\n\"{LETTER}\"");
            body.SetTextVariable("INTRODUCTION", introduction);
            body.SetTextVariable("LETTER", letter);
            Enqueue(key, title, body);
        }

        private void Enqueue(string key, TextObject title, TextObject body)
        {
            if (string.IsNullOrWhiteSpace(key) || !_queuedResponseKeys.Add(key))
                return;

            _pendingResponses.Enqueue(new PendingResponse(title?.ToString() ?? string.Empty, body?.ToString() ?? string.Empty));
        }

        private void OnTick(float dt)
        {
            if (_isShowingResponse || _pendingResponses.Count == 0 || InformationManager.IsAnyInquiryActive())
                return;

            if (!(Game.Current?.GameStateManager?.ActiveState is MapState))
                return;

            PendingResponse response = _pendingResponses.Dequeue();
            _isShowingResponse = true;
            InformationManager.ShowInquiry(new InquiryData(
                response.Title,
                response.Body,
                true,
                false,
                new TextObject("{=BC_CallResponse_Done}Done").ToString(),
                null,
                () => _isShowingResponse = false,
                null), true, false);
        }

        private static TextObject BuildIntroduction(Clan responder, Clan sponsor, CivilWarSolidarityReason reason)
        {
            bool sponsorIsPlayer = sponsor == Clan.PlayerClan;
            TextObject text;
            if (!sponsorIsPlayer)
            {
                text = new TextObject("{=BC_CallResponse_Intro_Associate}You receive a message from {LORD_NAME}, who was called by {SPONSOR_NAME} to support your cause.");
                text.SetTextVariable("SPONSOR_NAME", sponsor?.Leader?.Name ?? sponsor?.Name ?? new TextObject("?"));
            }
            else
            {
                switch (reason)
                {
                    case CivilWarSolidarityReason.DirectVassal:
                        text = new TextObject("{=BC_CallResponse_Intro_Vassal}You receive a message from {LORD_NAME}, your vassal.");
                        break;
                    case CivilWarSolidarityReason.MarriageAlliance:
                        text = responder.Leader.IsFemale
                            ? new TextObject("{=BC_CallResponse_Intro_Marriage_Female}You receive a message from {LORD_NAME}, your kinswoman by marriage.")
                            : new TextObject("{=BC_CallResponse_Intro_Marriage_Male}You receive a message from {LORD_NAME}, your kinsman by marriage.");
                        break;
                    case CivilWarSolidarityReason.DynasticKin:
                        text = responder.Leader.IsFemale
                            ? new TextObject("{=BC_CallResponse_Intro_Kin_Female}You receive a message from {LORD_NAME}, your kinswoman.")
                            : new TextObject("{=BC_CallResponse_Intro_Kin_Male}You receive a message from {LORD_NAME}, your kinsman.");
                        break;
                    case CivilWarSolidarityReason.Friendship:
                        text = new TextObject("{=BC_CallResponse_Intro_Friend}You receive a message from {LORD_NAME}, your close friend.");
                        break;
                    default:
                        text = new TextObject("{=BC_CallResponse_Intro_Sympathizer}You receive a message from {LORD_NAME}, a lord sympathetic to your cause.");
                        break;
                }
            }

            text.SetTextVariable("LORD_NAME", responder.Leader.Name);
            text.SetTextVariable("CLAN_NAME", responder.Name);
            return text;
        }

        private static TextObject BuildLetter(
            Clan responder,
            Clan sponsor,
            CivilWarSolidarityReason reason,
            bool accepted,
            TextObject cause,
            TextObject refusalReason)
        {
            bool sponsorIsPlayer = sponsor == Clan.PlayerClan;
            TextObject text;
            if (!sponsorIsPlayer)
            {
                text = accepted
                    ? new TextObject("{=BC_CallResponse_Letter_AssociateAccept}I have answered {SPONSOR_NAME}'s summons. The {CLAN_NAME} stands ready to march beneath your banners in {CAUSE}.")
                    : new TextObject("{=BC_CallResponse_Letter_AssociateRefuse}I have received {SPONSOR_NAME}'s summons, but the {CLAN_NAME} will not join you in {CAUSE}. {REASON}");
                text.SetTextVariable("SPONSOR_NAME", sponsor?.Leader?.Name ?? sponsor?.Name ?? new TextObject("?"));
            }
            else if (accepted)
            {
                switch (reason)
                {
                    case CivilWarSolidarityReason.DirectVassal:
                        text = new TextObject("{=BC_CallResponse_Letter_VassalAccept}My liege, I have received your summons and the {CLAN_NAME} stands ready to honor our fealty. We shall march beneath your banners in {CAUSE}.");
                        break;
                    case CivilWarSolidarityReason.MarriageAlliance:
                        text = new TextObject("{=BC_CallResponse_Letter_MarriageAccept}My {PLAYER_NAME}, an alliance exists between our houses, and today we honor that oath. The {CLAN_NAME} stands ready to march beneath your banners in {CAUSE}.");
                        break;
                    case CivilWarSolidarityReason.DynasticKin:
                        text = new TextObject("{=BC_CallResponse_Letter_KinAccept}My {PLAYER_NAME}, blood binds our houses. The {CLAN_NAME} will stand beside you in {CAUSE}.");
                        break;
                    case CivilWarSolidarityReason.Friendship:
                        text = new TextObject("{=BC_CallResponse_Letter_FriendAccept}My {PLAYER_NAME}, our bonds run truer than any oath. Let this be the hour when we draw swords together. The {CLAN_NAME} shall march to your aid in {CAUSE}.");
                        break;
                    default:
                        text = new TextObject("{=BC_CallResponse_Letter_GenericAccept}The {CLAN_NAME} has heard your call. We will cast our lot with you in {CAUSE} and march beneath your banners.");
                        break;
                }
            }
            else
            {
                switch (reason)
                {
                    case CivilWarSolidarityReason.DirectVassal:
                        text = new TextObject("{=BC_CallResponse_Letter_VassalRefuse}My liege, I have received your summons to honor our fealty. I fear we must decline, and the {CLAN_NAME} will not join you in {CAUSE}. {REASON}");
                        break;
                    case CivilWarSolidarityReason.MarriageAlliance:
                        text = new TextObject("{=BC_CallResponse_Letter_MarriageRefuse}My {PLAYER_NAME}, although an alliance exists between our houses, I fear we cannot join you in {CAUSE}. The {CLAN_NAME} refuses your call to arms. {REASON}");
                        break;
                    case CivilWarSolidarityReason.DynasticKin:
                        text = new TextObject("{=BC_CallResponse_Letter_KinRefuse}My {PLAYER_NAME}, blood alone cannot compel us to join you in {CAUSE}. The {CLAN_NAME} refuses your call to arms. {REASON}");
                        break;
                    case CivilWarSolidarityReason.Friendship:
                        text = new TextObject("{=BC_CallResponse_Letter_FriendRefuse}My {PLAYER_NAME}, although we have close bonds, I fear we cannot join you in {CAUSE}. The {CLAN_NAME} refuses your call to arms. {REASON}");
                        break;
                    default:
                        text = new TextObject("{=BC_CallResponse_Letter_GenericRefuse}The {CLAN_NAME} has heard your call, but we will take no part in {CAUSE}. {REASON}");
                        break;
                }
            }

            text.SetTextVariable("CLAN_NAME", responder.Name);
            text.SetTextVariable("PLAYER_NAME", Clan.PlayerClan?.Leader?.Name ?? new TextObject("?"));
            text.SetTextVariable("CAUSE", cause);
            text.SetTextVariable("REASON", refusalReason);
            return text;
        }

        private static TextObject BuildCivilWarCause(FactionObject faction)
        {
            switch (faction?.Type)
            {
                case FactionType.Independence:
                    return new TextObject("{=BC_CallResponse_Cause_Independence}your bid for independence");
                case FactionType.InstallRuler:
                    TextObject claim = new TextObject("{=BC_CallResponse_Cause_Throne}{LEADER_NAME}'s rightful claim to the throne");
                    claim.SetTextVariable("LEADER_NAME", faction.Leader?.Leader?.Name ?? faction.Leader?.Name ?? new TextObject("?"));
                    return claim;
                case FactionType.Abdication:
                    TextObject abdication = new TextObject("{=BC_CallResponse_Cause_Abdication}your demand for {RULER_NAME}'s abdication");
                    abdication.SetTextVariable("RULER_NAME", faction.ParentKingdom?.RulingClan?.Leader?.Name ?? new TextObject("?"));
                    return abdication;
                default:
                    // Legacy redistribution factions can still exist in old saves, but their
                    // retired demand is deliberately not revived in the new response text.
                    return new TextObject("{=BC_CallResponse_Cause_Rebellion}your rebellion against the crown");
            }
        }

        private static TextObject BuildRefusalReason(Clan responder, Clan sponsor, Kingdom kingdom)
        {
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (responder?.Leader != null && ruler != null && responder.Leader.GetRelation(ruler) >= 60)
                return new TextObject("{=BC_CallResponse_Refusal_Crown}I shall remain loyal to the crown, as is my duty.");
            if (sponsor?.Leader != null && responder?.Leader?.GetRelation(sponsor.Leader) < 0)
                return new TextObject("{=BC_CallResponse_Refusal_Grievance}Our grievances do not justify tearing the realm apart.");
            return new TextObject("{=BC_CallResponse_Refusal_Risk}I will not risk the lives of my people over a vain dispute.");
        }

        private static TextObject BuildFeudRefusalReason(Clan responder, Clan opponent)
        {
            if (responder?.Leader != null && opponent?.Leader != null && responder.Leader.GetRelation(opponent.Leader) >= 60)
                return new TextObject("{=BC_CallResponse_Refusal_OpposingTies}My obligations to the opposing house forbid me from taking your side.");
            return new TextObject("{=BC_CallResponse_Refusal_Risk}I will not risk the lives of my people over a vain dispute.");
        }

        private sealed class PendingResponse
        {
            internal PendingResponse(string title, string body)
            {
                Title = title;
                Body = body;
            }

            internal string Title { get; }
            internal string Body { get; }
        }
    }
}
