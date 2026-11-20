using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AdvancedStartOptions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        private const string AdvancedStartTitleReason = "advanced player start";

        // A missing saved value preserves the hierarchy policy of pre-feature saves.
        private string _generatedHierarchyScenarioId = string.Empty;
        private bool _useUnitedEmpireHierarchy;
        private bool _checkUnitedEmpireOnLaunch;
        private UnitedEmpireTitleHierarchy _unitedEmpireHierarchy;
        private bool _advancedTitleStartPending;
        private HashSet<Settlement> _preStartPlayerFiefs;
        private Dictionary<Kingdom, FeudalTitleRecord> _preStartCrowns;

        private IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> ConfiguredHierarchyTitles =>
            _useUnitedEmpireHierarchy ? GetUnitedEmpireHierarchy().Titles
                : string.IsNullOrEmpty(_generatedHierarchyScenarioId) ? FeudalTitleConfig.Instance.Titles : null;

        private UnitedEmpireTitleHierarchy GetUnitedEmpireHierarchy() =>
            _unitedEmpireHierarchy ?? (_unitedEmpireHierarchy = UnitedEmpireTitleHierarchy.From(
                FeudalTitleConfig.Instance.Titles, reference => ResolveKingdomReference(reference)?.StringId));

        private void SelectStartingHierarchy(CampaignGameStarter starter)
        {
            var options = Campaign.Current?.AdvancedStartData;
            _generatedHierarchyScenarioId = GetGeneratedHierarchyScenarioId(options?.GetScenario());
            _useUnitedEmpireHierarchy = false;
            _checkUnitedEmpireOnLaunch = _generatedHierarchyScenarioId == DefaultAdvancedStartOptions.ScenarioUnitedEmpireId;
            string startType = options?.GetStartType();
            _advancedTitleStartPending = startType == DefaultAdvancedStartOptions.StartTypeKingId
                || startType == DefaultAdvancedStartOptions.StartTypeVassalId
                || startType == DefaultAdvancedStartOptions.StartTypeFleetAdmiralId;

            if (!_checkUnitedEmpireOnLaunch)
            {
                BellumCivileLogger.Log(string.IsNullOrEmpty(_generatedHierarchyScenarioId)
                    ? "Feudal starting hierarchy: default scenario, configured titles allowed."
                    : $"Feudal starting hierarchy: scenario={_generatedHierarchyScenarioId}, generated titles only.");
            }
        }

        private void PrepareUnitedEmpireHierarchy()
        {
            if (!_checkUnitedEmpireOnLaunch)
                return;

            _checkUnitedEmpireOnLaunch = false;
            _useUnitedEmpireHierarchy = Kingdom.All.Any(kingdom => IsValidPermanentKingdom(kingdom)
                && kingdom.StringId == UnitedEmpireTitleHierarchy.KingdomId && kingdom.RulingClan?.Kingdom == kingdom);
            BellumCivileLogger.Log(_useUnitedEmpireHierarchy
                ? "Feudal starting hierarchy: using the configured hierarchy with a single United Empire crown."
                : "Feudal starting hierarchy: native United Empire is missing or invalid; keeping generated titles.");
        }

        private static string GetGeneratedHierarchyScenarioId(string scenarioId)
        {
            return string.IsNullOrEmpty(scenarioId) || scenarioId == DefaultAdvancedStartOptions.ScenarioNoneId
                ? string.Empty : scenarioId;
        }

        private FeudalTitleType ResolveStartingCrownRank(string kingdomId)
        {
            return _generatedHierarchyScenarioId == DefaultAdvancedStartOptions.ScenarioUnitedEmpireId
                && kingdomId == UnitedEmpireTitleHierarchy.KingdomId
                ? FeudalTitleType.Empire : FeudalTitleType.Kingdom;
        }

        private void ReconcileAdvancedPlayerStart(int phase)
        {
            if (!_advancedTitleStartPending)
                return;

            // The native and War Sails player starts both apply in phase 8.
            if (phase == 7)
            {
                Clan before = Clan.PlayerClan;
                if (before == null)
                    return;

                _preStartPlayerFiefs = new HashSet<Settlement>(before.Fiefs.Select(fief => fief.Settlement));
                if (Campaign.Current?.AdvancedStartData?.GetStartType() == DefaultAdvancedStartOptions.StartTypeKingId)
                {
                    _preStartCrowns = new Dictionary<Kingdom, FeudalTitleRecord>();
                    foreach (Kingdom kingdom in Kingdom.All.Where(IsValidPermanentKingdom))
                    {
                        if (kingdom.RulingClan != before && GetKingdomPoliticalTitle(kingdom) is FeudalTitleRecord crown)
                            _preStartCrowns[kingdom] = crown;
                    }
                }
                return;
            }

            if (phase != 9)
                return;

            _advancedTitleStartPending = false;
            var previousFiefs = _preStartPlayerFiefs;
            var previousCrowns = _preStartCrowns;
            _preStartPlayerFiefs = null;
            _preStartCrowns = null;
            Clan player = Clan.PlayerClan;
            Kingdom realm = player?.Kingdom;
            if (previousFiefs == null || player == null || !IsValidPermanentKingdom(realm))
                return;

            var changedTitles = new List<FeudalTitleRecord>();
            if (realm.RulingClan == player && previousCrowns != null
                && previousCrowns.TryGetValue(realm, out FeudalTitleRecord startingCrown)
                && SetAdvancedStartHolder(startingCrown, player))
            {
                changedTitles.Add(startingCrown);
            }

            foreach (Town fief in player.Fiefs)
            {
                Settlement settlement = fief.Settlement;
                if (!IsLandedSettlement(settlement) || settlement.OwnerClan != player || previousFiefs.Contains(settlement))
                    continue;

                FeudalTitleRecord barony = EnsureBaronyTitle(settlement, AdvancedStartTitleReason, out bool baronyChanged);
                if (SetAdvancedStartHolder(barony, player) || baronyChanged)
                    changedTitles.Add(barony);
            }

            if (changedTitles.Count == 0)
                return;

            RebuildRuntimeIndexes();
            if (ReconcileAllDeFactoParents(AdvancedStartTitleReason) > 0)
                RebuildRuntimeIndexes();
            ReconcileCurrentRealmTitles();
            foreach (FeudalTitleRecord title in changedTitles)
                QueueServiceReviewForTitleAndChildren(title, AdvancedStartTitleReason);
            RefreshCachedTitleNames();
            BellumCivileLogger.Log($"Advanced player start titles synchronized; realm={realm.StringId}; titles={string.Join(", ", changedTitles.Select(title => title.TitleId))}.");
        }

        private bool SetAdvancedStartHolder(FeudalTitleRecord title, Clan player)
        {
            if (title == null || !title.IsActive
                || (title.DeJureHolderClanId == player.StringId && title.DeFactoHolderClanId == player.StringId))
                return false;

            // Initial endowments are not a conquest or a succession. Keep the existing legal liege.
            title.SetDeJureHolder(player.StringId);
            title.SetDeFactoHolder(player.StringId);
            title.MarkSynced(CurrentDay);
            RemoveRedundantClaimsForHolder(player, title);
            return true;
        }
    }
}
