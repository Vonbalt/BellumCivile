using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    // Saved appeals and settlements, with staggered natural initiation.
    public sealed partial class SuccessionChallengeBehavior : CampaignBehaviorBase
    {
        private List<SuccessionChallengeRecord> _records = new List<SuccessionChallengeRecord>();
        private Dictionary<string, int> _lastInitiationChecks = new Dictionary<string, int>();
        private readonly HashSet<SuccessionChallengeRecord> _settling = new HashSet<SuccessionChallengeRecord>();
        public static SuccessionChallengeBehavior Instance => Campaign.Current?.GetCampaignBehavior<SuccessionChallengeBehavior>();

        internal bool IsPressingTitleClaim(Clan clan, FeudalTitleRecord title, FeudalTitleBehavior titles)
            => clan != null && title != null && _records.Any(r => r.IsOpen
                && (r.Challenger?.Clan ?? r.OriginalHouse) == clan
                && titles.GetRealmSovereignTitle(r.WarRealm)?.TitleId == title.TitleId);
        private static double Day => CampaignTime.Now.ToDays;
        private static Hero Sovereign(Kingdom realm) => RegencyBehavior.Instance?.GetLegalClanHead(realm?.RulingClan) ?? realm?.Leader;

        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_SuccessionChallenges", ref _records);
            store.SyncData("BC_SuccessionChallengeChecks", ref _lastInitiationChecks);
            _lastInitiationChecks = _lastInitiationChecks ?? new Dictionary<string, int>();
            _records = _records ?? new List<SuccessionChallengeRecord>();
            if (store.IsLoading) { _settling.Clear(); _inquiry = null; }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Maintain);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => ProcessChallengeInquiry());
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, (realm, clan) => ReconcileSovereigns());
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (old, current) => ReconcileSovereigns());
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (hero, killer, detail, show) => ReconcileSovereigns());
        }

        private void Maintain()
        {
            ReconcileSovereigns();
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.Gathering).ToList())
                ResolveAppeal(record);
            AdvanceRulerResponses();
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.LandSettlement).ToList())
                SettleConcession(record);
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.CrownSettlement).ToList())
                SettleChallengedCrown(record);
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.WarRequired).ToList())
                DispatchRefusedChallenge(record);
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.ActiveWar).ToList())
                MaintainActiveChallenge(record);
            foreach (var record in _records.Where(r => r.Phase == SuccessionChallengePhase.ResolvingWar).ToList())
                FinishWarOutcome(record);
            InitiateScheduledChallenges();
        }

        private void ReconcileSovereigns()
        {
            foreach (var record in _records)
            {
                if (record.SubmissionRuler != null && Sovereign(record.OutcomeRealm ?? record.Realm) != record.SubmissionRuler)
                    record.SubmissionRuler = null;
                if (record.Realm?.IsEliminated != false || record.Sovereign?.IsAlive != true || Sovereign(record.Realm) != record.Sovereign)
                {
                    record.LoyaltyEnded = true;
                    // Never discard partially delivered estates; their receipts must survive reconciliation.
                    if (record.Phase == SuccessionChallengePhase.Gathering || record.Phase == SuccessionChallengePhase.AwaitingResponse)
                    {
                        record.Phase = SuccessionChallengePhase.Cancelled;
                        record.ReportPending = record.Realm == Clan.PlayerClan?.Kingdom;
                    }
                }
                if ((record.Phase == SuccessionChallengePhase.Gathering || record.Phase == SuccessionChallengePhase.AwaitingResponse)
                    && (record.Challenger?.IsAlive != true || record.Challenger.Clan?.Kingdom != record.Realm
                        || record.Challenger.Clan != record.OriginalHouse
                        || !CrownAccessionBehavior.IsHereditaryRealm(record.Realm)
                        || HereditaryLoyaltyBehavior.Instance?.GetLine(record.Realm).Contains(record.Challenger) != true))
                {
                    record.Phase = SuccessionChallengePhase.Cancelled;
                    record.ReportPending = record.Realm == Clan.PlayerClan?.Kingdom;
                }
            }
        }

        internal static bool Available(Hero hero) => hero?.IsAlive == true && hero.IsActive
            && hero.Age >= SuccessionLawHelper.GetAgeOfMajority() && !hero.IsDisabled && !hero.IsPrisoner
            && !hero.IsTraveling && hero.PartyBelongedTo?.MapEvent == null && hero.PartyBelongedTo?.SiegeEvent == null;

        internal SuccessionChallengeRecord TryBegin(Kingdom realm, Hero challenger, double demandRoll, bool playerInitiated = false)
        {
            if (realm == null || realm.IsEliminated || !CrownAccessionBehavior.IsHereditaryRealm(realm)
                || SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) != realm
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true || challenger == Hero.MainHero && !playerInitiated
                || !Available(challenger) || challenger.Clan?.Kingdom != realm) return null;
            if (playerInitiated && (challenger != Hero.MainHero || challenger.Clan?.Leader != challenger
                || challenger.Clan == realm.RulingClan)) return null;
            Hero ruler = Sovereign(realm);
            if (ruler == null || ruler == challenger) return null;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null || manager.IsClanPacified(challenger.Clan)
                || manager.GetRebelFaction(challenger.Clan) != null
                || manager.GetFactionsInKingdom(realm).Any(f => f.IsCivilWarActive())) return null;
            var score = playerInitiated ? null : HereditaryLoyaltyBehavior.Instance?.Get(realm, challenger);
            bool blocked = _records.Any(r => (r.Realm == realm || r.OutcomeRealm == realm) && (r.IsOpen || r.RealmBlockedUntil > Day
                || r.Challenger == challenger && r.PersonalBlockedUntil > Day));
            if (playerInitiated)
            {
                if (blocked || HereditaryLoyaltyBehavior.Instance?.GetLine(realm).Contains(challenger) != true) return null;
            }
            else if (!SuccessionChallengeRules.CanInitiate(score?.Total ?? double.NaN, true, score != null, blocked)) return null;
            bool dependent = challenger.Clan == realm.RulingClan && challenger.Clan.Leader != challenger;
            // A married-out dependent needs its own donor/household plan, not a royal-clan split.
            if (!dependent && challenger.Clan.Leader != challenger) return null;
            var record = new SuccessionChallengeRecord
            {
                Id = Guid.NewGuid().ToString("N"), Realm = realm, Sovereign = ruler, Challenger = challenger,
                OriginalHouse = challenger.Clan, InitialLoyalty = score?.Total ?? 0, DemandRoll = demandRoll, StartedDay = Day,
                Demand = playerInitiated ? SuccessionChallengeDemand.Crown : SuccessionChallengeRules.ChooseDemand(score.Total,
                    dependent && !HasHouseholdGrant(ruler, challenger), demandRoll)
            };
            // A dependent needs a deliverable household path before asking anyone to pledge.
            if (dependent)
            {
                var estate = PreviewConcession(record, lawfulOnly: true);
                if (estate == null || !CrownEstateAvailable(estate)) return null;
                record.CrownEstate = estate;
                record.WarEstate = PreviewConcession(record, lawfulOnly: true, hostile: true);
                if (record.WarEstate == null) return null;
                record.FallbackGrant = estate.EndowmentFiefs.Count == 0;
            }
            _records.Add(record);
            return record;
        }

        // Called only after actual, exclusive pledges have been resolved by the appeal controller.
        internal bool RecordBacking(SuccessionChallengeRecord record, IEnumerable<Clan> backers, IEnumerable<Clan> loyalists,
            double backing, double loyalistPower)
        {
            if (!_records.Contains(record) || record.Phase != SuccessionChallengePhase.Gathering || !ValidParticipants(record)) return false;
            var rebels = (backers ?? Enumerable.Empty<Clan>()).Distinct().ToList();
            var loyal = (loyalists ?? Enumerable.Empty<Clan>()).Distinct().ToList();
            if (rebels.Concat(loyal).Any(c => c == null || c.IsEliminated || c.Kingdom != record.Realm)
                || rebels.Intersect(loyal).Any() || rebels.Contains(record.Realm.RulingClan)) return false;
            double threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(record.Challenger);
            if (!SuccessionChallengeRules.Finite(backing) || !SuccessionChallengeRules.Finite(loyalistPower)
                || backing < 0 || loyalistPower < 0) return false;
            record.Backers = rebels; record.Loyalists = loyal;
            if (record.OriginalHouse == record.Realm.RulingClan)
            {
                record.CrownEstate = PreviewConcession(record, lawfulOnly: true);
                if (record.CrownEstate == null) return false;
                record.WarEstate = PreviewConcession(record, lawfulOnly: true, hostile: true);
                if (record.WarEstate == null) return false;
            }
            record.CreditedHeirPartyPower = TransferableHeirPower(record, loyalistPower);
            backing += record.CreditedHeirPartyPower;
            loyalistPower -= record.CreditedHeirPartyPower;
            record.BackingPower = backing; record.LoyalistPower = loyalistPower; record.RequiredRatio = threshold;
            if (!SuccessionChallengeRules.CanProceed(backing, loyalistPower, threshold,
                record.Challenger == Hero.MainHero, HasCoalitionStronghold(record)))
            {
                WithdrawForInsufficientBacking(record);
                return true;
            }
            if (record.Demand == SuccessionChallengeDemand.InheritanceFirst) record.Estate = PreviewConcession(record);
            record.Phase = SuccessionChallengePhase.AwaitingResponse;
            record.ReportPending = record.Realm == Clan.PlayerClan?.Kingdom;
            return true;
        }

        internal bool RecordNpcResponse(SuccessionChallengeRecord record, double roll)
        {
            if (!_records.Contains(record) || record.Phase != SuccessionChallengePhase.AwaitingResponse
                || record.Realm.RulingClan == Clan.PlayerClan || !ValidParticipants(record)
                || !SuccessionChallengeRules.Finite(roll) || roll < 0 || roll >= 1) return false;
            RefreshMissingConcession(record);
            // Never silently replace the package already presented to the ruler.
            if (record.Estate != null && !CanDeliver(record)) return false;
            if (!RefreshResponseBacking(record)) return false;
            Hero ruler = record.Sovereign;
            record.AcceptanceChance = UltimatumAcceptanceRules.Calculate(record.BackingPower, record.LoyalistPower,
                Kingdom.All.Any(k => k != record.Realm && k.IsAtWarWith(record.Realm)),
                ruler.GetTraitLevel(DefaultTraits.Calculating), ruler.GetTraitLevel(DefaultTraits.Valor), ruler.GetTraitLevel(DefaultTraits.Mercy));
            record.ResponseRoll = roll;
            record.Phase = roll >= record.AcceptanceChance ? SuccessionChallengePhase.WarRequired
                : record.Estate != null ? SuccessionChallengePhase.LandSettlement : SuccessionChallengePhase.CrownSettlement;
            return true;
        }

        internal bool RecordPlayerResponse(SuccessionChallengeRecord record, SuccessionChallengePhase response)
        {
            if (!_records.Contains(record) || record.Phase != SuccessionChallengePhase.AwaitingResponse
                || record.Realm.RulingClan != Clan.PlayerClan || !ValidParticipants(record)) return false;
            if (response != SuccessionChallengePhase.LandSettlement && response != SuccessionChallengePhase.CrownSettlement
                && response != SuccessionChallengePhase.WarRequired) return false;
            if (response == SuccessionChallengePhase.LandSettlement
                && (record.Demand != SuccessionChallengeDemand.InheritanceFirst || !CanDeliver(record))) return false;
            if (!RefreshResponseBacking(record)) return false;
            record.Phase = response;
            return true;
        }

        private static bool ValidParticipants(SuccessionChallengeRecord record) => record.Realm?.IsEliminated == false
            && record.Sovereign?.IsAlive == true && Sovereign(record.Realm) == record.Sovereign
            && Available(record.Challenger) && record.Challenger.Clan?.Kingdom == record.Realm
            && (record.Challenger.Clan == record.OriginalHouse || record.Challenger.Clan == record.Estate?.Cadet)
            && HereditaryLoyaltyBehavior.Instance?.GetLine(record.Realm).Contains(record.Challenger) == true
            && CrownAccessionBehavior.Instance?.IsPending(record.Realm) != true;

        private CrownAccessionRecord PreviewConcession(SuccessionChallengeRecord record, bool lawfulOnly = false, bool hostile = false)
        {
            Clan source = record.OriginalHouse;
            if (source != record.Realm.RulingClan || source.Leader != record.Sovereign) return null;
            bool advanced = HasInheritanceAdvance(record.Sovereign, record.Challenger)
                || CrownAccessionBehavior.Instance?.HasInheritanceAdvance(record.Sovereign, record.Challenger) == true;
            bool establishment = hostile || !lawfulOnly;
            bool granted = HasHouseholdGrant(record.Sovereign, record.Challenger);
            if (granted && !lawfulOnly) return null;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return null;
            var plan = FeudalInheritancePlanner.BuildPlan(source, record.Sovereign, BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
            if (!plan.IsValid) return null;
            Hero primary = SuccessionLawHelper.GetLegalSuccessionLine(source, record.Sovereign, SuccessionLawHelper.GetLawsForClan(source)).FirstOrDefault();
            var share = advanced ? new FeudalInheritancePackage()
                : FeudalInheritancePlanner.GetLivingAccessionShare(plan, primary, record.Challenger, BellumCivileOptions.EnablePartitionSuccession);
            bool fallback = !lawfulOnly && (share == null || share.Fiefs.Count == 0);
            if (establishment)
                share = granted ? new FeudalInheritancePackage()
                    : FeudalInheritancePlanner.GetHouseholdEstablishmentShare(plan, titles);
            share = share ?? new FeudalInheritancePackage();
            if (share == null) return null;
            if (!lawfulOnly && !SuccessionChallengeRules.CanConcede(source.Fiefs.Count, share.Fiefs.Count)) return null;
            // Group packages may omit their baronies; every granted fief must be legalized too.
            var grantedTitles = share.Titles.ToList();
            foreach (var fief in share.Fiefs)
                if (titles.TryGetBarony(fief.Settlement, out var barony) && barony.IsActive
                    && barony.DeJureHolderClanId == source.StringId && !grantedTitles.Contains(barony))
                    grantedTitles.Add(barony);
            var crown = titles.GetRealmSovereignTitle(record.Realm);
            string political = titles.GetKingdomPoliticalTitle(record.Realm)?.TitleId;
            var estate = new CrownAccessionRecord
            {
                Realm = record.Realm, Predecessor = record.Sovereign, PreviousHouse = source, Heir = record.Challenger,
                HeirHouse = source, IncomingSourceHouse = source, IncomingSourceHead = record.Sovereign,
                IncomingSourceRealm = record.Realm, IncomingHousePrepared = true, HasLivingEndowment = true,
                CreatesCadet = true, ChallengeConcession = true, CadetId = "bc_challenge_" + record.Id,
                PreserveHeirParty = true,
                EndowmentFiefs = share.Fiefs.Select(f => f.Settlement.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                EndowmentTitles = grantedTitles.Where(t => t.TitleId != political && t.TitleId != crown?.TitleId
                    && (crown == null || t.TitleType < crown.TitleType)).Select(t => t.TitleId).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList(),
                Household = new[] { record.Challenger, record.Challenger.Spouse }
                    .Where(h => h?.IsAlive == true && h.Clan == source && h != source.Leader && h != Hero.MainHero)
                    .Distinct().Select(h => h.StringId).ToList(),
                EndowmentGold = establishment || advanced ? 0 : CrownAccessionBehavior.CalculateAbdicationGold(record.Sovereign.Gold,
                    BellumCivileOptions.EnablePartitionSuccession ? Math.Min(plan.SecondaryPackages.Count, plan.NaturalHeirs.Count(h => h != primary)) : 0)
            };
            var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (partition == null) return null;
            estate.CadetName = partition.PreviewAbdicationCadetName(estate);
            if (!lawfulOnly) record.FallbackGrant = fallback;
            return estate;
        }

        private void RefreshMissingConcession(SuccessionChallengeRecord record)
        {
            // Repair only an absent, unaccepted offer; never replace a promised package.
            if (record.Phase == SuccessionChallengePhase.AwaitingResponse
                && record.Demand == SuccessionChallengeDemand.InheritanceFirst && record.Estate == null
                && ValidParticipants(record)) record.Estate = PreviewConcession(record);
        }

        private static bool CanDeliver(SuccessionChallengeRecord record)
        {
            if (!ValidParticipants(record) || record.Estate == null) return false;
            Clan source = record.Estate.EndowmentHouse;
            var remaining = record.Estate.EndowmentFiefs.Select(Settlement.Find).Where(s => s?.OwnerClan == source).ToList();
            return source?.Leader == record.Sovereign && source.Fiefs.Count > remaining.Count
                && record.Estate.Household.Select(CrownAccessionBehavior.ResolveAbdicationHero).All(Available)
                && record.Estate.EndowmentFiefs.Select(Settlement.Find).All(s => s != null && s.SiegeEvent == null && s.Party?.MapEvent == null
                    && (s.OwnerClan == source || s.OwnerClan == record.Estate.Cadet));
        }

        private void SettleConcession(SuccessionChallengeRecord record)
        {
            if (!_settling.Add(record)) return;
            try
            {
                if (!CanDeliver(record)) { record.Failure = "Concession participants or estate unavailable"; return; }
                if (CrownAccessionBehavior.Instance?.SettleCrownCadet(record.Estate) != true) return;
                // These calls publish no campaign events between memory insertion and receipt recording.
                if (!record.RewardRecorded)
                {
                    if (DynamicRelationBehavior.Instance?.RefreshSuccessionConcession(record.Challenger, record.Sovereign) != true) return;
                    record.RewardUntil = Day + CampaignTime.Years(10).ToDays;
                    record.RewardRecorded = true;
                }
                record.Phase = SuccessionChallengePhase.Settled;
                record.Failure = null;
                HereditaryLoyaltyBehavior.Instance?.Invalidate();
                AnnounceConcession(record);
            }
            finally { _settling.Remove(record); }
        }

        internal bool HasInheritanceAdvance(Hero donor, Hero heir) => donor != null && heir != null
            && _records.Any(r => r.Sovereign == donor && r.Challenger == heir && r.HasAdvance);

        internal bool HasHouseholdGrant(Hero donor, Hero heir) => donor != null && heir != null
            && _records.Any(r => r.Sovereign == donor && r.Challenger == heir && r.HasHouseholdGrant);

        internal double LoyaltyBonus(Kingdom realm, Hero heir, Hero ruler) => _records
            .Where(r => (r.Realm == realm || r.OutcomeRealm == realm) && r.Challenger == heir).Select(r => r.LoyaltyAt(ruler, Day)).DefaultIfEmpty(0).Max();
        internal double SubmissionBonus(Kingdom realm, Hero heir, Hero ruler) => _records
            .Where(r => (r.Realm == realm || r.OutcomeRealm == realm) && r.Challenger == heir).Select(r => r.SubmissionAt(ruler, Day)).DefaultIfEmpty(0).Max();
    }
}
