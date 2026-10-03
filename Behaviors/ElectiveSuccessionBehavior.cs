using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    // Court maintenance owns reconsideration. This behavior only adds the player inquiry pump.
    public sealed partial class ElectiveSuccessionBehavior : CampaignBehaviorBase
    {
        private List<ElectiveSuccessionRecord> _elections = new List<ElectiveSuccessionRecord>();
        private ElectiveSuccessionRecord _inquiry;
        private bool _scoring;
        private bool _startingSession;
        internal ElectiveAcceptanceCache Acceptance { get; } = new ElectiveAcceptanceCache();
        public static ElectiveSuccessionBehavior Instance => Campaign.Current?.GetCampaignBehavior<ElectiveSuccessionBehavior>();
        public static Hero LegalHead(Clan clan) => RegencyBehavior.Instance?.GetLegalClanHead(clan) ?? clan?.Leader;
        public static bool UsesElection(Kingdom realm) => realm != null && !realm.IsEliminated
            && SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) == realm
            && SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw) == RealmSuccessionSystem.Elective;
        private static bool Voter(Clan clan, Kingdom realm) => clan != null && clan.Kingdom == realm
            && !clan.IsEliminated && !clan.IsUnderMercenaryService && !clan.IsBanditFaction
            && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan) && LegalHead(clan)?.IsAlive == true;
        private static bool Candidate(Hero hero, Kingdom realm, GenderSuccessionLaw gender, Hero excluded) =>
            hero != null && hero != excluded && Voter(hero.Clan, realm) && LegalHead(hero.Clan) == hero
            && hero.DeathMark == TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.None
            && !hero.IsDisabled && SuccessionLawHelper.IsEligibleUnderGenderLaw(hero, gender);

        public override void RegisterEvents()
        {
            CampaignEvents.HeroRelationChanged.AddNonSerializedListener(this, (a, b, delta, show, detail, source, target) => Acceptance.Clear());
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (hero, killer, detail, show) => Acceptance.Clear());
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, (hero, oldClan) => Acceptance.Clear());
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, (clan, oldRealm, newRealm, detail, show) => Acceptance.Clear());
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, (settlement, open, owner, old, capturer, detail) => Acceptance.Clear());
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => ProcessInquiry());
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ =>
            {
                _startingSession = true;
                try { foreach (var realm in Kingdom.All.Where(UsesElection).ToList()) Maintain(realm); }
                finally { _startingSession = false; }
            });
        }
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_StandingElections", ref _elections);
            _elections = _elections ?? new List<ElectiveSuccessionRecord>();
            Acceptance.Clear();
        }

        public ElectiveSuccessionRecord Get(Kingdom realm) => _elections.FirstOrDefault(e => e.Realm == realm);

        public void Maintain(Kingdom realm, bool renewal = false)
        {
            var record = Get(realm);
            if (!UsesElection(realm))
            {
                if (record != null && !record.Frozen) { _elections.Remove(record); InvalidateRelations(); }
                return;
            }
            if (record?.Frozen == true) return;
            if (record != null && CrownAccessionBehavior.Instance?.IsPending(realm) == true) return;
            if (record != null && !record.MandateInitialized && !record.DepositionPending)
                StartMandate(record, _startingSession ? Campaign.Current.Models.CampaignTimeModel.CampaignStartTime : CampaignTime.Now);
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            var voters = realm.Clans.Where(c => Voter(c, realm)).ToList();
            var voterSet = new HashSet<Clan>(voters);
            var candidates = new HashSet<Hero>(voters.Select(LegalHead).Where(h => Candidate(h, realm, laws.GenderLaw, record?.Excluded)));
            var previousCandidates = record?.Votes.FirstOrDefault()?.Preferences.Select(p => p.Candidate) ?? Enumerable.Empty<Hero>();
            bool changed = record == null || record.Completed || record.Law != laws.SuccessionLaw || record.Gender != laws.GenderLaw
                || record.Sovereign != LegalHead(realm.RulingClan) || record.Votes.Count != voters.Count
                || record.Votes.Any(v => !voterSet.Contains(v.Clan) || LegalHead(v.Clan) != v.Speaker
                    || v.Source != "natural" && v.Source != "player" && v.Until.ToDays <= CampaignTime.Now.ToDays)
                || !candidates.SetEquals(previousCandidates);
            if (!renewal && !changed)
            {
                // Power can change without a new political preference or court term.
                if (ElectiveSuccessionRules.RefreshWeights(record, clan => RebellionPowerHelper.CalculateClanPower(clan)))
                    InvalidateRelations();
                return;
            }
            if (record == null) { record = new ElectiveSuccessionRecord { Realm = realm }; _elections.Add(record); }
            if (!record.DepositionPending)
            {
                if (!record.MandateInitialized) StartMandate(record, _startingSession ? Campaign.Current.Models.CampaignTimeModel.CampaignStartTime : CampaignTime.Now);
                else if (record.MandateSovereign != LegalHead(realm.RulingClan)) StartMandate(record, CampaignTime.Now);
            }
            if (record.Completed) record.PlayerConfirmed = false;
            record.Sovereign = LegalHead(realm.RulingClan);
            Refresh(record, laws, voters, renewal || record.Law != laws.SuccessionLaw || record.Gender != laws.GenderLaw);
        }

        private static void StartMandate(ElectiveSuccessionRecord record, CampaignTime start)
        {
            var store = Campaign.Current.GetCampaignBehavior<RealmLawBehavior>();
            int years = RealmLawRegistry.Instance.Find(store.GetActiveLawId(record.Realm, RealmLawRegistry.TermGroup)).MandateYears.Value;
            record.StartMandate(LegalHead(record.Realm.RulingClan), years, start);
        }

        internal void ApplyMandateReform(Kingdom realm, int years)
        {
            if (!UsesElection(realm) || CrownAccessionBehavior.Instance?.IsPending(realm) == true) return;
            // Settle a completed ballot or changed sovereign before touching its mandate.
            Maintain(realm);
            var record = Get(realm);
            if (record == null || record.MandateSovereign != LegalHead(realm.RulingClan))
            {
                Maintain(realm);
                return;
            }
            if (!record.ReformMandate(years, CampaignTime.Now, BellumCivileOptions.PoliticalDeliberationDays)) return;
            BellumCivileLogger.Log($"Elective mandate reformed; realm={realm.StringId}; years={years}; start={record.MandateStart.ToDays}; election={record.ElectionDate.ToDays}; deliberating={record.ReformElectionPending}.");
            if (record.ReformElectionPending) Maintain(realm, renewal: true);
            if (Clan.PlayerClan?.Kingdom != realm) return;
            var text = new TextObject(record.ReformElectionPending
                ? "{=BC_MandateReformOverdue}The new law brings {RULER}'s mandate to an end. The nobles of {REALM} will deliberate until {DATE}, when they shall choose their sovereign. The present ruler remains in office until the succession is settled."
                : years == 0
                    ? "{=BC_MandateReformLifetime}Under the new law, {RULER} now holds a lifetime mandate over {REALM}."
                    : "{=BC_MandateReformDated}The new law has altered {RULER}'s mandate over {REALM}. The next election is now appointed for {DATE}.");
            text.SetTextVariable("RULER", record.MandateSovereign.Name);
            text.SetTextVariable("REALM", realm.Name);
            text.SetTextVariable("DATE", record.ElectionDate.ToString());
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Inheritance);
            if (record.ReformElectionPending) record.ElectionWarningSent = true;
        }

        // Invoked only by the existing court coordinator, never by panel refresh.
        public void CheckMandate(Kingdom realm)
        {
            if (AdvanceDeposition(realm)) return;
            Maintain(realm);
            var record = Get(realm);
            if (!UsesElection(realm) || record == null || !record.MandateInitialized || record.MandateYears == 0
                || record.Frozen || record.Completed || CrownAccessionBehavior.Instance?.IsPending(realm) == true) return;
            double days = record.ElectionDate.ToDays - CampaignTime.Now.ToDays;
            if (!record.ElectionWarningSent && days > 0 && days <= 7 && Clan.PlayerClan?.Kingdom == realm)
            {
                record.ElectionWarningSent = true;
                var text = new TextObject("{=BC_Election_Approaching}The mandate of {RULER} draws to a close. The nobles of {REALM} will choose their sovereign on {DATE}.");
                text.SetTextVariable("RULER", record.MandateSovereign.Name);
                text.SetTextVariable("REALM", realm.Name);
                text.SetTextVariable("DATE", record.ElectionDate.ToString());
                BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Inheritance);
            }
            if (days <= 0) CrownAccessionBehavior.Instance?.BeginMandateElection(realm);
        }

        public string MandateLabel(Kingdom realm)
        {
            var record = Get(realm);
            if (record?.DepositionPending == true) return DepositionDateText(record).ToString();
            if (record?.MandateInitialized != true) return string.Empty;
            if (record.MandateYears == 0) return new TextObject("{=BC_Term_Lifetime}Lifetime mandate").ToString();
            var text = new TextObject("{=BC_Election_NextDate}Next election: {DATE} (~{DAYS} days)");
            text.SetTextVariable("DATE", record.ElectionDate.ToString());
            text.SetTextVariable("DAYS", (int)Math.Max(0, Math.Ceiling(record.ElectionDate.ToDays - CampaignTime.Now.ToDays)));
            return text.ToString();
        }

        public void BeginSuccessorMandate(Kingdom realm)
        {
            var record = Get(realm);
            if (record != null) StartMandate(record, CampaignTime.Now);
        }

        private void Refresh(ElectiveSuccessionRecord record, SuccessionLawSet laws, List<Clan> voters, bool reconsiderAll = false)
        {
            var previous = record.Completed ? new List<ElectiveCommitment>() : record.Votes;
            var candidates = voters.Select(LegalHead).Where(h => Candidate(h, record.Realm, laws.GenderLaw, record.Excluded)).ToList();
            var votes = new List<ElectiveCommitment>();
            _scoring = true;
            InvalidateRelations();
            try
            {
                var profile = KingSelectionAIPatch.BuildStandingProfile(record.Realm, candidates.Select(h => h.Clan));
                var kinship = new RealmSuccessionKinship();
                Hero sovereign = record.Sovereign ?? LegalHead(record.Realm.RulingClan);
                var merits = candidates.ToDictionary(h => h, h => RealmSuccessionMerit.Rank(h, laws, kinship.Get(h, sovereign)));
                foreach (var clan in voters)
                {
                    Hero speaker = LegalHead(clan);
                    var old = previous.FirstOrDefault(v => v.Clan == clan && v.Speaker == speaker);
                    bool reevaluate = reconsiderAll || old == null
                        || !record.Frozen && old.Source != "natural" && old.Source != "player" && old.Until.ToDays <= CampaignTime.Now.ToDays;
                    var vote = new ElectiveCommitment { Clan = clan, Speaker = speaker,
                        Weight = record.Frozen ? previous.FirstOrDefault(v => v.Clan == clan)?.Weight ?? 0
                            : Math.Max(0, RebellionPowerHelper.CalculateClanPower(clan)),
                        Source = clan == Clan.PlayerClan ? "player" : "natural" };
                    foreach (Hero candidate in candidates)
                    {
                        var retained = !reevaluate ? old?.Preferences.FirstOrDefault(p => p.Candidate == candidate) : null;
                        if (retained != null) { vote.Preferences.Add(retained); continue; }
                        // Reuse political standing, relations, alliances, ideology and traits without the native self-vote shortcut.
                        double politics = (KingSelectionAIPatch.CalculateSupportScore(clan, candidate.Clan, profile, true) + 100) / 2.5;
                        vote.Preferences.Add(new ElectivePreference { Candidate = candidate, Law = merits[candidate], Politics = politics,
                            Score = ElectiveSuccessionRules.Preference(merits[candidate], politics, candidate.Age < SuccessionLawHelper.GetAgeOfMajority()) });
                    }
                    // A rebuilt frozen ballot retains commitments valid when voting began,
                    // but never carries an ineligible nominee into the replacement ballot.
                    bool promise = ElectiveSuccessionRules.RetainPromise(old, candidates, record.Frozen, CampaignTime.Now.ToDays);
                    if (promise || clan == Clan.PlayerClan && old?.Source == "player")
                    {
                        vote.Source = old.Source; vote.Until = old.Until;
                        vote.Nominee = candidates.Contains(old.Nominee) ? old.Nominee : null;
                    }
                    else if (vote.Source == "natural") vote.Nominee = !reevaluate && old?.Source == "natural"
                        && candidates.Contains(old.Nominee) ? old.Nominee : Best(vote)?.Candidate;
                    votes.Add(vote);
                }
            }
            finally { _scoring = false; InvalidateRelations(); }
            record.Law = laws.SuccessionLaw; record.Gender = laws.GenderLaw;
            record.Votes = votes; record.Completed = false;
            ElectiveSuccessionRules.Tally(record);
            InvalidateRelations();
        }

        private static ElectivePreference Best(ElectiveCommitment vote) => vote.Preferences
            .OrderByDescending(p => p.Score).ThenByDescending(p => p.Law)
            .ThenBy(p => p.Candidate.StringId, StringComparer.Ordinal).FirstOrDefault();

        public bool SetPlayerSupport(Kingdom realm, Hero candidate)
        {
            Maintain(realm);
            var record = Get(realm);
            var vote = record?.Votes.FirstOrDefault(v => v.Clan == Clan.PlayerClan && v.Speaker == LegalHead(Clan.PlayerClan));
            if (record == null || record.Completed || vote == null || record.Frozen && record.PlayerConfirmed
                || candidate != null && !record.Finalists.Contains(candidate)) return false;
            vote.Source = "player"; vote.Nominee = candidate;
            if (record.Frozen) RetallyFrozen(record); else ElectiveSuccessionRules.Tally(record);
            InvalidateRelations();
            return true;
        }

        internal bool CanCommitPromise(Kingdom realm, Hero speaker, Hero candidate, out CampaignTime until)
        {
            until = CourtAgendaBehavior.Current?.NextEvaluation(realm) ?? CampaignTime.Now;
            if (realm == null || speaker?.IsAlive != true || candidate?.IsAlive != true || until.ToDays <= CampaignTime.Now.ToDays) return false;
            Maintain(realm);
            var record = Get(realm);
            var vote = record?.Votes.FirstOrDefault(v => v.Speaker == speaker && v.Clan != Clan.PlayerClan);
            return UsesElection(realm) && record != null && !record.Frozen && !record.Completed
                && CrownAccessionBehavior.Instance?.IsPending(realm) != true && vote != null
                && LegalHead(vote.Clan) == speaker && vote.Clan.Kingdom == realm
                && vote.Preferences.Any(p => p.Candidate == candidate)
                && (vote.Source == "natural" || vote.Source == "player" || vote.Until.ToDays <= CampaignTime.Now.ToDays);
        }

        // Dialogue and barter validate before committing. This method moves no money.
        public bool TryCommitPromise(Kingdom realm, Hero speaker, Hero candidate, bool bribed)
        {
            if (!CanCommitPromise(realm, speaker, candidate, out var until)) return false;
            var record = Get(realm);
            var vote = record.Votes.First(v => v.Speaker == speaker);
            vote.Nominee = candidate; vote.Source = bribed ? "bribed" : "persuaded";
            vote.Until = until;
            ElectiveSuccessionRules.Tally(record);
            InvalidateRelations();
            return true;
        }

        public string Explain(Kingdom realm, Clan voter)
        {
            var vote = Get(realm)?.Votes.FirstOrDefault(v => v.Clan == voter);
            if (vote == null) return new TextObject("{=BC_Election_NoCommitment}No electoral commitment.").ToString();
            var natural = Best(vote);
            var text = new TextObject("{=BC_Election_Explanation}Natural preference: {NATURAL}. Law: {LAW}; politics: {POLITICS}; total: {TOTAL}. Commitment: {COMMITTED} ({SOURCE}).");
            text.SetTextVariable("NATURAL", natural?.Candidate?.Name?.ToString() ?? "-");
            text.SetTextVariable("LAW", (natural?.Law ?? 0).ToString("0.0"));
            text.SetTextVariable("POLITICS", (natural?.Politics ?? 0).ToString("0.0"));
            text.SetTextVariable("TOTAL", (natural?.Score ?? 0).ToString("0.0"));
            bool selfSupport = vote.Source == "natural" && vote.Speaker != null && vote.Supported == vote.Speaker;
            text.SetTextVariable("COMMITTED", (selfSupport ? vote.Speaker : vote.Nominee)?.Name?.ToString() ?? "-");
            text.SetTextVariable("SOURCE", selfSupport ? new TextObject("{=BC_Election_OwnCandidacy}own candidacy").ToString() : vote.Source);
            if (vote.Source != "natural" && vote.Source != "player")
            {
                var expiry = new TextObject("{=BC_Election_PromiseExpiry} Promise expires: {DATE}.");
                expiry.SetTextVariable("DATE", vote.Until.ToString());
                return text.ToString() + expiry;
            }
            return text.ToString();
        }

        public bool SelectSuccessor(CrownAccessionRecord accession)
        {
            Kingdom realm = accession.Realm;
            Maintain(realm);
            var record = Get(realm);
            if (record == null) return false;
            if (!record.Frozen)
            {
                record.Sovereign = accession.Predecessor;
                record.Excluded = accession.ElectiveExcluded ?? (accession.MandateExpiry ? null : accession.Predecessor);
                Refresh(record, new SuccessionLawSet(accession.GenderLaw, accession.HouseLaw), realm.Clans.Where(c => Voter(c, realm)).ToList());
                record.Frozen = true;
            }
            // Death or departure while a player prompt is pending invalidates the snapshot, not the native ownership guard.
            if (record.Votes.Any(v => !Voter(v.Clan, realm) || LegalHead(v.Clan) != v.Speaker)
                || (record.Votes.FirstOrDefault()?.Preferences.Any(p => !Candidate(p.Candidate, realm, record.Gender, record.Excluded)) ?? false))
            {
                record.PlayerConfirmed = false;
                Refresh(record, new SuccessionLawSet(record.Gender, record.Law), record.Votes.Select(v => v.Clan)
                    .Where(c => Voter(c, realm)).ToList());
                record.Frozen = true;
            }
            if (record.Finalists.Count == 0) { accession.Emergency = true; return true; }
            RetallyFrozen(record);
            if (!record.PlayerConfirmed && record.Votes.Any(v => v.Clan == Clan.PlayerClan)) return false;
            accession.Heir = record.Winner;
            ElectiveContestBehavior.Instance?.Capture(accession, record);
            return true;
        }

        private static void RetallyFrozen(ElectiveSuccessionRecord record)
        {
            var finalists = record.Finalists.ToList();
            foreach (var v in record.Votes)
                v.Supported = ElectiveSuccessionRules.ResolveSupport(v, finalists);
            record.Winner = finalists.OrderByDescending(record.Support)
                .ThenByDescending(c => record.Votes.Sum(v => v.Weight * (v.Preferences.FirstOrDefault(p => p.Candidate == c)?.Score ?? 0)))
                .ThenByDescending(c => record.Votes.SelectMany(v => v.Preferences).First(p => p.Candidate == c).Law)
                .ThenBy(c => c.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private void ProcessInquiry()
        {
            if (_inquiry != null || InformationManager.IsAnyInquiryActive()) return;
            var defeat = CrownAccessionBehavior.Instance?.PendingElectoralDefeat();
            if (defeat != null)
            {
                var text = new TextObject("{=BC_Election_PlayerDefeat}The nobles have chosen {RULER} to rule {REALM}. You retain your house, lands and wealth, and remain in the realm as a vassal.");
                text.SetTextVariable("RULER", defeat.Heir.Name);
                text.SetTextVariable("REALM", defeat.Realm.Name);
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_Election_Result}Election Result").ToString(),
                    text.ToString(), true, false, new TextObject("{=BC_Election_Continue}Continue").ToString(), "",
                    () => defeat.DefeatNoticePending = false, null), true);
                return;
            }
            var record = _elections.FirstOrDefault(e => e.Frozen && !e.Completed && !e.PlayerConfirmed && e.Finalists.Count > 0
                && e.Votes.Any(v => v.Clan == Clan.PlayerClan));
            if (record == null || Hero.MainHero?.IsDead == true) return;
            _inquiry = record;
            var current = record.Votes.First(v => v.Clan == Clan.PlayerClan).Supported;
            var summary = new TextObject("{=BC_Election_ConfirmChoice}The nobles assemble to choose their sovereign. Your house currently supports {CANDIDATE}. Confirm your choice, or reconsider before the tally is sealed.");
            summary.SetTextVariable("CANDIDATE", current?.Name ?? new TextObject("{=BC_Election_Abstention}no candidate"));
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_Election_Title}Choose the Sovereign").ToString(),
                summary.ToString() + "\n\n" + string.Join("\n", record.Finalists.Select(h => h.Name + ": "
                    + (record.TotalWeight > 0 ? record.Support(h) * 100 / record.TotalWeight : 0).ToString("0.0") + "%")),
                true, true, new TextObject("{=BC_Election_ConfirmSupport}Confirm support").ToString(),
                new TextObject("{=BC_Election_ChangeSupport}Change support").ToString(),
                () => ConfirmInquiry(record, current), () => ShowFinalSelection(record)), true);
        }

        private void ShowFinalSelection(ElectiveSuccessionRecord record)
        {
            var options = record.Finalists.Select(h => new InquiryElement(h, h.Name + " ("
                + (record.TotalWeight > 0 ? record.Support(h) * 100 / record.TotalWeight : 0).ToString("0.0") + "%)", null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Election_Title}Choose the Sovereign").ToString(),
                new TextObject("{=BC_Election_FinalChoice}The succession is upon us. Whom does your house support?").ToString(),
                options, true, 1, 1, new TextObject("{=BC_Election_Support}Support").ToString(),
                new TextObject("{=BC_Election_Abstain}Abstain").ToString(),
                selected => ConfirmInquiry(record, selected.FirstOrDefault()?.Identifier as Hero),
                _ => ConfirmInquiry(record, null)), true);
        }

        private void ConfirmInquiry(ElectiveSuccessionRecord record, Hero candidate)
        {
            _inquiry = null;
            if (Get(record.Realm) != record || record.Completed) return;
            if (!SetPlayerSupport(record.Realm, candidate)) return;
            record.PlayerConfirmed = true;
        }

        public void Complete(Kingdom realm)
        {
            var record = Get(realm);
            if (record == null) return;
            record.Completed = true; record.Frozen = false; record.Excluded = null;
            record.DepositionPending = false;
            record.DepositionAwaitingJudgments = false;
            InvalidateRelations();
        }

        public int EndorsementOpinion(Hero first, Hero second)
        {
            if (_scoring || first == null || second == null || first == second) return 0;
            Hero candidate = first.Clan == Clan.PlayerClan ? second : second.Clan == Clan.PlayerClan ? first : null;
            if (candidate == null) return 0;
            var record = Get(Clan.PlayerClan?.Kingdom);
            var vote = record?.Votes.FirstOrDefault(v => v.Clan == Clan.PlayerClan && v.Speaker == LegalHead(Clan.PlayerClan));
            if (record == null || record.Completed || vote?.Nominee == null || !record.Finalists.Contains(candidate)
                || !record.Finalists.Contains(vote.Nominee)) return 0;
            return vote.Nominee == candidate ? 10 : -5;
        }

        private static void InvalidateRelations() => Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>()?.InvalidateBaselineCache();
    }
}
