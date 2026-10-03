using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public sealed class FeudalFabricationStagePreview
    {
        private readonly FeudalClaimFabricationRecord _record;
        private readonly Hero _fabricator;
        private readonly float[] _stageSuccess = new float[3];

        public FeudalFabricationStagePreview(FeudalTitleRecord title, FeudalClaimFabricationRecord record)
        {
            _record = record;
            _fabricator = record == null ? Clan.PlayerClan?.Leader
                : Hero.FindFirst(hero => hero.StringId == record.FabricatorHeroId);
            Clan holder = Clan.All.FirstOrDefault(clan => clan.StringId == title?.DeJureHolderClanId)
                ?? Clan.All.FirstOrDefault(clan => clan.StringId == title?.DeFactoHolderClanId);
            Hero detector = holder?.Leader;
            for (int stage = 1; stage <= 3; stage++)
                _stageSuccess[stage - 1] = holder == null || holder == _fabricator?.Clan || detector == null || detector.IsDead
                    ? 1f : 1f - FeudalClaimFabricationBehavior.CalculateFabricationDiscoveryChance(_fabricator, detector, stage, out _, out _);
        }

        public static TextObject GetName(int stage)
        {
            return stage == 1 ? new TextObject("{=BC_Fabrication_Stage1}Gathering Legal Precedents")
                : stage == 2 ? new TextObject("{=BC_Fabrication_Stage2}Preparing Disputed Charters")
                : new TextObject("{=BC_Fabrication_Stage3}Forging Letters Patent");
        }

        public TextObject GetStatus(int stage)
        {
            if (_record?.PendingSetbackStage == stage)
                return new TextObject("{=BC_Fabrication_AwaitingInstructions}Awaiting instructions");
            if (_record?.ReworkStage == stage)
                return new TextObject("{=BC_Fabrication_Reworking}Repeating work (no further check)");
            if (_record?.DidStageFail(stage) == true)
                return new TextObject("{=BC_Fabrication_StageFailed}Failed");
            if (HasPassed(stage))
                return new TextObject("{=BC_Fabrication_StagePassed}Passed");
            TextObject text = stage < 3
                ? new TextObject("{=BC_Fabrication_SetbackRisk}Pending ({CHANCE}% setback risk)")
                : new TextObject("{=BC_Fabrication_StagePending}Pending ({CHANCE}% success)");
            text.SetTextVariable("CHANCE", ((stage < 3 ? 1f - _stageSuccess[stage - 1] : _stageSuccess[stage - 1]) * 100f).ToString("0.#"));
            return text;
        }

        private bool HasPassed(int stage)
        {
            return _record != null && (stage == 1 ? _record.HasPassed33 : stage == 2 ? _record.HasPassed66 : _record.HasPassed100);
        }

        public TextObject GetCurrentStageDescription()
        {
            for (int stage = 1; stage <= 3; stage++)
            {
                if (_record?.DidStageFail(stage) == true)
                    return null;
                if (HasPassed(stage) && _record?.PendingSetbackStage != stage && _record?.ReworkStage != stage)
                    continue;

                TextObject text = new TextObject("{=BC_Fabrication_StageState}{STAGE}: {STATUS}");
                text.SetTextVariable("STAGE", GetName(stage));
                text.SetTextVariable("STATUS", GetStatus(stage));
                return text;
            }
            return null;
        }

        public TextObject BuildSummary()
        {
            TextObject skills = new TextObject("{=BC_Fabrication_Skills}Relevant skills:\nStewardship {STEWARD}\nRoguery {ROGUERY}");
            skills.SetTextVariable("STEWARD", _fabricator?.GetSkillValue(DefaultSkills.Steward) ?? 0);
            skills.SetTextVariable("ROGUERY", _fabricator?.GetSkillValue(DefaultSkills.Roguery) ?? 0);
            float success = _record?.DidStageFail(3) == true ? 0f : HasPassed(3) ? 1f : _stageSuccess[2];
            TextObject estimate = new TextObject("{=BC_Fabrication_FinalEstimate}Estimated final acceptance: {CHANCE}%. Early setbacks can be overcome with additional funds or time.");
            estimate.SetTextVariable("CHANCE", (success * 100f).ToString("0.#"));
            List<string> lines = new List<string> { skills.ToString(), estimate.ToString() };
            return new TextObject("{=!}" + string.Join("\n\n", lines));
        }
    }
}
