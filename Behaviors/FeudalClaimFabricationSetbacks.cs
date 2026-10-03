using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class FeudalClaimFabricationBehavior
    {
        private Dictionary<string, float> _retryAfter = new Dictionary<string, float>();

        private static string RetryKey(string clan, string title) => clan.Length + ":" + clan + title;

        private bool IsOnCooldown(Clan clan, FeudalTitleRecord title) => clan != null && title != null
            && _retryAfter.TryGetValue(RetryKey(clan.StringId, title.TitleId), out float until) && until > CurrentDay;

        private void SetRetryCooldown(FeudalClaimFabricationRecord record, int years)
        {
            _retryAfter[RetryKey(record.FabricatorClanId, record.TargetTitleId)] = CurrentDay + years * GetCampaignDaysInYear();
        }

        private void ResolveNpcSetback(FeudalTitleBehavior titles, FeudalClaimFabricationRecord record, Clan clan, FeudalTitleRecord title)
        {
            bool worthwhile = TryBuildAiFabricationCandidate(titles, clan, title, GetHeldTitles(titles, clan),
                out AiFabricationCandidate candidate, requireStartingFunds: false) && candidate.Score >= GetMinimumScore(candidate.Track);
            int choice = ChooseNpcSetback(worthwhile, clan.Leader.Gold, record.SetbackGoldCost, MBRandom.RandomFloat);
            ResolveSetbackChoice(record, record.PendingSetbackStage, choice);
        }

        // Choice 0 pays, 1 repeats the work, 2 abandons it.
        internal static int ChooseNpcSetback(bool worthwhile, int gold, int fee, float roll)
        {
            if (!worthwhile) return 2;
            if (gold - fee >= BellumCivileConstants.FeudalClaimFabricationAiGoldReserve) return 0;
            return roll < 0.75f ? 1 : 2;
        }

        private void ResolveSetbackChoice(FeudalClaimFabricationRecord record, int stage, int choice)
        {
            if (!_fabrications.Contains(record) || !record.IsActive || !record.AwaitingSetback
                || record.PendingSetbackStage != stage || choice < 0 || choice > 2) return;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan clan = ResolveClan(record.FabricatorClanId);
            Hero fabricator = ResolveHero(record.FabricatorHeroId);
            var title = titles?.GetTitle(record.TargetTitleId);
            if (!IsFabricationStillValid(titles, record, clan, fabricator, title, out string reason))
            {
                CancelFabrication(record, clan, title, reason, refundGold: true);
                return;
            }
            if (choice == 2)
            {
                record.SetActive(false);
                SetRetryCooldown(record, 1);
            }
            else
            {
                int cost = record.SetbackGoldCost;
                if (choice == 0 && clan.Leader.Gold < cost) return;
                // Consume the saved decision before invoking gold-transfer callbacks.
                if (!record.ResolveSetback(choice == 0)) return;
                if (choice == 0 && cost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(clan.Leader, null, cost, true);
            }
            BellumCivileDebug.Trace("fabrication", $"setback resolved; clan={record.FabricatorClanId}; title={record.TargetTitleId}; stage={stage}; choice={choice}; progress={record.Progress:0.00}.");
        }

        private void ShowPlayerSetback()
        {
            var record = _fabrications.FirstOrDefault(r => r != null && r.IsActive && r.AwaitingSetback
                && r.FabricatorClanId == Clan.PlayerClan?.StringId);
            if (record == null) return;
            int stage = record.PendingSetbackStage;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var title = titles?.GetTitle(record.TargetTitleId);
            if (!IsFabricationStillValid(titles, record, Clan.PlayerClan, ResolveHero(record.FabricatorHeroId), title, out string reason))
            {
                CancelFabrication(record, Clan.PlayerClan, title, reason, refundGold: true);
                return;
            }
            TextObject body = stage == 1
                ? new TextObject("{=BC_Fabrication_SetbackPrecedents}My liege, the records concerning {TITLE} are less helpful than I had hoped. A missing grant leaves a gap in our account. With more coin I can secure the testimony we need; without it, I must begin these inquiries anew. No one has yet uncovered our purpose.")
                : new TextObject("{=BC_Fabrication_SetbackCharters}My liege, the charters concerning {TITLE} disagree on a detail that could betray our work. Additional coin would secure the materials and discreet assistance to put this right. Otherwise, I must prepare the charters again using what we have. Our earlier legal precedents remain sound, and our purpose is still secret.");
            body.SetTextVariable("TITLE", GetOutcomeTitleName(title));
            var payment = new TextObject("{=BC_Fabrication_SetbackPay}Send {GOLD} gold to put matters right.");
            payment.SetTextVariable("GOLD", record.SetbackGoldCost);
            var choices = new List<InquiryElement> {
                new InquiryElement(0, payment.ToString(), null, Clan.PlayerClan.Leader.Gold >= record.SetbackGoldCost,
                    new TextObject("{=BC_Fabrication_SetbackPayHint}Resume work without losing progress. No additional influence is required.").ToString()),
                new InquiryElement(1, new TextObject("{=BC_Fabrication_SetbackDelay}Work with what you have. I can wait.").ToString(), null),
                new InquiryElement(2, new TextObject("{=BC_Fabrication_SetbackAbandon}Abandon the scheme.").ToString(), null, true,
                    new TextObject("{=BC_Fabrication_SetbackAbandonHint}No funds are returned. Your house cannot attempt another forgery for this title for one year.").ToString()) };
            _isShowingPlayerOutcome = true;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Fabrication_SetbackTitle}A Letter from Your Scribe").ToString(), body.ToString(),
                choices, false, 1, 1, new TextObject("{=BC_Fabrication_SetbackConfirm}Send instructions").ToString(), null,
                selected => {
                    _isShowingPlayerOutcome = false;
                    if (selected?.Count == 1) ResolveSetbackChoice(record, stage, (int)selected[0].Identifier);
                }, null), true);
        }
    }
}
