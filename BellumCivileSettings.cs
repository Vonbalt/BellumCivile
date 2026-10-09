using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class BellumCivileSettings : AttributeGlobalSettings<BellumCivileSettings>
    {
        private const string Rebellions = "{=BC_MCM_Group_Rebellions}Rebellions and Civil Wars";
        private const string CourtPolitics = "{=BC_MCM_Group_CourtPolitics}Court Politics";
        private const string War = "{=BC_MCM_Group_War}War and Peace";
        private const string WarAftermath = "{=BC_MCM_Group_WarAftermath}War Aftermath";
        private const string FeudalTitles = "{=BC_MCM_Group_FeudalTitles}Feudal Titles";
        private const string Succession = "{=BC_MCM_Group_Succession}Succession";
        private const string Childhood = Succession + "/{=BC_MCM_Group_Childhood}Custom Adulthood and Education";
        private const string ArmyLoyalty = "{=BC_MCM_Group_ArmyLoyalty}Army Loyalty";
        private const string Marriage = "{=BC_MCM_Group_Marriage}Marriage";
        private const string Mercenaries = "{=BC_MCM_Group_Mercenaries}Mercenary Companies";
        private const string Relationships = "{=BC_MCM_Group_Relationships}Relationships";
        private const string Economy = "{=BC_MCM_Group_Economy}Political Economy";
        private const string Notifications = "{=BC_MCM_Group_Notifications}Notifications";
        private const string Debugging = "{=BC_MCM_Group_Debugging}Debugging";

        private const int RebellionsGroupOrder = 100;
        private const int CourtPoliticsGroupOrder = 200;
        private const int WarGroupOrder = 300;
        private const int EconomyGroupOrder = 400;
        private const int WarAftermathGroupOrder = 500;
        private const int FeudalTitlesGroupOrder = 50;
        private const int SuccessionGroupOrder = 600;
        private const int ArmyLoyaltyGroupOrder = 700;
        private const int MarriageGroupOrder = 650;
        private const int MercenariesGroupOrder = 850;
        private const int RelationshipsGroupOrder = 900;
        private const int NotificationsGroupOrder = 1000;
        private const int DebuggingGroupOrder = 1100;

        public override string Id => "BellumCivileSettings_v1";
        public override string DisplayName => new TextObject("{=BC_MCM_DisplayName}Bellum Civile").ToString();
        public override string FolderName => "BellumCivile";
        public override string FormatType => "json2";

        [SettingPropertyDropdown("{=BC_MCM_TitleStylePreset}Title Terminology Preset", Order = 0, RequireRestart = true, HintText = "{=BC_MCM_TitleStylePreset_Hint}Selects the cultural rank and landed-title terminology loaded from bellum_title_styles_*.xml. The selected preset is applied after the main hierarchy file, while compatibility patches still apply last. Additional preset files are discovered when the game starts. Default: Anglicized.")]
        [SettingPropertyGroup(FeudalTitles, GroupOrder = FeudalTitlesGroupOrder)]
        public Dropdown<string> TitleStylePreset { get; set; } = FeudalTitleStylePresetCatalog.CreateDropdown();

        [SettingPropertyDropdown("{=BC_MCM_RealmNameDisplay}Realm Name Display", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_RealmNameDisplay_Hint}Native keeps the original realm names. Realm Identity combines the current sovereign rank with the realm's established name, using bellum_realm_names.xml for configured realms. Sovereign Title uses the current sovereign title's territorial name and rank. Cultural terminology applies to both Bellum modes. Temporary rebellion and feud names remain unchanged. Default: Sovereign Title.")]
        [SettingPropertyGroup(FeudalTitles, GroupOrder = FeudalTitlesGroupOrder)]
        public Dropdown<string> RealmNameDisplay { get; set; } = new Dropdown<string>(new[]
        {
            "{=BC_MCM_RealmNameNative}Native",
            "{=BC_MCM_RealmNameIdentity}Realm Identity",
            "{=BC_MCM_RealmNameSovereign}Sovereign Title"
        }, (int)RealmNameDisplayMode.SovereignTitle);

        // Retain the old API for bridges; only the selector is shown and serialized by MCM.
        public bool UseSovereignTitlesAsRealmNames
        {
            get => RealmNameDisplay?.SelectedIndex == (int)RealmNameDisplayMode.SovereignTitle;
            set => RealmNameDisplay.SelectedIndex = (int)(value
                ? RealmNameDisplayMode.SovereignTitle : RealmNameDisplayMode.RealmIdentity);
        }

        [SettingPropertyBool("{=BC_MCM_EnableWarPeaceLogicRevamp}Enable War & Peace Logic Revamp", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_EnableWarPeaceLogicRevamp_Hint}When enabled, Bellum uses its War Will, target-ranking, War Score, and peace-parley systems to control war and peace politics. Disable this to return war and peace proposals and vote support to vanilla Bannerlord. Default: On.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public bool EnableWarPeaceLogicRevamp { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_EnableWarScoreMapWidget}Enable Campaign Map War Score Widget", Order = 2, RequireRestart = false, HintText = "{=BC_MCM_EnableWarScoreMapWidget_Hint}Shows every active conflict involving the player's current realm at the top-right of the campaign map, using the opposing realm's banner and Bellum War Score. This includes foreign wars, civil wars, and claim feuds in which the player is a participant. Requires the War & Peace Logic Revamp. Default: On.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public bool EnableWarScoreMapWidget { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_EnableClientStateMapWidget}Enable Campaign Map Client State Widget", Order = 3, RequireRestart = false, HintText = "{=BC_MCM_EnableClientStateMapWidget_Hint}When the player rules a suzerain realm, shows its client kingdoms at the top-left of the campaign map with their current Liberty Desire. Requires the War & Peace Logic Revamp. Default: On.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public bool EnableClientStateMapWidget { get; set; } = true;

        [SettingPropertyInteger("{=BC_MCM_WarDurationReluctance}War Duration Reluctance Period", 1, 500, Order = 5, RequireRestart = false, HintText = "{=BC_MCM_WarDurationReluctance_Hint}Days over which the full -100 peace reluctance decays to 0. Enthusiasm below 10 softens this penalty. After this period, enthusiasm below 10 grants +30 peace support, and zero enthusiasm allows an exhausted internal opponent to consider conceding at more than 10 War Score. This setting never blocks peace proposals. Default: 100 days.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public int WarDurationReluctanceDays { get; set; } = C.WarPeaceDurationReluctanceDays;

        [SettingPropertyInteger("{=BC_MCM_MinimumPeaceBeforeRenewedWar}Minimum Peace Before Renewed War", 0, 500, Order = 6, RequireRestart = false, HintText = "{=BC_MCM_MinimumPeaceBeforeRenewedWar_Hint}Minimum number of days two realms must remain at peace before Bellum's AI will consider another offensive against the same realm. A value of 0 disables this waiting period. Client liberation wars remain exempt. Default: 20.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public int MinimumPeaceBeforeRenewedWarDays { get; set; } = C.WarPeaceRevampRecentPeaceBlockDays;

        [SettingPropertyInteger("{=BC_MCM_TreatyReparationsPerWarScore}Reparations per War Score", 0, 100000, Order = 10, RequireRestart = false, HintText = "{=BC_MCM_TreatyReparationsPerWarScore_Hint}Denars paid as immediate reparations for each War Score spent on the treaty term. A value of 0 disables reparations. Default: 5,000 denars per War Score.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public int TreatyReparationsGoldPerWarScore { get; set; } = C.TreatyReparationsGoldPerWarScore;

        [SettingPropertyInteger("{=BC_MCM_TreatyTributePerWarScore}Daily Tribute per War Score", 0, 100000, Order = 20, RequireRestart = false, HintText = "{=BC_MCM_TreatyTributePerWarScore_Hint}Denars paid each day for 100 days for every War Score spent on the treaty term. A value of 0 disables tribute. Default: 250 denars per day per War Score.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public int TreatyDailyTributePerWarScore { get; set; } = C.TreatyDailyTributePerWarScore;

        [SettingPropertyFloatingInteger("{=BC_MCM_RetainedPrisonerDungeonEscape}Retained Prisoner Dungeon Escape Chance", 0f, 25f, "0.00", Order = 30, RequireRestart = false, HintText = "{=BC_MCM_RetainedPrisonerDungeonEscape_Hint}Base daily escape chance, as a percentage, for nobles retained in a settlement dungeon after a Bellum peace treaty. Governor and captor perks still apply. Default: 1%.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public float RetainedPrisonerDungeonEscapeChancePercent { get; set; } = C.RetainedPrisonerDungeonEscapeChancePercent;

        [SettingPropertyFloatingInteger("{=BC_MCM_RetainedPrisonerMobileEscape}Retained Prisoner Mobile Escape Chance", 0f, 25f, "0.00", Order = 40, RequireRestart = false, HintText = "{=BC_MCM_RetainedPrisonerMobileEscape_Hint}Base daily escape chance, as a percentage, for nobles retained in a mobile party after a Bellum peace treaty. Prisoner and captor perks still apply. Default: 3%.")]
        [SettingPropertyGroup(War, GroupOrder = WarGroupOrder)]
        public float RetainedPrisonerMobileEscapeChancePercent { get; set; } = C.RetainedPrisonerMobileEscapeChancePercent;

        [SettingPropertyBool("{=BC_MCM_EnableNpcSubterfuge}Enable NPC Subterfuge Missions", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_EnableNpcSubterfuge_Hint}When enabled, AI rulers can spend gold on covert subterfuge against neighboring realms and active rebellions. Player subterfuge missions remain available either way. Default: On.")]
        [SettingPropertyGroup(CourtPolitics, GroupOrder = CourtPoliticsGroupOrder)]
        public bool EnableNpcSubterfugeMissions { get; set; } = true;

        [SettingPropertyInteger("{=BC_MCM_RebellionThreshold}Rebellious Intent Threshold", 50, 1000, Order = 30, RequireRestart = false, HintText = "{=BC_MCM_RebellionThreshold_Hint}The rebellious intent a clan needs to form or join a rebel faction. Leaders disband and members reconsider their commitment below half this value. Higher values make conspiracies slower and rarer. Default: 100.")]
        [SettingPropertyGroup(Rebellions, GroupOrder = RebellionsGroupOrder)]
        public int RebellionThreshold { get; set; } = (int)C.RebelliousIntentThreshold;

        [SettingPropertyFloatingInteger("{=BC_MCM_CourtTerm}Court Term Length (Years)", 0.25f, 4f, "0.00", Order = 20, RequireRestart = false, HintText = "{=BC_MCM_CourtTerm_Hint}Time between public court agendas. Changes apply to the next term, not announced sessions or existing policy reconsideration dates. Default: 1 year.")]
        [SettingPropertyGroup(CourtPolitics, GroupOrder = CourtPoliticsGroupOrder)]
        public float CourtTermYears { get; set; } = 1f;



        [SettingPropertyInteger("{=BC_MCM_DeliberationDays}Political Deliberation Days", 0, 30, Order = 40, RequireRestart = false, HintText = "{=BC_MCM_DeliberationDays_Hint}Days before delayed policy, fief, and expulsion votes fire. One shared value keeps major votes paced consistently. Default: 5.")]
        [SettingPropertyGroup(CourtPolitics, GroupOrder = CourtPoliticsGroupOrder)]
        public int PoliticalDeliberationDays { get; set; } = C.PolicyDeliberationDays;

        [SettingPropertyFloatingInteger("{=BC_MCM_BribeMultiplier}Political Bribe Cost Multiplier", 0f, 5f, "0.00", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_BribeMultiplier_Hint}Multiplier applied to Bellum Civile policy, fief, and expulsion vote bribe costs. Default: 1.")]
        [SettingPropertyGroup(Economy, GroupOrder = EconomyGroupOrder)]
        public float PoliticalBribeCostMultiplier { get; set; } = 1f;

        [SettingPropertyFloatingInteger("{=BC_MCM_PostWarExecution}Post-War Execution Chance", 0f, 1f, "#0%", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_PostWarExecution_Hint}Base chance that a defeated clan leader is executed during post-war tribunal logic. Default: 10%. If execution fails, confiscation is checked next; if both fail, the clan is pardoned.")]
        [SettingPropertyGroup(WarAftermath, GroupOrder = WarAftermathGroupOrder)]
        public float PostWarExecutionBase { get; set; } = C.PostWarExecutionBase;

        [SettingPropertyFloatingInteger("{=BC_MCM_PostWarConfiscation}Post-War Confiscation Chance", 0f, 1f, "#0%", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_PostWarConfiscation_Hint}Base chance that a defeated clan has one fief confiscated if execution does not fire. Default: 20%. If execution and confiscation both fail, the clan is pardoned.")]
        [SettingPropertyGroup(WarAftermath, GroupOrder = WarAftermathGroupOrder)]
        public float PostWarConfiscationBase { get; set; } = C.PostWarConfiscationBase;

        [SettingPropertyBool("{=BC_MCM_EnableAutomaticTreasonIndictments}Enable Indictments of Treason", Order = 20, RequireRestart = false, HintText = "{=BC_MCM_TermTreason_Hint}Enables new automatic Crown treason agendas from -60 relations and scheduled royal decree cases at -100. Turning this off does not cancel existing agendas or deferred decree cases, or change their saved dates. Existing proceedings remain subject to normal validity checks. Manual player nominations remain available. Default: On.")]
        [SettingPropertyGroup(WarAftermath, GroupOrder = WarAftermathGroupOrder)]
        public bool EnableAutomaticTreasonIndictments { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_EnablePartitionSuccession}Enable Partition Succession", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_EnablePartitionSuccession_Hint}When enabled, large noble clans, including the player clan, can split spare inherited fiefs into cadet branches after clan succession.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public bool EnablePartitionSuccession { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_EnforcePlayerSuccessionLaw}Respect Succession Law When Choosing Heir", Order = 5, RequireRestart = false, HintText = "{=BC_MCM_EnforcePlayerSuccessionLaw_Hint}When enabled, the playable heir selected after your character's death must follow your house's current gender and succession laws. Disable this to retain vanilla free heir selection without changing succession elsewhere. Default: On.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public bool EnforcePlayerSuccessionLaw { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_EnableCustomAdulthoodAge}Use Custom Adulthood Age", IsToggle = true, Order = 0, RequireRestart = true, HintText = "{=BC_MCM_CustomEducationToggle_Hint}Customize adulthood and the six childhood education ages. When disabled, vanilla adulthood and education ages are restored. Requires a restart. Default: On.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public bool EnableCustomAdulthoodAge { get; set; } = true;

        [SettingPropertyInteger("{=BC_MCM_AdulthoodAge}Adulthood Age", 16, 21, Order = 8, RequireRestart = true, HintText = "{=BC_MCM_AdulthoodAge_Hint}Global age at which heroes become adults, can leave regency, lead clans and parties, and become eligible for marriage. Used only when custom adulthood is enabled. Default: 16.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int AdulthoodAge { get; set; } = 16;

        [SettingPropertyInteger("{=BC_MCM_EducationAge1}Education Milestone 1 Age", 1, 15, Order = 10, RequireRestart = true, HintText = "{=BC_MCM_EducationAge1_Hint}Age for childhood education stage 1. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 2.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge1 { get => GetEducationAge(0); set => _educationAge1 = value; }
        private int _educationAge1 = 2;

        [SettingPropertyInteger("{=BC_MCM_EducationAge2}Education Milestone 2 Age", 2, 16, Order = 11, RequireRestart = true, HintText = "{=BC_MCM_EducationAge2_Hint}Age for childhood education stage 2. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 5.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge2 { get => GetEducationAge(1); set => _educationAge2 = value; }
        private int _educationAge2 = 5;

        [SettingPropertyInteger("{=BC_MCM_EducationAge3}Education Milestone 3 Age", 3, 17, Order = 12, RequireRestart = true, HintText = "{=BC_MCM_EducationAge3_Hint}Age for childhood education stage 3. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 8.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge3 { get => GetEducationAge(2); set => _educationAge3 = value; }
        private int _educationAge3 = 8;

        [SettingPropertyInteger("{=BC_MCM_EducationAge4}Education Milestone 4 Age", 4, 18, Order = 13, RequireRestart = true, HintText = "{=BC_MCM_EducationAge4_Hint}Age for childhood education stage 4. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 10.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge4 { get => GetEducationAge(3); set => _educationAge4 = value; }
        private int _educationAge4 = 10;

        [SettingPropertyInteger("{=BC_MCM_EducationAge5}Education Milestone 5 Age", 5, 19, Order = 14, RequireRestart = true, HintText = "{=BC_MCM_EducationAge5_Hint}Age for childhood education stage 5. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 13.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge5 { get => GetEducationAge(4); set => _educationAge5 = value; }
        private int _educationAge5 = 13;

        [SettingPropertyInteger("{=BC_MCM_EducationAge6}Education Milestone 6 Age", 6, 20, Order = 15, RequireRestart = true, HintText = "{=BC_MCM_EducationAge6_Hint}Age for childhood education stage 6. Ages are kept in increasing order and below adulthood; conflicting values are adjusted automatically. Choices and rewards remain unchanged. Requires a restart. Default: 15.")]
        [SettingPropertyGroup(Childhood, GroupOrder = 7)]
        public int EducationAge6 { get => GetEducationAge(5); set => _educationAge6 = value; }
        private int _educationAge6 = 15;

        internal int GetEducationAge(int stage) => EducationAgeSchedule.Normalize(AdulthoodAge,
            _educationAge1, _educationAge2, _educationAge3, _educationAge4, _educationAge5, _educationAge6)[stage];

        public bool EnableNpcPartitionSuccession
        {
            get => EnablePartitionSuccession;
            set => EnablePartitionSuccession = value;
        }

        [SettingPropertyInteger("{=BC_MCM_PartitionMainHeirFiefs}Main Heir Reserved Personal Holdings", 1, 10, Order = 10, RequireRestart = false, HintText = "{=BC_MCM_PartitionMainHeirFiefs_Hint}Number of personal barony-level holdings the main heir keeps after the primary title chain is preserved. Spare title packages and fiefs can split into cadet branches. Default: 1.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public int PartitionSuccessionMainHeirReservedFiefs { get; set; } = C.PartitionSuccessionMainHeirReservedFiefs;

        [SettingPropertyInteger("{=BC_MCM_FeudalTitleMinimumChildren}Minimum Titles to Form Higher Title", 2, 5, Order = 20, RequireRestart = false, HintText = "{=BC_MCM_FeudalTitleMinimumChildren_Hint}Minimum number of contiguous same-rank titles needed to form a higher title. Lower values create denser title hierarchies that better fit Calradia's map density. Default: 2.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public int FeudalTitleMinimumChildren { get; set; } = C.FeudalTitleMinimumChildren;

        [SettingPropertyInteger("{=BC_MCM_FeudalClaimFabricationYears}Claim Fabrication Baseline Years", 1, 10, Order = 30, RequireRestart = false, HintText = "{=BC_MCM_FeudalClaimFabricationYears_Hint}Baseline number of in-game years required to fabricate a claim before the fabricator's Stewardship, Roguery, and personality traits modify progress. Default: 3 years.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public int FeudalClaimFabricationBaseYears { get; set; } = (int)C.FeudalClaimFabricationBaseYears;

        [SettingPropertyFloatingInteger("{=BC_MCM_FeudalClaimFabricationDiscovery}Fabricated Claim Discovery Chance", 0f, 1f, "#0%", Order = 40, RequireRestart = false, HintText = "{=BC_MCM_FeudalClaimFabricationDiscovery_Hint}Base risk at each milestone before skills and council modifiers. Early failures cause recoverable setbacks; only the final failure exposes the forgery. Default: 10%.")]
        [SettingPropertyGroup(Succession, GroupOrder = SuccessionGroupOrder)]
        public float FeudalClaimFabricationBaseDiscoveryChance { get; set; } = C.FeudalClaimFabricationBaseDiscoveryChance;

        [SettingPropertyBool("{=BC_MCM_EnableArmyRefusal}Enable Rebellious Army Refusal", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_EnableArmyRefusal_Hint}When enabled, openly rebellious court factions and vassals with very poor relations refuse calls to arms.")]
        [SettingPropertyGroup(ArmyLoyalty, GroupOrder = ArmyLoyaltyGroupOrder)]
        public bool EnableRebelliousArmyRefusal { get; set; } = true;

        [SettingPropertyFloatingInteger("{=BC_MCM_ArmyRefusalMood}Army Refusal Threshold", -100f, 0f, "0", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_ArmyRefusalMood_Hint}Court faction mood or vassal relation at or below this value refuses calls to arms. Default: -60.")]
        [SettingPropertyGroup(ArmyLoyalty, GroupOrder = ArmyLoyaltyGroupOrder)]
        public float ArmyRefusalMoodThreshold { get; set; } = C.ArmyMoodFurious;

        [SettingPropertyBool("{=BC_MCM_EnableFeudalArmySummons}Restrict Army Calls to Vassals", Order = 20, RequireRestart = false, HintText = "{=BC_MCM_EnableFeudalArmySummons_Hint}When enabled, non-ruler nobles can only summon clans within their de facto title authority. Rulers and temporary Bellum war leaders can still summon their full side. Disable this to keep vanilla-style army pooling.")]
        [SettingPropertyGroup(ArmyLoyalty, GroupOrder = ArmyLoyaltyGroupOrder)]
        public bool EnableFeudalArmySummons { get; set; } = true;

        [SettingPropertyInteger("{=BC_MCM_ArmyPersonalFriendRelation}Personal Army Favor Relation", 0, 100, Order = 30, RequireRestart = false, HintText = "{=BC_MCM_ArmyPersonalFriendRelation_Hint}Minimum relation at which a lord outside your de facto authority may answer your call for friendship's sake, either through the army menu or in person. Default: 60.")]
        [SettingPropertyGroup(ArmyLoyalty, GroupOrder = ArmyLoyaltyGroupOrder)]
        public int ArmyPersonalFriendRelationThreshold { get; set; } = C.ArmyPersonalFriendRelationThreshold;

        [SettingPropertyBool("{=BC_MCM_EnableBellumMarriage}Enable Strategic Marriage Logic", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_EnableBellumMarriage_Hint}When enabled, Bellum adds strategic NPC marriage logic for dynastic survival, royal pacification, and foreign royal alliances. Disable this if you want vanilla or another mod to fully control NPC marriages.")]
        [SettingPropertyGroup(Marriage, GroupOrder = MarriageGroupOrder)]
        public bool EnableBellumStrategicMarriageLogic { get; set; } = true;

        [SettingPropertyBool("{=BC_MCM_BellumOnlyMarriage}Block Vanilla Dice-Roll Marriage Logic", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_BellumOnlyMarriage_Hint}When enabled, Bellum blocks vanilla NPC-to-NPC marriage rolls so only Bellum's strategic marriage logic handles NPC marriages. Useful for testing and for players who want marriage to follow Bellum's political and dynastic scoring.")]
        [SettingPropertyGroup(Marriage, GroupOrder = MarriageGroupOrder)]
        public bool UseBellumStrategicNpcMarriagesOnly { get; set; } = true;

        [SettingPropertyInteger("{=BC_MCM_MarriageMaleMinAge}Male Minimum Marriage Age", 16, 80, Order = 20, RequireRestart = false, HintText = "{=BC_MCM_MarriageMaleMinAge_Hint}Minimum age for male nobles to be considered by Bellum's strategic NPC marriage logic. This cannot fall below the active adulthood age. Default: 25.")]
        [SettingPropertyGroup(Marriage, GroupOrder = MarriageGroupOrder)]
        public int MarriageMaleMinimumAge { get; set; } = C.MarriageMaleMinimumAge;

        [SettingPropertyInteger("{=BC_MCM_MarriageFemaleMinAge}Female Minimum Marriage Age", 16, 80, Order = 30, RequireRestart = false, HintText = "{=BC_MCM_MarriageFemaleMinAge_Hint}Minimum age for female nobles to be considered by Bellum's strategic NPC marriage logic. This cannot fall below the active adulthood age. Default: 25.")]
        [SettingPropertyGroup(Marriage, GroupOrder = MarriageGroupOrder)]
        public int MarriageFemaleMinimumAge { get; set; } = C.MarriageFemaleMinimumAge;

        [SettingPropertyInteger("{=BC_MCM_MarriageFemaleMaxAge}Female Maximum Marriage Age", 16, 80, Order = 40, RequireRestart = false, HintText = "{=BC_MCM_MarriageFemaleMaxAge_Hint}Maximum age for female nobles to be considered by Bellum's strategic NPC marriage logic. Default: 41.")]
        [SettingPropertyGroup(Marriage, GroupOrder = MarriageGroupOrder)]
        public int MarriageFemaleMaximumAge { get; set; } = C.MarriageFemaleMaximumAge;

        [SettingPropertyInteger("{=BC_MCM_DynamicMercenaryCompanyLimit}Maximum Dynamic Mercenary Companies", 0, 100, Order = 0, RequireRestart = false, HintText = "{=BC_MCM_DynamicMercenaryCompanyLimit_Hint}Maximum number of Bellum-created mercenary companies that may be active at once. A value of 0 disables new mercenary departures and formations. Existing companies are not destroyed if the limit is reduced. Default: 10.")]
        [SettingPropertyGroup(Mercenaries, GroupOrder = MercenariesGroupOrder)]
        public int DynamicMercenaryCompanyLimit { get; set; } = C.DynamicMercenaryCompanyLimit;

        [SettingPropertyInteger("{=BC_MCM_DynamicMercenaryEvaluationYears}Noble Evaluation Interval", 1, 20, Order = 10, RequireRestart = false, HintText = "{=BC_MCM_DynamicMercenaryEvaluationYears_Hint}Years between evaluations of each noble house for potential mercenary departures. Evaluations are distributed across the interval to avoid a single large campaign pulse. Default: 5 years.")]
        [SettingPropertyGroup(Mercenaries, GroupOrder = MercenariesGroupOrder)]
        public int DynamicMercenaryEvaluationIntervalYears { get; set; } = C.DynamicMercenaryEvaluationIntervalYears;

        [SettingPropertyInteger("{=BC_MCM_DynamicMercenaryMaximumOfficers}Maximum Company Nobles", 1, 5, Order = 20, RequireRestart = false, HintText = "{=BC_MCM_DynamicMercenaryMaximumOfficers_Hint}Maximum number of adult runaway nobles accepted by each dynamic company. Dependants are not counted. Default: 3.")]
        [SettingPropertyGroup(Mercenaries, GroupOrder = MercenariesGroupOrder)]
        public int DynamicMercenaryMaximumOfficers { get; set; } = C.DynamicMercenaryMaximumOfficers;

        [SettingPropertyInteger("{=BC_MCM_DynamicMercenaryStartingTier}Starting Company Tier", 1, 4, Order = 30, RequireRestart = false, HintText = "{=BC_MCM_DynamicMercenaryStartingTier_Hint}Clan tier assigned to a newly founded dynamic mercenary company. Lower tiers limit its initial number of parties and campaign cost. Default: 2.")]
        [SettingPropertyGroup(Mercenaries, GroupOrder = MercenariesGroupOrder)]
        public int DynamicMercenaryStartingTier { get; set; } = C.DynamicMercenaryStartingTier;

        [SettingPropertyBool("{=BC_MCM_EnableDynamicRelationDrift}Enable Relationship Memory", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_EnableDynamicRelationDrift_Hint}When enabled, noble relations combine a natural political baseline with lasting, timed memories of favors and grievances. Hero encyclopedia pages show the full breakdown.")]
        [SettingPropertyGroup(Relationships, GroupOrder = RelationshipsGroupOrder)]
        public bool EnableDynamicRelationDrift { get; set; } = true;

        [SettingPropertyFloatingInteger("{=BC_MCM_MemoryDuration}Memory Duration Multiplier", 0.25f, 5f, "0.00'x'", Order = 10, RequireRestart = false, HintText = "{=BC_MCM_MemoryDuration_Hint}Scales the duration of positive and negative timed memories. Changes proportionally adjust the remaining time of active memories. Permanent memories and ongoing political conditions are unaffected. Default: 1x.")]
        [SettingPropertyGroup(Relationships, GroupOrder = RelationshipsGroupOrder)]
        public float RelationMemoryDurationMultiplier { get; set; } = 1f;

        // Retained without an MCM attribute so old campaigns can migrate their configured drift speed exactly.
        public float DynamicRelationWeeklyDrift { get; set; } = C.DynamicRelationWeeklyDrift;

        [SettingPropertyDropdown("{=BC_MCM_Notifications}Bellum Civile Notifications", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_Notifications_Hint}Disabled: hides optional Bellum Civile chat messages. Kingdom Only: shows political events involving your kingdom, rebel side, or clan. Global: shows Bellum Civile political events from all kingdoms.")]
        [SettingPropertyGroup(Notifications, GroupOrder = NotificationsGroupOrder)]
        public Dropdown<string> BellumCivileNotifications { get; set; } = new Dropdown<string>(new string[]
        {
            "{=BC_MCM_NotifDisabled}Disabled",
            "{=BC_MCM_NotifKingdomOnly}Kingdom Only",
            "{=BC_MCM_NotifGlobal}Global"
        }, selectedIndex: 1);

        [SettingPropertyBool("{=BC_MCM_ShowDebug}Show Debug Messages In Game", Order = 0, RequireRestart = false, HintText = "{=BC_MCM_ShowDebug_Hint}Shows Bellum Civile debug traces in the in-game message feed when those traces request visible output.")]
        [SettingPropertyGroup(Debugging, GroupOrder = DebuggingGroupOrder)]
        public bool ShowDebugMessagesInGame { get; set; } = false;
    }
}
