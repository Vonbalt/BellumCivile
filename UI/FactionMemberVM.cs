using BellumCivile.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile.UI
{
    /// <summary>
    /// Why did I do this file?
    /// To provide the ViewModel for individual clan members within a faction, handling their visual banner display and generating the detailed tooltip breaking down exactly why they want to rebel.
    /// </summary>
    public class FactionMemberVM : ViewModel
    {
        private readonly Clan _clan;
        private readonly bool _showRebelliousIntent;
        private string _clanName;
        private BannerImageIdentifierVM _bannerVisual;
        private CharacterImageIdentifierVM _portraitVisual;
        private HintViewModel _scoreHint;
        private readonly BasicTooltipViewModel _scoreTooltip;

        public FactionMemberVM(Clan clan, bool showRebelliousIntent = true)
        {
            _clan = clan;
            _showRebelliousIntent = showRebelliousIntent;
            ClanName = clan.Name.ToString();
            BannerVisual = new BannerImageIdentifierVM(clan.Banner, true);
            if (clan.Leader?.CharacterObject != null)
                PortraitVisual = new CharacterImageIdentifierVM(BellumCivile.UI.PortraitAppearance.Create(clan.Leader.CharacterObject));
            if (_showRebelliousIntent)
            {
                ScoreHint = new HintViewModel(new TextObject(GenerateScoreBreakdown(clan)));
                _scoreTooltip = new BasicTooltipViewModel(() => BuildScoreTooltipProperties(_clan));
            }
        }

        public static List<TooltipProperty> BuildScoreTooltipProperties(Clan clan)
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            AddTitle(properties, clan);

            if (clan == null || clan.Kingdom == null || clan.Kingdom.RulingClan == null)
            {
                AddMessage(properties, new TextObject("{=BC_Score_DataUnavail}Data unavailable."));
                return properties;
            }

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            string activeRebellionText = TryBuildActiveRebellionBreakdown(clan, factionManager);
            if (!string.IsNullOrEmpty(activeRebellionText))
            {
                AddMessage(properties, activeRebellionText);
                return properties;
            }

            if (clan == Clan.PlayerClan)
            {
                AddMessage(properties, new TextObject("{=BC_Score_PlayerReasons}Only you know your own reasons."));
                return properties;
            }

            if (clan == clan.Kingdom.RulingClan)
            {
                AddMessage(properties, new TextObject("{=BC_Score_LiegeReasons}Your liege lord. The crown harbors no rebellious intent against itself."));
                return properties;
            }

            if (clan.Leader == null)
            {
                AddMessage(properties, new TextObject("{=BC_Score_DataUnavail}Data unavailable."));
                return properties;
            }

            RebellionIntentAssessment assessment = RebellionIntentCalculator.Assess(clan);
            if (!assessment.IsValid)
            {
                AddMessage(properties, new TextObject("{=BC_Score_DataUnavail}Data unavailable."));
                return properties;
            }

            foreach (RebellionIntentComponent component in assessment.Components)
                AddScoreRow(properties, component.Label, component.Value);

            AddSeparator(properties);
            AddValueRow(
                properties,
                new TextObject("{=BC_Score_Label_Total}Rebellious Intent"),
                FormatScore(assessment.Total) + " / " + O.RebelliousIntentThreshold.ToString("0"));
            return properties;
        }

        public static string GenerateScoreBreakdown(Clan clan)
        {
            if (clan == null || clan.Kingdom == null || clan.Kingdom.RulingClan == null)
                return new TextObject("{=BC_Score_DataUnavail}Data unavailable.").ToString();

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            string activeRebellionText = TryBuildActiveRebellionBreakdown(clan, factionManager);
            if (!string.IsNullOrEmpty(activeRebellionText))
                return activeRebellionText;

            if (clan == Clan.PlayerClan)
                return new TextObject("{=BC_Score_PlayerReasons}Only you know your own reasons.").ToString();

            if (clan == clan.Kingdom.RulingClan)
                return new TextObject("{=BC_Score_LiegeReasons}Your liege lord. The crown harbors no rebellious intent against itself.").ToString();

            RebellionIntentAssessment assessment = RebellionIntentCalculator.Assess(clan);
            if (!assessment.IsValid)
                return new TextObject("{=BC_Score_DataUnavail}Data unavailable.").ToString();

            List<string> lines = new List<string>();
            foreach (RebellionIntentComponent component in assessment.Components)
            {
                TextObject line = new TextObject("{=BC_Score_ComponentLine}{LABEL}: {SIGN}{SCORE}");
                line.SetTextVariable("LABEL", component.Label);
                line.SetTextVariable("SIGN", component.Value >= 0f ? "+" : string.Empty);
                line.SetTextVariable("SCORE", FormatUnsignedScore(component.Value));
                lines.Add(line.ToString());
            }

            TextObject total = new TextObject("{=BC_Score_Total}Rebellious Intent: {SCORE} / {THRESHOLD}");
            total.SetTextVariable("SCORE", FormatScore(assessment.Total));
            total.SetTextVariable("THRESHOLD", O.RebelliousIntentThreshold.ToString("0"));
            lines.Add(total.ToString());
            return string.Join("\n", lines);
        }

        private static string GenerateScoreBreakdownLegacy(Clan clan)
        {
            if (clan == null || clan.Kingdom == null || clan.Kingdom.RulingClan == null) return new TextObject("{=BC_Score_DataUnavail}Data unavailable.").ToString();

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            string activeRebellionText = TryBuildActiveRebellionBreakdown(clan, factionManager);
            if (!string.IsNullOrEmpty(activeRebellionText))
                return activeRebellionText;

            if (clan == Clan.PlayerClan) return new TextObject("{=BC_Score_PlayerReasons}Only you know your own reasons.").ToString();

            if (clan == clan.Kingdom.RulingClan) return new TextObject("{=BC_Score_LiegeReasons}Your liege lord. The crown harbors no rebellious intent against itself.").ToString();

            if (clan.Leader == null) return new TextObject("{=BC_Score_DataUnavail}Data unavailable.").ToString();

            Hero liege = clan.Kingdom.RulingClan.Leader;
            if (liege == null) return new TextObject("{=BC_Score_DataUnavail}Data unavailable.").ToString();
            List<string> lines = new List<string>();
            float totalScore = 0f;

            // What does this complex formula do?
            // Accumulates a detailed, player-facing breakdown of a clan's rebellion score, exposing exactly how relationships, cultural friction, under-landed grievances, and personality traits calculate their likelihood of committing treason.
            int relation = clan.Leader.GetRelation(liege);
            int relScore = -relation;
            TextObject relText = relation >= 0
                ? new TextObject("{=BC_Score_Regards}Regards: {SIGN}{SCORE}")
                : new TextObject("{=BC_Score_Grievances}Grievances: {SIGN}{SCORE}");
            relText.SetTextVariable("SIGN", relScore >= 0 ? "+" : "");
            relText.SetTextVariable("SCORE", relScore);
            lines.Add(relText.ToString());
            totalScore += relScore;

            if (clan.Culture != clan.Kingdom.Culture)
            {
                lines.Add(new TextObject("{=BC_Score_CultureFriction}Cultural Friction: +20").ToString());
                totalScore += 20f;
            }

            float hoardingScore = ClanFiefDesireHelper.CalculateRulerHoardingPressure(clan);
            if (hoardingScore > 0f)
            {
                TextObject hoardingText = new TextObject("{=BC_Score_HoardingFiefs}Ruler Hoarding Fiefs: +{SCORE}");
                hoardingText.SetTextVariable("SCORE", hoardingScore);
                lines.Add(hoardingText.ToString());
                totalScore += hoardingScore;
            }

            int calcLevel = clan.Leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calcLevel <= -2) { lines.Add(new TextObject("{=BC_Score_Hotheaded}Hotheaded: +20").ToString()); totalScore += 20f; }
            else if (calcLevel == -1) { lines.Add(new TextObject("{=BC_Score_Impulsive}Impulsive: +10").ToString()); totalScore += 10f; }
            else if (calcLevel == 1) { lines.Add(new TextObject("{=BC_Score_Calculating}Calculating: -10").ToString()); totalScore -= 10f; }
            else if (calcLevel >= 2) { lines.Add(new TextObject("{=BC_Score_Cerebral}Cerebral: -20").ToString()); totalScore -= 20f; }

            int honorLevel = clan.Leader.GetTraitLevel(DefaultTraits.Honor);
            if (honorLevel <= -2) { lines.Add(new TextObject("{=BC_Score_Deceitful}Deceitful: +20").ToString()); totalScore += 20f; }
            else if (honorLevel == -1) { lines.Add(new TextObject("{=BC_Score_Devious}Devious: +10").ToString()); totalScore += 10f; }
            else if (honorLevel == 1) { lines.Add(new TextObject("{=BC_Score_Honest}Honest: -10").ToString()); totalScore -= 10f; }
            else if (honorLevel >= 2) { lines.Add(new TextObject("{=BC_Score_Honorable}Honorable: -20").ToString()); totalScore -= 20f; }

            int valorLevel = clan.Leader.GetTraitLevel(DefaultTraits.Valor);
            if (valorLevel <= -2) { lines.Add(new TextObject("{=BC_Score_Craven}Craven: -20").ToString()); totalScore -= 20f; }
            else if (valorLevel == -1) { lines.Add(new TextObject("{=BC_Score_Cautious}Cautious: -10").ToString()); totalScore -= 10f; }
            else if (valorLevel == 1) { lines.Add(new TextObject("{=BC_Score_Brave}Brave: +10").ToString()); totalScore += 10f; }
            else if (valorLevel >= 2) { lines.Add(new TextObject("{=BC_Score_Fearless}Fearless: +20").ToString()); totalScore += 20f; }

            bool hasMarriageAlliance = MarriageAllianceHelper.HasMarriageAlliance(clan, clan.Kingdom.RulingClan);
            if (hasMarriageAlliance)
            {
                TextObject marriage = new TextObject("{=BC_Score_Marriage}Marriage Alliance: -{SCORE}");
                marriage.SetTextVariable("SCORE", C.RebelliousMarriageAllianceIntentReduction.ToString("0"));
                lines.Add(marriage.ToString());
                totalScore -= C.RebelliousMarriageAllianceIntentReduction;
            }

            bool respectsDynasticHead = Campaign.Current.GetCampaignBehavior<DynasticClaimBehavior>()
                ?.HasActiveCadetDynasticTieToRulingClan(clan, clan.Kingdom) == true;
            if (respectsDynasticHead)
            {
                TextObject dynasticHeadText = new TextObject("{=BC_Score_DynasticHead}Heir to the throne: -{SCORE}");
                dynasticHeadText.SetTextVariable("SCORE", C.CadetBranchDynasticHeadRebellionReduction.ToString("0"));
                lines.Add(dynasticHeadText.ToString());
                totalScore -= C.CadetBranchDynasticHeadRebellionReduction;
            }

            ClaimFeudBehavior claimFeudBehavior = Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>();
            float claimFeudPenalty = claimFeudBehavior?.GetRebelliousIntentPenalty(clan) ?? 0f;
            if (claimFeudPenalty > 0f)
            {
                TextObject claimFeudText = new TextObject("{=BC_Score_ClaimFeudPenaltyLine}{REASON}: -{SCORE}");
                claimFeudText.SetTextVariable("REASON", claimFeudBehavior.GetRebelliousIntentPenaltyLabel(clan));
                claimFeudText.SetTextVariable("SCORE", claimFeudPenalty.ToString("0"));
                lines.Add(claimFeudText.ToString());
                totalScore -= claimFeudPenalty;
            }

            FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
            PrivyCouncilBehavior councilBehavior = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            float councilAmbition = 0f;
            float councilRepresentation = 0f;
            float councilDismissal = 0f;
            councilBehavior?.GetRebelliousIntentComponents(clan, out councilAmbition, out councilRepresentation, out councilDismissal);
            AddCouncilBreakdownLine(lines, "{=BC_Score_WantsCouncilSeat}Desires a Council Seat: {SIGN}{SCORE}", councilAmbition, ref totalScore);
            AddCouncilBreakdownLine(lines, "{=BC_Score_FactionCouncilRepresentation}Court Faction Represented: {SIGN}{SCORE}", councilRepresentation, ref totalScore);
            AddCouncilBreakdownLine(lines, "{=BC_Score_DismissedFromCouncil}Dismissed from Council: {SIGN}{SCORE}", councilDismissal, ref totalScore);

            ControversyBehavior controversyBehavior = Campaign.Current.GetCampaignBehavior<ControversyBehavior>();
            int rulerControversy = controversyBehavior?.GetTotalControversy(clan.Kingdom) ?? 0;
            AddCouncilBreakdownLine(lines, "{=BC_Score_RulerControversy}Ruler Controversy: {SIGN}{SCORE}", rulerControversy, ref totalScore);

            int realmSize = clan.Kingdom.Settlements.Count(s => s.IsTown || s.IsCastle);
            float sizeScore = TaleWorlds.Library.MathF.Min(50f, (float)realmSize);
            if (sizeScore > 0)
            {
                TextObject sizeText = new TextObject("{=BC_Score_RealmSize}Realm Size: +{SCORE}");
                sizeText.SetTextVariable("SCORE", sizeScore);
                lines.Add(sizeText.ToString());
                totalScore += sizeScore;
            }

            TextObject totalText = new TextObject("{=BC_Score_Total}Rebellious Intent: {SCORE} / {THRESHOLD}");
            totalText.SetTextVariable("SCORE", totalScore);
            totalText.SetTextVariable("THRESHOLD", O.RebelliousIntentThreshold.ToString("0"));
            lines.Add(totalText.ToString());

            return string.Join("\n", lines);
        }

        private static void AddCouncilBreakdownLine(List<string> lines, string template, float value, ref float totalScore)
        {
            if (Math.Abs(value) < 0.01f)
                return;

            TextObject text = new TextObject(template);
            text.SetTextVariable("SIGN", value >= 0f ? "+" : string.Empty);
            text.SetTextVariable("SCORE", value.ToString("0"));
            lines.Add(text.ToString());
            totalScore += value;
        }

        private static string TryBuildActiveRebellionBreakdown(Clan clan, FactionManagerBehavior factionManager)
        {
            if (clan == null || factionManager == null)
                return null;

            FactionObject activeRebellion = factionManager.GetRebelFaction(clan);
            Kingdom rebelKingdom = activeRebellion?.GetRebelKingdom();

            if (activeRebellion == null || rebelKingdom == null || !activeRebellion.IsCivilWarActive())
            {
                if (!factionManager.IsClanOnActiveCivilWarRebelSide(clan, out activeRebellion, out rebelKingdom))
                    return null;
            }

            TextObject text = activeRebellion.Leader == clan
                ? new TextObject("{=BC_Score_ActiveRebelLeader}This clan is leading the {REBEL_KINGDOM} against {PARENT_KINGDOM} with the objective of {DEMAND}")
                : new TextObject("{=BC_Score_ActiveRebelMember}This clan is fighting in the {REBEL_KINGDOM} against {PARENT_KINGDOM} with the objective of {DEMAND}");

            text.SetTextVariable("PARENT_KINGDOM", activeRebellion.ParentKingdom?.Name ?? new TextObject("?"));
            text.SetTextVariable("DEMAND", activeRebellion.GetDemandDescription());
            text.SetTextVariable("REBEL_KINGDOM", rebelKingdom?.Name ?? new TextObject("?"));
            return text.ToString();
        }

        private static void AddTitle(List<TooltipProperty> properties, Clan clan)
        {
            string title;
            if (clan?.Leader != null)
            {
                TextObject text = new TextObject("{=BC_Score_TooltipTitle}{LEADER_NAME}");
                text.SetTextVariable("LEADER_NAME", clan.Leader.Name);
                title = text.ToString();
            }
            else if (clan != null)
            {
                title = clan.Name.ToString();
            }
            else
            {
                title = new TextObject("{=BC_Score_TooltipTitleUnknown}Rebellious Intent").ToString();
            }

            properties.Add(new TooltipProperty(title, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title));
            AddSeparator(properties);
        }

        private static void AddMessage(List<TooltipProperty> properties, TextObject message)
        {
            AddMessage(properties, message.ToString());
        }

        private static void AddMessage(List<TooltipProperty> properties, string message)
        {
            properties.Add(new TooltipProperty(new TextObject("{=BC_Score_Label_Status}Status").ToString(), message, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
        }

        private static void AddScoreRow(List<TooltipProperty> properties, string labelText, float value)
        {
            AddValueRow(properties, new TextObject(labelText), FormatScore(value));
        }

        private static void AddScoreRow(List<TooltipProperty> properties, TextObject label, float value)
        {
            AddValueRow(properties, label, FormatScore(value));
        }

        private static void AddValueRow(List<TooltipProperty> properties, TextObject label, string value)
        {
            properties.Add(new TooltipProperty(label.ToString(), value, 0));
        }

        private static void AddSeparator(List<TooltipProperty> properties)
        {
            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
        }

        private static string FormatScore(float value)
        {
            string number = Math.Abs(value - (float)Math.Round(value)) < 0.01f
                ? ((int)Math.Round(value)).ToString()
                : value.ToString("0.#");
            return value > 0f ? "+" + number : number;
        }

        private static string FormatUnsignedScore(float value)
        {
            float absolute = Math.Abs(value);
            return Math.Abs(absolute - Math.Round(absolute)) < 0.01f
                ? ((int)Math.Round(absolute)).ToString()
                : absolute.ToString("0.#");
        }

        public void ExecuteOpenClan()
        {
            if (_clan != null && Campaign.Current?.EncyclopediaManager != null)
                Campaign.Current.EncyclopediaManager.GoToLink(_clan.EncyclopediaLink);
        }

        [DataSourceProperty] public bool ShowRebelliousIntent => _showRebelliousIntent;
        [DataSourceProperty] public bool ShowPlainPortrait => !_showRebelliousIntent;

        public void ExecuteBeginScoreHint()
        {
            if (!_showRebelliousIntent)
                return;

            if (_scoreTooltip != null)
                _scoreTooltip.ExecuteBeginHint();
            else
                ScoreHint?.ExecuteBeginHint();
        }

        public void ExecuteEndScoreHint()
        {
            if (!_showRebelliousIntent)
                return;

            if (_scoreTooltip != null)
                _scoreTooltip.ExecuteEndHint();
            else
                ScoreHint?.ExecuteEndHint();
        }

        [DataSourceProperty]
        public string ClanName { get => _clanName; set { if (value != _clanName) { _clanName = value; OnPropertyChangedWithValue(value, "ClanName"); } } }

        [DataSourceProperty]
        public BannerImageIdentifierVM BannerVisual { get => _bannerVisual; set { if (value != _bannerVisual) { _bannerVisual = value; OnPropertyChangedWithValue(value, "BannerVisual"); } } }

        [DataSourceProperty]
        public CharacterImageIdentifierVM PortraitVisual { get => _portraitVisual; set { if (value != _portraitVisual) { _portraitVisual = value; OnPropertyChangedWithValue(value, "PortraitVisual"); } } }

        [DataSourceProperty]
        public HintViewModel ScoreHint { get => _scoreHint; set { if (value != _scoreHint) { _scoreHint = value; OnPropertyChangedWithValue(value, "ScoreHint"); } } }
    }
}
