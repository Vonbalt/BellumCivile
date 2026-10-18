using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtRoyalPeaceCase> _royalPeaceCases = new List<CourtRoyalPeaceCase>();
        private Dictionary<string, float> _royalPeaceReviewAfter = new Dictionary<string, float>();
        private static bool IsRoyalPeace(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtRoyalPeaceRules.Kind;
        private static ClaimFeudWarBehavior RoyalWars => Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
        private static RealmPeaceEnforcementBehavior RoyalExecutor => Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
        private static bool PermanentRoyalRealm(Kingdom realm) => ValidRealm(realm) && !realm.IsMinorFaction && !realm.IsBanditFaction;
        internal bool HasPendingRoyalPeace(Kingdom realm) => _royalPeaceCases.Any(c => c.Realm == realm && !c.Closed);

        private void OnRoyalPeaceInvasion(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            if (first is Kingdom attacker) ReviewWartimeRoyalPeace(attacker);
            if (second is Kingdom defender) ReviewWartimeRoyalPeace(defender);
        }

        private static bool RoyalCaseValid(CourtRoyalPeaceCase entry) => entry.RealmWide && PermanentRoyalRealm(entry.Realm)
            && entry.Realm.RulingClan?.Leader == entry.Ruler && entry.Ruler?.IsAlive == true
            && RealmPeaceEnforcementBehavior.KingdomHasExternalWar(entry.Realm) == entry.Wartime;

        private void OnRoyalPeaceEnded(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            // Mode changes are checked against all remaining foreign wars during maintenance.
        }

        internal double RoyalPeaceInclination(Kingdom realm, RoyalPeacePreview preview)
        {
            if (preview?.IsEnabled != true || !PermanentRoyalRealm(realm)) return 0;
            var wars = RoyalWars?.GetActiveWars().Where(w => w.ParentKingdomId == realm.StringId).ToList()
                ?? new List<ClaimFeudWarRecord>();
            var shells = new HashSet<string>(wars.SelectMany(w => new[] { w.ClaimantKingdomId, w.HolderKingdomId }));
            double diverted = Kingdom.All.Where(k => k != null && !k.IsEliminated && shells.Contains(k.StringId))
                .Sum(k => Math.Max(0, (double)k.CurrentTotalStrength));
            double enemy = Kingdom.All.Where(k => k != realm && PermanentRoyalRealm(k) && realm.IsAtWarWith(k))
                .Sum(k => Math.Max(0, (double)k.CurrentTotalStrength));
            double oldest = wars.Count == 0 ? 0 : Math.Max(0, CampaignTime.Now.ToDays - wars.Min(w => w.StartedDay));
            var ruler = realm.RulingClan.Leader;
            return CourtRoyalPeaceRules.RealmChance(preview.Wartime, preview.FeudCount, preview.WarCount, oldest,
                realm.CurrentTotalStrength, enemy, diverted, ruler.GetTraitLevel(DefaultTraits.Mercy),
                ruler.GetTraitLevel(DefaultTraits.Calculating), ruler.GetTraitLevel(DefaultTraits.Valor));
        }

        private void ReviewWartimeRoyalPeace(Kingdom realm)
        {
            if (!PermanentRoyalRealm(realm) || realm.RulingClan == Clan.PlayerClan || HasPendingRoyalPeace(realm)
                || !RealmPeaceEnforcementBehavior.KingdomHasExternalWar(realm) || RoyalExecutor?.HasRoyalPeace(realm) == true) return;
            float now = (float)CampaignTime.Now.ToDays;
            if (_royalPeaceReviewAfter.TryGetValue(realm.StringId, out float next) && next > now) return;
            _royalPeaceReviewAfter[realm.StringId] = now + CourtRoyalPeaceRules.ReviewDays;
            var preview = RoyalExecutor?.GetRealmPreview(realm, realm.RulingClan);
            double chance = RoyalPeaceInclination(realm, preview);
            double roll = MBRandom.RandomFloat * 100;
            BellumCivileLogger.Log($"Realm royal peace assessment; realm={realm.StringId}; chance={chance:0.0}; roll={roll:0.0}.");
            if (chance <= 0 || roll >= chance) return;
            var entry = NewRoyalPeaceCase(realm, preview);
            _royalPeaceCases.Add(entry);
            TryAssignRoyalPeace(entry, GetAgenda(realm, null));
        }

        private CourtRoyalPeaceCase NewRoyalPeaceCase(Kingdom realm, RoyalPeacePreview preview) => new CourtRoyalPeaceCase {
            Id = Guid.NewGuid().ToString("N"), Realm = realm, Ruler = realm.RulingClan.Leader,
            Created = CampaignTime.Now.ToDays, Expires = CampaignTime.Now.ToDays + Math.Max(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays + 1),
            Assessed = true, RealmWide = true, Wartime = preview.Wartime, QuotedCost = preview.InfluenceCost,
            PlayerInitiated = realm.RulingClan == Clan.PlayerClan
        };

        private void MaintainRoyalPeaceCases()
        {
            foreach (var entry in _royalPeaceCases.Where(c => !c.Closed).ToList())
            {
                if (entry.Started) { CloseRoyalPeace(entry, "interrupted_execution"); continue; }
                // Legacy single-feud agendas cannot silently acquire realm-wide scope or a new price.
                if (!entry.RealmWide) { CloseRoyalPeace(entry, "legacy_decree"); continue; }
                if (!RoyalCaseValid(entry)) { CloseRoyalPeace(entry, "war_or_crown_changed"); continue; }
                if (entry.Agenda != null && (!entry.Agenda.IsUnopened || !IsRoyalPeace(entry.Agenda)))
                { CloseRoyalPeace(entry, "agenda_replaced_or_cancelled"); continue; }
                if (entry.Agenda == null && CampaignTime.Now.ToDays >= entry.Expires)
                { CloseRoyalPeace(entry, "opportunity_expired"); continue; }
                if (entry.Agenda == null) TryAssignRoyalPeace(entry, GetAgenda(entry.Realm, null));
            }
            foreach (var realm in Kingdom.All.Where(PermanentRoyalRealm).ToList()) ReviewWartimeRoyalPeace(realm);
            _royalPeaceCases.RemoveAll(c => c.Closed && CampaignTime.Now.ToDays > c.Expires + CampaignTime.DaysInYear);
        }

        private bool AssignPriorityRoyalPeace(CourtAgendaRecord agenda)
        {
            var entry = _royalPeaceCases.Where(c => c.Realm == agenda.Realm && !c.Closed && c.Assessed && c.Agenda == null)
                .OrderBy(c => c.Created).FirstOrDefault();
            return entry != null && TryAssignRoyalPeace(entry, agenda);
        }

        public bool TryScheduleRoyalPeace(Kingdom realm, Clan ruler, out string report)
        {
            report = null;
            var preview = RoyalExecutor?.GetRealmPreview(realm, ruler);
            if (preview?.IsEnabled != true || HasPendingRoyalPeace(realm))
            {
                report = HasPendingRoyalPeace(realm) ? new TextObject("{=BC_RoyalPeaceAlreadyAnnounced}A Royal Peace decree has already been announced.").ToString()
                    : preview?.Hint.ToString();
                return false;
            }
            var entry = NewRoyalPeaceCase(realm, preview);
            var agenda = ruler == Clan.PlayerClan ? NewPlayerCrownBusiness(realm) : GetAgenda(realm, null);
            if (agenda == null) return false;
            if (ruler == Clan.PlayerClan) agenda.State = CourtAgendaState.NotProposed;
            _royalPeaceCases.Add(entry);
            if (!TryAssignRoyalPeace(entry, agenda))
            {
                _royalPeaceCases.Remove(entry);
                report = new TextObject("{=BC_RoyalPeaceAgendaBusy}The Crown cannot announce this decree while its other proceedings are committed.").ToString();
                return false;
            }
            if (!_agendas.Contains(agenda)) _agendas.Add(agenda);
            return true;
        }

        internal double PeacetimeRoyalPeaceWeight(Kingdom realm)
        {
            if (!PermanentRoyalRealm(realm) || realm.RulingClan == Clan.PlayerClan || HasPendingRoyalPeace(realm)) return 0;
            var preview = RoyalExecutor?.GetRealmPreview(realm, realm.RulingClan);
            return preview?.Wartime == false ? RoyalPeaceInclination(realm, preview) / 100d : 0;
        }

        internal void SelectPeacetimeRoyalPeace(CourtAgendaRecord agenda)
        {
            var preview = RoyalExecutor?.GetRealmPreview(agenda.Realm, agenda.Sponsor);
            if (preview?.IsEnabled != true || preview.Wartime || HasPendingRoyalPeace(agenda.Realm)) return;
            var entry = NewRoyalPeaceCase(agenda.Realm, preview);
            _royalPeaceCases.Add(entry);
            if (!TryAssignRoyalPeace(entry, agenda)) _royalPeaceCases.Remove(entry);
        }

        private bool TryAssignRoyalPeace(CourtRoyalPeaceCase entry, CourtAgendaRecord agenda)
        {
            if (entry.Closed || entry.Started || !entry.Assessed || entry.Agenda != null
                || !RoyalCaseValid(entry) || agenda == null || agenda.Faction != null || agenda.ResultApplied || agenda.IsFiled || agenda.IsOngoingObjective
                || agenda.PaymentSettled || agenda.PaidInfluence > 0 || agenda.SubstitutionInfluencePaid > 0
                || IsRoyalPeace(agenda) || agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree
                || agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis
                || !(agenda.IsUnopened && agenda.SessionDate.IsFuture || agenda.State == CourtAgendaState.NotProposed)
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(entry.Realm) != null
                || CrownAccessionBehavior.Instance?.IsPending(entry.Realm) == true
                || _decreeCases.Any(c => c.Realm == entry.Realm && !c.Closed && !c.Assigned)
                || _agendas.Any(a => a.Realm == entry.Realm && a.Sponsor == entry.Realm.RulingClan && a.IsFiled)) return false;
            var preview = RoyalExecutor?.GetRealmPreview(entry.Realm, entry.Realm.RulingClan);
            if (preview?.IsEnabled != true || !CourtRoyalPeaceRules.QuoteStillValid(entry.Wartime, entry.QuotedCost, preview.Wartime, preview.InfluenceCost)) return false;
            double now = CampaignTime.Now.ToDays;
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = CourtRoyalPeaceRules.Kind, TargetId = entry.Realm.StringId, ActionId = entry.Id };
            agenda.SessionDate = CampaignTime.Days((float)CourtRoyalPeaceRules.ExecutionDay(now, BellumCivileOptions.PoliticalDeliberationDays));
            agenda.ObjectiveData.FreezeTerm(now, agenda.SessionDate.ToDays);
            agenda.PolicyId = null;
            agenda.Sponsor = entry.Realm.RulingClan;
            agenda.State = CourtAgendaState.Announced;
            agenda.PlayerSelectionConfirmed = true;
            entry.Agenda = agenda;
            entry.Announced = true;
            RoyalPeaceNotice(entry, "royal_peace_warning", "warning");
            return true;
        }

        private void AdvanceRoyalPeace(CourtAgendaRecord agenda)
        {
            var entry = _royalPeaceCases.FirstOrDefault(c => c.Agenda == agenda && !c.Closed);
            if (entry == null) { if (!agenda.ResultApplied) Cancel(agenda, "royal_peace_case_missing"); return; }
            if (!RoyalCaseValid(entry) || ElectiveSuccessionBehavior.Instance?.PendingDeposition(entry.Realm) != null
                || CrownAccessionBehavior.Instance?.IsPending(entry.Realm) == true)
            { CloseRoyalPeace(entry, "war_or_crown_changed"); return; }
            if (!agenda.SessionDate.IsPast) return;
            var preview = RoyalExecutor?.GetRealmPreview(entry.Realm, entry.Realm.RulingClan);
            if (preview?.IsEnabled != true
                || !CourtRoyalPeaceRules.QuoteStillValid(entry.Wartime, entry.QuotedCost, preview.Wartime, preview.InfluenceCost))
            { CloseRoyalPeace(entry, "conditions_changed"); return; }
            if (!preview.SettlementReady)
            {
                if (CampaignTime.Now.ToDays > agenda.SessionDate.ToDays + CourtRoyalPeaceRules.ReviewDays) CloseRoyalPeace(entry, "unsafe_settlement");
                return;
            }
            if (!entry.TryBegin()) return;
            string report = null;
            try
            {
                bool success = RoyalExecutor.TryEnforceRealmPeace(entry.Realm, entry.Realm.RulingClan, out report);
                entry.Closed = true;
                entry.Result = success ? "enforced" : "execution_failed";
                FinishExecutive(agenda, success ? CourtAgendaState.Decreed : CourtAgendaState.Cancelled, entry.Result);
                if (!success) RoyalPeaceNotice(entry, "royal_peace_cancelled", "cancelled");
            }
            catch (Exception ex)
            {
                entry.Closed = true;
                entry.Result = "execution_exception";
                Cancel(agenda, entry.Result);
                BellumCivileLogger.Log("Royal peace execution interrupted; will not replay: " + ex);
            }
            BellumCivileLogger.Log($"Royal peace concluded; case={entry.Id}; result={entry.Result}; report={report}.");
        }

        private void CloseRoyalPeace(CourtRoyalPeaceCase entry, string reason)
        {
            if (entry.Closed) return;
            entry.Closed = true;
            entry.Result = reason;
            if (entry.Agenda != null && IsRoyalPeace(entry.Agenda) && !entry.Agenda.ResultApplied) Cancel(entry.Agenda, "royal_peace_" + reason);
            if (entry.Announced) RoyalPeaceNotice(entry, "royal_peace_cancelled", "cancelled");
            BellumCivileLogger.Log($"Royal peace closed; case={entry.Id}; reason={reason}.");
        }

        private static TextObject RoyalPeaceLabel(CourtAgendaRecord agenda) => new TextObject("{=BC_RealmPeaceAgenda}Enforce peace throughout {REALM}")
            .SetTextVariable("REALM", agenda.Realm.Name);

        private void RoyalPeaceNotice(CourtRoyalPeaceCase entry, string kind, string suffix)
        {
            var behavior = ConflictOutcomeBehavior.Current;
            var notice = behavior?.Begin("royal_peace:" + entry.Id + ":" + suffix, entry.Realm, null);
            if (notice == null) return;
            var preview = RoyalExecutor?.GetRealmPreview(entry.Realm, entry.Realm.RulingClan);
            if (Clan.PlayerClan != null && preview?.InvolvedClans.Contains(Clan.PlayerClan) == true)
            { notice.PlayerInvolved = true; notice.ChatAllowed = true; }
            ConflictOutcomeBehavior.Capture(notice, "RULER", entry.Ruler?.Name);
            ConflictOutcomeBehavior.Capture(notice, "REALM", entry.Realm?.Name);
            ConflictOutcomeBehavior.Capture(notice, "DATE", new TextObject("{=!}" + entry.Agenda?.SessionDate.ToString()));
            ConflictOutcomeBehavior.Capture(notice, "REASON", new TextObject("{=BC_RealmPeaceCancelledReason}The Crown, conflicts, price or wartime conditions of the announced decree have changed, or settlement could not safely proceed. No payment is taken for this cancelled decree."));
            behavior.Publish(notice, kind);
        }
    }
}
