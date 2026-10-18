using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public class RealmPeaceEnforcementBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, float> _tyrantsDebtUntilByClanId = new Dictionary<string, float>();
        private Dictionary<string, float> _royalPeaceUntilByRealmId = new Dictionary<string, float>();
        private List<RoyalPeaceDecreeRecord> _decrees = new List<RoyalPeaceDecreeRecord>();
        private Kingdom _executingRealm;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ResumeDecrees);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_TyrantsDebtUntilByClanId", ref _tyrantsDebtUntilByClanId);
            dataStore.SyncData("BC_RoyalPeaceUntilByRealm", ref _royalPeaceUntilByRealmId);
            dataStore.SyncData("BC_RoyalPeaceDecrees", ref _decrees);
            EnsureCollectionsInitialized();
        }

        public bool IsExecutingRealmPeace(Kingdom realm) => realm != null && _executingRealm == realm;
        internal bool OwnsUnfinishedFeudSettlement(string feudId)
        {
            EnsureCollectionsInitialized();
            return !string.IsNullOrEmpty(feudId) && _decrees.Any(d => !d.Complete && d.StartedFeudId == feudId);
        }
        public bool HasRoyalPeace(Kingdom realm) => GetRoyalPeaceRemainingDays(realm) > 0;
        public int GetRoyalPeaceRemainingDays(Kingdom realm)
        {
            EnsureCollectionsInitialized();
            return realm != null && !realm.IsEliminated && _royalPeaceUntilByRealmId.TryGetValue(realm.StringId, out float until)
                ? Math.Max(0, (int)Math.Ceiling(until - CurrentDay)) : 0;
        }

        public TextObject GetRoyalPeaceBlockReason(Kingdom realm)
        {
            int days = GetRoyalPeaceRemainingDays(realm);
            return days <= 0 ? null : new TextObject("{=BC_RoyalPeaceRestriction}Royal Peace is in force. The Crown has forbidden private feuds, private wars and claimed-title revocations throughout {REALM}. Remaining duration: {DAYS} days. Your claim is preserved.")
                .SetTextVariable("REALM", realm.Name).SetTextVariable("DAYS", days);
        }

        public List<ClaimFeudRecord> GetRealmFeuds(Kingdom realm) => realm == null ? new List<ClaimFeudRecord>()
            : Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>()?.GetActiveFeuds()
                .Where(f => ResolveKingdomForFeud(f) == realm).ToList() ?? new List<ClaimFeudRecord>();

        public RoyalPeacePreview GetClaimFeudPreview(ClaimFeudRecord record, Clan actingClan)
        {
            var feuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (record == null || feuds == null || !feuds.GetActiveFeuds().Contains(record))
                return new RoyalPeacePreview(null, false, 0, new TextObject("{=BC_ClaimFeud_ReportInactive}The feud is no longer active."), "", null);
            return GetRealmPreview(ResolveKingdomForFeud(record), actingClan);
        }

        public RoyalPeacePreview GetRealmPreview(Kingdom kingdom, Clan actingClan)
        {
            EnsureCollectionsInitialized();
            var feuds = GetRealmFeuds(kingdom);
            var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            int armed = feuds.Count(f => f.State == ClaimFeudState.WarActive || wars?.HasActiveWarForFeud(f.RecordId) == true);
            bool wartime = KingdomHasExternalWar(kingdom);
            int cost = CourtRoyalPeaceRules.Cost(feuds.Count - armed, armed, wartime);
            var involved = feuds.SelectMany(ResolveFeudInvolvedClans).Distinct().ToList();
            TextObject blocked = null;
            if (kingdom == null || kingdom.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                blocked = new TextObject("{=BC_RoyalPeace_ErrNoRealm}This conflict is not attached to a valid realm.");
            else if (actingClan == null || actingClan.IsEliminated || actingClan.Leader == null || kingdom.RulingClan != actingClan)
                blocked = new TextObject("{=BC_RoyalPeace_ErrNotRuler}Only the ruler may enforce the realm's peace.");
            else if (HasRoyalPeace(kingdom)) blocked = GetRoyalPeaceBlockReason(kingdom);
            else if (_decrees.Any(d => d.Realm == kingdom && !d.Complete && !d.Interrupted))
                blocked = new TextObject("{=BC_RoyalPeaceSettling}An earlier Royal Peace decree is still being settled.");
            else if (ElectiveSuccessionBehavior.Instance?.PendingDeposition(kingdom) != null || CrownAccessionBehavior.Instance?.IsPending(kingdom) == true)
                blocked = new TextObject("{=BC_CrownActionsUnavailable}The Crown cannot undertake new business while its authority is unsettled.");
            else if (feuds.Count == 0)
                blocked = new TextObject("{=BC_CrownPeaceNoFeuds}There are no active feuds in this realm to suppress.");
            else if (!NpcInfluenceBudgetService.CanAfford(actingClan, cost, NpcInfluenceExpenseKind.CrownEmergency))
                blocked = new TextObject("{=BC_RoyalPeace_ErrInfluence}The crown lacks the influence needed to impose peace. Required: {COST}.").SetTextVariable("COST", cost);

            bool ready = feuds.All(f => !(f.State == ClaimFeudState.WarActive || wars?.HasActiveWarForFeud(f.RecordId) == true)
                || wars != null && wars.CanEnforceRoyalPeace(f, actingClan, out _));
            var hint = blocked ?? new TextObject("{=BC_RealmPeacePreview}End all {FEUDS} private feuds and {WARS} private wars in {REALM}. Cost: {COST} influence ({PRICING}). Royal Peace forbids new private conflicts for one year. Involved houses: {INVOLVED} relations; other noble houses: {OTHERS}. Tyrant's Debt halves positive daily influence gain for one year.")
                .SetTextVariable("FEUDS", feuds.Count - armed).SetTextVariable("WARS", armed)
                .SetTextVariable("REALM", kingdom?.Name ?? new TextObject("?"))
                .SetTextVariable("COST", cost)
                .SetTextVariable("PRICING", new TextObject(wartime ? "{=BC_RoyalPeaceWartime}wartime" : "{=BC_RoyalPeacePeacetime}peacetime, triple cost"))
                .SetTextVariable("INVOLVED", CourtRoyalPeaceRules.RelationPenalty(true, wartime))
                .SetTextVariable("OTHERS", CourtRoyalPeaceRules.RelationPenalty(false, wartime));
            return new RoyalPeacePreview(kingdom, blocked == null, cost, hint, blocked?.ToString(), involved,
                feuds.Count - armed, armed, wartime, ready);
        }

        // Legacy callers announce the same realm-wide decree instead of bypassing its notice period.
        public bool TryEnforceClaimFeudPeace(ClaimFeudRecord record, Clan actingClan, out string report, bool showNotification = true)
        {
            report = null;
            var preview = GetClaimFeudPreview(record, actingClan);
            if (!preview.IsEnabled) { report = preview.Hint.ToString(); return false; }
            return CourtAgendaBehavior.Current?.TryScheduleRoyalPeace(preview.Kingdom, actingClan, out report) == true;
        }

        internal bool TryEnforceRealmPeace(Kingdom realm, Clan ruler, out string report)
        {
            var preview = GetRealmPreview(realm, ruler);
            report = preview.DisabledReason;
            if (!preview.IsEnabled || !preview.SettlementReady) return false;
            var feuds = GetRealmFeuds(realm);
            // Capture everything before payment and before returning any temporary kingdoms.
            var decree = new RoyalPeaceDecreeRecord {
                Id = Guid.NewGuid().ToString("N"), Realm = realm, Enforcer = ruler, InfluenceCost = preview.InfluenceCost,
                Wartime = preview.Wartime, UntilDay = CurrentDay + Math.Max(1, CampaignTime.DaysInYear) * C.RoyalPeaceTyrantsDebtYears,
                FeudIds = feuds.Select(f => f.RecordId).ToList(),
                InvolvedClanIds = preview.InvolvedClans.Select(c => c.StringId).ToList(),
                PlayerInvolved = Clan.PlayerClan != null && (Clan.PlayerClan.Kingdom == realm || preview.InvolvedClans.Contains(Clan.PlayerClan)),
                AffectedTitles = feuds.Select(f => FeudalTitleDisplayHelper.FormatTitleName(FeudalTitleBehavior.Instance?.GetTitle(f.TargetTitleId))).ToList()
            };
            if (!NpcInfluenceBudgetService.TrySpend(ruler, preview.InfluenceCost, NpcInfluenceExpenseKind.CrownEmergency, "royal_peace")) return false;
            _decrees.Add(decree);
            _royalPeaceUntilByRealmId[realm.StringId] = decree.UntilDay;
            _tyrantsDebtUntilByClanId[ruler.StringId] = decree.UntilDay;
            ContinueDecree(decree);
            report = decree.Complete ? "Royal Peace enforced throughout the realm." : "Royal Peace recorded; settlement is pending.";
            BellumCivileLogger.Log($"Realm royal peace charged; decree={decree.Id}; realm={realm.StringId}; cost={decree.InfluenceCost}; wartime={decree.Wartime}; feuds={decree.FeudIds.Count}; complete={decree.Complete}.");
            return true;
        }

        private void ResumeDecrees()
        {
            EnsureCollectionsInitialized();
            foreach (var decree in _decrees.Where(d => !d.Complete && !d.Interrupted).ToList()) ContinueDecree(decree);
            foreach (var decree in _decrees.Where(d => (d.Complete || d.Interrupted) && !d.NoticePublished).ToList()) PublishDecree(decree);
            _decrees.RemoveAll(d => d.Complete && d.UntilDay < CurrentDay);
        }

        private void ContinueDecree(RoyalPeaceDecreeRecord decree)
        {
            if (decree.Realm == null || decree.Realm.IsEliminated || decree.Enforcer == null)
            {
                decree.Interrupted = true;
                return;
            }
            if (decree.StartedFeudId != null)
            {
                decree.Interrupted = true;
                BellumCivileLogger.Log($"Royal peace settlement interrupted; decree={decree.Id}; feud={decree.StartedFeudId}; will not replay a partially applied settlement.");
                PublishDecree(decree);
                return;
            }
            var behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (behavior == null) return;
            _executingRealm = decree.Realm;
            try
            {
                foreach (string id in decree.FeudIds.Where(id => !decree.SettledFeudIds.Contains(id)).ToList())
                {
                    var feud = behavior.GetActiveFeuds().FirstOrDefault(f => f.RecordId == id);
                    if (feud == null) { decree.SettledFeudIds.Add(id); continue; }
                    var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
                    if ((feud.State == ClaimFeudState.WarActive || wars?.HasActiveWarForFeud(id) == true)
                        && (wars == null || !wars.CanEnforceRoyalPeace(feud, decree.Realm.RulingClan, out _))) return;
                    decree.StartedFeudId = id;
                    if (!behavior.TrySuppressByRoyalPeace(id, decree.Realm.RulingClan, out _)) { decree.StartedFeudId = null; return; }
                    decree.SettledFeudIds.Add(id);
                    decree.StartedFeudId = null;
                }
                ApplyRelationPenalties(decree);
                decree.Complete = true;
                PublishDecree(decree);
            }
            catch (Exception ex)
            {
                decree.Interrupted = true;
                BellumCivileLogger.Log($"Royal peace settlement failed; decree={decree.Id}; feud={decree.StartedFeudId}; no automatic replay: {ex}");
            }
            finally { _executingRealm = null; }
        }

        private void ApplyRelationPenalties(RoyalPeaceDecreeRecord decree)
        {
            if (decree.Enforcer.Leader == null) return;
            var involved = new HashSet<string>(decree.InvolvedClanIds);
            foreach (var clan in decree.Realm.Clans.Concat(decree.InvolvedClanIds.Select(ResolveClan)).Where(IsNobleClan).Distinct())
            {
                if (clan == decree.Enforcer || clan.Leader == null || decree.PenalizedClanIds.Contains(clan.StringId)) continue;
                bool directlyInvolved = involved.Contains(clan.StringId);
                // Record before dispatch: relation events may invoke third-party listeners.
                decree.PenalizedClanIds.Add(clan.StringId);
                RelationMemoryService.ApplyChange(clan.Leader, decree.Enforcer.Leader,
                    CourtRoyalPeaceRules.RelationPenalty(directlyInvolved, decree.Wartime), false,
                    directlyInvolved ? RelationMemorySources.EnforcedPeaceOnMyFeud : RelationMemorySources.CourtPolitics,
                    directlyInvolved ? 10f : 5f, RelationMemoryScope.House, decree.Realm.Name.ToString());
            }
        }

        private static void PublishDecree(RoyalPeaceDecreeRecord decree)
        {
            if (decree.NoticePublished || decree.Realm == null || decree.Enforcer == null) return;
            try
            {
                var behavior = ConflictOutcomeBehavior.Current;
                var notice = behavior?.Begin("realm_peace:" + decree.Id, decree.Realm, null);
                if (notice == null) return;
                notice.PlayerInvolved = decree.PlayerInvolved;
                ConflictOutcomeBehavior.Capture(notice, "REALM", decree.Realm.Name);
                ConflictOutcomeBehavior.Capture(notice, "RULER", decree.Enforcer.Leader?.Name ?? decree.Enforcer.Name);
                ConflictOutcomeBehavior.Capture(notice, "DATE", new TextObject("{=!}" + CampaignTime.Days(decree.UntilDay)));
                behavior.Publish(notice, decree.Interrupted ? "realm_royal_peace_interrupted" : "realm_royal_peace",
                    string.Join("\n", decree.AffectedTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct()));
                decree.NoticePublished = notice.Ready;
            }
            catch (Exception ex)
            {
                // The persisted notice ID deduplicates delivery; retry presentation, never settlement.
                BellumCivileLogger.Log($"Royal peace notice deferred; decree={decree.Id}: {ex}");
            }
        }

        public bool HasTyrantsDebt(Clan clan) => GetTyrantsDebtRemainingDays(clan) > 0;
        public int GetTyrantsDebtRemainingDays(Clan clan)
        {
            EnsureCollectionsInitialized();
            return clan != null && _tyrantsDebtUntilByClanId.TryGetValue(clan.StringId, out float until)
                ? Math.Max(0, (int)Math.Ceiling(until - CurrentDay)) : 0;
        }

        private static List<Clan> ResolveFeudInvolvedClans(ClaimFeudRecord record)
        {
            return new[] { record.ClaimantClanId, record.HolderClanId }
                .Concat((record.ClaimantSupporterIds ?? "").Split(','))
                .Concat((record.HolderSupporterIds ?? "").Split(','))
                .Select(id => ResolveClan(id?.Trim())).Where(c => c != null && !c.IsEliminated).Distinct().ToList();
        }

        internal static Kingdom ResolveKingdomForFeud(ClaimFeudRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record?.ParentKingdomId))
                return Kingdom.All.FirstOrDefault(k => k != null && k.StringId == record.ParentKingdomId);
            return ResolveClan(record?.ClaimantClanId)?.Kingdom ?? ResolveClan(record?.HolderClanId)?.Kingdom;
        }

        internal static bool KingdomHasExternalWar(Kingdom kingdom) => kingdom != null && Kingdom.All.Any(other =>
            other != null && other != kingdom && !other.IsEliminated && !other.IsMinorFaction && !other.IsBanditFaction
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other) && kingdom.IsAtWarWith(other));

        private static Clan ResolveClan(string id) => string.IsNullOrWhiteSpace(id) ? null : Clan.All.FirstOrDefault(c => c.StringId == id);
        private static bool IsNobleClan(Clan clan) => clan != null && !clan.IsEliminated && !clan.IsMinorFaction && !clan.IsUnderMercenaryService;
        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
        private void EnsureCollectionsInitialized()
        {
            if (_tyrantsDebtUntilByClanId == null) _tyrantsDebtUntilByClanId = new Dictionary<string, float>();
            if (_royalPeaceUntilByRealmId == null) _royalPeaceUntilByRealmId = new Dictionary<string, float>();
            if (_decrees == null) _decrees = new List<RoyalPeaceDecreeRecord>();
        }
    }

    public sealed class RoyalPeacePreview
    {
        public RoyalPeacePreview(Kingdom kingdom, bool isEnabled, int influenceCost, TextObject hint, string disabledReason,
            IReadOnlyList<Clan> involvedClans, int feudCount = 0, int warCount = 0, bool wartime = false, bool settlementReady = true)
        {
            Kingdom = kingdom; IsEnabled = isEnabled; InfluenceCost = influenceCost;
            Hint = hint ?? new TextObject(string.Empty); DisabledReason = disabledReason ?? string.Empty;
            InvolvedClans = involvedClans ?? new List<Clan>(); FeudCount = feudCount; WarCount = warCount;
            Wartime = wartime; SettlementReady = settlementReady;
        }
        public Kingdom Kingdom { get; }
        public bool IsEnabled { get; }
        public int InfluenceCost { get; }
        public TextObject Hint { get; }
        public string DisabledReason { get; }
        public IReadOnlyList<Clan> InvolvedClans { get; }
        public int FeudCount { get; }
        public int WarCount { get; }
        public bool Wartime { get; }
        public bool SettlementReady { get; }
    }
}
