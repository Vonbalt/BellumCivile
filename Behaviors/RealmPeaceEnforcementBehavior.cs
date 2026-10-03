using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public class RealmPeaceEnforcementBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, float> _tyrantsDebtUntilByClanId = new Dictionary<string, float>();

        public override void RegisterEvents()
        {
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_TyrantsDebtUntilByClanId", ref _tyrantsDebtUntilByClanId);
            EnsureCollectionsInitialized();
        }

        public RoyalPeacePreview GetClaimFeudPreview(ClaimFeudRecord record, Clan actingClan)
        {
            Kingdom kingdom = ResolveKingdomForFeud(record);
            List<Clan> involved = ResolveFeudInvolvedClans(record);
            int cost = CalculateClaimFeudCost(record, involved);
            ClaimFeudBehavior feuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (record == null || feuds == null || !feuds.GetActiveFeuds().Contains(record))
                return UnavailablePreview(kingdom, cost, involved, new TextObject("{=BC_ClaimFeud_ReportInactive}The feud is no longer active."));
            var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            if (record.State == ClaimFeudState.WarActive || wars?.HasActiveWarForFeud(record.RecordId) == true)
            {
                if (wars == null || !wars.CanEnforceRoyalPeace(record, actingClan, out _))
                    return UnavailablePreview(kingdom, cost, involved, new TextObject("{=BC_RoyalPeace_ArmedUnavailable}The armed feud cannot safely be recalled to the Crown at present."));
            }
            return BuildPreview(kingdom, actingClan, cost, involved);
        }

        private static RoyalPeacePreview UnavailablePreview(Kingdom kingdom, int cost, List<Clan> involved, TextObject reason)
            => new RoyalPeacePreview(kingdom, false, cost, reason, reason.ToString(), involved);

        public bool TryEnforceClaimFeudPeace(ClaimFeudRecord record, Clan actingClan, out string report, bool showNotification = true)
        {
            report = null;
            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (record == null || feudBehavior == null)
            {
                report = "Claim feud unavailable.";
                return false;
            }

            RoyalPeacePreview preview = GetClaimFeudPreview(record, actingClan);
            if (!preview.IsEnabled)
            {
                report = preview.DisabledReason;
                return false;
            }

            List<Clan> involved = ResolveFeudInvolvedClans(record);
            bool armed = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.HasActiveWarForFeud(record.RecordId) == true;
            if (!NpcInfluenceBudgetService.TrySpend(
                actingClan,
                preview.InfluenceCost,
                NpcInfluenceExpenseKind.CrownEmergency,
                "royal_peace"))
            {
                report = "The crown no longer has enough available influence to impose peace.";
                return false;
            }

            if (!feudBehavior.TrySuppressByRoyalPeace(record.RecordId, actingClan, out report))
            {
                NpcInfluenceBudgetService.Refund(
                    actingClan,
                    preview.InfluenceCost,
                    NpcInfluenceExpenseKind.CrownEmergency,
                    "royal_peace");
                return false;
            }

            ApplyTyrantsDebt(actingClan);
            FeudalTitleRecord title = FeudalTitleBehavior.Instance?.GetTitle(record.TargetTitleId);
            ApplyRelationPenalties(preview.Kingdom, actingClan, involved, title?.Name?.ToString());
            // Armed settlements publish their own persisted outcome, including the player's popup.
            if (showNotification && !armed)
                ShowClaimFeudRoyalPeace(preview.Kingdom, actingClan, record, involved);
            BellumCivileLogger.Log($"Royal peace enforced over claim feud; kingdom={preview.Kingdom?.StringId ?? "null"} ruler={actingClan.StringId} feud={record.RecordId} cost={preview.InfluenceCost}.");
            return true;
        }

        public bool HasTyrantsDebt(Clan clan)
        {
            EnsureCollectionsInitialized();
            return clan != null
                && _tyrantsDebtUntilByClanId.TryGetValue(clan.StringId, out float untilDay)
                && untilDay > CurrentDay;
        }

        public int GetTyrantsDebtRemainingDays(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (clan == null || !_tyrantsDebtUntilByClanId.TryGetValue(clan.StringId, out float untilDay))
                return 0;

            return Math.Max(0, (int)Math.Ceiling(untilDay - CurrentDay));
        }

        private RoyalPeacePreview BuildPreview(
            Kingdom kingdom,
            Clan actingClan,
            int cost,
            List<Clan> involved)
        {
            TextObject tooltip = new TextObject("{=BC_RoyalPeace_HintReady}Spend {COST} influence to impose the {PEACE_NAME}. This will anger involved clans and burden the crown with Tyrant's Debt for one year.");
            tooltip.SetTextVariable("COST", cost);
            tooltip.SetTextVariable("PEACE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatRulerPeaceName(kingdom)));

            string disabledReason = string.Empty;
            bool enabled = true;
            if (kingdom == null || kingdom.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
            {
                enabled = false;
                disabledReason = new TextObject("{=BC_RoyalPeace_ErrNoRealm}This conflict is not attached to a valid realm.").ToString();
            }
            else if (actingClan == null || kingdom.RulingClan != actingClan)
            {
                enabled = false;
                disabledReason = new TextObject("{=BC_RoyalPeace_ErrNotRuler}Only the ruler may enforce the realm's peace.").ToString();
            }
            else if (!KingdomHasExternalWar(kingdom))
            {
                enabled = false;
                disabledReason = new TextObject("{=BC_RoyalPeace_ErrNoExternalWar}The realm's peace can only be imposed during an external war.").ToString();
            }
            else if (!NpcInfluenceBudgetService.CanAfford(
                actingClan,
                cost,
                NpcInfluenceExpenseKind.CrownEmergency))
            {
                enabled = false;
                TextObject influenceText = new TextObject("{=BC_RoyalPeace_ErrInfluence}The crown lacks the influence needed to impose peace. Required: {COST}.");
                influenceText.SetTextVariable("COST", cost);
                disabledReason = influenceText.ToString();
            }

            if (!enabled)
                tooltip = new TextObject("{=!}" + disabledReason);
            else if (HasTyrantsDebt(actingClan))
            {
                TextObject debtText = new TextObject("{=BC_RoyalPeace_HintDebt}Spend {COST} influence to impose the {PEACE_NAME}. The crown is already weakened by Tyrant's Debt for {DAYS} more days.");
                debtText.SetTextVariable("COST", cost);
                debtText.SetTextVariable("PEACE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatRulerPeaceName(kingdom)));
                debtText.SetTextVariable("DAYS", GetTyrantsDebtRemainingDays(actingClan));
                tooltip = debtText;
            }

            return new RoyalPeacePreview(kingdom, enabled, cost, tooltip, disabledReason, involved ?? new List<Clan>());
        }

        private int CalculateClaimFeudCost(ClaimFeudRecord record, List<Clan> involved)
        {
            Clan claimant = ResolveClan(record?.ClaimantClanId);
            Clan holder = ResolveClan(record?.HolderClanId);
            int cost = C.RoyalPeaceFeudBaseInfluenceCost;
            cost += GetHierarchyTier(claimant) * C.RoyalPeaceFeudLeaderTierCost;
            cost += GetHierarchyTier(holder) * C.RoyalPeaceFeudLeaderTierCost;

            foreach (Clan supporter in (involved ?? new List<Clan>()).Where(clan => clan != claimant && clan != holder))
                cost += GetHierarchyTier(supporter) * C.RoyalPeaceFeudSupporterTierCost;

            return Math.Max(0, cost);
        }

        private void ApplyTyrantsDebt(Clan clan)
        {
            if (clan == null)
                return;

            EnsureCollectionsInitialized();
            int daysPerYear = Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
            _tyrantsDebtUntilByClanId[clan.StringId] = CurrentDay + daysPerYear * C.RoyalPeaceTyrantsDebtYears;
        }

        private void ApplyRelationPenalties(Kingdom kingdom, Clan rulerClan, List<Clan> involved, string contextText)
        {
            if (kingdom?.Clans == null || rulerClan?.Leader == null)
                return;

            HashSet<Clan> involvedSet = new HashSet<Clan>((involved ?? new List<Clan>()).Where(IsValidClan));
            foreach (Clan clan in kingdom.Clans.Where(IsValidClan))
            {
                if (clan == rulerClan || clan.Leader == null)
                    continue;

                int penalty = involvedSet.Contains(clan)
                    ? C.RoyalPeaceInvolvedRelationPenalty
                    : C.RoyalPeaceRealmRelationPenalty;
                if (penalty != 0)
                {
                    bool directlyInvolved = involvedSet.Contains(clan);
                    RelationMemoryService.ApplyChange(
                        clan.Leader,
                        rulerClan.Leader,
                        penalty,
                        false,
                        directlyInvolved
                            ? RelationMemorySources.EnforcedPeaceOnMyFeud
                            : RelationMemorySources.CourtPolitics,
                        directlyInvolved ? 10f : 5f,
                        RelationMemoryScope.House,
                        contextText);
                }
            }
        }

        private void ShowClaimFeudRoyalPeace(Kingdom kingdom, Clan enforcingClan, ClaimFeudRecord record, List<Clan> involved)
        {
            Clan claimant = ResolveClan(record?.ClaimantClanId);
            Clan holder = ResolveClan(record?.HolderClanId);
            FeudalTitleRecord title = FeudalTitleBehavior.Instance?.GetTitle(record?.TargetTitleId);
            TextObject text = new TextObject("{=BC_ClaimFeud_RoyalPeace}With the realm threatened by foreign war, {RULER_NAME} has imposed the {PEACE_NAME} upon the feud between the {CLAIMANT_CLAN} and the {HOLDER_CLAN} over the {TITLE_NAME}. The matter is silenced for now, though neither house forgets its claim.");
            text.SetTextVariable("RULER_NAME", enforcingClan?.Leader?.Name ?? new TextObject("?"));
            text.SetTextVariable("PEACE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatRulerPeaceName(kingdom)));
            text.SetTextVariable("CLAIMANT_CLAN", claimant?.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_CLAN", holder?.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder)));
            BellumCivileNotifications.Show(text, BellumNotificationColors.Warning, primaryKingdom: kingdom, primaryClan: enforcingClan, isMajorEvent: true);
        }

        private List<Clan> ResolveFeudInvolvedClans(ClaimFeudRecord record)
        {
            List<Clan> clans = new List<Clan>();
            AddClan(clans, ResolveClan(record?.ClaimantClanId));
            AddClan(clans, ResolveClan(record?.HolderClanId));
            foreach (string id in SplitIds(record?.ClaimantSupporterIds))
                AddClan(clans, ResolveClan(id));
            foreach (string id in SplitIds(record?.HolderSupporterIds))
                AddClan(clans, ResolveClan(id));
            return clans;
        }

        internal static Kingdom ResolveKingdomForFeud(ClaimFeudRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record?.ParentKingdomId))
            {
                Kingdom byId = Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == record.ParentKingdomId);
                return byId;
            }

            return ResolveClan(record?.ClaimantClanId)?.Kingdom ?? ResolveClan(record?.HolderClanId)?.Kingdom;
        }

        private static int GetHierarchyTier(Clan clan)
        {
            if (clan == null)
                return 1;

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            if (titleBehavior == null)
                return Math.Max(1, clan.Tier);

            FeudalTitleRecord highest = titleBehavior.GetTitlesHeldByClan(clan, deJure: true)
                .Concat(titleBehavior.GetTitlesHeldByClan(clan, deJure: false))
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .FirstOrDefault();

            return highest != null
                ? Math.Max(1, (int)highest.TitleType + 1)
                : Math.Max(1, Math.Min(3, clan.Tier));
        }

        private static bool KingdomHasExternalWar(Kingdom kingdom)
        {
            return kingdom != null
                && Kingdom.All.Any(other => other != null
                                         && other != kingdom
                                         && !other.IsEliminated
                                         && !other.IsMinorFaction
                                         && !other.IsBanditFaction
                                         && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)
                                         && kingdom.IsAtWarWith(other));
        }

        private static IEnumerable<string> SplitIds(string ids)
        {
            return string.IsNullOrWhiteSpace(ids)
                ? Enumerable.Empty<string>()
                : ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim());
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static void AddClan(List<Clan> clans, Clan clan)
        {
            if (IsValidClan(clan) && !clans.Contains(clan))
                clans.Add(clan);
        }

        private static bool IsValidClan(Clan clan)
        {
            return clan != null && !clan.IsEliminated;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private void EnsureCollectionsInitialized()
        {
            if (_tyrantsDebtUntilByClanId == null)
                _tyrantsDebtUntilByClanId = new Dictionary<string, float>();
        }
    }

    public sealed class RoyalPeacePreview
    {
        public RoyalPeacePreview(Kingdom kingdom, bool isEnabled, int influenceCost, TextObject hint, string disabledReason, IReadOnlyList<Clan> involvedClans)
        {
            Kingdom = kingdom;
            IsEnabled = isEnabled;
            InfluenceCost = influenceCost;
            Hint = hint ?? new TextObject(string.Empty);
            DisabledReason = disabledReason ?? string.Empty;
            InvolvedClans = involvedClans ?? new List<Clan>();
        }

        public Kingdom Kingdom { get; }
        public bool IsEnabled { get; }
        public int InfluenceCost { get; }
        public TextObject Hint { get; }
        public string DisabledReason { get; }
        public IReadOnlyList<Clan> InvolvedClans { get; }
    }
}
