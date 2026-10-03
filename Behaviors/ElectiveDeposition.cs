using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveSuccessionBehavior
    {
        internal ElectiveSuccessionRecord PendingDeposition(Kingdom realm) =>
            Get(realm)?.DepositionPending == true ? Get(realm) : null;

        internal bool BeginDeposition(Kingdom realm, Clan caretaker, Hero deposed)
        {
            if (!UsesElection(realm) || caretaker == null || deposed == null
                || !NobleClanEligibilityHelper.IsValidRulingClan(caretaker, realm)
                || LegalHead(caretaker)?.IsAlive != true || LegalHead(caretaker) == deposed) return false;
            var record = Get(realm);
            if (record?.DepositionPending == true)
                return record.DeposedRuler == deposed && record.InterimHouse == caretaker;
            if (record?.Frozen == true || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()) return false;
            if (record == null)
            {
                record = new ElectiveSuccessionRecord { Realm = realm };
                _elections.Add(record);
            }
            // Journal before the ruler/title callbacks. Maintenance can resume a partial installation.
            record.ScheduleDeposition(deposed, caretaker,
                CampaignTime.Never);
            Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?
                .ClearSuccessionStateForKingdom(realm, "elective deposition deliberation owns succession");
            TryPrepareInterim(record);
            BellumCivileLogger.Log($"Elective deposition scheduled; realm={realm.StringId}; deposed={deposed.StringId}; caretaker={caretaker.StringId}; election_day={record.DepositionElectionDate.ToDays}.");
            return true;
        }

        private bool PrepareInterim(ElectiveSuccessionRecord record)
        {
            var realm = record.Realm;
            var caretaker = record.InterimHouse;
            bool valid = NobleClanEligibilityHelper.IsValidRulingClan(caretaker, realm)
                && LegalHead(caretaker)?.IsAlive == true && LegalHead(caretaker) != record.DeposedRuler;
            if (record.InterimPrepared && valid) return true;
            record.InterimPrepared = false;
            if (!valid)
            {
                caretaker = realm.Clans.Where(c => NobleClanEligibilityHelper.IsValidRulingClan(c, realm)
                    && LegalHead(c)?.IsAlive == true && LegalHead(c) != record.DeposedRuler)
                    .OrderByDescending(RebellionPowerHelper.CalculateClanPower).ThenBy(c => c.StringId, StringComparer.Ordinal).FirstOrDefault();
                if (caretaker == null) return false;
                record.InterimHouse = caretaker;
            }
            if (realm.RulingClan != caretaker) ChangeRulingClanAction.Apply(realm, caretaker);
            // Custody is not a lawful electoral victory and grants no new mandate or dynasty recognition.
            if (Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.TrySetKingdomTitleRuler(
                realm, caretaker, legalTransfer: false, reason: "caretaker pending deposition election") != true) return false;
            CourtAgendaBehavior.Current?.ReplaceCrownAgendaForElection(realm);
            record.InterimPrepared = true;
            Maintain(realm);
            return true;
        }

        private bool TryPrepareInterim(ElectiveSuccessionRecord record)
        {
            try { return PrepareInterim(record); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Elective caretaker installation deferred; realm={record.Realm.StringId}; error={ex}");
                return false;
            }
        }

        private bool AdvanceDeposition(Kingdom realm)
        {
            var record = PendingDeposition(realm);
            if (record == null) return false;
            if (!UsesElection(realm))
            {
                record.DepositionPending = false;
                record.Excluded = null;
                return false;
            }
            // Death accessions and the final ballot own their transfers once started.
            if (CrownAccessionBehavior.Instance?.IsPending(realm) == true) return true;
            if (realm.RulingClan != record.InterimHouse
                && (record.InterimPrepared || realm.RulingClan != record.DeposedRuler?.Clan))
            {
                record.DepositionPending = false;
                record.Excluded = null;
                BellumCivileLogger.Log($"Elective deposition superseded by another Crown transfer; realm={realm.StringId}.");
                return false;
            }
            if (!TryPrepareInterim(record)) return true;
            var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolution == null) return true;
            // Also adopts deliberations saved before the caretaker-tribunal sequence was introduced.
            if (resolution.HasPendingPostWarJudgments(realm))
            {
                record.DepositionAwaitingJudgments = true;
                record.DepositionElectionDate = CampaignTime.Never;
                return true;
            }
            if (record.DepositionAwaitingJudgments)
            {
                // Reconsider natural preferences after verdicts and executions; Refresh preserves live promises and player support.
                Maintain(realm, renewal: true);
                record.DepositionElectionDate = CampaignTime.Now + CampaignTime.Days(Math.Max(1, BellumCivileOptions.PoliticalDeliberationDays));
                record.DepositionAnnounced = false;
                record.DepositionAwaitingJudgments = false;
                BellumCivileLogger.Log($"Elective deposition judgments complete; realm={realm.StringId}; preferences_refreshed=true; election_day={record.DepositionElectionDate.ToDays}.");
            }
            Maintain(realm);
            if (!record.DepositionAnnounced)
            {
                var notices = ConflictOutcomeBehavior.Current;
                if (notices == null) return true;
                var notice = notices.Begin("deposition-election:" + realm.StringId + ":" + record.DeposedRuler.StringId
                    + ":" + record.DepositionElectionDate.ToDays.ToString("R", System.Globalization.CultureInfo.InvariantCulture), realm, null);
                ConflictOutcomeBehavior.Capture(notice, "DEPOSED", record.DeposedRuler.Name);
                ConflictOutcomeBehavior.Capture(notice, "CARETAKER", LegalHead(record.InterimHouse).Name);
                ConflictOutcomeBehavior.Capture(notice, "REALM", realm.Name);
                ConflictOutcomeBehavior.Capture(notice, "DATE", new TextObject(record.DepositionElectionDate.ToString()));
                notices.Publish(notice, "election_called");
                record.DepositionAnnounced = true;
            }
            if (record.DepositionElectionDate.IsPast) CrownAccessionBehavior.Instance?.BeginMandateElection(realm);
            return true;
        }

        internal static TextObject DepositionDateText(ElectiveSuccessionRecord record)
        {
            if (record.DepositionAwaitingJudgments)
                return new TextObject("{=BC_Deposition_JudgmentsPending}Election to follow the post-war judgments");
            var text = new TextObject("{=BC_Election_NextDate}Next election: {DATE} (~{DAYS} days)");
            text.SetTextVariable("DATE", record.DepositionElectionDate.ToString());
            text.SetTextVariable("DAYS", (int)Math.Max(0, Math.Ceiling(record.DepositionElectionDate.ToDays - CampaignTime.Now.ToDays)));
            return text;
        }

        internal string DepositionAgenda(Kingdom realm)
        {
            var record = PendingDeposition(realm);
            if (record == null) return null;
            if (record.DepositionAwaitingJudgments)
                return new TextObject("{=BC_Deposition_TribunalAgenda}Current agenda: Judge the defeated houses").ToString();
            var text = new TextObject("{=BC_Deposition_Agenda}Current agenda: Elect the sovereign ({DATE})");
            text.SetTextVariable("DATE", record.DepositionElectionDate.ToString());
            return text.ToString();
        }
    }
}
