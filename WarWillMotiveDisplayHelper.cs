using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public static class WarWillMotiveDisplayHelper
    {
        public static WarWillMotiveBreakdown BuildBreakdown(
            Clan clan,
            Kingdom target,
            float warWill,
            IEnumerable<WarWillPressureRecord> pressureRecords,
            float peaceThreshold,
            float warThreshold)
        {
            WarWillVoteStance stance = warWill >= warThreshold
                ? WarWillVoteStance.Yay
                : warWill <= peaceThreshold
                    ? WarWillVoteStance.Nay
                    : WarWillVoteStance.Neutral;

            IEnumerable<WarWillPressureRecord> filtered = pressureRecords ?? Enumerable.Empty<WarWillPressureRecord>();
            if (target != null)
                filtered = filtered.Where(record => record != null && record.TargetKingdomId == target.StringId);

            List<WarWillMotiveLine> motives = filtered
                .Where(record => record != null)
                .OrderByDescending(record => Math.Abs(record.Amount))
                .ThenByDescending(record => record.CreatedDay)
                .Select(record => BuildLine(record, clan))
                .Where(line => line != null)
                .ToList();

            return new WarWillMotiveBreakdown(warWill, stance, motives);
        }

        public static WarWillMotiveLine BuildLine(WarWillPressureRecord record, Clan clan = null)
        {
            if (record == null)
                return null;

            string label = BuildShortLabel(record, clan);
            TextObject line = new TextObject("{=BC_WarWill_Motive_TooltipLine}{REASON}: {AMOUNT}");
            line.SetTextVariable("REASON", label);
            line.SetTextVariable("AMOUNT", FormatAmount(record.Amount));
            return new WarWillMotiveLine(record.ReasonType, record.Amount, label, line.ToString());
        }

        public static string BuildShortLabel(WarWillPressureRecord record, Clan clan = null)
        {
            if (record == null)
                return string.Empty;

            string context = ResolveContextName(record, clan);
            TextObject text = GetLabelText(record.ReasonType, string.IsNullOrWhiteSpace(context));
            text.SetTextVariable("CONTEXT", context);
            string label = text.ToString();
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            return string.IsNullOrWhiteSpace(record.Reason)
                ? new TextObject("{=BC_WarWill_Reason_Unknown}War pressure").ToString()
                : record.Reason;
        }

        private static TextObject GetLabelText(WarWillReasonType type, bool noContext)
        {
            switch (type)
            {
                case WarWillReasonType.WarDeclared:
                    return new TextObject("{=BC_WarWill_Reason_WarDeclared}War declared");
                case WarWillReasonType.RealmAttacked:
                    return new TextObject("{=BC_WarWill_Reason_RealmAttacked}Realm attacked");
                case WarWillReasonType.OwnTownLost:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_OwnTownLost_NoContext}Town lost")
                        : new TextObject("{=BC_WarWill_Reason_OwnTownLost}Lost {CONTEXT}");
                case WarWillReasonType.OwnCastleLost:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_OwnCastleLost_NoContext}Castle lost")
                        : new TextObject("{=BC_WarWill_Reason_OwnCastleLost}Lost {CONTEXT}");
                case WarWillReasonType.NearbyFiefLost:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_NearbyFiefLost_NoContext}Nearby fief lost")
                        : new TextObject("{=BC_WarWill_Reason_NearbyFiefLost}Nearby fief lost: {CONTEXT}");
                case WarWillReasonType.RealmFiefLost:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_RealmFiefLost_NoContext}Realm fief lost")
                        : new TextObject("{=BC_WarWill_Reason_RealmFiefLost}Realm fief lost: {CONTEXT}");
                case WarWillReasonType.SettlementCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_SettlementCaptured_NoContext}Settlement captured")
                        : new TextObject("{=BC_WarWill_Reason_SettlementCaptured}Captured {CONTEXT}");
                case WarWillReasonType.ClaimedObjectiveAchieved:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_ClaimedObjectiveAchieved_NoContext}Claim objective achieved")
                        : new TextObject("{=BC_WarWill_Reason_ClaimedObjectiveAchieved}Claim to {CONTEXT} achieved");
                case WarWillReasonType.OwnVillageRaided:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_OwnVillageRaided_NoContext}Village raided")
                        : new TextObject("{=BC_WarWill_Reason_OwnVillageRaided}Village raided: {CONTEXT}");
                case WarWillReasonType.NearbyVillageRaided:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_NearbyVillageRaided_NoContext}Nearby village raided")
                        : new TextObject("{=BC_WarWill_Reason_NearbyVillageRaided}Nearby village raided: {CONTEXT}");
                case WarWillReasonType.RulerCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_RulerCaptured_NoContext}Ruler captured")
                        : new TextObject("{=BC_WarWill_Reason_RulerCaptured}{CONTEXT} captured");
                case WarWillReasonType.HeirCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_HeirCaptured_NoContext}Heir captured")
                        : new TextObject("{=BC_WarWill_Reason_HeirCaptured}{CONTEXT} captured");
                case WarWillReasonType.NobleCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_NobleCaptured_NoContext}Noble captured")
                        : new TextObject("{=BC_WarWill_Reason_NobleCaptured}{CONTEXT} captured");
                case WarWillReasonType.EnemyRulerCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_EnemyRulerCaptured_NoContext}Enemy ruler captured")
                        : new TextObject("{=BC_WarWill_Reason_EnemyRulerCaptured}Captured enemy ruler {CONTEXT}");
                case WarWillReasonType.EnemyHeirCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_EnemyHeirCaptured_NoContext}Enemy heir captured")
                        : new TextObject("{=BC_WarWill_Reason_EnemyHeirCaptured}Captured enemy heir {CONTEXT}");
                case WarWillReasonType.EnemyNobleCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_EnemyNobleCaptured_NoContext}Enemy noble captured")
                        : new TextObject("{=BC_WarWill_Reason_EnemyNobleCaptured}Captured {CONTEXT}");
                case WarWillReasonType.RealmRulerCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_RealmRulerCaptured_NoContext}Realm ruler captured")
                        : new TextObject("{=BC_WarWill_Reason_RealmRulerCaptured}Realm ruler captured: {CONTEXT}");
                case WarWillReasonType.RealmHeirCaptured:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_RealmHeirCaptured_NoContext}Realm heir captured")
                        : new TextObject("{=BC_WarWill_Reason_RealmHeirCaptured}Realm heir captured: {CONTEXT}");
                case WarWillReasonType.NobleKilledInBattle:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_NobleKilledInBattle_NoContext}Noble killed in battle")
                        : new TextObject("{=BC_WarWill_Reason_NobleKilledInBattle}{CONTEXT} killed in battle");
                case WarWillReasonType.EnemyNobleKilled:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_EnemyNobleKilled_NoContext}Enemy noble killed")
                        : new TextObject("{=BC_WarWill_Reason_EnemyNobleKilled}Enemy noble killed: {CONTEXT}");
                case WarWillReasonType.BattleVictory:
                    return new TextObject("{=BC_WarWill_Reason_BattleVictory}Battle victory");
                case WarWillReasonType.MajorBattleVictory:
                    return new TextObject("{=BC_WarWill_Reason_MajorBattleVictory}Major battle victory");
                case WarWillReasonType.BattleDefeat:
                    return new TextObject("{=BC_WarWill_Reason_BattleDefeat}Battle defeat");
                case WarWillReasonType.MajorBattleDefeat:
                    return new TextObject("{=BC_WarWill_Reason_MajorBattleDefeat}Major battle defeat");
                case WarWillReasonType.BattleCasualties:
                    return new TextObject("{=BC_WarWill_Reason_BattleCasualties}Battle casualties");
                case WarWillReasonType.WarDuration:
                    return new TextObject("{=BC_WarWill_Reason_WarDuration}War duration");
                case WarWillReasonType.MultipleWars:
                    return new TextObject("{=BC_WarWill_Reason_MultipleWars}Multiple wars");
                case WarWillReasonType.ClaimObjectiveUnfulfilled:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_ClaimObjectiveUnfulfilled_NoContext}Claim not achieved")
                        : new TextObject("{=BC_WarWill_Reason_ClaimObjectiveUnfulfilled}Claim to {CONTEXT} not achieved");
                case WarWillReasonType.RulingClanClaim:
                    return noContext
                        ? new TextObject("{=BC_WarWill_Reason_RulingClanClaim_NoContext}Sovereign claim")
                        : new TextObject("{=BC_WarWill_Reason_RulingClanClaim}Sovereign claim to {CONTEXT}");
                case WarWillReasonType.MarriageTies:
                    return new TextObject("{=BC_WarWill_Reason_MarriageTies}Marriage ties");
                case WarWillReasonType.FormalAlliance:
                    return new TextObject("{=BC_WarWill_Reason_FormalAlliance}Formal alliance");
                case WarWillReasonType.TradeAgreement:
                    return new TextObject("{=BC_WarWill_Reason_TradeAgreement}Trade agreement");
                default:
                    return new TextObject("{=BC_WarWill_Reason_Unknown}War pressure");
            }
        }

        private static string ResolveContextName(WarWillPressureRecord record, Clan clan)
        {
            if (!string.IsNullOrWhiteSpace(record.ContextTitleId))
            {
                FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(record.ContextTitleId);
                if (title != null)
                    return FeudalTitleDisplayHelper.FormatTitleName(title, clan);
            }

            if (!string.IsNullOrWhiteSpace(record.ContextSettlementId))
            {
                Settlement settlement = Settlement.All.FirstOrDefault(candidate => candidate?.StringId == record.ContextSettlementId);
                if (settlement != null)
                    return settlement.Name?.ToString() ?? settlement.StringId;
            }

            if (!string.IsNullOrWhiteSpace(record.ContextHeroId))
            {
                Hero hero = Hero.FindFirst(candidate => candidate != null && candidate.StringId == record.ContextHeroId);
                if (hero != null)
                    return hero.Name?.ToString() ?? hero.StringId;
            }

            return string.Empty;
        }

        private static string FormatAmount(float amount)
        {
            return amount.ToString("+0.0;-0.0;0.0");
        }
    }
}
