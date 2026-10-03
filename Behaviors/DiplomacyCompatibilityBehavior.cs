using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Audits optional Diplomacy settings without taking a compile-time dependency or changing
    /// another mod's configuration. Unsafe settings are reported once on every campaign load until
    /// the player corrects them in Diplomacy's MCM.
    /// </summary>
    public sealed class DiplomacyCompatibilityBehavior : CampaignBehaviorBase
    {
        private bool _warningShownThisSession;
        private List<TextObject> _pendingIssues;
        private int _postLoadTicksRemaining;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (_warningShownThisSession || _pendingIssues != null || !ModIntegrationHelper.IsDiplomacyLoaded)
                return;

            List<TextObject> issues = CollectUnsafeSettings();
            if (issues.Count == 0)
            {
                BellumCivileLogger.Log("Diplomacy compatibility audit passed; no conflicting MCM settings detected.");
                return;
            }

            // OnSessionLaunched still runs while Bannerlord is assembling the campaign state.
            // Opening an inquiry synchronously here can prevent both new games and loaded saves
            // from ever leaving the loading screen. Queue it for the first safe campaign UI ticks.
            _pendingIssues = issues;
            _postLoadTicksRemaining = 2;
            BellumCivileLogger.Log(
                $"Diplomacy compatibility audit queued {issues.Count} unsafe setting warning(s) for post-load display.");
        }

        private void OnTick(float dt)
        {
            if (_warningShownThisSession || _pendingIssues == null)
                return;

            if (_postLoadTicksRemaining > 0)
            {
                _postLoadTicksRemaining--;
                return;
            }

            // Do not replace a native startup/tutorial inquiry. The warning remains queued and is
            // shown on a later tick after the existing popup closes.
            if (InformationManager.IsAnyInquiryActive())
                return;

            List<TextObject> issues = _pendingIssues;
            _pendingIssues = null;
            _warningShownThisSession = true;

            string issueList = string.Join(Environment.NewLine, issues.Select(issue => "- " + issue));
            TextObject body = new TextObject(
                "{=BC_DiplomacyCompat_Body}Bellum Civile detected Diplomacy settings that can independently alter systems currently controlled by Bellum. Adjust the following options in Diplomacy's MCM before continuing this campaign:{NEW_LINE}{NEW_LINE}{SETTINGS}{NEW_LINE}{NEW_LINE}This warning will return whenever the campaign is loaded until these conflicts are corrected. Bellum does not change another mod's settings automatically.");
            body.SetTextVariable("NEW_LINE", Environment.NewLine);
            body.SetTextVariable("SETTINGS", issueList);

            TextObject title = new TextObject("{=BC_DiplomacyCompat_Title}Diplomacy Compatibility Warning");
            TextObject understood = new TextObject("{=BC_DiplomacyCompat_Understood}Understood");
            InformationManager.ShowInquiry(new InquiryData(
                title.ToString(),
                body.ToString(),
                true,
                false,
                understood.ToString(),
                null,
                null,
                null), true, false);

            BellumCivileLogger.Log(
                $"Diplomacy compatibility audit found {issues.Count} unsafe setting(s): {string.Join(", ", issues.Select(issue => issue.ToString()))}.");
        }

        private static List<TextObject> CollectUnsafeSettings()
        {
            List<TextObject> issues = new List<TextObject>();

            // Bellum's faction, rebellion, and temporary-kingdom framework is always active.
            AddPositiveFloatIssue(issues, "DailyChanceToStartRebelFaction",
                "{=BC_DiplomacyCompat_Setting_StartFaction}Civil Wars: Daily Chance to Start Rebel Faction must be 0");
            AddPositiveFloatIssue(issues, "DailyChanceToJoinRebelFaction",
                "{=BC_DiplomacyCompat_Setting_JoinFaction}Civil Wars: Daily Chance to Join Rebel Faction must be 0");
            AddPositiveFloatIssue(issues, "DailyChanceToStartCivilWar",
                "{=BC_DiplomacyCompat_Setting_StartCivilWar}Civil Wars: Daily Chance to Start Civil War must be 0");

            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return issues;

            AddEnabledBoolIssue(issues, "EnableWarExhaustion",
                "{=BC_DiplomacyCompat_Setting_WarExhaustion}War Exhaustion must be disabled (restart required by Diplomacy)");
            AddHostagePactGuidance(issues);
            AddEnabledBoolIssue(issues, "EnableFiefRepatriation",
                "{=BC_DiplomacyCompat_Setting_FiefRepatriation}Fief Repatriation must be disabled");
            AddEnabledBoolIssue(issues, "PlayerDiplomacyControl",
                "{=BC_DiplomacyCompat_Setting_PlayerControl}Player Diplomacy Control must be disabled");
            AddEnabledBoolIssue(issues, "EnableKingdomElimination",
                "{=BC_DiplomacyCompat_Setting_KingdomElimination}Kingdom Elimination must be disabled");
            AddPositiveIntIssue(issues, "MinimumWarDurationInDays",
                "{=BC_DiplomacyCompat_Setting_MinimumWarDuration}Minimum War Duration must be 0");
            AddPositiveIntIssue(issues, "DeclareWarCooldownInDays",
                "{=BC_DiplomacyCompat_Setting_WarCooldown}Declare War Cooldown must be 0");

            return issues;
        }

        private static void AddHostagePactGuidance(List<TextObject> issues)
        {
            if (ModIntegrationHelper.TryGetDiplomacySetting("NonAggressionPactDuration", out int duration) && duration != 0)
                issues.Add(new TextObject("{=BC_DiplomacyCompat_PactDuration}Non-Aggression Pact Duration in Days: set to 0."));
            if (ModIntegrationHelper.TryGetDiplomacySetting("NonAggressionPactTendency", out int tendency) && tendency != -100)
                issues.Add(new TextObject("{=BC_DiplomacyCompat_PactTendency}Non-Aggression Pact Tendency: set to -100."));
        }

        private static void AddEnabledBoolIssue(List<TextObject> issues, string propertyName, string text)
        {
            if (ModIntegrationHelper.TryGetDiplomacySetting(propertyName, out bool value) && value)
                issues.Add(new TextObject(text));
        }

        private static void AddPositiveIntIssue(List<TextObject> issues, string propertyName, string text)
        {
            if (ModIntegrationHelper.TryGetDiplomacySetting(propertyName, out int value) && value > 0)
                issues.Add(new TextObject(text));
        }

        private static void AddPositiveFloatIssue(List<TextObject> issues, string propertyName, string text)
        {
            if (ModIntegrationHelper.TryGetDiplomacySetting(propertyName, out float value) && value > 0.0001f)
                issues.Add(new TextObject(text));
        }
    }
}
