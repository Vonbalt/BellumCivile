using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using Helpers;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To introduce a subterfuge system where rulers can use their wealth to destabilize rivals or fund active rebellions. It handles the UI dialogue hooks, AI evaluation, and the math for discovery/execution.
    /// </summary>
    public class ProxyWarBehavior : CampaignBehaviorBase
    {
        internal const string HighwaymenPartyIdPrefix = "bellum_highwaymen_";

        private const int COST_FUND_HIGHWAYMEN = 50000;
        private const int COST_INCITE_REVOLT = 80000;
        private const int COST_COURT_SCANDAL = 120000;
        private const int COST_SMEAR_CAMPAIGN = 150000;
        private const int COST_FUND_DISSENT = 250000;

        private const int COST_FOREIGN_ADVISORS = 60000;
        private const int COST_WAR_CHEST = 100000;
        private const int COST_BUY_OUT_CONTRACTS = 150000;
        private const int COST_EXPEDITIONARY_MERCENARIES = 180000;
        private const int COST_ORCHESTRATE_DEFECTION = 300000;

        private const int ACTION_FUND_HIGHWAYMEN = 0;
        private const int ACTION_INCITE_REVOLT = 1;
        private const int ACTION_COURT_SCANDAL = 2;
        private const int ACTION_SMEAR_CAMPAIGN = 3;
        private const int ACTION_FUND_DISSENT = 4;

        private const int ACTION_FOREIGN_ADVISORS = 5;
        private const int ACTION_WAR_CHEST = 6;
        private const int ACTION_BUY_OUT_CONTRACTS = 7;
        private const int ACTION_EXPEDITIONARY_MERCENARIES = 8;
        private const int ACTION_ORCHESTRATE_DEFECTION = 9;

        private const int ACTION_NO_SUBTERFUGE = -1;

        private const int AI_MIN_RESERVE_GOLD = 100000;

        private const int WAR_CHEST_TRANSFER_AMOUNT = 75000;

        private const int REBEL_ASSIST_RELATION_BONUS = 10;

        private sealed class EspionageOutcome
        {
            public bool AbortMission;
            public TextObject AbortReason = new TextObject("");
            public Hero SpecificTarget;
            public Kingdom VictimKingdom;
            public Settlement AffectedSettlement;
            public FactionObject AffectedFaction;
            public Clan AffectedClan;
        }

        private const int   DISCOVERY_RELATION_PENALTY_T1     = -25;   
        private const float DISCOVERY_INFLUENCE_REWARD_T1      = 50f;
        private const int   DISCOVERY_RELATION_PENALTY_T2     = -50;   
        private const float DISCOVERY_INFLUENCE_REWARD_T2      = 100f;
        private const int   DISCOVERY_RELATION_PENALTY_T3     = -75;   
        private const float DISCOVERY_INFLUENCE_REWARD_T3      = 150f;
        private const int   DISCOVERY_SPECIFIC_TARGET_BONUS   = 10;

        private const float DISCOVERY_BASE_CHANCE_T1      = 0.20f;  
        private const float DISCOVERY_BASE_CHANCE_T2      = 0.25f;  
        private const float DISCOVERY_BASE_CHANCE_T3      = 0.30f;  
        private const float DISCOVERY_SKILL_MODIFIER      = 0.002f;
        private const float DISCOVERY_MIN_CHANCE          = 0.05f;
        private const float DISCOVERY_MAX_CHANCE          = 0.80f;
        private const float DISCOVERY_CALCULATING_BONUS   = 30f;  
        private const float DISCOVERY_IMPULSIVE_PENALTY   = 20f;  
        private const float AGENT_CALCULATING_BONUS       = 20f;  
        private const float AGENT_RECKLESS_PENALTY        = 15f;  

        private const float EXECUTION_BASE_CHANCE         = 0.25f;
        private const float EXECUTION_MIN_CHANCE          = 0.01f;
        private const float EXECUTION_ESCAPE_DENOMINATOR  = 1250f;
        private const float EXECUTION_ROGUERY_WEIGHT      = 0.6f;
        private const float EXECUTION_ATHLETICS_WEIGHT    = 0.4f;

        private const float AI_ACT_BASE_CHANCE           = 0.35f;
        private const float AI_ACT_CALC_BONUS            = 0.20f;
        private const float AI_ACT_IMPULSIVE_PENALTY     = 0.10f;
        private const float AI_ACT_HONOR_PENALTY         = 0.20f;
        private const float AI_ACT_DISHONORABLE_BONUS    = 0.20f;
        private const float AI_ACT_MERCY_PENALTY         = 0.10f;
        private const float AI_ACT_CRUEL_BONUS           = 0.05f;
        private const float AI_ACT_AT_WAR_BONUS          = 0.20f;
        private const float AI_ACT_LOW_REL_BONUS         = 0.10f;
        private const float AI_ACT_VERY_LOW_REL_BONUS    = 0.15f;
        private const float AI_ACT_MIN_CHANCE            = 0.05f;
        private const float AI_ACT_MAX_CHANCE            = 0.90f;
        private const float AI_NEIGHBOR_DISTANCE_MULTIPLIER = 3f;

        private const int COOLDOWN_FUND_HIGHWAYMEN         = 14;
        private const int COOLDOWN_INCITE_REVOLT           = 21;
        private const int COOLDOWN_COURT_SCANDAL           = 30;
        private const int COOLDOWN_SMEAR_CAMPAIGN          = 30;
        private const int COOLDOWN_FUND_DISSENT            = 45;
        private const int COOLDOWN_FOREIGN_ADVISORS        = 14;
        private const int COOLDOWN_WAR_CHEST               = 21;
        private const int COOLDOWN_BUY_OUT_CONTRACTS       = 28;
        private const int COOLDOWN_EXPEDITIONARY_MERCS     = 28;
        private const int COOLDOWN_ORCHESTRATE_DEFECTION   = 60;

        private const int WEIGHT_FUND_HIGHWAYMEN           = 40;
        private const int WEIGHT_INCITE_REVOLT             = 30;
        private const int WEIGHT_COURT_SCANDAL             = 20;
        private const int WEIGHT_SMEAR_CAMPAIGN            = 15;
        private const int WEIGHT_FUND_DISSENT              = 10;
        private const int WEIGHT_FOREIGN_ADVISORS          = 35;
        private const int WEIGHT_WAR_CHEST                 = 30;
        private const int WEIGHT_BUY_OUT_CONTRACTS         = 20;
        private const int WEIGHT_EXPEDITIONARY_MERCS       = 15;
        private const int WEIGHT_ORCHESTRATE_DEFECTION     = 10;

        private const float STUCK_PARTY_THRESHOLD_SQ = 16f;

        private static readonly Dictionary<string, string> CultureToBanditClan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "sturgia",  "sea_raiders"      },
            { "nord",     "sea_raiders"      },
            { "battania", "forest_bandits"   },
            { "vlandia",  "mountain_bandits" },
            { "empire",   "mountain_bandits" },
            { "khuzait",  "steppe_bandits"   },
            { "aserai",   "desert_bandits"   },
        };

        private Dictionary<string, CampaignTime> _aiCooldowns = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _courtScandalTargetCooldowns = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _nextAiEvaluationDates = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _activeAgitationTimers = new Dictionary<string, CampaignTime>();

        private Dictionary<string, CampaignTime> _playerMissionReturnDates = new Dictionary<string, CampaignTime>();
        private Dictionary<string, string> _playerMissionTargets = new Dictionary<string, string>();
        private Dictionary<string, int> _playerMissionActions = new Dictionary<string, int>();

        private Hero _activeCompanion;
        private Kingdom _selectedTarget;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Dictionary<Kingdom, CampaignTime> legacyAiCooldowns = new Dictionary<Kingdom, CampaignTime>();
            Dictionary<Clan, CampaignTime> legacyAgitationTimers = new Dictionary<Clan, CampaignTime>();

            dataStore.SyncData("BellumCivile_ProxyCooldowns", ref legacyAiCooldowns);
            dataStore.SyncData("BellumCivile_AgitationTimers", ref legacyAgitationTimers);
            dataStore.SyncData("BellumCivile_ProxyCooldownsById", ref _aiCooldowns);
            dataStore.SyncData("BellumCivile_CourtScandalTargetCooldowns", ref _courtScandalTargetCooldowns);
            dataStore.SyncData("BellumCivile_ProxyNextAiEvaluationDates", ref _nextAiEvaluationDates);
            dataStore.SyncData("BellumCivile_AgitationTimersById", ref _activeAgitationTimers);
            dataStore.SyncData("BellumCivile_PlayerMissionDates", ref _playerMissionReturnDates);
            dataStore.SyncData("BellumCivile_PlayerMissionTargets", ref _playerMissionTargets);
            dataStore.SyncData("BellumCivile_PlayerMissionActions", ref _playerMissionActions);
            EnsureCollectionsInitialized();

            if (legacyAiCooldowns != null)
            {
                foreach (KeyValuePair<Kingdom, CampaignTime> entry in legacyAiCooldowns)
                {
                    if (entry.Key == null || string.IsNullOrEmpty(entry.Key.StringId)) continue;
                    _aiCooldowns[entry.Key.StringId] = entry.Value;
                }
            }

            if (legacyAgitationTimers != null)
            {
                foreach (KeyValuePair<Clan, CampaignTime> entry in legacyAgitationTimers)
                {
                    if (entry.Key == null || string.IsNullOrEmpty(entry.Key.StringId)) continue;
                    _activeAgitationTimers[entry.Key.StringId] = entry.Value;
                }
            }
        }

        private void EnsureCollectionsInitialized()
        {
            if (_aiCooldowns == null) _aiCooldowns = new Dictionary<string, CampaignTime>();
            if (_courtScandalTargetCooldowns == null) _courtScandalTargetCooldowns = new Dictionary<string, CampaignTime>();
            if (_nextAiEvaluationDates == null) _nextAiEvaluationDates = new Dictionary<string, CampaignTime>();
            if (_activeAgitationTimers == null) _activeAgitationTimers = new Dictionary<string, CampaignTime>();
            if (_playerMissionReturnDates == null) _playerMissionReturnDates = new Dictionary<string, CampaignTime>();
            if (_playerMissionTargets == null) _playerMissionTargets = new Dictionary<string, string>();
            if (_playerMissionActions == null) _playerMissionActions = new Dictionary<string, int>();
        }

        public bool HasActiveAgitation(Clan clan)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return false;
            if (!_activeAgitationTimers.TryGetValue(clan.StringId, out CampaignTime expiry)) return false;
            if (!expiry.IsPast) return true;

            _activeAgitationTimers.Remove(clan.StringId);
            return false;
        }

        private void PruneSavedState()
        {
            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            HashSet<string> liveKingdomIds = new HashSet<string>(
                Kingdom.All
                    .Where(k => k != null && !k.IsEliminated && !string.IsNullOrEmpty(k.StringId))
                    .Select(k => k.StringId));

            foreach (string kingdomId in _aiCooldowns.Keys.ToList())
            {
                if (_aiCooldowns[kingdomId].IsPast)
                {
                    _aiCooldowns.Remove(kingdomId);
                    continue;
                }

                if (liveKingdomIds.Contains(kingdomId)) continue;
                if (resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdomId)) continue;
                _aiCooldowns.Remove(kingdomId);
            }

            foreach (string kingdomId in _nextAiEvaluationDates.Keys.ToList())
            {
                if (liveKingdomIds.Contains(kingdomId)) continue;
                if (resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdomId)) continue;
                _nextAiEvaluationDates.Remove(kingdomId);
            }

            foreach (string kingdomId in _courtScandalTargetCooldowns.Keys.ToList())
            {
                if (_courtScandalTargetCooldowns[kingdomId].IsPast)
                {
                    _courtScandalTargetCooldowns.Remove(kingdomId);
                    continue;
                }

                if (liveKingdomIds.Contains(kingdomId)) continue;
                if (resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdomId)) continue;
                _courtScandalTargetCooldowns.Remove(kingdomId);
            }

            foreach (string clanId in _activeAgitationTimers.Keys.ToList())
            {
                if (_activeAgitationTimers[clanId].IsPast)
                {
                    _activeAgitationTimers.Remove(clanId);
                    continue;
                }

                Clan clan = Clan.All.FirstOrDefault(c => c.StringId == clanId);
                if (clan != null && !clan.IsEliminated) continue;
                if (resolutionBehavior != null && resolutionBehavior.IsClanIdReferencedByBellumCivileState(clanId)) continue;
                _activeAgitationTimers.Remove(clanId);
            }
        }

        private int GetCostForAction(int actionType)
        {
            switch (actionType)
            {
                case ACTION_FUND_HIGHWAYMEN: return COST_FUND_HIGHWAYMEN;
                case ACTION_INCITE_REVOLT: return COST_INCITE_REVOLT;
                case ACTION_COURT_SCANDAL: return COST_COURT_SCANDAL;
                case ACTION_SMEAR_CAMPAIGN: return COST_SMEAR_CAMPAIGN;
                case ACTION_FUND_DISSENT: return COST_FUND_DISSENT;
                case ACTION_FOREIGN_ADVISORS: return COST_FOREIGN_ADVISORS;
                case ACTION_WAR_CHEST: return COST_WAR_CHEST;
                case ACTION_BUY_OUT_CONTRACTS: return COST_BUY_OUT_CONTRACTS;
                case ACTION_EXPEDITIONARY_MERCENARIES: return COST_EXPEDITIONARY_MERCENARIES;
                case ACTION_ORCHESTRATE_DEFECTION: return COST_ORCHESTRATE_DEFECTION;
                default: return 0;
            }
        }

        private static int GetCooldownForAction(int actionType)
        {
            switch (actionType)
            {
                case ACTION_FUND_HIGHWAYMEN:            return COOLDOWN_FUND_HIGHWAYMEN;
                case ACTION_INCITE_REVOLT:              return COOLDOWN_INCITE_REVOLT;
                case ACTION_COURT_SCANDAL:              return COOLDOWN_COURT_SCANDAL;
                case ACTION_SMEAR_CAMPAIGN:             return COOLDOWN_SMEAR_CAMPAIGN;
                case ACTION_FUND_DISSENT:               return COOLDOWN_FUND_DISSENT;
                case ACTION_FOREIGN_ADVISORS:           return COOLDOWN_FOREIGN_ADVISORS;
                case ACTION_WAR_CHEST:                  return COOLDOWN_WAR_CHEST;
                case ACTION_BUY_OUT_CONTRACTS:          return COOLDOWN_BUY_OUT_CONTRACTS;
                case ACTION_EXPEDITIONARY_MERCENARIES:  return COOLDOWN_EXPEDITIONARY_MERCS;
                case ACTION_ORCHESTRATE_DEFECTION:      return COOLDOWN_ORCHESTRATE_DEFECTION;
                default:                                return 14;
            }
        }

        private bool IsActionValid(int actionType, Kingdom targetKingdom, FactionObject activeRebellion)
        {
            Kingdom parentKingdom = activeRebellion?.ParentKingdom;

            switch (actionType)
            {
                case ACTION_FUND_HIGHWAYMEN:
                    return targetKingdom.Settlements.Any(s => s.IsTown)
                        && GetCultureAppropriateBanditClan(targetKingdom) != null;

                case ACTION_INCITE_REVOLT:
                    return targetKingdom.Fiefs.Any(f => f.IsTown);

                case ACTION_COURT_SCANDAL:
                    return targetKingdom?.RulingClan != null
                        && targetKingdom.RulingClan.Influence >= C.NpcCourtScandalMinimumTargetInfluence
                        && (!(_courtScandalTargetCooldowns.TryGetValue(targetKingdom.StringId, out CampaignTime scandalCooldown))
                            || scandalCooldown.IsPast);

                case ACTION_SMEAR_CAMPAIGN:
                {
                    var fm = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                    return fm != null && fm.GetFactionsInKingdom(targetKingdom).Any(f => f.IsIdeology);
                }

                case ACTION_FUND_DISSENT:
                    return targetKingdom.Clans.Any(c =>
                        c != targetKingdom.RulingClan && !c.IsEliminated &&
                        !c.IsUnderMercenaryService && !c.IsMinorFaction && c.Leader != null);

                case ACTION_FOREIGN_ADVISORS:
                    return targetKingdom.Armies.Count > 0;

                case ACTION_WAR_CHEST:
                    return activeRebellion?.Leader?.Leader != null;

                case ACTION_BUY_OUT_CONTRACTS:
                    return parentKingdom != null
                        && parentKingdom.Clans.Any(c => c.IsUnderMercenaryService);

                case ACTION_EXPEDITIONARY_MERCENARIES:
                    return targetKingdom.Clans
                        .SelectMany(c => c.Heroes)
                        .Any(h => h.IsAlive && !h.IsDisabled
                            && h.PartyBelongedTo?.MemberRoster != null
                            && h.PartyBelongedTo.IsLordParty);

                case ACTION_ORCHESTRATE_DEFECTION:
                {
                    Hero parentRuler = parentKingdom?.RulingClan?.Leader;
                    return parentRuler != null
                        && parentKingdom.Clans.Any(c =>
                            c != parentKingdom.RulingClan && !c.IsUnderMercenaryService
                            && c.Leader != null && c.Leader.GetRelation(parentRuler) < 0);
                }

                default:
                    return false;
            }
        }

        private static int PickWeightedAction(List<int> actionIds, List<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            int roll = MBRandom.RandomInt(total);
            int cumulative = 0;
            for (int i = 0; i < actionIds.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return actionIds[i];
            }
            return actionIds[actionIds.Count - 1];
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            CleanUpStuckHighwaymenParties();
            SeedMissingAiEvaluationSchedules();

            starter.AddPlayerLine("spymaster_start", "hero_main_options", "spymaster_response",
                "{=BC_Proxy_StartMission}I need you to undertake a mission of subterfuge in a foreign realm.",
                () => Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom.RulingClan == Clan.PlayerClan &&
                      Hero.OneToOneConversationHero != null && Hero.OneToOneConversationHero.Clan == Clan.PlayerClan,
                null, 100,
                (out TextObject explanation) =>
                {
                    if (_playerMissionReturnDates.ContainsKey(Hero.OneToOneConversationHero.StringId))
                    {
                        explanation = new TextObject("{=BC_Proxy_AlreadyOnMission}This clan member is already undertaking a clandestine mission.");
                        return false;
                    }
                    if (Hero.MainHero.Gold < COST_FUND_HIGHWAYMEN)
                    {
                        explanation = new TextObject("{=BC_Proxy_NeedGold}You need at least 50,000 denars to fund even the cheapest subterfuge.");
                        return false;
                    }
                    explanation = null;
                    return true;
                });

            starter.AddDialogLine("spymaster_response", "spymaster_response", "spymaster_action",
                "{=BC_Proxy_Response}Of course, my liege. Subterfuge requires a heavy purse and careful plotting. Who is our target?",
                null, null);

            starter.AddPlayerLine("spymaster_action", "spymaster_action", "close_window",
                "{=BC_Proxy_DevisePlan}Let us devise a plan",
                () => true,
                () => {
                    _activeCompanion = Hero.OneToOneConversationHero;
                    OpenTargetSelectionInquiry();
                },
                100);
        }

        private void CleanUpStuckHighwaymenParties()
        {
            foreach (MobileParty party in MobileParty.All
                .Where(p => p.StringId.StartsWith(HighwaymenPartyIdPrefix) && p.IsActive)
                .ToList())
            {
                bool isStuck = party.CurrentSettlement != null ||
                               Settlement.All.Any(s => s.IsTown &&
                                   party.Position.DistanceSquared(s.GatePosition) < STUCK_PARTY_THRESHOLD_SQ);
                if (isStuck)
                    DestroyPartyAction.Apply(null, party);
            }
        }

        private void OpenTargetSelectionInquiry()
        {
            List<InquiryElement> elements = new List<InquiryElement>();
            foreach (Kingdom k in Kingdom.All)
            {
                if (k.IsEliminated || k == Clan.PlayerClan.Kingdom || k.IsMinorFaction) continue;
                if (IsAlliedSafe(Clan.PlayerClan.Kingdom, k)) continue;
                elements.Add(new InquiryElement(k, k.Name.ToString(), null));
            }

            TextObject title = new TextObject("{=BC_Proxy_SelectTarget}Select Target Kingdom");
            TextObject desc = new TextObject("{=BC_Proxy_ChooseRealm}Choose a realm to subvert.");

            MultiSelectionInquiryData data = new MultiSelectionInquiryData(title.ToString(),
                desc.ToString(), elements, true, 1, 1,
                new TextObject("{=BC_UI_Select}Select").ToString(), new TextObject("{=BC_UI_Cancel}Cancel").ToString(), OnTargetSelected, null);

            MBInformationManager.ShowMultiSelectionInquiry(data, true);
        }

        private void OnTargetSelected(List<InquiryElement> elements)
        {
            Kingdom target = elements[0].Identifier as Kingdom;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();

            FactionObject activeRebellion = factionManager?.GetFactionByRebelKingdom(target);
            bool isRebelKingdom = activeRebellion != null;
            bool isCivilWarActive = isRebelKingdom && target.IsAtWarWith(activeRebellion.ParentKingdom);

            List<InquiryElement> actions = new List<InquiryElement>();

            if (isRebelKingdom)
            {
                if (isCivilWarActive)
                {
                    TextObject act5 = new TextObject("{=BC_Proxy_Act_5}Foreign Advisors ({COST} Denars)"); act5.SetTextVariable("COST", COST_FOREIGN_ADVISORS.ToString("N0"));
                    TextObject desc5 = new TextObject("{=BC_Proxy_MenuDesc_5}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Grants +40 cohesion to all rebel armies.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Keeps rebel hosts together longer in the field.\n<a style=\"Tooltip.Value.Text\"><b>Best used for</b></a>: rebellions that already have armies but risk falling apart.");
                    actions.Add(new InquiryElement(ACTION_FOREIGN_ADVISORS, act5.ToString(), null, Hero.MainHero.Gold >= COST_FOREIGN_ADVISORS, BuildMissionTooltip(desc5, ACTION_FOREIGN_ADVISORS, target, activeRebellion)));

                    TextObject act6 = new TextObject("{=BC_Proxy_Act_6}The War Chest ({COST} Denars)"); act6.SetTextVariable("COST", COST_WAR_CHEST.ToString("N0"));
                    TextObject desc6 = new TextObject("{=BC_Proxy_MenuDesc_6}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Funnels 75,000 denars directly into the rebel treasury.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Helps rebels pay wages, raise parties, and survive attrition.\n<a style=\"Tooltip.Value.Text\"><b>Best used for</b></a>: poor rebellions with enough nobles to keep fighting.");
                    actions.Add(new InquiryElement(ACTION_WAR_CHEST, act6.ToString(), null, Hero.MainHero.Gold >= COST_WAR_CHEST, BuildMissionTooltip(desc6, ACTION_WAR_CHEST, target, activeRebellion)));

                    TextObject act7 = new TextObject("{=BC_Proxy_Act_7}Buy Out Contracts ({COST} Denars)"); act7.SetTextVariable("COST", COST_BUY_OUT_CONTRACTS.ToString("N0"));
                    TextObject desc7 = new TextObject("{=BC_Proxy_MenuDesc_7}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Bribes mercenary clans to abandon the parent kingdom.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Strips hired swords away from the loyalist war effort.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: realms relying on mercenary contracts.");
                    actions.Add(new InquiryElement(ACTION_BUY_OUT_CONTRACTS, act7.ToString(), null, Hero.MainHero.Gold >= COST_BUY_OUT_CONTRACTS, BuildMissionTooltip(desc7, ACTION_BUY_OUT_CONTRACTS, target, activeRebellion)));

                    TextObject act8 = new TextObject("{=BC_Proxy_Act_8}Expeditionary Mercenaries ({COST} Denars)"); act8.SetTextVariable("COST", COST_EXPEDITIONARY_MERCENARIES.ToString("N0"));
                    TextObject desc8 = new TextObject("{=BC_Proxy_MenuDesc_8}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Smuggles 50 elite troops into the rebel leader's ranks.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Gives the rebellion a stronger field army immediately.\n<a style=\"Tooltip.Value.Text\"><b>Best used for</b></a>: fragile rebellions that need a hard military boost.");
                    actions.Add(new InquiryElement(ACTION_EXPEDITIONARY_MERCENARIES, act8.ToString(), null, Hero.MainHero.Gold >= COST_EXPEDITIONARY_MERCENARIES, BuildMissionTooltip(desc8, ACTION_EXPEDITIONARY_MERCENARIES, target, activeRebellion)));

                    TextObject act9 = new TextObject("{=BC_Proxy_Act_9}Orchestrate Defection ({COST} Denars)"); act9.SetTextVariable("COST", COST_ORCHESTRATE_DEFECTION.ToString("N0"));
                    TextObject desc9 = new TextObject("{=BC_Proxy_MenuDesc_9}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Bribes a loyalist clan to defect to the rebels.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Shifts manpower, fiefs, and legitimacy in one dangerous stroke.\n<a style=\"Tooltip.Value.Text\"><b>Best used when</b></a>: a loyalist clan already dislikes the crown.");
                    actions.Add(new InquiryElement(ACTION_ORCHESTRATE_DEFECTION, act9.ToString(), null, Hero.MainHero.Gold >= COST_ORCHESTRATE_DEFECTION, BuildMissionTooltip(desc9, ACTION_ORCHESTRATE_DEFECTION, target, activeRebellion)));
                }
                else
                {
                    actions.Add(new InquiryElement(ACTION_NO_SUBTERFUGE, new TextObject("{=BC_Proxy_Act_NoSubterfuge}No Subterfuge Available").ToString(), null, false, new TextObject("{=BC_Proxy_MenuDesc_NoSubterfuge}This rebellion is not currently fighting its parent kingdom.\nSubterfuge support is only available while the rebel state is actively at war with its parent realm.").ToString()));
                }
            }
            else
            {
                TextObject act0 = new TextObject("{=BC_Proxy_Act_0}Fund Highwaymen ({COST} Denars)"); act0.SetTextVariable("COST", COST_FUND_HIGHWAYMEN.ToString("N0"));
                TextObject desc0 = new TextObject("{=BC_Proxy_MenuDesc_0}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Spawns large bandit parties near the enemy capital.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Harasses travel, caravans, and local security.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: neighboring realms already strained by war.");
                actions.Add(new InquiryElement(ACTION_FUND_HIGHWAYMEN, act0.ToString(), null, Hero.MainHero.Gold >= COST_FUND_HIGHWAYMEN, BuildMissionTooltip(desc0, ACTION_FUND_HIGHWAYMEN, target, null)));

                TextObject act1 = new TextObject("{=BC_Proxy_Act_1}Incite Peasant Revolt ({COST} Denars)"); act1.SetTextVariable("COST", COST_INCITE_REVOLT.ToString("N0"));
                TextObject desc1 = new TextObject("{=BC_Proxy_MenuDesc_1}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Strikes the kingdom's most vulnerable town with a major loyalty shock.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Makes urban unrest and rebellion more likely.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: realms holding unstable or foreign-culture towns.");
                actions.Add(new InquiryElement(ACTION_INCITE_REVOLT, act1.ToString(), null, Hero.MainHero.Gold >= COST_INCITE_REVOLT, BuildMissionTooltip(desc1, ACTION_INCITE_REVOLT, target, null)));

                TextObject act2 = new TextObject("{=BC_Proxy_Act_2}The Court Scandal ({COST} Denars)"); act2.SetTextVariable("COST", COST_COURT_SCANDAL.ToString("N0"));
                TextObject desc2 = new TextObject("{=BC_Proxy_MenuDesc_2}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Burns a large share of the target ruler's influence.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Weakens their ability to lead armies and dominate votes.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: powerful rulers holding the realm together personally.");
                actions.Add(new InquiryElement(
                    ACTION_COURT_SCANDAL,
                    act2.ToString(),
                    null,
                    Hero.MainHero.Gold >= COST_COURT_SCANDAL && IsActionValid(ACTION_COURT_SCANDAL, target, null),
                    BuildMissionTooltip(desc2, ACTION_COURT_SCANDAL, target, null)));

                TextObject act3 = new TextObject("{=BC_Proxy_Act_3}Smear Campaign ({COST} Denars)"); act3.SetTextVariable("COST", COST_SMEAR_CAMPAIGN.ToString("N0"));
                TextObject desc3 = new TextObject("{=BC_Proxy_MenuDesc_3}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Outrages one court faction with scandalous rumors.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Drives faction mood downward and feeds obstruction.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: realms already divided by court politics.");
                actions.Add(new InquiryElement(ACTION_SMEAR_CAMPAIGN, act3.ToString(), null, Hero.MainHero.Gold >= COST_SMEAR_CAMPAIGN, BuildMissionTooltip(desc3, ACTION_SMEAR_CAMPAIGN, target, null)));

                TextObject act4 = new TextObject("{=BC_Proxy_Act_4}Fund Dissent ({COST} Denars)"); act4.SetTextVariable("COST", COST_FUND_DISSENT.ToString("N0"));
                TextObject desc4 = new TextObject("{=BC_Proxy_MenuDesc_4}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>: Bribes stable clans into seditious circles.\n<a style=\"Tooltip.Value.Text\"><b>Pressure</b></a>: Accelerates rebellious intent among vulnerable vassals.\n<a style=\"Tooltip.Value.Text\"><b>Best used against</b></a>: realms with disgruntled but hesitant nobles.");
                actions.Add(new InquiryElement(ACTION_FUND_DISSENT, act4.ToString(), null, Hero.MainHero.Gold >= COST_FUND_DISSENT, BuildMissionTooltip(desc4, ACTION_FUND_DISSENT, target, null)));
            }

            _selectedTarget = target;

            TextObject menuTitle = isRebelKingdom ? new TextObject("{=BC_Proxy_MenuTitle_Rebel}Select Method of Intervention") : new TextObject("{=BC_Proxy_MenuTitle_Stable}Select Method of Subterfuge");
            TextObject menuDescription = isRebelKingdom
                ? new TextObject("{=BC_Proxy_MenuDesc_Rebel}How shall we support the {TARGET_NAME}?")
                : new TextObject("{=BC_Proxy_MenuDesc_Stable}How shall we destabilize {TARGET_NAME}?");
            menuDescription.SetTextVariable("TARGET_NAME", _selectedTarget.Name);

            MultiSelectionInquiryData data = new MultiSelectionInquiryData(menuTitle.ToString(),
                menuDescription.ToString(), actions, true, 1, 1,
                new TextObject("{=BC_Proxy_DispatchAgent}Dispatch Agent").ToString(), new TextObject("{=BC_Proxy_Return}Return").ToString(), OnActionSelected, OnActionMenuCanceled);

            MBInformationManager.ShowMultiSelectionInquiry(data, true);
        }

        private void OnActionMenuCanceled(List<InquiryElement> elements) { OpenTargetSelectionInquiry(); }

        private string BuildMissionTooltip(TextObject baseDescription, int actionType, Kingdom targetKingdom, FactionObject activeRebellion)
        {
            string description = baseDescription?.ToString() ?? string.Empty;
            Hero agent = _activeCompanion;
            Hero counterIntrigueRuler = GetCounterIntrigueRuler(targetKingdom, actionType, activeRebellion);

            if (agent == null || counterIntrigueRuler == null)
            {
                return description;
            }

            float discoveryChance = CalculateDiscoveryChance(agent, counterIntrigueRuler, actionType);
            float baselineDiscoveryChance = CalculateDiscoveryChance(0f, 0, counterIntrigueRuler, actionType);
            float missionSuccessChance = 1f - discoveryChance;
            float skillSuccessDelta = baselineDiscoveryChance - discoveryChance;

            float escapeChance = CalculateEscapeChance(agent);
            float baselineEscapeChance = 1f - EXECUTION_BASE_CHANCE;
            float skillEscapeDelta = escapeChance - baselineEscapeChance;

            TextObject tooltip = new TextObject("{=BC_Proxy_MissionRiskBlock}{DESCRIPTION}\n\n<a style=\"Tooltip.Value.Text\"><b>Agent</b></a>\n{AGENT_NAME}\n\n<a style=\"Tooltip.Value.Text\"><b>Secrecy</b></a>\nSuccess chance: {SUCCESS_CHANCE}\nAgent impact: {SUCCESS_DELTA}\n\n<a style=\"Tooltip.Value.Text\"><b>Exposure</b></a>\nSurvival if exposed: {SURVIVAL_CHANCE}\nAgent impact: {SURVIVAL_DELTA}\n\n<a style=\"Tooltip.Value.Text\"><b>Counter-intrigue</b></a>\n{RULER_NAME}'s skill and traits affect exposure risk.");
            tooltip.SetTextVariable("DESCRIPTION", description);
            tooltip.SetTextVariable("AGENT_NAME", new TextObject("{=!}" + StyleTooltipValue(agent.Name.ToString())));
            tooltip.SetTextVariable("SUCCESS_CHANCE", new TextObject("{=!}" + StyleTooltipValue(FormatPercent(missionSuccessChance))));
            tooltip.SetTextVariable("SURVIVAL_CHANCE", new TextObject("{=!}" + StyleTooltipValue(FormatPercent(escapeChance))));
            tooltip.SetTextVariable("SUCCESS_DELTA", new TextObject("{=!}" + StyleTooltipValue(FormatSignedPercent(skillSuccessDelta))));
            tooltip.SetTextVariable("SURVIVAL_DELTA", new TextObject("{=!}" + StyleTooltipValue(FormatSignedPercent(skillEscapeDelta))));
            tooltip.SetTextVariable("RULER_NAME", new TextObject("{=!}" + StyleTooltipValue(counterIntrigueRuler.Name.ToString())));
            return tooltip.ToString();
        }

        private static string StyleTooltipValue(string value)
        {
            return "<a style=\"Tooltip.Value.Text\"><b>" + value + "</b></a>";
        }

        private Hero GetCounterIntrigueRuler(Kingdom targetKingdom, int actionType, FactionObject activeRebellion = null)
        {
            if (targetKingdom == null) return null;

            Kingdom defendingKingdom = targetKingdom;

            if (actionType >= ACTION_FOREIGN_ADVISORS)
            {
                activeRebellion = activeRebellion ?? Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(targetKingdom);
                if (activeRebellion?.ParentKingdom != null && targetKingdom.IsAtWarWith(activeRebellion.ParentKingdom))
                    defendingKingdom = activeRebellion.ParentKingdom;
            }

            return Campaign.Current?
                .GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetIntrigueProxy(defendingKingdom)
                ?? defendingKingdom.RulingClan?.Leader;
        }

        private Kingdom GetDiscoveryFalloutKingdom(Kingdom targetKingdom, int actionType)
        {
            if (targetKingdom == null) return null;

            if (actionType >= ACTION_FOREIGN_ADVISORS)
            {
                FactionObject activeRebellion = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(targetKingdom);
                if (activeRebellion?.ParentKingdom != null && targetKingdom.IsAtWarWith(activeRebellion.ParentKingdom))
                    return activeRebellion.ParentKingdom;
            }

            return targetKingdom;
        }

        private static string FormatPercent(float value)
        {
            return MathF.Round(MathF.Clamp(value, 0f, 1f) * 100f).ToString("0") + "%";
        }

        private static string FormatSignedPercent(float value)
        {
            int percent = (int)MathF.Round(value * 100f);
            return percent >= 0 ? "+" + percent + "%" : percent + "%";
        }

        private void OnActionSelected(List<InquiryElement> elements)
        {
            int actionType = (int)elements[0].Identifier;
            if (actionType == ACTION_NO_SUBTERFUGE) return;

            int cost = GetCostForAction(actionType);
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, true);

            if (_activeCompanion.PartyBelongedTo != null && _activeCompanion.PartyBelongedTo.MemberRoster != null)
                _activeCompanion.PartyBelongedTo.MemberRoster.RemoveTroop(_activeCompanion.CharacterObject, 1);

            DisableHeroAction.Apply(_activeCompanion);

            _playerMissionReturnDates[_activeCompanion.StringId] = CampaignTime.Now + CampaignTime.Days(10);
            _playerMissionTargets[_activeCompanion.StringId] = _selectedTarget.StringId;
            _playerMissionActions[_activeCompanion.StringId] = actionType;

            TextObject departMsg = new TextObject("{=BC_Proxy_Departed}{AGENT_NAME} has departed on a clandestine mission. They will return in 10 days.");
            departMsg.SetTextVariable("AGENT_NAME", _activeCompanion.Name);
            BellumCivileNotifications.ShowPersonal(departMsg, BellumNotificationColors.CovertAction);
        }

        private void OnDailyTick()
        {
            PruneSavedState();

            List<string> completedMissions = _playerMissionReturnDates.Where(kvp => kvp.Value.IsPast).Select(kvp => kvp.Key).ToList();

            foreach (string heroId in completedMissions)
            {
                Hero companion = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId) ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == heroId);
                Kingdom target = Kingdom.All.FirstOrDefault(k => k.StringId == _playerMissionTargets[heroId]);
                int action = _playerMissionActions[heroId];

                if (companion != null)
                {
                    if (target == null || target.IsEliminated || target.RulingClan?.Leader == null)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, GetCostForAction(action), true);
                        TextObject abortFallen = new TextObject("{=BC_Proxy_TargetFallen}Upon returning, {AGENT_NAME} discovered the target realm had fallen. The mission was aborted and gold safely returned.");
                        abortFallen.SetTextVariable("AGENT_NAME", companion.Name);
                        BellumCivileNotifications.ShowPersonal(abortFallen, BellumNotificationColors.Warning);
                        if (companion.HeroState == Hero.CharacterStates.Disabled) companion.ChangeState(Hero.CharacterStates.Active);
                        AddHeroToPartyAction.Apply(companion, MobileParty.MainParty);
                    }
                    else
                    {
                        Hero counterIntrigueRuler = GetCounterIntrigueRuler(target, action);
                        Kingdom discoveryFalloutKingdom = GetDiscoveryFalloutKingdom(target, action);
                        bool isDiscovered = counterIntrigueRuler != null && RollForDiscovery(companion, counterIntrigueRuler, action);

                        if (isDiscovered)
                        {
                            ApplyDiscoveryFallout(Clan.PlayerClan.Kingdom, discoveryFalloutKingdom, action, null);

                            float deathChance = CalculateExecutionChance(companion);

                            if (MBRandom.RandomFloat < deathChance)
                            {
                                TextObject execMsg = new TextObject("{=BC_Proxy_AgentExecuted}{AGENT_NAME} was captured by {RULER_NAME} during the failed plot and executed for high treason!");
                                execMsg.SetTextVariable("AGENT_NAME", companion.Name);
                                execMsg.SetTextVariable("RULER_NAME", counterIntrigueRuler.Name);
                                BellumCivileNotifications.ShowPersonal(execMsg, BellumNotificationColors.Danger);
                                KillCharacterAction.ApplyByExecution(companion, counterIntrigueRuler, true, true);
                            }
                            else
                            {
                                TextObject escMsg = new TextObject("{=BC_Proxy_AgentEscaped}{AGENT_NAME} barely escaped {TARGET_KINGDOM} with their life. The plot was exposed, the gold was confiscated, and the mission failed.");
                                escMsg.SetTextVariable("AGENT_NAME", companion.Name);
                                escMsg.SetTextVariable("TARGET_KINGDOM", discoveryFalloutKingdom?.Name ?? target.Name);
                                BellumCivileNotifications.ShowPersonal(escMsg, BellumNotificationColors.Warning);
                                if (companion.HeroState == Hero.CharacterStates.Disabled) companion.ChangeState(Hero.CharacterStates.Active);
                                AddHeroToPartyAction.Apply(companion, MobileParty.MainParty);
                            }
                        }
                        else
                        {
                            EspionageOutcome outcome = ApplyEspionageEffects(Clan.PlayerClan.Kingdom, target, action);

                            if (outcome.AbortMission)
                            {
                                GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, GetCostForAction(action), true);
                                TextObject abortArrived = new TextObject("{=BC_Proxy_ArrivedAborted}Upon arriving at his destination, {AGENT_NAME} discovered {ABORT_REASON}. The mission was aborted, and the gold safely returned.");
                                abortArrived.SetTextVariable("AGENT_NAME", companion.Name);
                                abortArrived.SetTextVariable("ABORT_REASON", outcome.AbortReason);
                                BellumCivileNotifications.ShowPersonal(abortArrived, BellumNotificationColors.Warning);
                            }
                            else
                            {
                                TextObject successMsg = new TextObject("{=BC_Proxy_MissionSuccess}The mission was a complete success, and {AGENT_NAME} left no trace.");
                                successMsg.SetTextVariable("AGENT_NAME", companion.Name);
                                BellumCivileNotifications.ShowPersonal(successMsg, BellumNotificationColors.Success);
                                if (action >= ACTION_FOREIGN_ADVISORS && outcome.SpecificTarget != null && !outcome.SpecificTarget.IsDead && !outcome.SpecificTarget.IsDisabled)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, outcome.SpecificTarget, REBEL_ASSIST_RELATION_BONUS, true);
                            }

                            if (companion.HeroState == Hero.CharacterStates.Disabled) companion.ChangeState(Hero.CharacterStates.Active);
                            AddHeroToPartyAction.Apply(companion, MobileParty.MainParty);
                        }
                    }
                }

                _playerMissionReturnDates.Remove(heroId);
                _playerMissionTargets.Remove(heroId);
                _playerMissionActions.Remove(heroId);
            }

            RunDueAiEspionageEvaluations();
        }

        private void RunDueAiEspionageEvaluations()
        {
            if (!BellumCivileOptions.EnableNpcSubterfugeMissions)
                return;

            SeedMissingAiEvaluationSchedules();

            int checksRemaining = Math.Max(1, C.ProxyWarRulerMaxChecksPerDailyTick);
            foreach (Kingdom actingKingdom in Kingdom.All.ToList())
            {
                if (!IsValidAiSubterfugeKingdom(actingKingdom)) continue;

                string kingdomId = actingKingdom.StringId;
                if (string.IsNullOrEmpty(kingdomId)) continue;
                if (!_nextAiEvaluationDates.TryGetValue(kingdomId, out CampaignTime dueDate))
                {
                    ScheduleNextAiEvaluation(actingKingdom, true);
                    continue;
                }
                if (!dueDate.IsPast) continue;

                if (checksRemaining <= 0) break;
                checksRemaining--;

                if (_aiCooldowns.TryGetValue(kingdomId, out CampaignTime cooldown) && !cooldown.IsPast)
                {
                    _nextAiEvaluationDates[kingdomId] = cooldown + CampaignTime.Days(1 + MBRandom.RandomInt(3));
                    continue;
                }

                EvaluateEspionageTarget(actingKingdom);

                if (_aiCooldowns.TryGetValue(kingdomId, out CampaignTime newCooldown) && !newCooldown.IsPast)
                {
                    _nextAiEvaluationDates[kingdomId] = newCooldown + CampaignTime.Days(1 + MBRandom.RandomInt(3));
                }
                else
                {
                    ScheduleNextAiEvaluation(actingKingdom, false);
                }
            }
        }

        private void SeedMissingAiEvaluationSchedules()
        {
            if (!BellumCivileOptions.EnableNpcSubterfugeMissions)
                return;

            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (!IsValidAiSubterfugeKingdom(kingdom)) continue;
                if (string.IsNullOrEmpty(kingdom.StringId)) continue;
                if (_nextAiEvaluationDates.ContainsKey(kingdom.StringId)) continue;
                ScheduleNextAiEvaluation(kingdom, true);
            }
        }

        private static bool IsValidAiSubterfugeKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !kingdom.IsMinorFaction
                && kingdom.RulingClan?.Leader != null
                && kingdom.RulingClan != Clan.PlayerClan;
        }

        private void ScheduleNextAiEvaluation(Kingdom kingdom, bool initialSchedule)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId)) return;

            int delayDays = initialSchedule
                ? GetStableInitialEvaluationDelayDays(kingdom)
                : C.ProxyWarRulerCheckMinDays + MBRandom.RandomInt(Math.Max(1, C.ProxyWarRulerCheckRandomExtraDays));

            _nextAiEvaluationDates[kingdom.StringId] = CampaignTime.Now + CampaignTime.Days(delayDays);
        }

        private static int GetStableInitialEvaluationDelayDays(Kingdom kingdom)
        {
            int window = Math.Max(1, C.ProxyWarRulerCheckMinDays + C.ProxyWarRulerCheckRandomExtraDays - 1);
            int hash = 17;
            string id = kingdom?.StringId ?? string.Empty;
            for (int i = 0; i < id.Length; i++)
                hash = unchecked(hash * 31 + id[i]);

            return 1 + Math.Abs(hash % window);
        }

        private void EvaluateEspionageTarget(Kingdom actingKingdom)
        {
            Hero actingRuler = actingKingdom.RulingClan.Leader;
            if (_aiCooldowns.TryGetValue(actingKingdom.StringId, out CampaignTime cooldown) && !cooldown.IsPast) return;

            int calcTrait  = actingRuler.GetTraitLevel(DefaultTraits.Calculating);
            int honorTrait = actingRuler.GetTraitLevel(DefaultTraits.Honor);
            int mercyTrait = actingRuler.GetTraitLevel(DefaultTraits.Mercy);

            foreach (Kingdom targetKingdom in Kingdom.All.ToList())
            {
                if (targetKingdom == actingKingdom || targetKingdom.IsEliminated || targetKingdom.RulingClan?.Leader == null || targetKingdom.IsMinorFaction) continue;
                if (IsAlliedSafe(actingKingdom, targetKingdom)) continue;

                var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                FactionObject activeRebellion = factionManager?.GetFactionByRebelKingdom(targetKingdom);
                bool isRebelKingdom = activeRebellion != null;

                if (!IsNeighboringSubterfugeTarget(actingKingdom, targetKingdom, activeRebellion)) continue;

                Hero targetRulerToCheck = isRebelKingdom ? activeRebellion.ParentKingdom?.RulingClan?.Leader : targetKingdom.RulingClan.Leader;
                if (targetRulerToCheck == null) continue;

                int relationWithTarget = actingRuler.GetRelation(targetRulerToCheck);

                float actChance = AI_ACT_BASE_CHANCE;

                if      (calcTrait >= 1)   actChance += AI_ACT_CALC_BONUS;
                else if (calcTrait <= -1)  actChance -= AI_ACT_IMPULSIVE_PENALTY;

                if      (honorTrait <= -1) actChance += AI_ACT_DISHONORABLE_BONUS;
                else if (honorTrait >= 1)  actChance -= AI_ACT_HONOR_PENALTY;

                if      (mercyTrait <= -1) actChance += AI_ACT_CRUEL_BONUS;
                else if (mercyTrait >= 1)  actChance -= AI_ACT_MERCY_PENALTY;

                StanceLink stance = actingKingdom.GetStanceWith(isRebelKingdom ? activeRebellion.ParentKingdom : targetKingdom);
                if (stance != null && stance.IsAtWar) actChance += AI_ACT_AT_WAR_BONUS;

                if      (relationWithTarget < -40) actChance += AI_ACT_VERY_LOW_REL_BONUS;
                else if (relationWithTarget < -20) actChance += AI_ACT_LOW_REL_BONUS;

                actChance = MathF.Clamp(actChance, AI_ACT_MIN_CHANCE, AI_ACT_MAX_CHANCE);

                if (MBRandom.RandomFloat > actChance) continue;

                var actionIds     = new List<int>();
                var actionWeights = new List<int>();

                if (isRebelKingdom && activeRebellion != null && targetKingdom.IsAtWarWith(activeRebellion.ParentKingdom))
                {
                    if (IsActionValid(ACTION_FOREIGN_ADVISORS,          targetKingdom, activeRebellion)) { actionIds.Add(ACTION_FOREIGN_ADVISORS);          actionWeights.Add(WEIGHT_FOREIGN_ADVISORS); }
                    if (IsActionValid(ACTION_WAR_CHEST,                 targetKingdom, activeRebellion)) { actionIds.Add(ACTION_WAR_CHEST);                 actionWeights.Add(WEIGHT_WAR_CHEST); }
                    if (IsActionValid(ACTION_BUY_OUT_CONTRACTS,         targetKingdom, activeRebellion)) { actionIds.Add(ACTION_BUY_OUT_CONTRACTS);         actionWeights.Add(WEIGHT_BUY_OUT_CONTRACTS); }
                    if (IsActionValid(ACTION_EXPEDITIONARY_MERCENARIES, targetKingdom, activeRebellion)) { actionIds.Add(ACTION_EXPEDITIONARY_MERCENARIES); actionWeights.Add(WEIGHT_EXPEDITIONARY_MERCS); }
                    if (IsActionValid(ACTION_ORCHESTRATE_DEFECTION,     targetKingdom, activeRebellion)) { actionIds.Add(ACTION_ORCHESTRATE_DEFECTION);     actionWeights.Add(WEIGHT_ORCHESTRATE_DEFECTION); }
                }
                else if (!isRebelKingdom)
                {
                    if (IsActionValid(ACTION_FUND_HIGHWAYMEN,  targetKingdom, null)) { actionIds.Add(ACTION_FUND_HIGHWAYMEN);  actionWeights.Add(WEIGHT_FUND_HIGHWAYMEN); }
                    if (IsActionValid(ACTION_INCITE_REVOLT,    targetKingdom, null)) { actionIds.Add(ACTION_INCITE_REVOLT);    actionWeights.Add(WEIGHT_INCITE_REVOLT); }
                    if (IsActionValid(ACTION_COURT_SCANDAL,    targetKingdom, null)) { actionIds.Add(ACTION_COURT_SCANDAL);    actionWeights.Add(WEIGHT_COURT_SCANDAL); }
                    if (IsActionValid(ACTION_SMEAR_CAMPAIGN,   targetKingdom, null)) { actionIds.Add(ACTION_SMEAR_CAMPAIGN);   actionWeights.Add(WEIGHT_SMEAR_CAMPAIGN); }
                    if (IsActionValid(ACTION_FUND_DISSENT,     targetKingdom, null)) { actionIds.Add(ACTION_FUND_DISSENT);     actionWeights.Add(WEIGHT_FUND_DISSENT); }
                }

                if (actionIds.Count == 0) continue;

                for (int i = actionIds.Count - 1; i >= 0; i--)
                {
                    if (actingRuler.Gold < GetCostForAction(actionIds[i]) + AI_MIN_RESERVE_GOLD)
                    {
                        actionIds.RemoveAt(i);
                        actionWeights.RemoveAt(i);
                    }
                }

                if (actionIds.Count == 0) continue;

                int rolledAction = PickWeightedAction(actionIds, actionWeights);
                int cost = GetCostForAction(rolledAction);

                GiveGoldAction.ApplyBetweenCharacters(actingRuler, null, cost, true);

                Hero actingAgent = Campaign.Current?
                    .GetCampaignBehavior<PrivyCouncilBehavior>()?
                    .GetIntrigueProxy(actingKingdom)
                    ?? actingRuler;
                Hero counterIntrigue = GetCounterIntrigueRuler(targetKingdom, rolledAction, activeRebellion);
                bool isDiscovered = RollForDiscovery(actingAgent, counterIntrigue, rolledAction);

                if (isDiscovered)
                {
                    ApplyDiscoveryFallout(actingKingdom, isRebelKingdom ? activeRebellion.ParentKingdom : targetKingdom, rolledAction, null);
                    _aiCooldowns[actingKingdom.StringId] = CampaignTime.Now + CampaignTime.Days(GetCooldownForAction(rolledAction));
                    return;
                }
                else
                {
                    EspionageOutcome outcome = ApplyEspionageEffects(actingKingdom, targetKingdom, rolledAction);

                    if (outcome.AbortMission)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(null, actingRuler, cost, true);
                    }
                    else
                    {
                        _aiCooldowns[actingKingdom.StringId] = CampaignTime.Now + CampaignTime.Days(GetCooldownForAction(rolledAction));
                        if (rolledAction >= ACTION_FOREIGN_ADVISORS && outcome.SpecificTarget != null && !outcome.SpecificTarget.IsDead && !outcome.SpecificTarget.IsDisabled)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(actingRuler, outcome.SpecificTarget, REBEL_ASSIST_RELATION_BONUS, false);

                        TryShowUndetectedImpactToPlayer(targetKingdom, rolledAction, outcome);
                    }
                    return;
                }
            }
        }

        private static bool IsNeighboringSubterfugeTarget(Kingdom actingKingdom, Kingdom targetKingdom, FactionObject activeRebellion)
        {
            if (AreNeighboringKingdoms(actingKingdom, targetKingdom)) return true;

            Kingdom parentKingdom = activeRebellion?.ParentKingdom;
            return parentKingdom != null
                && parentKingdom != targetKingdom
                && AreNeighboringKingdoms(actingKingdom, parentKingdom);
        }

        private static bool AreNeighboringKingdoms(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            if (firstKingdom == null || secondKingdom == null || firstKingdom == secondKingdom)
                return false;

            List<Settlement> firstFiefs = firstKingdom.Settlements
                .Where(IsStrategicFief)
                .ToList();
            List<Settlement> secondFiefs = secondKingdom.Settlements
                .Where(IsStrategicFief)
                .ToList();

            if (firstFiefs.Count == 0 || secondFiefs.Count == 0)
                return false;

            float averageTownDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            float neighborThreshold = averageTownDistance > 0f
                ? averageTownDistance * AI_NEIGHBOR_DISTANCE_MULTIPLIER
                : Campaign.MapDiagonal * 0.15f;

            foreach (Settlement firstFief in firstFiefs)
            {
                foreach (Settlement secondFief in secondFiefs)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        firstFief,
                        secondFief,
                        isFromPort: false,
                        isTargetingPort: false,
                        MobileParty.NavigationType.All);

                    if (distance <= neighborThreshold)
                        return true;
                }
            }

            return false;
        }

        private static bool IsStrategicFief(Settlement settlement)
        {
            return settlement != null && (settlement.IsTown || settlement.IsCastle);
        }

        private EspionageOutcome ApplyEspionageEffects(Kingdom actingKingdom, Kingdom targetKingdom, int actionType)
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();

            var outcome = new EspionageOutcome
            {
                VictimKingdom = targetKingdom
            };

            if (targetKingdom == null || targetKingdom.IsEliminated)
            {
                outcome.AbortMission = true;
                outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_Fallen}the target realm had fallen");
            }
            else
            {
                if (actionType >= ACTION_FOREIGN_ADVISORS)
                {
                    FactionObject activeRebellion = factionManager?.GetFactionByRebelKingdom(targetKingdom);
                    if (activeRebellion != null && targetKingdom.IsAtWarWith(activeRebellion.ParentKingdom))
                    {
                        outcome.VictimKingdom = activeRebellion.ParentKingdom;
                        outcome.SpecificTarget = activeRebellion.Leader?.Leader;

                        if (actionType == ACTION_FOREIGN_ADVISORS)
                        {
                            if (targetKingdom.Armies.Count == 0)
                            {
                                outcome.AbortMission = true;
                                outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoArmies}the rebel forces had no armies in the field to bolster");
                            }
                            else
                            {
                                foreach (Army army in targetKingdom.Armies) army.Cohesion = MathF.Min(army.Cohesion + 40f, 100f);
                            }
                        }
                        else if (actionType == ACTION_WAR_CHEST)
                        {
                            if (outcome.SpecificTarget != null)
                            {
                                GiveGoldAction.ApplyBetweenCharacters(null, outcome.SpecificTarget, WAR_CHEST_TRANSFER_AMOUNT, true);
                                outcome.AffectedClan = outcome.SpecificTarget.Clan;
                            }
                        }
                        else if (actionType == ACTION_BUY_OUT_CONTRACTS)
                        {
                            var mercenaries = outcome.VictimKingdom.Clans.Where(c => c.IsUnderMercenaryService).ToList();
                            if (mercenaries.Count > 0)
                            {
                                outcome.AffectedClan = mercenaries[0];
                                int limit = Math.Min(mercenaries.Count, 2);
                                CampaignTime contractEnd = CampaignTime.Now + CampaignTime.Days(60);
                                for (int i = 0; i < limit; i++)
                                {
                                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(mercenaries[i], showNotification: true);
                                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(mercenaries[i], targetKingdom, contractEnd, 1, showNotification: true);
                                }
                            }
                            else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoMercs}the King had no mercenary contracts to sever"); }
                        }
                        else if (actionType == ACTION_EXPEDITIONARY_MERCENARIES)
                        {
                            List<CharacterObject> eliteTroops = TroopSelectionHelper.GetCultureEliteTroops(actingKingdom.Culture);

                            Hero troopTarget = null;
                            if (outcome.SpecificTarget?.PartyBelongedTo?.MemberRoster != null)
                                troopTarget = outcome.SpecificTarget;
                            else
                                troopTarget = targetKingdom.Clans
                                    .SelectMany(c => c.Heroes)
                                    .FirstOrDefault(h => h.IsAlive && !h.IsDisabled && h.PartyBelongedTo?.MemberRoster != null && h.PartyBelongedTo.IsLordParty);

                            if (troopTarget == null)
                            {
                                outcome.AbortMission = true;
                                outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoRebelParty}the rebel forces had no active parties to reinforce with our men");
                            }
                            else if (eliteTroops.Count > 0)
                            {
                                troopTarget.PartyBelongedTo.MemberRoster.AddToCounts(eliteTroops[MBRandom.RandomInt(eliteTroops.Count)], 50);
                                outcome.AffectedClan = troopTarget.Clan;
                            }
                        }
                        else if (actionType == ACTION_ORCHESTRATE_DEFECTION)
                        {
                            Hero victimRuler = outcome.VictimKingdom.RulingClan?.Leader;
                            if (victimRuler == null)
                            {
                                outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoDefectors}there were no disloyal lords willing to defect");
                            }
                            else
                            {
                                var potentialDefectors = outcome.VictimKingdom.Clans.Where(c => c != outcome.VictimKingdom.RulingClan && !c.IsUnderMercenaryService && c.Leader != null && c.Leader.GetRelation(victimRuler) < 0).ToList();

                                if (potentialDefectors.Count > 0)
                                {
                                    Clan defector = potentialDefectors.OrderBy(c => c.Leader.GetRelation(victimRuler)).First();
                                    ChangeKingdomAction.ApplyByJoinToKingdom(defector, targetKingdom, showNotification: true);
                                    outcome.SpecificTarget = defector.Leader;
                                    outcome.AffectedClan = defector;
                                }
                                else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoDefectors}there were no disloyal lords willing to defect"); }
                            }
                        }
                    }
                    else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_WarOver}the civil war had already concluded"); }
                }
                else
                {
                    outcome.VictimKingdom = targetKingdom;

                    if (actionType == ACTION_FUND_HIGHWAYMEN)
                    {
                        Clan banditClan = GetCultureAppropriateBanditClan(targetKingdom);
                        if (banditClan == null || !targetKingdom.Settlements.Any(s => s.IsTown))
                        {
                            outcome.AbortMission = true;
                            outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoCapital}the kingdom possessed no major towns to blockade");
                        }
                        else
                        {
                            int bandSpawned = SpawnHighwaymenBands(targetKingdom, banditClan);
                            if (bandSpawned == 0) { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoCapital}the kingdom possessed no major towns to blockade"); }
                            else
                            {
                                outcome.SpecificTarget = targetKingdom.RulingClan.Leader;
                                outcome.AffectedClan = targetKingdom.RulingClan;
                            }
                        }
                    }
                    else if (actionType == ACTION_INCITE_REVOLT)
                    {
                        Town targetTown = targetKingdom.Fiefs.Where(f => f.IsTown).OrderBy(f => f.Loyalty).FirstOrDefault();
                        if (targetTown != null)
                        {
                            targetTown.Loyalty = MathF.Max(0f, targetTown.Loyalty - 40f);
                            outcome.SpecificTarget = targetTown.OwnerClan?.Leader;
                            outcome.AffectedSettlement = targetTown.Settlement;
                        }
                        else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoCity}the kingdom possessed no cities to incite"); }
                    }
                    else if (actionType == ACTION_COURT_SCANDAL)
                    {
                        if (!IsActionValid(ACTION_COURT_SCANDAL, targetKingdom, null))
                        {
                            outcome.AbortMission = true;
                            outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_ScandalSpent}the target court was no longer vulnerable to another scandal");
                        }
                        else
                        {
                            float requestedLoss = MBRandom.RandomFloatRanged(200f, 300f);
                            float appliedLoss = NpcInfluenceBudgetService.ApplyClampedLoss(
                                targetKingdom.RulingClan,
                                requestedLoss,
                                "court_scandal",
                                C.NpcCourtScandalProtectedFloor,
                                hostile: true);
                            if (appliedLoss <= 0f)
                            {
                                outcome.AbortMission = true;
                                outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_ScandalSpent}the target court was no longer vulnerable to another scandal");
                            }
                            else
                            {
                                _courtScandalTargetCooldowns[targetKingdom.StringId] = CampaignTime.Now
                                    + CampaignTime.Days(C.NpcCourtScandalTargetCooldownDays);
                                outcome.SpecificTarget = targetKingdom.RulingClan?.Leader;
                                outcome.AffectedClan = targetKingdom.RulingClan;
                            }
                        }
                    }
                    else if (actionType == ACTION_SMEAR_CAMPAIGN)
                    {
                        var ideologies = factionManager?.GetFactionsInKingdom(targetKingdom).Where(f => f.IsIdeology).ToList();
                        if (ideologies != null && ideologies.Count > 0)
                        {
                            FactionObject targetIdeologyFaction = ideologies[MBRandom.RandomInt(ideologies.Count)];
                            Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>()?.RecordAgitation(targetIdeologyFaction);
                            outcome.SpecificTarget = targetIdeologyFaction.Leader?.Leader;
                            outcome.AffectedFaction = targetIdeologyFaction;
                        }
                        else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoFactions}the target court lacked political factions"); }
                    }
                    else if (actionType == ACTION_FUND_DISSENT)
                    {
                        var validClans = targetKingdom.Clans.Where(c => c != targetKingdom.RulingClan && !c.IsEliminated && !c.IsUnderMercenaryService && !c.IsMinorFaction && c.Leader != null).ToList();
                        if (validClans.Count > 0)
                        {
                            Clan ringLeader = validClans[MBRandom.RandomInt(validClans.Count)];
                            _activeAgitationTimers[ringLeader.StringId] = CampaignTime.Now + CampaignTime.Days(60);

                            int spreadCount = MBRandom.RandomInt(1, 3);
                            var ideologyFaction = factionManager?.GetIdeologicalFaction(ringLeader);
                            var spreadTargets = validClans.Where(c => c != ringLeader && ((ideologyFaction != null && ideologyFaction.Members.Contains(c)) || ringLeader.Leader.GetRelation(c.Leader) > 10))
                                                          .OrderBy(x => MBRandom.RandomInt()).Take(spreadCount).ToList();

                            foreach (var target in spreadTargets) _activeAgitationTimers[target.StringId] = CampaignTime.Now + CampaignTime.Days(60);
                            outcome.SpecificTarget = ringLeader.Leader;
                            outcome.AffectedClan = ringLeader;
                        }
                        else { outcome.AbortMission = true; outcome.AbortReason = new TextObject("{=BC_Proxy_AbortReason_NoClans}there were no stable clans left to subvert"); }
                    }
                }
            }

            return outcome;
        }

        private static void TryShowUndetectedImpactToPlayer(Kingdom targetKingdom, int actionType, EspionageOutcome outcome)
        {
            if (outcome == null || outcome.AbortMission)
                return;

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null || (!IsSameKingdom(playerKingdom, targetKingdom) && !IsSameKingdom(playerKingdom, outcome.VictimKingdom)))
                return;

            NotificationHelper.ShowProxyWarUndetectedImpact(
                actionType,
                targetKingdom,
                outcome.VictimKingdom,
                outcome.AffectedSettlement,
                outcome.AffectedFaction,
                outcome.AffectedClan);
        }

        private static bool IsSameKingdom(Kingdom first, Kingdom second)
        {
            if (first == null || second == null)
                return false;

            return ReferenceEquals(first, second)
                || (!string.IsNullOrEmpty(first.StringId) && first.StringId == second.StringId);
        }

        private static int GetActionTier(int actionType)
        {
            if (actionType <= ACTION_INCITE_REVOLT) return 1;
            if (actionType <= ACTION_FUND_DISSENT)  return 2;
            return 3;
        }

        // What does this complex formula do?
        // Calculates the probability of a spy being discovered based on the difference in Roguery skills, modified by traits (Calculating vs Impulsive) and the severity tier of the action.
        private bool RollForDiscovery(Hero actingAgent, Hero targetRuler, int actionType)
        {
            return MBRandom.RandomFloat < CalculateDiscoveryChance(actingAgent, targetRuler, actionType);
        }

        private float CalculateDiscoveryChance(Hero actingAgent, Hero targetRuler, int actionType)
        {
            if (actingAgent == null || targetRuler == null) return DISCOVERY_MAX_CHANCE;
            float actingRoguery = actingAgent.GetSkillValue(DefaultSkills.Roguery)
                + GetActingIntrigueModifier(actingAgent);
            return CalculateDiscoveryChance(
                actingRoguery,
                actingAgent.GetTraitLevel(DefaultTraits.Calculating),
                targetRuler,
                actionType);
        }

        private float CalculateDiscoveryChance(float actingRoguery, int agentCalculating, Hero targetRuler, int actionType)
        {
            if (targetRuler == null) return DISCOVERY_MAX_CHANCE;

            float targetRoguery = targetRuler.GetSkillValue(DefaultSkills.Roguery);
            targetRoguery += GetDefendingIntrigueModifier(targetRuler);
            int targetCalculating = targetRuler.GetTraitLevel(DefaultTraits.Calculating);
            if (targetCalculating >= 1) targetRoguery += DISCOVERY_CALCULATING_BONUS;
            else if (targetCalculating <= -1) targetRoguery -= DISCOVERY_IMPULSIVE_PENALTY;

            if (agentCalculating >= 1) actingRoguery += AGENT_CALCULATING_BONUS;
            else if (agentCalculating <= -1) actingRoguery -= AGENT_RECKLESS_PENALTY;

            int tier = GetActionTier(actionType);
            float baseChance;
            if (tier == 1) baseChance = DISCOVERY_BASE_CHANCE_T1;
            else if (tier == 2) baseChance = DISCOVERY_BASE_CHANCE_T2;
            else baseChance = DISCOVERY_BASE_CHANCE_T3;
            float skillDiff = targetRoguery - actingRoguery;
            float chanceModifier = skillDiff * DISCOVERY_SKILL_MODIFIER;

            return MathF.Clamp(baseChance + chanceModifier, DISCOVERY_MIN_CHANCE, DISCOVERY_MAX_CHANCE);
        }

        private static float GetActingIntrigueModifier(Hero actingAgent)
        {
            Kingdom kingdom = actingAgent?.Clan?.Kingdom;
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            Clan spymaster = council?.GetOfficeHolder(kingdom, PrivyCouncilOffice.Spymaster);
            if (kingdom == null || spymaster?.Leader == null || spymaster.Leader == actingAgent)
                return 0f;

            return council.GetIntrigueSkillModifier(kingdom, actingSide: true);
        }

        private static float GetDefendingIntrigueModifier(Hero defender)
        {
            Kingdom kingdom = defender?.Clan?.Kingdom;
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (kingdom == null || council == null)
                return 0f;

            Clan spymaster = council.GetOfficeHolder(kingdom, PrivyCouncilOffice.Spymaster);
            return spymaster?.Leader == defender
                ? 0f
                : council.GetIntrigueSkillModifier(kingdom, actingSide: false);
        }

        private float CalculateExecutionChance(Hero actingAgent)
        {
            if (actingAgent == null) return EXECUTION_BASE_CHANCE;

            float escapeScore = actingAgent.GetSkillValue(DefaultSkills.Roguery) * EXECUTION_ROGUERY_WEIGHT
                              + actingAgent.GetSkillValue(DefaultSkills.Athletics) * EXECUTION_ATHLETICS_WEIGHT;
            return MathF.Max(EXECUTION_MIN_CHANCE, EXECUTION_BASE_CHANCE - (escapeScore / EXECUTION_ESCAPE_DENOMINATOR));
        }

        private float CalculateEscapeChance(Hero actingAgent)
        {
            return 1f - CalculateExecutionChance(actingAgent);
        }

        private void ApplyDiscoveryFallout(Kingdom actingKingdom, Kingdom targetKingdom, int actionType, Hero specificTarget = null)
        {
            Hero actingRuler = actingKingdom?.RulingClan?.Leader;
            Hero targetRuler = targetKingdom?.RulingClan?.Leader;
            if (actingRuler == null || actingRuler.IsDead || targetRuler == null || targetRuler.IsDead) return;

            int tier = GetActionTier(actionType);
            int relationPenalty   = tier == 1 ? DISCOVERY_RELATION_PENALTY_T1 : tier == 2 ? DISCOVERY_RELATION_PENALTY_T2 : DISCOVERY_RELATION_PENALTY_T3;
            float influenceReward = tier == 1 ? DISCOVERY_INFLUENCE_REWARD_T1  : tier == 2 ? DISCOVERY_INFLUENCE_REWARD_T2  : DISCOVERY_INFLUENCE_REWARD_T3;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(actingRuler, targetRuler, relationPenalty, true);
            if (targetRuler.Clan != null) targetRuler.Clan.Influence += influenceReward;

            if (specificTarget != null && !specificTarget.IsDead && !specificTarget.IsDisabled && specificTarget != targetRuler)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetRuler, specificTarget, DISCOVERY_SPECIFIC_TARGET_BONUS, false);

            NotificationHelper.ShowProxyWarDiscovered(actingKingdom, targetKingdom, actingRuler, targetRuler, actionType);
        }

        private static Clan GetCultureAppropriateBanditClan(Kingdom targetKingdom)
        {
            string cultureId = targetKingdom.Culture?.StringId ?? string.Empty;

            if (CultureToBanditClan.TryGetValue(cultureId, out string banditClanId))
            {
                Clan mapped = Clan.BanditFactions.FirstOrDefault(c => c.StringId == banditClanId && c.DefaultPartyTemplate != null);
                if (mapped != null) return mapped;
            }

            return Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters" && c.DefaultPartyTemplate != null)
                ?? Clan.BanditFactions.FirstOrDefault(c => c.DefaultPartyTemplate != null);
        }

        private int SpawnHighwaymenBands(Kingdom targetKingdom, Clan banditClan)
        {
            List<Settlement> targets = targetKingdom.Settlements
                .Where(s => s.IsTown && s.OwnerClan == targetKingdom.RulingClan)
                .OrderByDescending(s => s.Town.Prosperity)
                .ToList();

            if (targets.Count == 0)
                targets = targetKingdom.Settlements.Where(s => s.IsTown).ToList();

            if (targets.Count == 0) return 0;

            int spawned = 0;
            for (int i = 0; i < 3; i++)
            {
                Settlement target = targets[i % targets.Count];

                CampaignVec2 spawnCenter = NavigationHelper.FindReachablePointAroundPosition(
                    target.GatePosition, MobileParty.NavigationType.Default, 15f, 8f, false);

                if (target.GatePosition.DistanceSquared(spawnCenter) > 400f)
                    continue;

                string partyId = $"{HighwaymenPartyIdPrefix}{target.StringId}_{i}_{MBRandom.RandomInt(99999)}";
                MobileParty banditParty = BanditPartyComponent.CreateLooterParty(
                    partyId, banditClan, target, false, banditClan.DefaultPartyTemplate, spawnCenter);

                if (banditParty.CurrentSettlement != null ||
                    banditParty.Position.DistanceSquared(target.GatePosition) < STUCK_PARTY_THRESHOLD_SQ)
                {
                    DestroyPartyAction.Apply(null, banditParty);
                    continue;
                }

                if (banditClan.DefaultPartyTemplate?.Stacks?.Count > 0)
                {
                    CharacterObject troopType = banditClan.DefaultPartyTemplate.Stacks[0].Character;
                    if (troopType != null)
                        banditParty.MemberRoster.AddToCounts(troopType, MBRandom.RandomInt(20, 35));
                }

                spawned++;
            }
            return spawned;
        }

        private static bool IsAlliedSafe(Kingdom kingdom1, Kingdom kingdom2)
        {
            return kingdom1 != null && kingdom2 != null && kingdom1.IsAllyWith(kingdom2);
        }

        public (int Spawned, string FailReason) ForceFundHighwaymen(Kingdom targetKingdom)
        {
            if (targetKingdom == null || targetKingdom.IsEliminated)
                return (0, "target kingdom does not exist");

            if (!targetKingdom.Settlements.Any(s => s.IsTown))
                return (0, "the kingdom has no towns to spawn near");

            Clan banditClan = GetCultureAppropriateBanditClan(targetKingdom);

            if (banditClan == null) return (0, "no valid bandit faction found for this culture");

            int spawned = SpawnHighwaymenBands(targetKingdom, banditClan);
            return spawned > 0
                ? (spawned, null)
                : (0, "all spawn positions were invalid terrain; try again");
        }
    }
}
