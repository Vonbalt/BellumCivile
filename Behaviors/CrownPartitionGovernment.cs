using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        // Initially defer subordinate realms, internal/scripted wars and unresolved hostage
        // custody rather than silently freeing obligations or invalidating a pledge.
        internal static bool CanPrepareCrownPartitionDiplomacy(Kingdom parent, Kingdom successor, out string reason)
        {
            reason = "diplomatic services or independent source realm unavailable";
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            var hostages = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            if (parent == null || parent.IsEliminated || clients == null || hostages == null
                || clients.GetSuzerain(parent) != null || successor != null && clients.GetSuzerain(successor) != null) return false;
            if (SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(parent).SuccessionLaw) != RealmSuccessionSystem.Hereditary)
            { reason = "source realm no longer uses hereditary succession"; return false; }
            if (hostages.HasPendingPartitionCustody(parent) || hostages.HasPendingPartitionCustody(successor))
            { reason = "hostage obligations require custody handling before partition"; return false; }
            foreach (var enemy in Kingdom.All.Where(k => k != null && !k.IsEliminated && k != parent && parent.IsAtWarWith(k)))
                if (enemy == successor || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(enemy)
                    || StorylineWarProtectionHelper.IsPeaceBlocked(parent, enemy))
                { reason = "internal or scripted war requires separate partition handling"; return false; }
            reason = null;
            return true;
        }

        internal bool TryPrepareCrownPromotionGovernment(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "government setup requires a registered initialized successor";
            if (journal == null || journal.Completed || _pendingPartitions == null
                || !_pendingPartitions.Any(p => p?.CrownPromotions?.Contains(journal) == true)
                || !journal.TryRecoverFounder(journal.Founder) || !journal.TryRecoverRealm(journal.Successor)
                || journal.Parent.RulingClan != journal.RetainedHouse || !journal.HasVerifiedEstateReceipts()) return false;
            journal.TransferDiplomacyPrepared = false;
            journal.GovernmentVerified = false;
            if (!CanPrepareCrownPartitionDiplomacy(journal.Parent, journal.Successor, out reason)) return false;
            var laws = Campaign.Current?.GetCampaignBehavior<RealmLawBehavior>();
            if (laws == null) { reason = "realm law service unavailable"; return false; }
            try
            {
                var enemies = Kingdom.All.Where(k => k != null && !k.IsEliminated && k != journal.Parent
                    && journal.Parent.IsAtWarWith(k)).Select(k => k.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList();
                var policies = journal.Parent.ActivePolicies.Select(p => p.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (journal.InheritedLaws == null && !journal.GovernmentStarted)
                {
                    var snapshot = laws.CapturePartitionLaws(journal.Parent);
                    journal.InheritedPolicies = policies;
                    journal.InheritedEnemyIds = enemies;
                    journal.InheritedLaws = snapshot;
                }
                if (journal.InheritedLaws?.RealmId != journal.Parent.StringId || journal.InheritedPolicies == null
                    || journal.InheritedEnemyIds == null || !policies.SequenceEqual(journal.InheritedPolicies)
                    || !enemies.SequenceEqual(journal.InheritedEnemyIds) || !laws.MatchesPartitionLaws(journal.Parent, journal.InheritedLaws))
                { reason = "source government or wars changed after setup capture"; return false; }
                if (journal.GovernmentStarted && !journal.GovernmentReturned)
                { reason = "government setup was interrupted; law and policy callbacks will not be replayed"; return false; }
                if (!journal.GovernmentStarted)
                {
                    journal.GovernmentStarted = true;
                    laws.InitializePartitionLaws(journal);
                    RebelPolicyHelper.InitializePartitionPolicies(journal);
                    Campaign.Current.GetCampaignBehavior<SuccessionLawBehavior>()?.NotifyLawChanged(journal.Successor);
                    journal.GovernmentReturned = true;
                }
                if (!laws.MatchesPartitionLaws(journal.Successor, journal.InheritedLaws)
                    || !new HashSet<string>(journal.InheritedPolicies).SetEquals(journal.Successor.ActivePolicies.Select(p => p.StringId)))
                { reason = "successor government no longer matches inherited selections"; return false; }
                journal.GovernmentVerified = true;
                if (journal.WarDeclarationsStarted == null || journal.WarDeclarationsReturned == null
                    || journal.WarDeclarationsStarted.Distinct().Count() != journal.WarDeclarationsStarted.Count
                    || journal.WarDeclarationsReturned.Distinct().Count() != journal.WarDeclarationsReturned.Count
                    || journal.WarDeclarationsStarted.Except(enemies).Any()
                    || journal.WarDeclarationsReturned.Except(journal.WarDeclarationsStarted).Any())
                { reason = "inconsistent inherited-war receipts"; return false; }
                foreach (string id in enemies)
                {
                    Kingdom enemy = Kingdom.All.Single(k => k.StringId == id && !k.IsEliminated);
                    if (!CrownPartitionWarSetup.TryDeclare(journal, id, () => journal.Successor.IsAtWarWith(enemy),
                        () => ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                            () => DeclareWarAction.ApplyByDefault(journal.Successor, enemy)), out reason)) return false;
                }
                var actualEnemies = Kingdom.All.Where(k => k != null && !k.IsEliminated && k != journal.Successor
                    && journal.Successor.IsAtWarWith(k)).Select(k => k.StringId);
                if (!new HashSet<string>(enemies).SetEquals(actualEnemies))
                { reason = "war callbacks produced unexpected successor enemies"; return false; }
                journal.TransferDiplomacyPrepared = true;
                journal.PendingReason = null;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "successor government or war setup interrupted: " + ex.Message;
                journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
                return false;
            }
        }
    }
}
