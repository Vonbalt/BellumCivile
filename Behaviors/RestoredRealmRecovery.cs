using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private List<RestoredRealmCreationRecord> _restoredCreations = new List<RestoredRealmCreationRecord>();
        private readonly HashSet<RestoredRealmCreationRecord> _resumingRestoredCreations = new HashSet<RestoredRealmCreationRecord>();
        private readonly HashSet<RestoredRealmCreationRecord> _preparingRestoredCreations = new HashSet<RestoredRealmCreationRecord>();

        internal bool IsRestoredCreationPending(Kingdom realm) => _restoredCreations.Any(r => r.Protects(realm));

        private void ResumePendingRestoredCreations()
        {
            foreach (var record in _restoredCreations.Where(r => !r.Ready).ToList())
            {
                if (!_resumingRestoredCreations.Add(record)) continue;
                try
                {
                    if (record.Challenge != null) ResumeChallengeOutcome(record.Challenge);
                    else if (record.Owner != null) HandleParentCollapse(record.Parent, record.ParentWasEliminated);
                    else TryRepairEliminatedKingdomWithLiveState(record.Parent, "resume restored realm creation");
                }
                catch (Exception ex) { RecordRestorationFailure(record, ex.Message); }
                finally { _resumingRestoredCreations.Remove(record); }
            }
        }

        private Kingdom PrepareRestoredRealm(Kingdom parent, Kingdom source, Clan founder, bool replacement,
            bool immediateRepair, SuccessionChallengeRecord challenge = null, FactionObject owner = null, IEnumerable<Kingdom> inheritedWars = null)
        {
            if (parent == null || source == null || !KingdomCreationSafetyHelper.IsValidFounder(founder)) return null;
            var record = _restoredCreations.LastOrDefault(r => r.Parent == parent && r.Source == source && r.Replacement == replacement);
            if (record == null)
            {
                if (!ReleaseMercenariesFromKingdom(source)) return null;
                string seed = parent.StringId + "_restored" + (challenge == null ? "" : "_challenge_" + challenge.Id);
                string id = seed;
                int suffix = 0;
                var adopted = challenge == null ? null : Kingdom.All.FirstOrDefault(k => k.StringId == seed);
                if (adopted == null)
                    while (Kingdom.All.Any(k => k.StringId == id)) id = seed + "_" + (++suffix);
                record = new RestoredRealmCreationRecord {
                    Parent = parent, Source = source, Founder = founder, Replacement = replacement,
                    SuccessorId = adopted?.StringId ?? id, Successor = adopted,
                    Initialized = adopted != null && challenge.RestoredRealmInitialized,
                    ParentWasEliminated = parent.IsEliminated, Owner = owner ?? challenge?.WarFaction, Challenge = challenge,
                    Houses = source.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c.Kingdom == source).ToList(),
                    ParentHouses = GetLiveNobleClans(parent).ToList(),
                    ExternalEnemies = (inheritedWars ?? (parent.IsEliminated ? new List<Kingdom>()
                        : GetExternalEnemiesToInherit(parent, new[] { source }))).ToList()
                };
                _restoredCreations.Add(record);
            }
            if (record.Ready) return record.Successor?.IsEliminated == false ? record.Successor : null;
            if (!_preparingRestoredCreations.Add(record)) return null;
            try
            {
                var realm = record.Successor ?? Kingdom.All.FirstOrDefault(k => k.StringId == record.SuccessorId);
                record.Successor = realm ?? KingdomCreationSafetyHelper.CreateKingdom(record.SuccessorId, record.Founder);
                realm = record.Successor;
                if (realm == null || realm.IsEliminated) { RecordRestorationFailure(record, "chosen successor is unavailable"); return null; }
                if (record.Founder.Kingdom != source && record.Founder.Kingdom != realm)
                { RecordRestorationFailure(record, "founder left the recorded realms"); return null; }
                if (realm.Leader == null && !KingdomCreationSafetyHelper.PrepareRuler(realm, record.Founder, "resume restored creation")) return null;
                if (challenge != null) challenge.OutcomeRealm = realm;
                var visuals = KingdomVisualHelper.ResolveInheritedKingdomVisuals(parent, record.Founder, realm.StringId);
                if (!record.Initialized)
                {
                    var capital = record.Founder.Settlements.FirstOrDefault() ?? source.Settlements.FirstOrDefault()
                        ?? parent.Settlements.FirstOrDefault() ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
                    realm.InitializeKingdom(parent.Name, parent.Name, parent.Culture ?? source.Culture,
                        visuals.Banner, visuals.PrimaryColor, visuals.SecondaryColor, capital,
                        new TextObject(""), new TextObject(""), new TextObject(""));
                    record.Initialized = true;
                    if (challenge != null) challenge.RestoredRealmInitialized = true;
                }
                KingdomVisualHelper.ApplyKingdomPalette(realm, visuals);
                RebelPolicyHelper.CopyPolicies(parent.ActivePolicies.Any() ? parent : source, realm);
                if (!record.MovesComplete)
                {
                    bool moved = replacement ? TransferLiveClansFromEliminatedKingdom(source, realm, record.Founder)
                        : TransferClansToKingdom(source, realm, record.Founder);
                    if (!moved || record.Houses.Any(c => !c.IsEliminated && c.Kingdom != realm)
                        || !NobleClanEligibilityHelper.IsValidRulingClan(realm.RulingClan, realm))
                    { RecordRestorationFailure(record, "household transfer pending"); return null; }
                    record.MovesComplete = true;
                }
                if (!replacement)
                {
                    PreservePromotedRebelForeignWars(source, realm, parent);
                    if (!DrainAndDestroyRebelKingdom(source, realm))
                    { RecordRestorationFailure(record, "source retirement pending"); return null; }
                    if (parent.IsEliminated)
                    {
                        if (!TransferLiveClansFromEliminatedKingdom(parent, realm, record.Founder))
                        { RecordRestorationFailure(record, "former parent household transfer pending"); return null; }
                    }
                    else if (!DrainAndDestroyRebelKingdom(parent, realm))
                    { RecordRestorationFailure(record, "parent retirement pending"); return null; }
                    if (record.ParentHouses.Any(c => !c.IsEliminated && c.Kingdom != realm))
                    { RecordRestorationFailure(record, "recorded parent household transfer pending"); return null; }
                }
                EnsureValidRulingClan(realm, record.Founder);
                if (!NobleClanEligibilityHelper.IsValidRulingClan(realm.RulingClan, realm)) return null;
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                if (titles == null) { RecordRestorationFailure(record, "title service unavailable"); return null; }
                titles.RegisterRestoredRealmMantle(parent, realm,
                    realm.RulingClan, replacement ? "emergency restored replacement" : "parent collapse succession");
                var crown = titles.GetIndependentRealmSovereignTitle(realm);
                if (crown == null || crown.AssociatedKingdomId != realm.StringId || crown.DeFactoHolderClanId != realm.RulingClan.StringId)
                { RecordRestorationFailure(record, "successor crown transfer pending"); return null; }
                ClearTemporaryKingdomRepair(realm);
                if (replacement) Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(realm);
                if (immediateRepair) RepairCompatibilityStateNow($"prepared restored realm {realm.StringId} from {parent.StringId}");
                // Publish only after membership and leadership are usable. Mark before callbacks
                // so a save or exception inside another listener cannot replay earlier listeners.
                if (!record.CreatedNotified)
                {
                    record.CreatedNotified = true;
                    CampaignEventDispatcher.Instance.OnKingdomCreated(realm);
                }
                if (realm.IsEliminated || !NobleClanEligibilityHelper.IsValidRulingClan(realm.RulingClan, realm)) return null;
                record.Ready = true;
                record.Failure = null;
                if (challenge != null) challenge.RestoredRealmReady = true;
                return realm;
            }
            catch (Exception ex) { RecordRestorationFailure(record, ex.Message); return null; }
            finally { _preparingRestoredCreations.Remove(record); }
        }

        private bool IsRestoredDrainAuthorized(Kingdom realm) => realm != null
            && _preparingRestoredCreations.Any(r => r.Parent == realm && r.MovesComplete && !r.Ready)
            && !CivilWarConflictBehavior.IsOtherRealmTransferPending(realm);

        internal bool IsRestoredRetirementAuthorized(Kingdom realm) => IsRestoredDrainAuthorized(realm)
            && !realm.Clans.Any(c => c != null && !c.IsEliminated) && CountStrongholds(realm) == 0;

        private static void RecordRestorationFailure(RestoredRealmCreationRecord record, string failure)
        {
            if (record.Failure != failure)
                BellumCivileLogger.Log($"Restored realm creation deferred; parent={record.Parent?.StringId}; source={record.Source?.StringId}; "
                    + $"successor={record.SuccessorId}; moves_complete={record.MovesComplete}; reason={failure}.");
            record.Failure = failure;
        }
    }
}
