using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    // Hereditary and elective Crown transfers share a saved journal; household estates retain their owner.
    public sealed partial class CrownAccessionBehavior : CampaignBehaviorBase
    {
        private List<CrownAccessionRecord> _accessions = new List<CrownAccessionRecord>();
        private readonly HashSet<CrownAccessionRecord> _resolving = new HashSet<CrownAccessionRecord>();
        public static CrownAccessionBehavior Instance => Campaign.Current?.GetCampaignBehavior<CrownAccessionBehavior>();

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ResumePending);
            CampaignEvents.OnPlayerCharacterChangedEvent.AddNonSerializedListener(this, OnPlayerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_CrownAccessions", ref _accessions);
            _accessions = _accessions ?? new List<CrownAccessionRecord>();
        }

        public void Capture(Hero victim)
        {
            Kingdom realm = victim?.Clan?.Kingdom;
            if (realm == null || realm.IsEliminated || realm.RulingClan != victim.Clan
                || _accessions.Any(x => x.Realm == realm && ((!x.IsAbdication && !x.MandateExpiry && x.Predecessor == victim) || !x.Completed))
                || SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) != realm)
                return;
            Hero sovereign = RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.Leader;
            if (victim != sovereign && victim != realm.RulingClan.Leader) return;
            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            if (SuccessionRealmRules.Classify(laws.SuccessionLaw) == RealmSuccessionSystem.Unknown)
                return;
            // An already pending abdication/election retains ownership of its settlement.
            if (realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()) return;
            var record = new CrownAccessionRecord
            {
                Realm = realm, Predecessor = victim, PreviousHouse = victim.Clan,
                RegentReplacement = sovereign != victim, PlayerContinuation = victim == Hero.MainHero,
                Started = CampaignTime.Now, HouseLaw = laws.SuccessionLaw, GenderLaw = laws.GenderLaw,
                ElectiveElection = sovereign == victim && ElectiveSuccessionBehavior.UsesElection(realm),
                ElectiveExcluded = ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm)?.DeposedRuler,
                DepositionElection = ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
            };
            record.Heir = record.RegentReplacement ? sovereign : record.ElectiveElection ? null
                : HereditaryRealmSuccession.GetLine(realm, victim, laws).FirstOrDefault();
            _accessions.Add(record);
        }

        public bool BeginElectiveAbdication(Kingdom realm)
        {
            if (!ElectiveSuccessionBehavior.UsesElection(realm) || IsPending(realm)
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()
                || realm.RulingClan != Clan.PlayerClan || ElectiveSuccessionBehavior.LegalHead(realm.RulingClan) != Hero.MainHero) return false;
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            var record = new CrownAccessionRecord { Realm = realm, Predecessor = Hero.MainHero, PreviousHouse = realm.RulingClan,
                Started = CampaignTime.Now, HouseLaw = laws.SuccessionLaw, GenderLaw = laws.GenderLaw,
                VoluntaryAbdication = true, ElectiveElection = true };
            _accessions.Add(record);
            Resolve(record);
            return true;
        }

        public bool BeginMandateElection(Kingdom realm, bool testContest = false)
        {
            var standing = ElectiveSuccessionBehavior.Instance?.Get(realm);
            if (standing?.ReformElectionPending == true && standing.ElectionDate.ToDays > CampaignTime.Now.ToDays) return false;
            var deposition = ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm);
            if (deposition != null && (deposition.DepositionAwaitingJudgments || !deposition.InterimPrepared || !deposition.DepositionElectionDate.IsPast)) return false;
            if (!ElectiveSuccessionBehavior.UsesElection(realm) || IsPending(realm)
                || realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()
                || ElectiveSuccessionBehavior.LegalHead(realm.RulingClan)?.IsAlive != true) return false;
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            var record = new CrownAccessionRecord { Realm = realm, Predecessor = ElectiveSuccessionBehavior.LegalHead(realm.RulingClan),
                PreviousHouse = realm.RulingClan, Started = CampaignTime.Now, HouseLaw = laws.SuccessionLaw,
                GenderLaw = laws.GenderLaw, MandateExpiry = true, ElectiveElection = true, ElectiveContestTest = testContest,
                ElectiveExcluded = deposition?.DeposedRuler, DepositionElection = deposition != null };
            _accessions.Add(record);
            Resolve(record);
            return true;
        }

        public bool OwnsDeath(Kingdom realm, Hero victim) =>
            _accessions.Any(x => x.Realm == realm && ((!x.IsAbdication && (!x.MandateExpiry || !x.Completed) && x.Predecessor == victim)
                || !x.Completed && x.ForcedAbdication && x.Predecessor == victim
                || !x.Completed && x.Emergency && victim?.Clan != null && victim.Clan == realm.RulingClan));

        public bool IsPending(Kingdom realm) => _accessions.Any(x => x.Realm == realm && !x.Completed);

        public bool IsPendingCrownHeir(Hero heir) => heir != null
            && _accessions.Any(x => !x.Completed && !x.Emergency && x.Heir == heir);

        public CrownAccessionRecord GetEmergency(KingSelectionKingdomDecision decision) =>
            decision == null ? null : _accessions.FirstOrDefault(x => x.Emergency && x.EmergencyElection == decision);

        public bool KeepInterregnumHouse(Clan clan)
        {
            var record = clan == null ? null : _accessions.FirstOrDefault(x =>
                !x.Completed && x.Realm?.RulingClan == clan);
            if (record == null) return false;
            if (record.DeferredHouseDestructions == null) record.DeferredHouseDestructions = new List<Clan>();
            if (!record.DeferredHouseDestructions.Contains(clan)) record.DeferredHouseDestructions.Add(clan);
            return true;
        }

        public void AfterDeath(Hero victim)
        {
            foreach (var record in _accessions.Where(x => !x.Completed && (x.Predecessor == victim
                || x.Emergency && victim?.Clan != null && x.Realm.RulingClan == victim.Clan)).ToList())
            {
                if (record.MandateExpiry && victim == Hero.MainHero) record.PlayerContinuation = true;
                Resolve(record);
            }
        }

        private void OnPlayerChanged(Hero oldPlayer, Hero newPlayer, MobileParty party, bool changed) => AfterDeath(oldPlayer);

        private void ResumePending()
        {
            foreach (var record in _accessions.Where(x => !x.Completed).ToList()) Resolve(record);
            foreach (var record in _accessions.Where(x => x.DeferredHouseDestructions?.Count > 0).ToList())
                ReleaseInterregnumHouse(record);
            SettleSatisfiedAbdicationFactions();
        }

        private static void ReleaseInterregnumHouse(CrownAccessionRecord record)
        {
            foreach (Clan house in record.DeferredHouseDestructions.ToList())
            {
                if (house != null && !house.IsEliminated && house.Leader?.IsAlive != true)
                {
                    if (record.Realm.RulingClan == house) continue;
                    DestroyClanAction.ApplyByClanLeaderDeath(house);
                }
                record.DeferredHouseDestructions.Remove(house);
            }
        }

        private void Resolve(CrownAccessionRecord record)
        {
            if (record.Completed || (!record.IsAbdication && !record.MandateExpiry && record.Predecessor?.IsDead != true)
                || !_resolving.Add(record)) return;
            try
            {
                if (record.Union != null)
                {
                    TryAdvanceRealmUnion(record, out _);
                    return;
                }
                if (record.Realm == null || record.Realm.IsEliminated)
                {
                    record.Completed = true;
                    return;
                }
                if (record.ForcedAbdication && record.Predecessor.IsDead && !record.CadetInitialized)
                {
                    // No estate has moved: death now owns the lawful succession. Native
                    // household inheritance proceeds without a second living endowment.
                    record.ForcedAbdication = false;
                    record.CreatesCadet = false;
                    record.PlayerContinuation = record.Predecessor == Hero.MainHero;
                    record.Heir = record.Heir ?? HereditaryRealmSuccession.GetLine(record.Realm,
                        record.Predecessor, new SuccessionLawSet(record.GenderLaw, record.HouseLaw)).FirstOrDefault();
                }
                if (record.PlayerContinuation && Hero.MainHero == record.Predecessor) return;
                if (record.MandateExpiry && record.Predecessor.IsDead && !record.TitleTransferred)
                {
                    if (!record.ExpiryPredecessorDied)
                    {
                        record.ExpiryPredecessorDied = true;
                        record.Started = CampaignTime.Now;
                    }
                    // Let the household death/regency hooks finish before rebuilding this ballot.
                    if ((record.Started + CampaignTime.Hours(1f)).IsFuture
                        && (record.PreviousHouse.Leader?.IsAlive != true
                            || ElectiveSuccessionBehavior.LegalHead(record.PreviousHouse) == record.Predecessor)) return;
                }
                if (record.ElectiveElection && !record.IsAbdication && !record.MandateExpiry && !record.ElectiveSelected
                    && (record.Started + CampaignTime.Hours(1f)).IsFuture
                    && (record.PreviousHouse.Leader?.IsAlive != true
                        || ElectiveSuccessionBehavior.LegalHead(record.PreviousHouse) == record.Predecessor))
                    return; // Native household/regency replacement runs after the post-death Crown hook.
                if (record.ElectiveElection && record.ElectiveSelected && !record.Emergency && !record.TitleTransferred
                    && record.Realm.RulingClan == record.PreviousHouse
                    && (record.Heir?.IsAlive != true || record.Heir.IsDisabled || record.Heir.Clan?.Kingdom != record.Realm
                        || ElectiveSuccessionBehavior.LegalHead(record.Heir.Clan) != record.Heir))
                    record.ElectiveSelected = false;
                if (record.ElectiveElection && !record.ElectiveSelected)
                {
                    if (ElectiveSuccessionBehavior.Instance?.SelectSuccessor(record) != true) return;
                    record.ElectiveSelected = true;
                }
                if (!ReconcileCrownHousehold(record)) return;
                if (record.ForcedAbdication && !PrepareForcedAbdication(record)) return;
                if (record.Emergency) { EnsureEmergency(record); return; }
                if (record.IsAbdication && !record.ElectiveElection && !PrepareAbdicationTransfer(record)) return;
                Hero heir = record.Heir;
                if (heir == null || !heir.IsAlive || heir.IsDisabled || !HereditaryRealmSuccession.CanConsiderClan(heir.Clan, record.Realm)
                    || heir.Clan.IsEliminated)
                {
                    if (record.IncomingHousePrepared || record.ForeignMovingClan != null)
                    {
                        DeferAbdication(record, "the recorded incoming heir is unavailable during settlement");
                        return;
                    }
                    record.Emergency = true;
                    EnsureEmergency(record);
                    return;
                }
                if (!PrepareIncomingCrownHouse(record)) return;
                Clan house = heir.Clan;
                if (heir.Age < SuccessionLawHelper.GetAgeOfMajority())
                {
                    if (RegencyBehavior.Instance?.EnsureCrownHeirRegency(house, heir, record.Predecessor) != true) return;
                }
                else if (house.Leader != heir)
                {
                    return;
                }
                if (!record.MandateExpiry && Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>()?.SettleCrownDeathEstate(heir) == false)
                    return;
                if (!PrepareForeignCrownClan(record, house)) return;
                if (record.Realm.RulingClan != house) ChangeRulingClanAction.Apply(record.Realm, house);
                record.Heir = heir;
                Finish(record, house);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Crown accession deferred; realm={record.Realm?.StringId}; predecessor={record.Predecessor?.StringId}; error={ex}");
            }
            finally { _resolving.Remove(record); }
        }

        public List<Clan> EmergencyCandidates(CrownAccessionRecord record)
        {
            var candidates = record.Realm.Clans.Where(clan =>
                NobleClanEligibilityHelper.IsValidRulingClan(clan, record.Realm)
                && clan.Leader.IsAlive && !clan.Leader.IsDisabled
                && (!record.IsAbdication || clan.Leader != record.Predecessor)
                && clan.Leader != record.ElectiveExcluded
                && clan.Leader.Age >= SuccessionLawHelper.GetAgeOfMajority()).ToList();
            var legal = candidates.Where(clan => SuccessionLawHelper.IsEligibleUnderGenderLaw(
                clan.Leader, new SuccessionLawSet(record.GenderLaw, record.HouseLaw))).ToList();
            record.RelaxGender = legal.Count == 0 && candidates.Count > 0;
            return legal.Count > 0 ? legal : candidates;
        }

        private void EnsureEmergency(CrownAccessionRecord record)
        {
            if (record.OutcomeApplied)
            {
                if (record.Heir?.Clan == record.Realm.RulingClan) Finish(record, record.Heir.Clan);
                return;
            }
            if (record.EmergencyElection != null
                && record.Realm.UnresolvedDecisions.Contains(record.EmergencyElection))
            {
                if (NobleClanEligibilityHelper.IsValidRulingClan(record.EmergencyElection.ProposerClan, record.Realm)
                    && !record.Nominees.Any(hero => hero != null
                    && (!hero.IsAlive || hero.IsDisabled || hero.Clan?.Leader != hero
                        || hero.Clan.Kingdom != record.Realm))) return;
                record.Realm.RemoveDecision(record.EmergencyElection);
            }
            var candidates = EmergencyCandidates(record);
            if (candidates.Count == 0) return;
            if (record.Realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()) return;
            Clan proposer = candidates.OrderBy(clan => clan.StringId, StringComparer.Ordinal).First();
            if (!NobleClanEligibilityHelper.IsValidRulingClan(record.Realm.RulingClan, record.Realm))
                ChangeRulingClanAction.Apply(record.Realm, proposer);
            // Save the exact decision identity before AddDecision can call candidate hooks.
            record.FirstNominee = record.SecondNominee = record.ThirdNominee = null;
            record.EmergencyElection = new KingSelectionKingdomDecision(proposer, null);
            record.Realm.AddDecision(record.EmergencyElection, true);
        }

        public void CompleteEmergency(KingSelectionKingdomDecision decision, Clan winner)
        {
            var record = GetEmergency(decision);
            if (record == null || record.Completed || winner == null || record.Realm.RulingClan != winner) return;
            record.Heir = RegencyBehavior.Instance?.GetLegalClanHead(winner) ?? winner.Leader;
            record.OutcomeApplied = true;
            try
            {
                Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>()?.CaptureElectionEndorsements(record);
                Finish(record, winner);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Emergency Crown result applied; completion deferred; realm={record.Realm.StringId}; error={ex}");
            }
        }

        private void Finish(CrownAccessionRecord record, Clan house)
        {
            if (!record.TitleTransferred)
            {
                if (Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.TrySetKingdomTitleRuler(
                    record.Realm, house, true, record.ForcedAbdication ? "forced hereditary abdication"
                        : record.ElectiveElection ? "elective succession"
                        : record.VoluntaryAbdication ? "voluntary hereditary abdication" : "hereditary death accession") != true) return;
                record.TitleTransferred = true;
            }
            if (record.ElectiveElection && !record.MandateStarted)
            {
                ElectiveSuccessionBehavior.Instance?.BeginSuccessorMandate(record.Realm);
                record.MandateStarted = true;
                record.DefeatNoticePending = record.MandateExpiry && record.PreviousHouse == Clan.PlayerClan
                    && house != Clan.PlayerClan && record.Predecessor == Hero.MainHero && Hero.MainHero.IsAlive;
            }
            if (!record.Announced && !record.RegentReplacement)
            {
                record.Announced = true;
                var message = new TextObject(record.Emergency || record.ElectiveElection
                    ? "{=BC_CrownEmergencyAccession}The nobles of {REALM} have chosen {HEIR} to bear the Crown."
                    : record.ForcedAbdication
                    ? "{=BC_CrownForcedAccession}{RULER} has been compelled to relinquish the Crown of {REALM}. By right of succession, {HEIR} now reigns."
                    : record.VoluntaryAbdication
                    ? "{=BC_CrownVoluntaryAccession}{RULER} has laid down the Crown of {REALM}. By right of succession, {HEIR} now reigns."
                    : "{=BC_CrownLawfulAccession}Following the death of {RULER}, the Crown of {REALM} passes to {HEIR} by right of succession.");
                message.SetTextVariable("REALM", record.Realm.Name);
                message.SetTextVariable("HEIR", record.Heir.Name);
                message.SetTextVariable("RULER", record.Predecessor.Name);
                if (record.ElectiveElection)
                {
                    message = new TextObject(record.Heir == record.Predecessor && !record.DepositionElection
                        ? "{=BC_Election_Reelected}The nobles of {REALM} have renewed their trust in {HEIR}, granting a new mandate: {TERM}."
                        : "{=BC_Election_Elected}The nobles of {REALM} have chosen {HEIR} to bear the Crown for a new mandate: {TERM}.");
                    message.SetTextVariable("REALM", record.Realm.Name);
                    message.SetTextVariable("HEIR", record.Heir.Name);
                    var years = ElectiveSuccessionBehavior.Instance?.Get(record.Realm)?.MandateYears ?? 0;
                    message.SetTextVariable("TERM", RealmLawRegistry.Instance.ForTerm(years).Name);
                }
                if (Clan.PlayerClan?.Kingdom == record.Realm || record.EndowmentHouse == Clan.PlayerClan
                    || record.ForeignOriginRealm != null && Clan.PlayerClan?.Kingdom == record.ForeignOriginRealm)
                    BellumCivileNotifications.ShowPersonal(message, BellumNotificationColors.Inheritance);
            }
            if (!record.RegentReplacement)
                Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.RecognizeLawfulCrownAccession(record.Realm);
            if (!record.RegentReplacement)
            {
                var shocks = Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>();
                if (shocks == null) return;
                if ((record.Heir != record.Predecessor || record.DepositionElection)
                    && !shocks.CompleteCrownAccession(record.Realm, record.Heir, record)) return;
                if ((record.ElectiveElection || record.Emergency)
                    && !shocks.CompleteElectionReactions(record,
                        record.ElectiveElection ? ElectiveSuccessionBehavior.Instance?.Get(record.Realm) : null)) return;
            }
            record.Completed = true;
            ElectiveContestBehavior.Instance?.CompleteAccession(record);
            if (!record.RegentReplacement) HereditaryLoyaltyBehavior.Instance?.Observe(record.Realm, false);
            if (record.ElectiveElection) ElectiveSuccessionBehavior.Instance?.Complete(record.Realm);
        }

        public CrownAccessionRecord PendingElectoralDefeat() => _accessions.FirstOrDefault(x => x.Completed && x.DefeatNoticePending);
    }
}
