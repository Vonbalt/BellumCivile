using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class RealmLawBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, RealmLawSelectionRecord> _realms = new Dictionary<string, RealmLawSelectionRecord>(StringComparer.Ordinal);
        public override void RegisterEvents() { }
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BellumCivile_RealmLawGroups", ref _realms);
            if (_realms == null) _realms = new Dictionary<string, RealmLawSelectionRecord>(StringComparer.Ordinal);
        }

        internal SuccessionLawSet GetSuccessionLaws(Kingdom realm)
        {
            var record = GetOrInitialize(realm);
            var registry = RealmLawRegistry.Instance;
            return new SuccessionLawSet(
                registry.Find(record.SelectedLaws[RealmLawRegistry.GenderGroup]).GenderValue.Value,
                registry.Find(record.SelectedLaws[RealmLawRegistry.SuccessionGroup]).SuccessionValue.Value);
        }

        public string GetActiveLawId(Kingdom realm, string group)
        {
            realm = Campaign.Current.GetCampaignBehavior<SuccessionLawBehavior>().ResolvePermanentRealm(realm);
            if (realm == null) return null;
            return GetOrInitialize(realm).SelectedLaws.TryGetValue(group, out string id) ? id : null;
        }

        private RealmLawSelectionRecord GetOrInitialize(Kingdom realm)
        {
            if (realm == null) throw new ArgumentNullException(nameof(realm));
            if (_realms.TryGetValue(realm.StringId, out var existing))
            {
                // Extend an S3 test save with the newly introduced group only.
                if (existing.SelectedLaws?.Count == 2 && !existing.SelectedLaws.ContainsKey(RealmLawRegistry.TermGroup))
                    existing.SelectedLaws.Add(RealmLawRegistry.TermGroup, RealmLawRegistry.Instance.ForTerm(
                        SuccessionConfig.Instance.GetDefaultMandateYears(realm.Culture?.StringId, realm.StringId)).Id);
                Validate(existing);
                return existing;
            }
            var registry = RealmLawRegistry.Instance;
            var selections = registry.Groups.ToDictionary(g => g, registry.DefaultFor, StringComparer.Ordinal);
            // Generated permanent realms inherit their originating realm's full group selection.
            var origin = _realms.Where(p => realm.StringId.StartsWith(p.Key + "_restored", StringComparison.Ordinal)
                    || realm.StringId.StartsWith(p.Key + "_indep_", StringComparison.Ordinal))
                .OrderByDescending(p => p.Key.Length).Select(p => p.Value).FirstOrDefault();
            if (origin != null)
            {
                Validate(origin);
                selections = new Dictionary<string, string>(origin.SelectedLaws, StringComparer.Ordinal);
            }
            else
            {
                var defaults = SuccessionConfig.Instance.GetDefaultLaws(realm.Culture?.StringId, realm.StringId);
                selections[RealmLawRegistry.GenderGroup] = registry.ForGender(defaults.GenderLaw).Id;
                selections[RealmLawRegistry.SuccessionGroup] = registry.ForSuccession(defaults.SuccessionLaw).Id;
                selections[RealmLawRegistry.TermGroup] = registry.ForTerm(
                    SuccessionConfig.Instance.GetDefaultMandateYears(realm.Culture?.StringId, realm.StringId)).Id;
            }
            var record = new RealmLawSelectionRecord { RealmId = realm.StringId, SelectedLaws = selections };
            Validate(record);
            _realms.Add(realm.StringId, record);
            return record;
        }

        internal RealmLawSelectionRecord CapturePartitionLaws(Kingdom source)
        {
            var current = GetOrInitialize(source);
            Validate(current);
            return new RealmLawSelectionRecord { RealmId = source.StringId,
                SelectedLaws = new Dictionary<string, string>(current.SelectedLaws, StringComparer.Ordinal) };
        }

        internal bool MatchesPartitionLaws(Kingdom realm, RealmLawSelectionRecord snapshot)
        {
            if (realm == null || snapshot == null) return false;
            Validate(snapshot);
            var current = GetOrInitialize(realm);
            return current.SelectedLaws.Count == snapshot.SelectedLaws.Count
                && snapshot.SelectedLaws.All(p => current.SelectedLaws.TryGetValue(p.Key, out string id) && id == p.Value);
        }

        internal void InitializePartitionLaws(CrownPartitionPromotionRecord journal)
        {
            if (journal?.GovernmentStarted != true || journal.GovernmentReturned || journal.Completed
                || journal.Parent == null || journal.Successor == null || journal.Successor == journal.Parent
                || journal.Successor.StringId != journal.SuccessorId || journal.Successor.RulingClan != journal.Founder
                || journal.InheritedLaws?.RealmId != journal.Parent.StringId)
                throw new InvalidOperationException("Invalid Crown partition law initialization context.");
            Validate(journal.InheritedLaws);
            _realms[journal.SuccessorId] = new RealmLawSelectionRecord { RealmId = journal.SuccessorId,
                SelectedLaws = new Dictionary<string, string>(journal.InheritedLaws.SelectedLaws, StringComparer.Ordinal) };
        }

        private static void Validate(RealmLawSelectionRecord record)
        {
            var registry = RealmLawRegistry.Instance;
            if (record?.SelectedLaws == null || record.SelectedLaws.Count != registry.Groups.Count()
                || registry.Groups.Any(g => !record.SelectedLaws.TryGetValue(g, out string id) || registry.Find(id)?.GroupId != g))
                throw new InvalidOperationException("Invalid saved realm law groups; no default substitution was applied.");
        }

        // All player law controls use this replacement operation, never remove/enact pairs.
        // Court adapters must add their own explicit authorization path before using this store.
        public bool TryApplyPlayerLaw(Kingdom realm, string lawId, string expectedActiveLawId, out TextObject failure)
        {
            var law = RealmLawRegistry.Instance.Find(lawId);
            if (law == null)
            {
                failure = new TextObject("{=BC_LawGroup_Unknown}This law is not registered.");
                return false;
            }
            var succession = Campaign.Current.GetCampaignBehavior<SuccessionLawBehavior>();
            if (!succession.CanPlayerChangeLaws(realm, out failure)) return false;
            if (law.GroupId == RealmLawRegistry.TermGroup && !ElectiveSuccessionBehavior.UsesElection(realm))
            {
                failure = new TextObject("{=BC_SuccessionPanel_TermsHereditary}Elective mandates do not apply to hereditary succession.");
                return false;
            }
            var record = GetOrInitialize(realm);
            if (!TryReplace(record, law, expectedActiveLawId, out failure)) return false;
            if (law.GroupId == RealmLawRegistry.TermGroup) ElectiveSuccessionBehavior.Instance?.ApplyMandateReform(realm, law.MandateYears.Value);
            succession.CompletePlayerLawChange(realm, law.Name);
            HereditaryLoyaltyBehavior.Instance?.Invalidate();
            if (law.GroupId == RealmLawRegistry.SuccessionGroup) ElectiveSuccessionBehavior.Instance?.Maintain(realm);
            return true;
        }

        internal bool TryApplyCourtMandate(MandateReformDecision decision, out TextObject failure)
        {
            failure = new TextObject("{=BC_MandateInvalid}The proposal could no longer be decided.");
            if (decision == null || !decision.Attempted || CourtAgendaBehavior.Current?.ValidateMandateDecision(decision) != true) return false;
            var law = RealmLawRegistry.Instance.Find(decision.NewLaw);
            if (law?.GroupId != RealmLawRegistry.TermGroup || !TryReplace(GetOrInitialize(decision.Kingdom), law, decision.OldLaw, out failure)) return false;
            ElectiveSuccessionBehavior.Instance?.ApplyMandateReform(decision.Kingdom, law.MandateYears.Value);
            try { Campaign.Current?.GetCampaignBehavior<SuccessionLawBehavior>()?.NotifyLawChanged(decision.Kingdom); }
            catch (Exception ex) { BellumCivileLogger.Log($"Mandate law enacted but view refresh failed; motion={decision.MotionId}; error={ex}."); }
            return true;
        }

        internal static bool TryReplace(RealmLawSelectionRecord record, RealmLawDefinition law,
            string expectedActiveLawId, out TextObject failure)
        {
            if (record?.SelectedLaws == null || law == null
                || !record.SelectedLaws.TryGetValue(law.GroupId, out string current)
                || current != expectedActiveLawId)
            {
                failure = new TextObject("{=BC_LawGroup_Changed}The active law has changed. Review the proposal again.");
                return false;
            }
            if (current == law.Id)
            {
                failure = new TextObject("{=BC_SuccessionLaw_AlreadyActive}This law is already in force.");
                return false;
            }
            record.SelectedLaws[law.GroupId] = law.Id;
            failure = new TextObject(string.Empty);
            return true;
        }
    }
}
