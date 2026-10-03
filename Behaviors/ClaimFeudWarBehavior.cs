using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Runs the armed phase of private claim feuds. This intentionally stays separate from
    /// FactionObject civil wars because feud wars are peer conflicts, not crown-vs-rebel wars.
    /// </summary>
    public partial class ClaimFeudWarBehavior : CampaignBehaviorBase
    {
        private static int _peaceHandlingSuppressionDepth;
        private List<ClaimFeudWarRecord> _wars = new List<ClaimFeudWarRecord>();
        private int _yearlyClaimantVictories;
        private int _yearlyHolderVictories;
        private int _yearlyWhitePeaces;

        public static bool IsPeaceHandlingSuppressed => _peaceHandlingSuppressionDepth > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnObjectiveSettlementOwnerChanged);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_ClaimFeudWars", ref _wars);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyClaimantVictories", ref _yearlyClaimantVictories);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyHolderVictories", ref _yearlyHolderVictories);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyWhitePeaces", ref _yearlyWhitePeaces);
            EnsureCollectionsInitialized();
        }

        public ClaimFeudWarYearlyTelemetry ConsumeYearlyTelemetry()
        {
            ClaimFeudWarYearlyTelemetry result = new ClaimFeudWarYearlyTelemetry(
                _yearlyClaimantVictories,
                _yearlyHolderVictories,
                _yearlyWhitePeaces);
            _yearlyClaimantVictories = 0;
            _yearlyHolderVictories = 0;
            _yearlyWhitePeaces = 0;
            return result;
        }

        public bool IsTemporaryFeudKingdom(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            return kingdom != null
                && _wars.Any(war => war != null
                                 && war.IsActive
                                 && (war.ClaimantKingdomId == kingdom.StringId || war.HolderKingdomId == kingdom.StringId));
        }

        public bool IsTemporaryFeudKingdomForParent(Kingdom kingdom, Kingdom parentKingdom)
        {
            EnsureCollectionsInitialized();
            return kingdom != null
                && parentKingdom != null
                && _wars.Any(war => war != null
                                 && war.IsActive
                                 && war.ParentKingdomId == parentKingdom.StringId
                                 && (war.ClaimantKingdomId == kingdom.StringId || war.HolderKingdomId == kingdom.StringId));
        }

        public Kingdom GetParentKingdomForTemporaryRealm(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return null;

            ClaimFeudWarRecord war = _wars.FirstOrDefault(record => record != null
                && record.IsActive
                && (record.ClaimantKingdomId == kingdom.StringId || record.HolderKingdomId == kingdom.StringId));
            return ResolveKingdom(war?.ParentKingdomId);
        }

        public IReadOnlyList<ClaimFeudWarRecord> GetActiveWars()
        {
            EnsureCollectionsInitialized();
            return _wars.Where(war => war != null && war.IsActive).ToList();
        }

        public bool HasActiveWarForFeud(string feudRecordId)
        {
            if (string.IsNullOrWhiteSpace(feudRecordId))
                return false;

            EnsureCollectionsInitialized();
            return _wars.Any(war => war != null
                                  && war.IsActive
                                  && string.Equals(war.FeudRecordId, feudRecordId, StringComparison.Ordinal));
        }

        public bool TryCancelActiveWarForFeud(string feudRecordId, string reason)
        {
            if (string.IsNullOrWhiteSpace(feudRecordId))
                return false;

            EnsureCollectionsInitialized();
            ClaimFeudWarRecord war = _wars.FirstOrDefault(record => record != null
                && record.IsActive
                && string.Equals(record.FeudRecordId, feudRecordId, StringComparison.Ordinal));
            if (war == null)
                return false;

            ResolveWar(war, ClaimFeudWarOutcome.WhitePeace, reason ?? "claim feud became invalid");
            return true;
        }

        public bool CanEnforceRoyalPeace(ClaimFeudRecord feud, Clan ruler, out string report)
        {
            EnsureCollectionsInitialized();
            report = "The armed feud cannot safely be recalled to the Crown at present.";
            if (feud == null || ruler == null) return false;
            var war = _wars.FirstOrDefault(w => w != null && w.IsActive && w.FeudRecordId == feud.RecordId);
            var parent = ResolveKingdom(war?.ParentKingdomId);
            if (war == null || parent == null || parent.IsEliminated || parent.RulingClan != ruler
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(parent)
                || parent != RealmPeaceEnforcementBehavior.ResolveKingdomForFeud(feud)
                || war.PendingResolutionOutcome != ClaimFeudWarOutcome.None
                || war.ClaimantLeaderClanId != feud.ClaimantClanId || war.HolderLeaderClanId != feud.HolderClanId
                || war.TargetTitleId != feud.TargetTitleId) return false;
            var claimant = ResolveKingdom(war.ClaimantKingdomId);
            var holder = ResolveKingdom(war.HolderKingdomId);
            if (claimant == null || claimant.IsEliminated || holder == null || holder.IsEliminated
                || claimant == parent || holder == parent || claimant == holder) return false;
            report = null;
            return true;
        }

        internal bool TryEnforceRoyalPeace(ClaimFeudRecord feud, Clan ruler, out string report)
        {
            if (!CanEnforceRoyalPeace(feud, ruler, out report)) return false;
            var war = _wars.First(w => w != null && w.IsActive && w.FeudRecordId == feud.RecordId);
            ResolveWar(war, ClaimFeudWarOutcome.WhitePeace, "peace imposed by the Crown during foreign war", peaceEnforcer: ruler);
            report = new TextObject("{=BC_ClaimFeud_ReportSuppressed}The feud was suppressed by the realm's peace.").ToString();
            return true;
        }

        public bool IsClanCommittedToActiveWar(Clan clan, string excludingFeudRecordId = null)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return false;

            EnsureCollectionsInitialized();
            return _wars.Any(war => war != null
                && war.IsActive
                && !string.Equals(war.FeudRecordId, excludingFeudRecordId, StringComparison.Ordinal)
                && (DecodeIds(war.ClaimantClanIds).Contains(clan.StringId)
                    || DecodeIds(war.HolderClanIds).Contains(clan.StringId)));
        }

        internal bool ShouldBlockRecruitment(Clan clan, Kingdom targetKingdom)
        {
            return clan != null && targetKingdom != null && clan.Kingdom != targetKingdom
                && (IsClanCommittedToActiveWar(clan) || IsTemporaryFeudKingdom(clan.Kingdom));
        }

        public bool AreOpposingFeudKingdoms(Kingdom first, Kingdom second)
        {
            if (first == null || second == null || first == second)
                return false;

            EnsureCollectionsInitialized();
            return _wars.Any(war => war != null
                                 && war.IsActive
                                 && ((war.ClaimantKingdomId == first.StringId && war.HolderKingdomId == second.StringId)
                                  || (war.ClaimantKingdomId == second.StringId && war.HolderKingdomId == first.StringId)));
        }

        public bool ShouldBlockExternalWar(IFaction factionDeclaresWar, IFaction factionDeclaredWar, out TextObject reason)
        {
            reason = null;
            Kingdom first = factionDeclaresWar as Kingdom;
            Kingdom second = factionDeclaredWar as Kingdom;
            if (first == null || second == null)
                return false;

            bool firstFeud = IsTemporaryFeudKingdom(first);
            bool secondFeud = IsTemporaryFeudKingdom(second);
            if (!firstFeud && !secondFeud)
                return false;

            if (AreOpposingFeudKingdoms(first, second))
                return false;

            reason = new TextObject("{=BC_Diplo_NoFeudIntervention}They are locked in a private feud; outside intervention is forbidden.");
            return true;
        }

        public bool TryResolveUnscriptedPeace(Kingdom first, Kingdom second)
        {
            EnsureCollectionsInitialized();
            ClaimFeudWarRecord war = _wars.FirstOrDefault(active => active != null
                                                                 && active.IsActive
                                                                 && ((active.ClaimantKingdomId == first?.StringId && active.HolderKingdomId == second?.StringId)
                                                                  || (active.ClaimantKingdomId == second?.StringId && active.HolderKingdomId == first?.StringId)));
            if (war == null)
                return false;

            ResolveWar(war, ClaimFeudWarOutcome.WhitePeace, "unscripted peace");
            return true;
        }

        public bool TryResolveClaimantSurrender(string feudRecordId, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;
            if (string.IsNullOrWhiteSpace(feudRecordId))
            {
                report = "The active feud war could not be identified.";
                return false;
            }

            ClaimFeudWarRecord war = _wars.FirstOrDefault(record => record != null
                && record.IsActive
                && string.Equals(record.FeudRecordId, feudRecordId, StringComparison.Ordinal));
            if (war == null)
            {
                report = "The armed feud could not be found and was not altered.";
                return false;
            }

            ResolveWar(
                war,
                ClaimFeudWarOutcome.HolderVictory,
                "claimant surrendered the active feud",
                claimantDefaulted: true);
            report = "The claimant surrendered and both sides were restored to their parent realm.";
            return true;
        }

        public string BuildDebugReport()
        {
            EnsureCollectionsInitialized();
            if (_wars.Count == 0)
                return "No claim feud wars have been recorded.";

            List<string> lines = new List<string>();
            foreach (ClaimFeudWarRecord war in _wars.OrderByDescending(w => w != null && w.IsActive).ThenBy(w => w?.StartedDay ?? 0f))
            {
                if (war == null)
                    continue;

                FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(war.TargetTitleId);
                lines.Add(
                    $"{(war.IsActive ? "active" : "resolved")}: feud={war.FeudRecordId}; title={title?.Name ?? war.TargetTitleId}; parent={war.ParentKingdomId}; claimant_kingdom={war.ClaimantKingdomId}; holder_kingdom={war.HolderKingdomId}; claimant_side={war.ClaimantClanIds}; holder_side={war.HolderClanIds}; objective_control={(war.ClaimantControlsObjective ? "claimant" : "holder")}; claimant_ever_controlled={war.ClaimantHasControlledObjective}; pending_capture_winner={war.PendingCaptureWinnerClanId}; started_day={war.StartedDay:0}.");
            }

            return string.Join("\n", lines);
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            RepairOrphanedTemporaryFeudKingdoms();
            StartPendingFeudWars();
            TickActiveFeudWars();
        }

        private void StartPendingFeudWars()
        {
            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (feudBehavior == null)
                return;

            foreach (ClaimFeudRecord feud in feudBehavior.GetActiveFeuds()
                .Where(record => record != null && record.State == ClaimFeudState.DefiedPendingWar)
                .ToList())
            {
                if (_wars.Any(war => war != null && war.IsActive && war.FeudRecordId == feud.RecordId))
                    continue;

                TryStartFeudWar(feud);
            }
        }

        private bool TryStartFeudWar(ClaimFeudRecord feud)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || feud == null)
                return false;

            Clan claimant = ResolveClan(feud.ClaimantClanId);
            Clan holder = ResolveClan(feud.HolderClanId);
            FeudalTitleRecord title = titleBehavior.GetTitle(feud.TargetTitleId);
            Kingdom parent = ResolveKingdom(feud.ParentKingdomId) ?? claimant?.Kingdom ?? holder?.Kingdom;
            if (!IsValidWarLeader(claimant) || !IsValidWarLeader(holder)
                || title == null || !title.IsActive || parent == null || parent.IsEliminated)
            {
                feud.SetDebugReason($"{feud.DebugReason}; open war launch failed missing context");
                return false;
            }

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (HasActiveCivilWar(parent))
            {
                feudBehavior?.PauseFeudForConflict(feud, "civil war has suspended private warfare");
                return false;
            }

            if (CountExternalWars(parent) > 0)
            {
                feudBehavior?.PauseFeudForConflict(feud, "the realm is at foreign war, so private banners cannot yet be raised");
                return false;
            }

            if (IsClanCommittedToActiveWar(claimant, feud.RecordId)
                || IsClanCommittedToActiveWar(holder, feud.RecordId))
            {
                feudBehavior?.PauseFeudForConflict(feud, "one of the principal houses is already fighting another private war");
                return false;
            }

            List<Clan> claimantSide = BuildSide(feud.ClaimantClanId, feud.ClaimantSupporterIds, parent, feud.RecordId);
            List<Clan> holderSide = BuildSide(feud.HolderClanId, feud.HolderSupporterIds, parent, feud.RecordId);
            if (claimantSide.Count == 0 || holderSide.Count == 0)
                return false;

            Settlement unsafeClaimant;
            Settlement unsafeHolder;
            bool claimantUnsafe = IsLeaderUnsafeForFeudWar(claimant, holderSide, out unsafeClaimant);
            bool holderUnsafe = IsLeaderUnsafeForFeudWar(holder, claimantSide, out unsafeHolder);
            if (claimantUnsafe || holderUnsafe)
            {
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"Delayed feud war launch for unsafe leader location; feud={feud.RecordId}; claimant_unsafe={unsafeClaimant?.StringId ?? "none"}; holder_unsafe={unsafeHolder?.StringId ?? "none"}.",
                    requestInGameDisplay: true);
                return false;
            }

            RemoveSidesFromRebelFactions(claimantSide.Concat(holderSide));

            string baseId = "bc_feud_" + Sanitize(feud.RecordId);
            Kingdom claimantKingdom = CreateTemporaryFeudKingdom(baseId + "_claimant", parent, claimant, claimant);
            Kingdom holderKingdom = CreateTemporaryFeudKingdom(baseId + "_holder", parent, holder, holder);
            if (claimantKingdom == null || holderKingdom == null)
            {
                DestroyTemporaryKingdom(claimantKingdom, parent);
                DestroyTemporaryKingdom(holderKingdom, parent);
                return false;
            }

            Dictionary<string, float> influenceSnapshot = CaptureInfluenceSnapshot(claimantSide.Concat(holderSide));
            string fiefSnapshot = CaptureFiefSnapshot(claimantSide.Concat(holderSide));
            MoveSideToKingdom(claimantSide, claimantKingdom, influenceSnapshot);
            MoveSideToKingdom(holderSide, holderKingdom, influenceSnapshot);

            claimantKingdom.RulingClan = claimant;
            holderKingdom.RulingClan = holder;
            Campaign.Current?.GetCampaignBehavior<CivilWarInterventionBehavior>()?.ApplyPermanentLock(claimantKingdom);
            Campaign.Current?.GetCampaignBehavior<CivilWarInterventionBehavior>()?.ApplyPermanentLock(holderKingdom);

            RunWithoutPeaceHandling(() =>
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(claimantKingdom, holderKingdom)));

            ClaimFeudWarRecord war = new ClaimFeudWarRecord(
                baseId,
                feud.RecordId,
                parent.StringId,
                claimantKingdom.StringId,
                holderKingdom.StringId,
                title.TitleId,
                claimant.StringId,
                holder.StringId,
                EncodeClanIds(claimantSide),
                EncodeClanIds(holderSide),
                EncodeInfluenceSnapshot(influenceSnapshot),
                fiefSnapshot,
                CurrentDay);
            war.InitializeObjectiveControl(GetObjectiveController(titleBehavior, title,
                claimantSide.Select(clan => clan.StringId), holderSide.Select(clan => clan.StringId)) == true);
            _wars.Add(war);
            Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.RegisterClaimFeudWar(war);
            UpdateObjectiveScorePressure(war);
            feud.SetState(ClaimFeudState.WarActive);
            NotificationHelper.ShowClaimFeudWarStarted(parent, claimant, holder, title);
            BellumCivileLogger.Log($"Claim feud war started; feud={feud.RecordId}; title={title.TitleId}; claimant_kingdom={claimantKingdom.StringId}; holder_kingdom={holderKingdom.StringId}; claimant_side={war.ClaimantClanIds}; holder_side={war.HolderClanIds}.");
            return true;
        }

        private void TickActiveFeudWars()
        {
            foreach (ClaimFeudWarRecord war in _wars.Where(record => record != null && record.IsActive).ToList())
            {
                if (war.PendingResolutionOutcome != ClaimFeudWarOutcome.None)
                {
                    ResolveWar(
                        war,
                        war.PendingResolutionOutcome,
                        string.IsNullOrWhiteSpace(war.PendingResolutionReason)
                            ? "deferred feud war resolution"
                            : war.PendingResolutionReason);
                    continue;
                }

                ClaimFeudWarOutcome outcome = DetermineOutcome(war);
                if (outcome != ClaimFeudWarOutcome.None)
                {
                    ResolveWar(war, outcome, "daily feud war tick");
                }
                else
                {
                    UpdateObjectiveScorePressure(war);
                }
            }
        }

        private ClaimFeudWarOutcome DetermineOutcome(ClaimFeudWarRecord war)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord title = titleBehavior?.GetTitle(war.TargetTitleId);
            if (title == null || !title.IsActive)
                return ClaimFeudWarOutcome.WhitePeace;
            return ClaimFeudWarOutcome.None;
        }

        internal void ReconcileObjectiveControlScores()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()) return;
            foreach (var war in GetActiveWars()) UpdateObjectiveScorePressure(war);
        }

        private void OnObjectiveSettlementOwnerChanged(
            Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // FeudalTitleBehavior is registered first and has already refreshed de facto ownership.
            // Observe every transfer so losing and recovering control between daily ticks resets time.
            if (settlement != null && (settlement.IsTown || settlement.IsCastle))
                ReconcileObjectiveControlScores();
        }

        private void UpdateObjectiveScorePressure(ClaimFeudWarRecord war)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || war?.IsActive != true
                || war.PendingResolutionOutcome != ClaimFeudWarOutcome.None) return;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord title = titleBehavior?.GetTitle(war.TargetTitleId);
            if (title == null || !title.IsActive)
                return;

            bool? claimantControls = GetObjectiveController(titleBehavior, title,
                DecodeIds(war.ClaimantClanIds), DecodeIds(war.HolderClanIds));

            Kingdom claimantKingdom = ResolveKingdom(war.ClaimantKingdomId);
            Kingdom holderKingdom = ResolveKingdom(war.HolderKingdomId);
            if (claimantKingdom == null || holderKingdom == null)
                return;

            war.SetObjectiveControl(claimantControls == true);
            WarScoreEventType eventType = WarScoreEventType.TickingLeverage;
            string controller = claimantControls.HasValue ? (claimantControls.Value ? "claimant" : "holder") : "neither side";
            string action = "feud objective time pressure reconciled; verified controller=" + controller;
            Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.SetFeudObjectiveControl(
                claimantKingdom,
                holderKingdom,
                claimantControls,
                eventType,
                title.CapitalSettlementId,
                action);
        }

        public bool TryResolveWarScore(Kingdom claimantKingdom, Kingdom holderKingdom, bool claimantVictory, string reason)
        {
            EnsureCollectionsInitialized();
            ClaimFeudWarRecord war = _wars.FirstOrDefault(active => active != null
                                                                 && active.IsActive
                                                                 && active.ClaimantKingdomId == claimantKingdom?.StringId
                                                                 && active.HolderKingdomId == holderKingdom?.StringId);
            if (war == null)
                return false;

            ResolveWar(
                war,
                claimantVictory ? ClaimFeudWarOutcome.ClaimantVictory : ClaimFeudWarOutcome.HolderVictory,
                reason);
            return true;
        }

        private static bool? GetObjectiveController(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            IEnumerable<string> claimantClanIds,
            IEnumerable<string> holderClanIds)
        {
            if (titleBehavior == null || title == null || !title.IsActive)
                return null;

            if (title.TitleType == FeudalTitleType.Barony)
                return ResolveObjectiveController(new[] { title.DeFactoHolderClanId }, claimantClanIds, holderClanIds);

            List<FeudalTitleRecord> objectives = titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeFacto)
                .Where(child => child != null && child.IsActive)
                .ToList();
            if (objectives.Count == 0)
            {
                objectives = titleBehavior.GetDescendantBaronyTitles(title, FeudalHierarchyMode.DeFacto)
                    .Where(child => child != null && child.IsActive)
                    .ToList();
            }

            return ResolveObjectiveController(objectives.Select(child => child.DeFactoHolderClanId),
                claimantClanIds, holderClanIds);
        }

        internal static bool? ResolveObjectiveController(IEnumerable<string> objectiveHolderIds,
            IEnumerable<string> claimantClanIds, IEnumerable<string> holderClanIds)
        {
            var claimantIds = new HashSet<string>(claimantClanIds ?? Enumerable.Empty<string>());
            var holderIds = new HashSet<string>(holderClanIds ?? Enumerable.Empty<string>());
            int total = 0, claimantHeld = 0, holderHeld = 0;
            foreach (string owner in objectiveHolderIds ?? Enumerable.Empty<string>())
            {
                total++;
                if (string.IsNullOrWhiteSpace(owner)) continue;
                bool claimant = claimantIds.Contains(owner), holder = holderIds.Contains(owner);
                // Missing, foreign, or conflicting side membership does not verify either side.
                if (claimant && !holder) claimantHeld++;
                if (holder && !claimant) holderHeld++;
            }
            if (claimantHeld * 2 > total) return true;
            if (holderHeld * 2 > total) return false;
            return null;
        }

        private void ResolveWar(ClaimFeudWarRecord war, ClaimFeudWarOutcome outcome, string reason, bool claimantDefaulted = false, Clan peaceEnforcer = null)
        {
            if (war == null || !war.IsActive)
                return;

            float paidCrownInfluence = peaceEnforcer?.Influence ?? 0f;

            // Mark resolved before cleanup so peace and kingdom-destruction events cannot re-enter
            // this same feud war and announce a second, contradictory outcome.
            war.SetActive(false);

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            ClaimFeudRecord feud = feudBehavior?.GetActiveFeuds().FirstOrDefault(record => record != null && record.RecordId == war.FeudRecordId);
            Clan claimant = ResolveClan(war.ClaimantLeaderClanId);
            Clan holder = ResolveClan(war.HolderLeaderClanId);
            Kingdom parent = ResolveReturnKingdom(war);
            Kingdom originalParent = ResolveKingdom(war.ParentKingdomId);
            if (peaceEnforcer != null) parent = originalParent;
            Kingdom claimantKingdom = ResolveKingdom(war.ClaimantKingdomId);
            Kingdom holderKingdom = ResolveKingdom(war.HolderKingdomId);
            FeudalTitleRecord title = titleBehavior?.GetTitle(war.TargetTitleId);

            BellumCivileLogger.Log(
                $"Resolving claim feud destination; war={war.WarId}; original_parent={originalParent?.StringId ?? war.ParentKingdomId ?? "none"}; " +
                $"original_parent_eliminated={originalParent?.IsEliminated.ToString() ?? "missing"}; return_realm={parent?.StringId ?? "independent promotion"}; " +
                $"claimant_shell={claimantKingdom?.StringId ?? war.ClaimantKingdomId}; holder_shell={holderKingdom?.StringId ?? war.HolderKingdomId}.");

            var resultNotice = ConflictOutcomeBehavior.Current?.Begin("feud:" + war.WarId, claimantKingdom, holderKingdom,
                originalParent?.RulingClan == Clan.PlayerClan ? originalParent : null);
            if (resultNotice != null && Clan.PlayerClan != null
                && (DecodeIds(war.ClaimantClanIds).Contains(Clan.PlayerClan.StringId)
                    || DecodeIds(war.HolderClanIds).Contains(Clan.PlayerClan.StringId))) resultNotice.PlayerInvolved = true;
            ConflictOutcomeBehavior.Capture(resultNotice, "CLAIMANT_HOUSE", claimant?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "HOLDER_HOUSE", holder?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "HOLDER", holder?.Leader?.Name ?? holder?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "REALM", originalParent?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "RULER", peaceEnforcer?.Leader?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "TITLE", new TextObject("{=!}" + (title == null ? war.TargetTitleId : FeudalTitleDisplayHelper.FormatTitleName(title))));
            string resultKind = "feud_peace";
            string claimConsequence = null;
            if (title == null || !title.IsActive)
            {
                resultKind = "ended";
                ConflictOutcomeBehavior.Capture(resultNotice, "CAUSE", new TextObject("{=BC_Result_FeudInvalid}The disputed title no longer stands. This feud can no longer be pursued."));
            }

            ApplyPeaceIfNeeded(claimantKingdom, holderKingdom);
            Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                .CompleteClaimFeudWar(war.ClaimantKingdomId, war.HolderKingdomId, reason);

            if (outcome == ClaimFeudWarOutcome.ClaimantVictory && titleBehavior != null && claimant != null && title != null)
            {
                RestoreFiefSnapshot(war.FiefSnapshot, title.TitleType == FeudalTitleType.Barony ? title.CapitalSettlementId : null);
                if (titleBehavior.TryResolveClaimFeudForClaimant(
                    claimant,
                    holder,
                    title,
                    "open claim feud victory",
                    out string claimantAwardResult))
                {
                    feud?.SetState(ClaimFeudState.Settled);
                    resultKind = "feud_claimant";
                    ConflictOutcomeBehavior.Capture(resultNotice, "NEW_HOLDER", claimant.Leader?.Name ?? claimant.Name);
                }
                else
                {
                    BellumCivileLogger.Log(
                        $"Claim feud claimant award failed; war={war.WarId}; feud={war.FeudRecordId}; title={title.TitleId}; " +
                        $"claimant={claimant.StringId}; reason={claimantAwardResult ?? "unknown"}. Reverting the disputed fief and resolving as white peace.");
                    RestoreFiefSnapshot(war.FiefSnapshot, null);
                    outcome = ClaimFeudWarOutcome.WhitePeace;
                    feud?.SetState(ClaimFeudState.Cooldown);
                }
            }
            else if (outcome == ClaimFeudWarOutcome.HolderVictory && titleBehavior != null && claimant != null && holder != null && title != null)
            {
                RestoreFiefSnapshot(war.FiefSnapshot, null);
                bool applied = titleBehavior.TryResolveClaimFeudForHolder(claimant, holder, title, feud?.ClaimStrength ?? FeudalClaimStrength.Weak, "open claim feud holder victory", out _);
                feud?.SetState(ClaimFeudState.Settled);
                if (applied)
                {
                    resultKind = claimantDefaulted ? "feud_default" : "feud_holder";
                    claimConsequence = new TextObject(feud?.ClaimStrength == FeudalClaimStrength.Strong && claimant.Leader?.IsAlive == true
                        ? "{=BC_Result_ClaimWeakened}The defeated house's claim has been weakened."
                        : "{=BC_Result_ClaimExtinguished}The defeated house's claim has been extinguished.").ToString();
                }
                else
                {
                    resultKind = "ended";
                    ConflictOutcomeBehavior.Capture(resultNotice, "CAUSE", new TextObject("{=BC_Result_FeudUnawarded}The fighting has ended, but the disputed claim could not be settled. No title was awarded by this settlement."));
                }
            }
            else
            {
                RestoreFiefSnapshot(war.FiefSnapshot, null);
                feud?.SetState(peaceEnforcer != null ? ClaimFeudState.SuppressedCooldown : ClaimFeudState.Cooldown);
            }

            if (peaceEnforcer != null) resultKind = "feud_royal_peace";

            RecordYearlyOutcome(outcome);

            if (feud != null)
            {
                feud.SetCooldownUntilDay(CurrentDay + CampaignTime.DaysInYear * BellumCivileConstants.ClaimFeudCooldownYears);
                feud.SetDebugReason($"{feud.DebugReason}; open war resolved {outcome} ({reason})");
            }

            ApplyPostWarRelations(claimant, holder, outcome);
            if (parent != null && !parent.IsEliminated)
            {
                ReturnWarClans(war, parent);
                DestroyTemporaryKingdom(claimantKingdom, parent);
                DestroyTemporaryKingdom(holderKingdom, parent);
            }
            else
            {
                PromoteTemporarySideToIndependentRealm(claimantKingdom, claimant, originalParent, "claim feud parent collapsed");
                PromoteTemporarySideToIndependentRealm(holderKingdom, holder, originalParent, "claim feud parent collapsed");
            }
            var influenceSnapshot = DecodeInfluenceSnapshot(war.InfluenceSnapshot);
            // The Crown has already paid for enforcement; never restore that expenditure.
            if (peaceEnforcer != null) influenceSnapshot.Remove(peaceEnforcer.StringId);
            RestoreInfluenceSnapshot(influenceSnapshot);
            // Returning a house also restores its snapshot and can change influence on joining.
            if (peaceEnforcer != null) peaceEnforcer.Influence = paidCrownInfluence;
            Campaign.Current?.GetCampaignBehavior<CivilWarInterventionBehavior>()?.RemoveLock(claimantKingdom);
            Campaign.Current?.GetCampaignBehavior<CivilWarInterventionBehavior>()?.RemoveLock(holderKingdom);
            string destinationText = parent != null && !parent.IsEliminated
                ? new TextObject("{=BC_Result_FeudReturn}The surviving houses have returned to {REALM}.").SetTextVariable("REALM", parent.Name).ToString()
                : new TextObject("{=BC_Result_FeudIndependent}With their former realm gone, surviving houses that could establish themselves now stand independently.").ToString();
            ConflictOutcomeBehavior.Current?.Publish(resultNotice, resultKind,
                string.IsNullOrEmpty(claimConsequence) ? destinationText : claimConsequence + "\n\n" + destinationText);
            BellumCivileLogger.Log($"Claim feud war resolved; war={war.WarId}; feud={war.FeudRecordId}; outcome={outcome}; reason={reason ?? "unknown"}.");
        }

        private void RecordYearlyOutcome(ClaimFeudWarOutcome outcome)
        {
            switch (outcome)
            {
                case ClaimFeudWarOutcome.ClaimantVictory:
                    _yearlyClaimantVictories++;
                    break;
                case ClaimFeudWarOutcome.HolderVictory:
                    _yearlyHolderVictories++;
                    break;
                default:
                    _yearlyWhitePeaces++;
                    break;
            }
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            foreach (ClaimFeudWarRecord war in _wars.Where(record => record != null && record.IsActive
                                                                  && (record.ClaimantKingdomId == kingdom.StringId || record.HolderKingdomId == kingdom.StringId)).ToList())
            {
                ClaimFeudWarOutcome outcome = war.ClaimantKingdomId == kingdom.StringId
                    ? ClaimFeudWarOutcome.HolderVictory
                    : ClaimFeudWarOutcome.ClaimantVictory;
                war.QueueResolution(outcome, "temporary feud kingdom destroyed");
                BellumCivileLogger.Log($"Claim feud kingdom destruction queued for safe resolution; war={war.WarId}; destroyed={kingdom.StringId}; outcome={outcome}.");
            }
        }

        private Kingdom CreateTemporaryFeudKingdom(string baseId, Kingdom parent, Clan leader, Clan visualClan)
        {
            string kingdomId = baseId;
            int suffix = 0;
            while (Kingdom.All.Any(k => k.StringId == kingdomId))
                kingdomId = baseId + "_" + (++suffix);

            Kingdom kingdom = KingdomCreationSafetyHelper.CreateKingdom(kingdomId, leader);
            if (kingdom == null)
                return null;
            var visuals = KingdomVisualHelper.ResolveBreakawayKingdomVisuals(parent, visualClan, kingdomId);
            Settlement capital = leader?.Settlements.FirstOrDefault()
                              ?? parent?.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
            TextObject name = ResolveTemporaryFeudKingdomName(leader);
            kingdom.InitializeKingdom(name, name, leader?.Culture ?? parent?.Culture, visuals.Banner, visuals.PrimaryColor, visuals.SecondaryColor, capital, new TextObject(""), new TextObject(""), new TextObject(""));
            KingdomVisualHelper.ApplyKingdomPalette(kingdom, visuals);
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.ClearTemporaryKingdomRepair(kingdom);
            return kingdom;
        }

        private static TextObject ResolveTemporaryFeudKingdomName(Clan leader)
        {
            FeudalTitleDisplayHelper.HeldTitleDisplayEntry highestTitle = FeudalTitleDisplayHelper.GetHeldTitleEntries(leader).FirstOrDefault();
            string titleName = highestTitle?.Title != null
                ? FeudalTitleDisplayHelper.FormatTitleName(highestTitle.Title, leader)
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(titleName))
                return new TextObject("{=!}" + titleName);

            TextObject fallback = new TextObject("{=BC_ClaimFeud_TempKingdomName}{CLAN_NAME} Feud");
            fallback.SetTextVariable("CLAN_NAME", leader?.Name ?? new TextObject("?"));
            return fallback;
        }

        private static void MoveSideToKingdom(IEnumerable<Clan> clans, Kingdom kingdom, Dictionary<string, float> influenceSnapshot)
        {
            foreach (Clan clan in clans ?? Enumerable.Empty<Clan>())
            {
                if (clan == null || clan.IsEliminated || clan.Kingdom == kingdom)
                    continue;

                KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, kingdom, showNotification: false);
                if (influenceSnapshot != null && influenceSnapshot.TryGetValue(clan.StringId, out float influence) && clan.Influence < influence)
                    clan.Influence = influence;
            }
        }

        private void ReturnWarClans(ClaimFeudWarRecord war, Kingdom parent)
        {
            Dictionary<string, float> influenceSnapshot = DecodeInfluenceSnapshot(war.InfluenceSnapshot);
            foreach (Clan clan in DecodeIds(war.ClaimantClanIds).Concat(DecodeIds(war.HolderClanIds)).Select(ResolveClan).Where(c => c != null && !c.IsEliminated).Distinct())
            {
                if (DismissMercenaryFromTemporaryFeudRealm(clan))
                    continue;

                if (parent != null && !parent.IsEliminated)
                    KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, parent, showNotification: false);
                if (influenceSnapshot.TryGetValue(clan.StringId, out float influence) && clan.Influence < influence)
                    clan.Influence = influence;
            }
        }

        private Kingdom ResolveReturnKingdom(ClaimFeudWarRecord war)
        {
            Kingdom parent = ResolveKingdom(war.ParentKingdomId);
            if (IsValidPermanentReturnKingdom(parent))
                return parent;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior != null && parent != null)
            {
                Clan sovereign = titleBehavior.GetDeFactoSovereignClan(parent);
                if (IsValidPermanentReturnKingdom(sovereign?.Kingdom))
                    return sovereign.Kingdom;

                if (sovereign?.Kingdom != null && BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(sovereign.Kingdom))
                    BellumCivileLogger.Log($"Rejected temporary feud return realm; war={war.WarId}; candidate={sovereign.Kingdom.StringId}; source=de_facto_sovereign.");
            }

            Clan claimant = ResolveClan(war.ClaimantLeaderClanId);
            if (IsValidPermanentReturnKingdom(claimant?.Kingdom))
                return claimant.Kingdom;

            Clan holder = ResolveClan(war.HolderLeaderClanId);
            if (IsValidPermanentReturnKingdom(holder?.Kingdom))
                return holder.Kingdom;

            return null;
        }

        private static bool IsValidPermanentReturnKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
        }

        private void RepairOrphanedTemporaryFeudKingdoms()
        {
            HashSet<string> activeShellIds = new HashSet<string>(
                _wars.Where(war => war != null && war.IsActive)
                    .SelectMany(war => new[] { war.ClaimantKingdomId, war.HolderKingdomId })
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);

            List<Kingdom> orphanedShells = Kingdom.All
                .Where(kingdom => kingdom != null
                    && !kingdom.IsEliminated
                    && BellumKingdomVisibilityHelper.IsTemporaryFeudKingdom(kingdom)
                    && !activeShellIds.Contains(kingdom.StringId))
                .ToList();
            if (orphanedShells.Count == 0)
                return;

            HashSet<string> processedShellIds = new HashSet<string>(StringComparer.Ordinal);
            CivilWarInterventionBehavior intervention = Campaign.Current?.GetCampaignBehavior<CivilWarInterventionBehavior>();

            foreach (Kingdom orphanedShell in orphanedShells)
            {
                if (orphanedShell == null
                    || orphanedShell.IsEliminated
                    || !processedShellIds.Add(orphanedShell.StringId))
                {
                    continue;
                }

                ClaimFeudWarRecord matchingWar = _wars
                    .Where(war => war != null
                        && (war.ClaimantKingdomId == orphanedShell.StringId
                            || war.HolderKingdomId == orphanedShell.StringId))
                    .OrderByDescending(war => war.StartedDay)
                    .FirstOrDefault();

                List<Kingdom> relatedShells = new List<Kingdom> { orphanedShell };
                if (matchingWar != null)
                {
                    Kingdom claimantShell = ResolveKingdom(matchingWar.ClaimantKingdomId);
                    Kingdom holderShell = ResolveKingdom(matchingWar.HolderKingdomId);
                    foreach (Kingdom related in new[] { claimantShell, holderShell })
                    {
                        if (related != null
                            && !related.IsEliminated
                            && BellumKingdomVisibilityHelper.IsTemporaryFeudKingdom(related)
                            && !activeShellIds.Contains(related.StringId)
                            && !relatedShells.Contains(related))
                        {
                            relatedShells.Add(related);
                        }
                    }

                    ApplyPeaceIfNeeded(claimantShell, holderShell);
                }

                foreach (Kingdom related in relatedShells)
                    processedShellIds.Add(related.StringId);

                Kingdom returnKingdom = matchingWar != null ? ResolveReturnKingdom(matchingWar) : null;
                if (returnKingdom != null)
                {
                    foreach (Kingdom related in relatedShells)
                    {
                        intervention?.RemoveLock(related);
                        DestroyTemporaryKingdom(related, returnKingdom);
                    }

                    BellumCivileLogger.Log(
                        $"Repaired orphaned claim feud realm by restoring its clans; shells={string.Join(",", relatedShells.Select(shell => shell.StringId))}; " +
                        $"return_realm={returnKingdom.StringId}; war={matchingWar.WarId}.");
                    continue;
                }

                Kingdom formerParent = matchingWar != null ? ResolveKingdom(matchingWar.ParentKingdomId) : null;
                foreach (Kingdom related in relatedShells)
                {
                    Clan preferredLeader = null;
                    if (matchingWar != null)
                    {
                        preferredLeader = related.StringId == matchingWar.ClaimantKingdomId
                            ? ResolveClan(matchingWar.ClaimantLeaderClanId)
                            : ResolveClan(matchingWar.HolderLeaderClanId);
                    }

                    intervention?.RemoveLock(related);
                    PromoteTemporarySideToIndependentRealm(
                        related,
                        preferredLeader,
                        formerParent,
                        matchingWar != null
                            ? "existing-save orphaned claim feud repair"
                            : "untracked orphaned claim feud repair");
                }
            }
        }

        private static Clan ResolvePromotionLeader(Kingdom temporaryKingdom, Clan preferredLeader)
        {
            if (temporaryKingdom == null)
                return null;

            if (IsValidWarLeader(preferredLeader) && preferredLeader.Kingdom == temporaryKingdom)
                return preferredLeader;

            if (IsValidWarLeader(temporaryKingdom.RulingClan) && temporaryKingdom.RulingClan.Kingdom == temporaryKingdom)
                return temporaryKingdom.RulingClan;

            return temporaryKingdom.Clans
                .Where(clan => IsValidWarLeader(clan) && clan.Kingdom == temporaryKingdom)
                .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                .FirstOrDefault();
        }

        private static List<Kingdom> GetPermanentExternalEnemies(Kingdom temporaryKingdom)
        {
            if (temporaryKingdom == null || temporaryKingdom.IsEliminated)
                return new List<Kingdom>();

            return Kingdom.All
                .Where(other => other != null
                    && other != temporaryKingdom
                    && IsValidPermanentReturnKingdom(other)
                    && temporaryKingdom.IsAtWarWith(other))
                .Distinct()
                .ToList();
        }

        private static void RestorePermanentExternalWars(Kingdom permanentKingdom, IEnumerable<Kingdom> enemies)
        {
            if (permanentKingdom == null || permanentKingdom.IsEliminated)
                return;

            foreach (Kingdom enemy in enemies ?? Enumerable.Empty<Kingdom>())
            {
                if (!IsValidPermanentReturnKingdom(enemy)
                    || enemy == permanentKingdom
                    || permanentKingdom.IsAtWarWith(enemy))
                {
                    continue;
                }

                RunWithoutPeaceHandling(() =>
                    ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                        () => DeclareWarAction.ApplyByDefault(permanentKingdom, enemy)));
                BellumCivileLogger.Log($"Restored external war after orphaned claim feud promotion; realm={permanentKingdom.StringId}; enemy={enemy.StringId}.");
            }
        }

        private void DestroyTemporaryKingdom(Kingdom kingdom, Kingdom fallback)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return;

            if (kingdom == fallback)
            {
                BellumCivileLogger.Log($"Refused self-targeted claim feud cleanup; kingdom={kingdom.StringId}.");
                return;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            Action destroy = () =>
            {
                foreach (Clan clan in kingdom.Clans.ToList())
                {
                    if (clan == null || clan.IsEliminated || clan.Kingdom != kingdom)
                        continue;

                    if (DismissMercenaryFromTemporaryFeudRealm(clan))
                        continue;

                    if (fallback != null && !fallback.IsEliminated)
                        KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, fallback, showNotification: false);
                }

                if (!kingdom.Clans.Any(c => c != null && !c.IsEliminated))
                {
                    factionManager?.ClearTemporaryKingdomRepair(kingdom);
                    DestroyKingdomAction.Apply(kingdom);
                }
            };

            if (factionManager != null)
                factionManager.RunWithRulerRepairSuppressed(kingdom, destroy);
            else
                destroy();
        }

        private void PromoteTemporarySideToIndependentRealm(Kingdom temporaryKingdom, Clan leader, Kingdom formerParent, string reason)
        {
            if (temporaryKingdom == null || temporaryKingdom.IsEliminated)
                return;

            leader = ResolvePromotionLeader(temporaryKingdom, leader);
            if (!KingdomCreationSafetyHelper.IsValidFounder(leader))
            {
                BellumCivileLogger.Log($"Unable to promote orphaned claim feud realm; old={temporaryKingdom.StringId}; no valid noble clan remained; reason={reason ?? "unknown"}.");
                return;
            }

            List<Kingdom> externalEnemies = GetPermanentExternalEnemies(temporaryKingdom);

            string baseId = "bc_indep_feud_" + Sanitize(leader.StringId);
            string kingdomId = baseId;
            int suffix = 0;
            while (Kingdom.All.Any(k => k.StringId == kingdomId))
                kingdomId = baseId + "_" + (++suffix);

            Kingdom permanent = KingdomCreationSafetyHelper.CreateKingdom(kingdomId, leader);
            if (permanent == null)
                return;
            IndependentKingdomProfile profile = IndependentKingdomProfileHelper.Create(leader, formerParent);
            var visuals = KingdomVisualHelper.ResolveIndependentSuccessorKingdomVisuals(temporaryKingdom, leader, kingdomId);
            Settlement capital = leader.Settlements.FirstOrDefault()
                              ?? temporaryKingdom.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);

            permanent.InitializeKingdom(
                profile.Name,
                profile.EncyclopediaTitle,
                profile.Culture ?? leader.Culture ?? temporaryKingdom.Culture,
                visuals.Banner,
                visuals.PrimaryColor,
                visuals.SecondaryColor,
                capital,
                profile.RulerTitle,
                new TextObject(""),
                profile.EncyclopediaText);
            KingdomVisualHelper.ApplyKingdomPalette(permanent, visuals);

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            foreach (Clan clan in temporaryKingdom.Clans.ToList())
            {
                if (clan == null || clan.IsEliminated || clan.Kingdom != temporaryKingdom)
                    continue;

                if (DismissMercenaryFromTemporaryFeudRealm(clan))
                    continue;

                CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, formerParent, permanent,
                    () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, permanent, showNotification: false));
            }

            permanent.RulingClan = leader;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (profile.UsesExistingSourceTitle)
                titleBehavior?.RegisterIndependentRealmShell(permanent, titleBehavior.GetTitle(profile.SourceTitleId), reason);
            else
                titleBehavior?.TrySetKingdomTitleRuler(permanent, leader, legalTransfer: false, reason: reason);

            RestorePermanentExternalWars(permanent, externalEnemies);

            DestroyTemporaryKingdom(temporaryKingdom, permanent);
            factionManager?.ClearTemporaryKingdomRepair(permanent);
            BellumCivileLogger.Log($"Promoted orphaned claim feud side to independent realm; old={temporaryKingdom.StringId}; new={permanent.StringId}; leader={leader.StringId}; reason={reason ?? "unknown"}.");
        }

        private static bool DismissMercenaryFromTemporaryFeudRealm(Clan clan)
        {
            if (clan == null || clan.IsEliminated || !clan.IsUnderMercenaryService)
                return false;

            string temporaryKingdomId = clan.Kingdom?.StringId ?? "none";
            ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
            BellumCivileLogger.Log($"Dismissed mercenary from temporary claim feud realm; clan={clan.StringId}; realm={temporaryKingdomId}.");
            return true;
        }

        private static void ApplyPeaceIfNeeded(Kingdom first, Kingdom second)
        {
            if (first == null || second == null || first == second || first.IsEliminated || second.IsEliminated || !first.IsAtWarWith(second))
                return;

            RunWithoutPeaceHandling(() =>
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => MakePeaceAction.Apply(first, second)));
        }

        private static void RunWithoutPeaceHandling(Action action)
        {
            _peaceHandlingSuppressionDepth++;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _peaceHandlingSuppressionDepth--;
            }
        }

        private List<Clan> BuildSide(string leaderClanId, string supporterIds, Kingdom parent, string feudRecordId)
        {
            List<Clan> clans = new List<Clan>();
            Clan leader = ResolveClan(leaderClanId);
            if (leader != null)
                clans.Add(leader);

            foreach (string id in DecodeIds(supporterIds))
            {
                Clan clan = ResolveClan(id);
                if (clan != null
                    && clan != leader
                    && clan != parent?.RulingClan
                    && clan.Kingdom == parent
                    && !IsClanCommittedToActiveWar(clan, feudRecordId)
                    && !clans.Contains(clan))
                {
                    clans.Add(clan);
                }
            }

            return clans.Where(IsValidWarClan).ToList();
        }

        private static bool HasActiveCivilWar(Kingdom kingdom)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return kingdom != null
                && factionManager?.GetFactionsInKingdom(kingdom)
                    .Any(faction => faction != null && !faction.IsIdeology && faction.IsCivilWarActive()) == true;
        }

        private static int CountExternalWars(Kingdom kingdom)
        {
            if (kingdom == null)
                return 0;

            return Kingdom.All.Count(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && !other.IsMinorFaction
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)
                && kingdom.IsAtWarWith(other));
        }

        private static bool IsValidWarLeader(Clan clan)
        {
            return IsValidWarClan(clan) && clan.Leader != null && !clan.Leader.IsDead;
        }

        private static bool IsValidWarClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan);
        }

        private static bool IsLeaderUnsafeForFeudWar(Clan leader, IEnumerable<Clan> opposingSide, out Settlement unsafeSettlement)
        {
            unsafeSettlement = null;
            Hero hero = leader?.Leader;
            if (hero == null || hero == Hero.MainHero)
                return false;

            Settlement settlement = hero.CurrentSettlement;
            if (settlement == null || !settlement.IsFortification)
                return false;

            Clan owner = settlement.OwnerClan;
            if (owner == null)
                return false;

            if ((opposingSide ?? Enumerable.Empty<Clan>()).Contains(owner))
            {
                unsafeSettlement = settlement;
                return true;
            }

            return false;
        }

        private static void RemoveSidesFromRebelFactions(IEnumerable<Clan> clans)
        {
            FactionManagerBehavior manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null)
                return;

            foreach (Clan clan in (clans ?? Enumerable.Empty<Clan>()).Where(c => c != null).Distinct().ToList())
            {
                FactionObject rebel = manager.GetRebelFaction(clan);
                if (rebel == null)
                    continue;

                rebel.RemoveMember(clan);
                if (rebel.Members.Count == 0 || rebel.Leader == clan)
                    manager.RemoveFaction(rebel);
            }
        }

        private static void ApplyPostWarRelations(Clan claimant, Clan holder, ClaimFeudWarOutcome outcome)
        {
            if (claimant?.Leader == null || holder?.Leader == null)
                return;

            int penalty = outcome == ClaimFeudWarOutcome.WhitePeace ? -10 : -25;
            RelationMemoryService.ApplyChange(claimant.Leader, holder.Leader, penalty, false,
                RelationMemorySources.CivilWarSettlement, 10f, RelationMemoryScope.House);
        }

        private static Dictionary<string, float> CaptureInfluenceSnapshot(IEnumerable<Clan> clans)
        {
            Dictionary<string, float> snapshot = new Dictionary<string, float>();
            foreach (Clan clan in clans ?? Enumerable.Empty<Clan>())
            {
                if (clan == null || string.IsNullOrWhiteSpace(clan.StringId) || snapshot.ContainsKey(clan.StringId))
                    continue;
                snapshot[clan.StringId] = MathF.Max(0f, clan.Influence);
            }
            return snapshot;
        }

        private static string CaptureFiefSnapshot(IEnumerable<Clan> clans)
        {
            HashSet<Clan> clanSet = new HashSet<Clan>((clans ?? Enumerable.Empty<Clan>()).Where(c => c != null));
            List<string> entries = new List<string>();
            foreach (Settlement settlement in Settlement.All.Where(s => s != null && (s.IsTown || s.IsCastle)))
            {
                Clan owner = settlement.OwnerClan;
                if (owner != null && clanSet.Contains(owner))
                    entries.Add(settlement.StringId + ":" + owner.StringId);
            }
            return string.Join(";", entries);
        }

        private static void RestoreFiefSnapshot(string snapshot, string excludedSettlementId)
        {
            foreach (string entry in (snapshot ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2)
                    continue;

                string settlementId = parts[0];
                if (!string.IsNullOrWhiteSpace(excludedSettlementId) && settlementId == excludedSettlementId)
                    continue;

                Settlement settlement = Settlement.All.FirstOrDefault(s => s != null && s.StringId == settlementId);
                Clan owner = ResolveClan(parts[1]);
                if (settlement?.OwnerClan != owner && owner?.Leader != null)
                    ChangeOwnerOfSettlementAction.ApplyByDefault(owner.Leader, settlement);
            }
        }

        private static void RestoreInfluenceSnapshot(Dictionary<string, float> snapshot)
        {
            foreach (KeyValuePair<string, float> entry in snapshot)
            {
                Clan clan = ResolveClan(entry.Key);
                if (clan != null && clan.Influence < entry.Value)
                    clan.Influence = entry.Value;
            }
        }

        private static string EncodeClanIds(IEnumerable<Clan> clans)
        {
            return string.Join(",", (clans ?? Enumerable.Empty<Clan>()).Where(c => c != null).Select(c => c.StringId).Distinct());
        }

        private static IEnumerable<string> DecodeIds(string ids)
        {
            return (ids ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id));
        }

        private static string EncodeInfluenceSnapshot(Dictionary<string, float> snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            return string.Join(";", snapshot.Select(kvp => kvp.Key + ":" + kvp.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static Dictionary<string, float> DecodeInfluenceSnapshot(string snapshot)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            foreach (string entry in (snapshot ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2)
                    continue;
                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    result[parts[0]] = value;
            }
            return result;
        }

        private static Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrWhiteSpace(clanId))
                return null;
            return Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            if (string.IsNullOrWhiteSpace(kingdomId))
                return null;
            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";
            char[] chars = value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            return new string(chars);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_wars == null)
                _wars = new List<ClaimFeudWarRecord>();
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }

    public sealed class ClaimFeudWarYearlyTelemetry
    {
        public int ClaimantVictories { get; }
        public int HolderVictories { get; }
        public int WhitePeaces { get; }

        public ClaimFeudWarYearlyTelemetry(int claimantVictories, int holderVictories, int whitePeaces)
        {
            ClaimantVictories = claimantVictories;
            HolderVictories = holderVictories;
            WhitePeaces = whitePeaces;
        }
    }
}
