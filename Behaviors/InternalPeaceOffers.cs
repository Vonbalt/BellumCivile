using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BellumCivile.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class InternalPeaceSettlementBehavior
    {
        private Dictionary<string, string> _offeredWinner = new Dictionary<string, string>();
        private Dictionary<string, KingdomDecision> _offerDecisions = new Dictionary<string, KingdomDecision>();
        private readonly ConditionalWeakTable<KingdomDecision, TextObject> _voteResults = new ConditionalWeakTable<KingdomDecision, TextObject>();

        private void SyncOffers(IDataStore store)
        {
            store.SyncData("BC_InternalPeaceOfferedWinner", ref _offeredWinner);
            store.SyncData("BC_InternalPeaceOfferDecisions", ref _offerDecisions);
            _offeredWinner = _offeredWinner ?? new Dictionary<string, string>();
            _offerDecisions = _offerDecisions ?? new Dictionary<string, KingdomDecision>();
        }

        internal void ShowTerms(Kingdom first, Kingdom second)
        {
            if (!TryIdentify(first, second, out string key, out bool rival)) return;
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
            {
                if (!rival) Propose(first, second, key, "");
                else Notify(new TextObject("{=BC_InternalPeace_Rival}Rival claimants cannot end their challenges through a separate white peace. Reconciliation must be sought with the Crown."));
                return;
            }
            var options = new List<InquiryElement>();
            void AddTerm(string winner, TextObject label)
            {
                bool willing = CanAcceptTerms(first, second, winner, out TextObject reason);
                TextObject forecast = willing
                    ? new TextObject("{=BC_InternalPeace_ExpectedAcceptance}The opposing houses are expected to accept, should your council agree.")
                    : reason;
                options.Add(new InquiryElement(winner, label.ToString(), null, willing,
                    DescribeTerms(first, second, winner) + "\n\n" + forecast));
            }
            if (!rival) AddTerm("", new TextObject("{=BC_InternalPeace_White}Seek White Peace"));
            AddTerm(second.StringId, new TextObject("{=BC_InternalPeace_Concede}Concede Defeat"));
            AddTerm(first.StringId, new TextObject("{=BC_InternalPeace_Demand}Demand Their Submission"));
            string introduction = new TextObject("{=BC_InternalPeace_Introduction}What terms shall we put before the assembled houses? Your own side must endorse the settlement, and the opposing side must be willing to accept it.").ToString();
            var leverage = DescribeLeverage(first, second, GetCourierWar(first, second));
            if (leverage != null) introduction += "\n\n" + leverage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_InternalPeace_Title}Terms of Reconciliation").ToString(),
                introduction,
                options, true, 1, 1, new TextObject("{=BC_InternalPeace_Propose}Propose Terms").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected => Propose(first, second, key, (string)selected[0].Identifier), null));
        }

        private void Propose(Kingdom first, Kingdom second, string key, string winner)
        {
            Clan player = Clan.PlayerClan;
            if (player?.Kingdom != first || player.IsUnderMercenaryService || !first.IsAtWarWith(second)
                || !TryIdentify(first, second, out string current, out bool rival) || current != key
                || rival && string.IsNullOrEmpty(winner) || _first.ContainsKey(key)) return;
            if (first.UnresolvedDecisions.OfType<MakePeaceKingdomDecision>().Any(d => d.FactionToMakePeaceWith == second))
            { Notify(new TextObject("{=BC_InternalPeace_Pending}The houses are already considering terms for this conflict.")); return; }
            if (WarPeaceRevampBehavior.IsRevampEnabled() && !CanAcceptTerms(first, second, winner, out TextObject reason)) { Notify(reason); return; }
            var decision = new MakePeaceKingdomDecision(player, second, 0, 0);
            int cost = Math.Max(0, decision.GetProposalInfluenceCost());
            _offeredWinner[key] = winner;
            _offerDecisions[key] = decision;
            if (player.Influence < cost || !decision.IsAllowed())
            {
                ForgetOffer(key);
                Notify(new TextObject("{=BC_InternalPeace_CannotPropose}You cannot bring these terms before the council at present.")); return;
            }
            first.AddDecision(decision, false);
            if (first.UnresolvedDecisions.Contains(decision) || _voteResults.TryGetValue(decision, out _))
                ChangeClanInfluenceAction.Apply(player, -cost);
            else ForgetOffer(key);
        }

        internal bool CanAcceptTerms(Kingdom first, Kingdom second, string winner, out TextObject reason)
        {
            reason = new TextObject("{=BC_InternalPeace_NotReady}The opposing houses are not yet willing to accept these terms.");
            if (!TryIdentify(first, second, out _, out bool rival) || !first.IsAtWarWith(second)
                || first.IsEliminated || second.IsEliminated
                || CivilWarConflictBehavior.IsRealmTransferPending(first) || CivilWarConflictBehavior.IsRealmTransferPending(second)
                || rival && string.IsNullOrEmpty(winner)
                || !string.IsNullOrEmpty(winner) && winner != first.StringId && winner != second.StringId) return false;
            if (Clan.PlayerClan != null && second.RulingClan == Clan.PlayerClan) return true;
            if (winner == second.StringId) return true;
            var scores = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            var war = scores?.GetActiveWar(first, second);
            if (war?.IsActive != true) return false;
            return scores.CanNegotiateInternalOutcome(war, winner, out reason);
        }

        internal TextObject TermsFor(MakePeaceKingdomDecision decision)
        {
            Kingdom other = decision?.FactionToMakePeaceWith as Kingdom;
            string winner = HasOffer(decision) && TryIdentify(decision.Kingdom, other, out string key, out _)
                && _offeredWinner.TryGetValue(key, out string saved) ? saved : "";
            return DescribeTerms(decision?.Kingdom, other, winner);
        }

        private static TextObject DescribeTerms(Kingdom first, Kingdom second, string winner)
        {
            if (!TryIdentify(first, second, out string key, out bool rival)) return InternalPeaceVoteText.Terms();
            if (key.StartsWith("feud:", StringComparison.Ordinal))
            {
                var feud = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.GetActiveWars()
                    .FirstOrDefault(w => "feud:" + w.WarId == key);
                if (feud == null) return InternalPeaceVoteText.Terms();
                var title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(feud.TargetTitleId);
                if (title == null) return InternalPeaceVoteText.Terms();
                string text = string.IsNullOrEmpty(winner)
                    ? "{=BC_InternalPeace_FeudWhite}Lay down arms without settling the claim to {TITLE}. The houses shall return to the realm and restore their prewar holdings."
                    : winner == feud.ClaimantKingdomId
                        ? "{=BC_InternalPeace_FeudAward}Recognize the claimant's right to {TITLE}. The other wartime occupations shall be restored."
                        : "{=BC_InternalPeace_FeudRetain}Leave {TITLE} with its holder. The defeated claim may be weakened or struck down.";
                return new TextObject(text).SetTextVariable("TITLE", FeudalTitleDisplayHelper.FormatTitleName(title));
            }
            if (string.IsNullOrEmpty(winner)) return InternalPeaceVoteText.Terms();
            var victor = winner == first.StringId ? first : second;
            if (rival) return new TextObject("{=BC_InternalPeace_RivalTerms}The defeated houses shall submit to {REALM}. The contest with the Crown shall continue.")
                .SetTextVariable("REALM", victor.Name);
            var faction = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(victor);
            switch (faction?.Type)
            {
                case FactionType.InstallRuler:
                    return new TextObject("{=BC_InternalPeace_CrownTerms}Recognize {CLAIMANT} as sovereign. The departing houses shall rejoin the realm, and the defeated houses shall answer for the war.")
                        .SetTextVariable("CLAIMANT", faction.Leader?.Leader?.Name ?? faction.Leader?.Name);
                case FactionType.Abdication:
                    return new TextObject("{=BC_InternalPeace_AbdicationTerms}The sovereign shall relinquish the Crown. The succession shall be settled according to the laws of the realm.");
                case FactionType.Independence:
                    return new TextObject("{=BC_InternalPeace_IndependenceTerms}Recognize the departing houses as independent of the Crown. They may go forth under more than one banner.");
            }
            return new TextObject("{=BC_InternalPeace_VictoryTerms}Recognize the cause of {REALM} and end this conflict in their favor. This is a concession of defeat, not a white peace; the existing laws of settlement and judgment will apply.")
                .SetTextVariable("REALM", victor.Name);
        }

        internal bool HasOffer(MakePeaceKingdomDecision decision)
            => decision != null && TryIdentify(decision.Kingdom, decision.FactionToMakePeaceWith as Kingdom, out string key, out _)
                && _offerDecisions.TryGetValue(key, out KingdomDecision offered) && ReferenceEquals(offered, decision);

        internal bool KnowsOffer(KingdomDecision decision) => decision != null && _offerDecisions.Values.Contains(decision);

        private void ForgetOffer(string key) { _offeredWinner.Remove(key); _offerDecisions.Remove(key); }

        private void PruneFinishedVotes()
        {
            // Only called back on the map, outside AddDecision's synchronous notification dispatch.
            foreach (var pair in _offerDecisions.ToList())
                if (pair.Value?.Kingdom?.UnresolvedDecisions.Contains(pair.Value) != true) ForgetOffer(pair.Key);
        }

        internal void CompleteVote(MakePeaceKingdomDecision decision, bool approved)
        {
            if (_voteResults.TryGetValue(decision, out _)) return;
            var first = decision.Kingdom;
            var second = decision.FactionToMakePeaceWith as Kingdom;
            if (!TryIdentify(first, second, out string key, out bool rival))
            {
                foreach (var pair in _offerDecisions.Where(p => ReferenceEquals(p.Value, decision)).ToList()) ForgetOffer(pair.Key);
                _voteResults.Add(decision, new TextObject("{=BC_InternalPeace_NoAgreement}No settlement has been concluded. The conflict continues."));
                return;
            }
            bool offered = HasOffer(decision);
            string winner = offered && _offeredWinner.TryGetValue(key, out string value) ? value : "";
            TextObject terms = TermsFor(decision);
            if (offered) ForgetOffer(key);
            bool accepted = approved && (!rival || !string.IsNullOrEmpty(winner));
            if (accepted && (offered || WarPeaceRevampBehavior.IsRevampEnabled()))
                accepted = CanAcceptTerms(first, second, winner, out _);
            Kingdom winnerRealm = string.IsNullOrEmpty(winner) ? null : FindRealm(winner);
            bool awaitingPlayer = Clan.PlayerClan != null && second.RulingClan == Clan.PlayerClan;
            accepted = accepted && (string.IsNullOrEmpty(winner) || winnerRealm != null)
                && QueueSettlementWithReason(first, second, winnerRealm, awaitingPlayer, resolutionAlreadyBegun: false,
                    reason: InternalWarEnding.CouncilTerms);
            if (accepted && awaitingPlayer && _couriers.TryGetValue(key, out var courier))
                _awaitingBallot.Add(decision, courier);
            _voteResults.Add(decision, accepted
                ? (awaitingPlayer
                    ? new TextObject("{=BC_InternalPeace_AwaitingAnswer}The council has endorsed these terms. The opposing leader must now give an answer.\n\n{TERMS}")
                    : new TextObject("{=BC_InternalPeace_Agreed}The terms have been agreed. They will take effect once these deliberations conclude.\n\n{TERMS}"))
                    .SetTextVariable("TERMS", terms)
                : new TextObject("{=BC_InternalPeace_NoAgreement}No settlement has been concluded. The conflict continues."));
        }

        internal TextObject ResultFor(KingdomDecision decision)
            => _voteResults.TryGetValue(decision, out TextObject result) ? result : null;

        private static void Notify(TextObject text) => InformationManager.DisplayMessage(
            new InformationMessage(text.ToString(), BellumNotificationColors.Warning));
    }
}
