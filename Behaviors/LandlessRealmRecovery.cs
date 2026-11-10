using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private bool _landlessRecoveryPending;
        private readonly Dictionary<Kingdom, string> _landlessRecoveryDeferrals = new Dictionary<Kingdom, string>();
        private static readonly MethodInfo NativeCanDiscontinueRealm = AccessTools.Method(
            typeof(FactionDiscontinuationCampaignBehavior), "CanKingdomBeDiscontinued");

        private void ProcessLandlessRealmRecovery()
        {
            if (!_landlessRecoveryPending) return;
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (map == null || map.MapConversationActive || CampaignMission.Current != null
                || Campaign.Current?.ConversationManager?.IsConversationInProgress == true
                || Campaign.Current?.ConversationManager?.IsConversationFlowActive == true) return;
            _landlessRecoveryPending = false;
            RecoverLandlessRealms();
        }

        private void RecoverLandlessRealms()
        {
            EnsureCollectionsInitialized();
            var candidates = Kingdom.All.Where(k => k != null && !k.IsEliminated
                && k != Clan.PlayerClan?.Kingdom && CountStrongholds(k) == 0
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(k)).ToList();
            foreach (var old in _landlessRecoveryDeferrals.Keys.Where(k => !candidates.Contains(k)).ToList())
                _landlessRecoveryDeferrals.Remove(old);
            if (candidates.Count == 0) return;

            // Cross-check old realm caches against actual ownership, not retained titles.
            var landed = new HashSet<Kingdom>(Settlement.All.Where(s => s != null && (s.IsTown || s.IsCastle))
                .Select(s => s.OwnerClan?.Kingdom).Where(k => k != null));
            var native = Campaign.Current?.GetCampaignBehavior<FactionDiscontinuationCampaignBehavior>();
            foreach (var realm in candidates)
            {
                if (landed.Contains(realm) || realm.IsEliminated) continue;
                try
                {
                    string reason = LandlessRealmDeferral(realm);
                    if (reason == null && (native == null || NativeCanDiscontinueRealm == null))
                        reason = "native kingdom discontinuation is unavailable";
                    if (reason == null && Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>() == null)
                        reason = "exile recovery is unavailable";
                    if (reason == null && !(bool)NativeCanDiscontinueRealm.Invoke(native, new object[] { realm }))
                        reason = "native kingdom discontinuation was vetoed";
                    if (reason == null && !ReleaseMercenariesFromKingdom(realm))
                        reason = "mercenary dismissal is pending";
                    // Mercenary departure callbacks may change political state or membership.
                    reason = reason ?? LandlessRealmDeferral(realm);
                    if (realm.IsEliminated) { _landlessRecoveryDeferrals.Remove(realm); continue; }
                    if (reason == null && !(bool)NativeCanDiscontinueRealm.Invoke(native, new object[] { realm }))
                        reason = "native kingdom discontinuation was vetoed";
                    if (reason != null) { DeferLandlessRealm(realm, reason); continue; }

                    if (!_landlessRecoveryDeferrals.ContainsKey(realm))
                        BellumCivileLogger.Log($"Retiring landless realm through safe discontinuation; realm={realm.StringId}; clans={realm.Clans.Count}.");
                    // Native LeaveByKingdomDestruction events feed ExiledClanRecoveryBehavior,
                    // preserving surviving houses, their exile cause and their search for refuge.
                    RunWithRulerRepairSuppressed(realm, () => reason = RetireLandlessRealm(realm, native));
                    if (realm.IsEliminated)
                    {
                        ClearTemporaryKingdomRepair(realm);
                        _landlessRecoveryDeferrals.Remove(realm);
                        BellumCivileLogger.Log($"Retired landless realm; realm={realm.StringId}.");
                    }
                    else DeferLandlessRealm(realm, reason ?? "native retirement did not complete");
                }
                catch (Exception ex)
                {
                    DeferLandlessRealm(realm, "retirement interrupted: " + (ex.InnerException ?? ex));
                }
            }
        }

        private string RetireLandlessRealm(Kingdom realm, FactionDiscontinuationCampaignBehavior native)
        {
            // Native DiscontinueKingdom destroys any houses left by a blocked leave action.
            // Use its departure detail, but verify each move and keep the ruling house last.
            foreach (var clan in realm.Clans.Where(c => c != null && !c.IsEliminated && c.Kingdom == realm)
                .OrderBy(c => c == realm.RulingClan ? 1 : 0).ToList())
            {
                string reason = LandlessRealmDeferral(realm);
                if (reason != null) return reason;
                if (clan.Kingdom != realm || clan.IsEliminated) continue;
                if (clan.IsUnderMercenaryService) return "mercenary service changed during retirement";
                ChangeKingdomAction.ApplyByLeaveByKingdomDestruction(clan);
                if (realm.IsEliminated) return null;
                if (clan.Kingdom == realm || !clan.IsEliminated && realm.Clans.Contains(clan))
                    return "clan departure is blocked: " + clan.StringId;
            }
            if (realm.Clans.Any(c => c != null && !c.IsEliminated)
                || Clan.All.Any(c => c != null && !c.IsEliminated && c.Kingdom == realm))
                return "surviving clans still belong to the realm";
            string deferred = LandlessRealmDeferral(realm);
            if (deferred != null) return deferred;
            if (!(bool)NativeCanDiscontinueRealm.Invoke(native, new object[] { realm }))
                return "native kingdom discontinuation was vetoed";
            DestroyKingdomAction.Apply(realm);
            if (realm.IsEliminated) realm.RulingClan = null;
            return null;
        }

        private string LandlessRealmDeferral(Kingdom realm)
        {
            if (realm == Clan.PlayerClan?.Kingdom || CountStrongholds(realm) > 0
                || Settlement.All.Any(s => s != null && (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == realm))
                return "realm is no longer an eligible landless NPC realm";
            if (CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.IsCrownPromotionRealmProtected(realm) == true
                || _restoredCreations.Any(r => r.Protects(realm))
                || _collapses.Any(r => !r.Completed && (r.Parent == realm || r.WinnerRealm == realm || r.SuccessorId == realm.StringId))
                || IsKingdomIdReferencedByResolutionState(realm.StringId))
                return "political transition is pending";
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager?.GetFactionByRebelKingdom(realm) != null
                || manager?.GetFactionsInKingdom(realm).Any(f => !f.IsIdeology
                    && (f.HasTrackedRebelKingdom || f.IsChallengeStartupPending || f.IsUltimatumPending)) == true
                || Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.GetActiveWars().Any(w => w.ParentKingdomId == realm.StringId) == true
                || ModIntegrationHelper.IsDiplomacyRebelKingdom(realm))
                return "internal conflict is still resolving";
            if (Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWars().Any(w =>
                (w.AttackerKingdomId == realm.StringId || w.DefenderKingdomId == realm.StringId)
                && (w.ParleyPending || w.ResolutionPending || w.TerminalResolutionQueued)) == true)
                return "peace settlement is pending";
            if (realm.Clans.Any(c => c != null && c.Kingdom != realm)
                || Clan.All.Any(c => c != null && !c.IsEliminated && c.Kingdom == realm && !realm.Clans.Contains(c)))
                return "clan membership is inconsistent";
            if (realm.Clans.Where(c => c != null && !c.IsEliminated).SelectMany(c => c.WarPartyComponents)
                .Any(p => p?.MobileParty != null && (p.MobileParty.MapEvent != null || p.MobileParty.SiegeEvent != null)))
                return "a clan party is in a battle or siege";
            return null;
        }

        private void DeferLandlessRealm(Kingdom realm, string reason)
        {
            if (_landlessRecoveryDeferrals.TryGetValue(realm, out string previous) && previous == reason) return;
            _landlessRecoveryDeferrals[realm] = reason;
            BellumCivileLogger.Log($"Landless realm retirement deferred; realm={realm.StringId}; reason={reason}.");
        }
    }
}
