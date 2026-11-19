using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class InternalPeaceSettlementBehavior
    {
        private Dictionary<string, InternalPeaceCourierNotice> _couriers = new Dictionary<string, InternalPeaceCourierNotice>();
        private Dictionary<string, string> _councilReports = new Dictionary<string, string>();
        private readonly ConditionalWeakTable<KingdomDecision, InternalPeaceCourierNotice> _awaitingBallot
            = new ConditionalWeakTable<KingdomDecision, InternalPeaceCourierNotice>();

        private static float Today => (float)CampaignTime.Now.ToDays;

        private void SyncCouriers(IDataStore store)
        {
            store.SyncData("BC_InternalPeaceCouriers", ref _couriers);
            store.SyncData("BC_InternalPeaceCouncilReports", ref _councilReports);
            _couriers = _couriers ?? new Dictionary<string, InternalPeaceCourierNotice>();
            _councilReports = _councilReports ?? new Dictionary<string, string>();
        }

        internal bool IsCurrentCourier(InternalPeaceCourierNotice notice) => notice != null
            && !string.IsNullOrEmpty(notice.ConflictId) && _consentLeader.ContainsKey(notice.ConflictId)
            && _couriers.TryGetValue(notice.ConflictId, out var current) && ReferenceEquals(current, notice)
            && Today < notice.ExpiresDay;

        private bool IsExhaustionOffer(string key) => _settlementReason.TryGetValue(key, out string reason)
            && reason == InternalWarEnding.MutualExhaustion;

        private static WarScoreRecord GetCourierWar(Kingdom first, Kingdom second) => Campaign.Current?
            .GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(first, second);

        private static TextObject DescribeLeverage(Kingdom playerRealm, Kingdom opponent, WarScoreRecord score)
        {
            if (score == null || !WarPeaceRevampBehavior.IsRevampEnabled()) return null;
            return new TextObject("{=BC_InternalPeace_CourierLeverage}War Score for {YOUR_REALM}: {SCORE}\nWar Enthusiasm: {YOUR_REALM} {YOUR_WILL}, {OTHER_REALM} {OTHER_WILL}.")
                .SetTextVariable("YOUR_REALM", playerRealm.Name).SetTextVariable("OTHER_REALM", opponent.Name)
                .SetTextVariable("SCORE", score.GetSelfRelativeScore(playerRealm.StringId).ToString("+0.#;-0.#;0"))
                .SetTextVariable("YOUR_WILL", WarScoreBehavior.CalculatePowerWeightedWarWill(playerRealm, score).ToString("0.#"))
                .SetTextVariable("OTHER_WILL", WarScoreBehavior.CalculatePowerWeightedWarWill(opponent, score).ToString("0.#"));
        }

        private bool CanReviewCourier(string key, Kingdom first, Kingdom second, out WarScoreRecord score, bool recheckWill = true)
        {
            score = null;
            if (first == null || second == null || first.IsEliminated || second.IsEliminated
                || first.Leader?.IsAlive != true || second.Leader?.IsAlive != true
                || !_consentLeader.TryGetValue(key, out string expectedLeader) || second.Leader?.StringId != expectedLeader
                || Clan.PlayerClan == null || second.RulingClan != Clan.PlayerClan || Clan.PlayerClan.Kingdom != second
                || Clan.PlayerClan.IsUnderMercenaryService || !first.IsAtWarWith(second)
                || !TryIdentify(first, second, out string current, out bool rival) || current != key
                || CivilWarConflictBehavior.IsRealmTransferPending(first) || CivilWarConflictBehavior.IsRealmTransferPending(second)) return false;
            if (_couriers.TryGetValue(key, out var notice) && first.Leader?.StringId != notice.SenderLeaderId) return false;
            _winner.TryGetValue(key, out string winner);
            if (rival && string.IsNullOrEmpty(winner) || !string.IsNullOrEmpty(winner) && winner != first.StringId && winner != second.StringId)
                return false;
            score = GetCourierWar(first, second);
            if (score != null && (score.TerminalResolutionQueued || score.ResolutionPending || !score.IsActive
                || Math.Abs(score.Score) >= C.WarScoreForcePeaceThreshold
                || CivilWarConflictBehavior.IsScoreTransferPending(score))) return false;
            return !IsExhaustionOffer(key) || WarPeaceRevampBehavior.IsRevampEnabled() && !rival
                && (!recheckWill || Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.IsMutualWhitePeaceEligible(score, out _, out _) == true);
        }

        private void ProcessCourier(string key, Kingdom first, Kingdom second)
        {
            ReleaseReservation(key); // Older saves reserved War Score before the player had agreed to the offer.
            if (!CanReviewCourier(key, first, second, out var score, recheckWill: false)) { Remove(key); return; }
            if (!_couriers.TryGetValue(key, out var notice))
            {
                notice = new InternalPeaceCourierNotice(key, first, Today + C.TreatyPlayerParleyResponseDays);
                _couriers.Add(key, notice);
            }
            if (!IsCurrentCourier(notice)) { DeclineCourier(key, score); return; }
            var information = Campaign.Current?.CampaignInformationManager;
            if (notice.Delivered || information == null) return;
            information.NewMapNoticeAdded(notice);
            notice.Delivered = true;
        }

        internal void InspectCourier(InternalPeaceCourierNotice notice)
        {
            if (InformationManager.IsAnyInquiryActive() || !TryReviewCourier(notice, out var first, out var second, out var score)) return;
            string key = notice.ConflictId;
            string winner = _winner[key];
            var body = IsExhaustionOffer(key)
                ? new TextObject("{=BC_InternalPeace_CourierExhaustion}The envoys tell of weary hosts on either side. With neither able to press its advantage, {REALM} offers to lay down arms.")
                : new TextObject("{=BC_InternalPeace_CourierEndorsed}The envoy of {REALM} lays these terms before you, bearing the assent of its realm.");
            body.SetTextVariable("REALM", first.Name);
            var paragraphs = new List<string> { body.ToString(), DescribeTerms(first, second, winner).ToString() };
            var leverage = DescribeLeverage(second, first, score);
            if (leverage != null) paragraphs.Add(leverage.ToString());
            if (IsExhaustionOffer(key))
                paragraphs.Add(new TextObject("{=BC_InternalPeace_CourierNoVote}No council vote was held. The envoys seek your assent alone.").ToString());
            else if (_councilReports.TryGetValue(key, out string ballot)) paragraphs.Add(ballot);
            paragraphs.Add(new TextObject("{=BC_InternalPeace_CourierDeadline}The envoy will await your answer until {DATE}.")
                .SetTextVariable("DATE", CampaignTime.Days(notice.ExpiresDay).ToString()).ToString());
            InformationManager.ShowInquiry(new InquiryData(notice.TitleText.ToString(), string.Join("\n\n", paragraphs), true, true,
                new TextObject("{=BC_InternalPeace_Accept}Accept the Terms").ToString(),
                new TextObject("{=BC_InternalPeace_Refuse}Continue the Conflict").ToString(),
                () => RespondToCourier(notice, true), () => RespondToCourier(notice, false)), true);
        }

        private bool TryReviewCourier(InternalPeaceCourierNotice notice, out Kingdom first, out Kingdom second, out WarScoreRecord score)
        {
            first = second = null; score = null;
            if (!IsCurrentCourier(notice)) return false;
            string key = notice.ConflictId;
            if (_first.TryGetValue(key, out string firstId) && _second.TryGetValue(key, out string secondId))
            {
                first = FindRealm(firstId); second = FindRealm(secondId);
                if (CanReviewCourier(key, first, second, out score)) return true;
            }
            Remove(key);
            return false;
        }

        private void RespondToCourier(InternalPeaceCourierNotice notice, bool accept)
        {
            if (!TryReviewCourier(notice, out _, out _, out var score)) return;
            string key = notice.ConflictId;
            if (!accept) { DeclineCourier(key, score); return; }
            if (score != null)
            {
                if (!score.BeginResolution()) { Remove(key); return; }
                _reservedScore[key] = score.WarKey;
            }
            _consentLeader.Remove(key);
            _couriers.Remove(key);
            _councilReports.Remove(key);
        }

        private void DeclineCourier(string key, WarScoreRecord score)
        {
            if (IsExhaustionOffer(key)) score?.ScheduleNextWhitePeaceCheck(Today + C.WarScorePlayerWhitePeaceDeclineCooldownDays);
            Remove(key);
        }

        internal void RecordCouncilBallot(MakePeaceKingdomDecision decision, IEnumerable<DecisionOutcome> outcomes, DecisionOutcome chosen)
        {
            if (decision == null || outcomes == null || !_awaitingBallot.TryGetValue(decision, out var notice)) return;
            _awaitingBallot.Remove(decision);
            if (!IsCurrentCourier(notice) || (chosen as MakePeaceKingdomDecision.MakePeaceDecisionOutcome)?.ShouldPeaceBeDeclared != true) return;
            string Names(bool forPeace) => string.Join(", ", outcomes.OfType<MakePeaceKingdomDecision.MakePeaceDecisionOutcome>()
                .Where(outcome => outcome.ShouldPeaceBeDeclared == forPeace).SelectMany(outcome => outcome.SupporterList)
                .Where(s => s?.Clan != null && s.SupportWeight > Supporter.SupportWeights.StayNeutral)
                .Select(s => s.Clan.Name?.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
            string support = Names(true), opposition = Names(false);
            var lines = new List<string>();
            if (decision.SupportStatusOfFinalDecision == KingdomDecision.SupportStatus.Minority)
                lines.Add(new TextObject("{=BC_InternalPeace_CourierOverride}{RULER} approved the terms despite the council's opposition.")
                    .SetTextVariable("RULER", decision.Kingdom.Leader?.Name).ToString());
            if (support.Length > 0) lines.Add(new TextObject("{=BC_InternalPeace_CourierSupporters}For the terms: {HOUSES}.").SetTextVariable("HOUSES", support).ToString());
            if (opposition.Length > 0) lines.Add(new TextObject("{=BC_InternalPeace_CourierOpponents}Against the terms: {HOUSES}.").SetTextVariable("HOUSES", opposition).ToString());
            if (lines.Count > 0) _councilReports[notice.ConflictId] = string.Join("\n", lines);
        }
    }
}
