using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtRoyalPeaceCase> _royalPeaceCases = new List<CourtRoyalPeaceCase>();
        private static bool IsRoyalPeace(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtRoyalPeaceRules.Kind;
        private static ClaimFeudRecord RoyalFeud(string id) => Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>()?
            .GetActiveFeuds().FirstOrDefault(f => f.RecordId == id);
        private static ClaimFeudWarBehavior RoyalWars => Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
        private static RealmPeaceEnforcementBehavior RoyalExecutor => Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
        private static bool PermanentRoyalRealm(Kingdom realm) => realm != null && !realm.IsEliminated
            && !realm.IsMinorFaction && !realm.IsBanditFaction && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);

        private void OnRoyalPeaceInvasion(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            if (!(first is Kingdom attacker) || !(second is Kingdom defender)
                || !PermanentRoyalRealm(attacker) || !ValidRealm(defender)
                || defender.RulingClan == Clan.PlayerClan || !attacker.IsAtWarWith(defender)
                || RoyalWars?.GetActiveWars().Any(w => w.ParentKingdomId == defender.StringId) != true) return;
            double warStarted = defender.GetStanceWith(attacker).WarStartDate.ToDays;
            if (_royalPeaceCases.Any(c => c.Realm == defender && c.Attacker == attacker && c.WarStarted == warStarted)) return;
            double now = CampaignTime.Now.ToDays;
            _royalPeaceCases.Add(new CourtRoyalPeaceCase { Id = Guid.NewGuid().ToString("N"), Realm = defender,
                Attacker = attacker, Ruler = defender.RulingClan.Leader, Created = now, WarStarted = warStarted,
                Expires = now + Math.Max(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays + 1) });
            BellumCivileLogger.Log($"Royal peace invasion recorded; realm={defender.StringId}; attacker={attacker.StringId}.");
        }

        private static bool RoyalCaseValid(CourtRoyalPeaceCase entry) => PermanentRoyalRealm(entry.Realm)
            && PermanentRoyalRealm(entry.Attacker) && entry.Realm.RulingClan?.Leader == entry.Ruler
            && entry.Ruler?.IsAlive == true && entry.Realm.RulingClan != Clan.PlayerClan
            && entry.Realm.IsAtWarWith(entry.Attacker)
            && entry.Realm.GetStanceWith(entry.Attacker).WarStartDate.ToDays == entry.WarStarted;

        private void OnRoyalPeaceEnded(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            foreach (var entry in _royalPeaceCases.Where(c => !c.Closed
                && (c.Realm == first && c.Attacker == second || c.Realm == second && c.Attacker == first)).ToList())
                CloseRoyalPeace(entry, "foreign_peace");
        }

        private static double RecoverablePower(ClaimFeudWarRecord war)
        {
            var ids = new[] { war.ClaimantKingdomId, war.HolderKingdomId };
            return Kingdom.All.Where(k => k != null && !k.IsEliminated && ids.Contains(k.StringId))
                .Sum(k => (double)k.CurrentTotalStrength);
        }

        private void MaintainRoyalPeaceCases()
        {
            foreach (var entry in _royalPeaceCases.Where(c => !c.Closed).ToList())
            {
                if (entry.Started) { CloseRoyalPeace(entry, "interrupted_execution"); continue; }
                if (!RoyalCaseValid(entry)) { CloseRoyalPeace(entry, "war_or_crown_changed"); continue; }
                if (entry.Agenda != null && (!entry.Agenda.IsUnopened || !IsRoyalPeace(entry.Agenda)))
                { CloseRoyalPeace(entry, "agenda_replaced_or_cancelled"); continue; }
                if (entry.Agenda == null && CampaignTime.Now.ToDays >= entry.Expires)
                { CloseRoyalPeace(entry, "opportunity_expired"); continue; }
                if (entry.Assessed)
                {
                    if (RoyalFeud(entry.FeudId) == null || RoyalWars?.HasActiveWarForFeud(entry.FeudId) != true)
                    { CloseRoyalPeace(entry, "feud_ended"); continue; }
                    if (!CourtRoyalPeaceRules.Substantial(entry.Realm.CurrentTotalStrength, entry.Attacker.CurrentTotalStrength))
                    { CloseRoyalPeace(entry, "threat_receded"); continue; }
                }
                else
                {
                    if (!CourtRoyalPeaceRules.Substantial(entry.Realm.CurrentTotalStrength, entry.Attacker.CurrentTotalStrength)) continue;
                    var choice = RoyalWars?.GetActiveWars().Where(w => w.ParentKingdomId == entry.Realm.StringId)
                        .Where(w => !_royalPeaceCases.Any(c => c != entry && !c.Closed && c.FeudId == w.FeudRecordId))
                        .Select(w => new { War = w, Feud = RoyalFeud(w.FeudRecordId), Power = RecoverablePower(w) })
                        .Where(x => x.Feud != null && x.Power > 0 && RoyalExecutor?.GetClaimFeudPreview(x.Feud, entry.Realm.RulingClan).IsEnabled == true)
                        .OrderByDescending(x => x.Power).ThenBy(x => x.War.FeudRecordId, StringComparer.Ordinal).FirstOrDefault();
                    if (choice == null) continue;
                    entry.FeudId = choice.Feud.RecordId;
                    entry.TitleId = choice.Feud.TargetTitleId;
                    entry.Claimant = Clan.All.FirstOrDefault(c => c.StringId == choice.Feud.ClaimantClanId);
                    entry.Holder = Clan.All.FirstOrDefault(c => c.StringId == choice.Feud.HolderClanId);
                    entry.Assessed = true;
                    var ruler = entry.Ruler;
                    double chance = CourtRoyalPeaceRules.Chance(entry.Realm.CurrentTotalStrength, entry.Attacker.CurrentTotalStrength,
                        choice.Power, ruler.GetTraitLevel(DefaultTraits.Mercy), ruler.GetTraitLevel(DefaultTraits.Calculating), ruler.GetTraitLevel(DefaultTraits.Valor));
                    double roll = MBRandom.RandomFloat * 100;
                    BellumCivileLogger.Log($"Royal peace assessed; case={entry.Id}; realm={entry.Realm.StringId}; feud={entry.FeudId}; chance={chance:0.0}; roll={roll:0.0}; diverted={choice.Power:0}.");
                    if (roll >= chance) { CloseRoyalPeace(entry, "ruler_declined"); continue; }
                }
                if (entry.Agenda == null) TryAssignRoyalPeace(entry, GetAgenda(entry.Realm, null));
            }
            _royalPeaceCases.RemoveAll(c => c.Closed && CampaignTime.Now.ToDays > c.Expires + CampaignTime.DaysInYear);
        }

        private bool AssignPriorityRoyalPeace(CourtAgendaRecord agenda)
        {
            var entry = _royalPeaceCases.Where(c => c.Realm == agenda.Realm && !c.Closed && c.Assessed && c.Agenda == null)
                .OrderBy(c => c.Created).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
            return entry != null && TryAssignRoyalPeace(entry, agenda);
        }

        private bool TryAssignRoyalPeace(CourtRoyalPeaceCase entry, CourtAgendaRecord agenda)
        {
            if (entry.Closed || entry.Started || !entry.Assessed || entry.Agenda != null
                || !RoyalCaseValid(entry) || agenda == null || agenda.Faction != null || agenda.ResultApplied || agenda.IsFiled || agenda.IsOngoingObjective
                || agenda.PaymentSettled
                || agenda.PaidInfluence > 0 || agenda.SubstitutionInfluencePaid > 0
                || IsRoyalPeace(agenda) || agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree
                || agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis
                || !(agenda.IsUnopened && agenda.SessionDate.IsFuture || agenda.State == CourtAgendaState.NotProposed)
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(entry.Realm) != null
                || CrownAccessionBehavior.Instance?.IsPending(entry.Realm) == true
                || _decreeCases.Any(c => c.Realm == entry.Realm && !c.Closed && !c.Assigned)
                || _agendas.Any(a => a.Realm == entry.Realm && a.Sponsor == entry.Realm.RulingClan && a.IsFiled)) return false;
            var feud = RoyalFeud(entry.FeudId);
            if (feud == null || RoyalExecutor?.GetClaimFeudPreview(feud, entry.Realm.RulingClan).IsEnabled != true) return false;
            double now = CampaignTime.Now.ToDays;
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = CourtRoyalPeaceRules.Kind, TargetId = entry.FeudId, ActionId = entry.Id };
            agenda.SessionDate = CampaignTime.Days((float)CourtRoyalPeaceRules.ExecutionDay(now, BellumCivileOptions.PoliticalDeliberationDays));
            agenda.ObjectiveData.FreezeTerm(now, agenda.SessionDate.ToDays);
            agenda.PolicyId = null;
            agenda.Sponsor = entry.Realm.RulingClan;
            agenda.State = CourtAgendaState.Announced;
            agenda.PlayerSelectionConfirmed = true;
            entry.Agenda = agenda;
            entry.Announced = true;
            RoyalPeaceNotice(entry, "royal_peace_warning", "warning");
            BellumCivileLogger.Log($"Royal peace scheduled; case={entry.Id}; feud={entry.FeudId}; date={agenda.SessionDate.ToDays:0.0}.");
            return true;
        }

        private void AdvanceRoyalPeace(CourtAgendaRecord agenda)
        {
            var entry = _royalPeaceCases.FirstOrDefault(c => c.Agenda == agenda && !c.Closed);
            if (entry == null) { Cancel(agenda, "royal_peace_case_missing"); return; }
            if (!RoyalCaseValid(entry) || ElectiveSuccessionBehavior.Instance?.PendingDeposition(entry.Realm) != null
                || CrownAccessionBehavior.Instance?.IsPending(entry.Realm) == true)
            { CloseRoyalPeace(entry, "war_or_crown_changed"); return; }
            if (!agenda.SessionDate.IsPast) return;
            var feud = RoyalFeud(entry.FeudId);
            if (!CourtRoyalPeaceRules.Substantial(entry.Realm.CurrentTotalStrength, entry.Attacker.CurrentTotalStrength)
                || feud == null || RoyalWars?.HasActiveWarForFeud(entry.FeudId) != true
                || RoyalExecutor?.GetClaimFeudPreview(feud, entry.Realm.RulingClan).IsEnabled != true)
            { CloseRoyalPeace(entry, "conditions_changed"); return; }
            if (!entry.TryBegin()) return;
            string report = null;
            try
            {
                bool success = RoyalExecutor.TryEnforceClaimFeudPeace(feud, entry.Realm.RulingClan, out report);
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
            if (entry.Agenda != null && IsRoyalPeace(entry.Agenda) && !entry.Agenda.ResultApplied)
                Cancel(entry.Agenda, "royal_peace_" + reason);
            if (entry.Announced) RoyalPeaceNotice(entry, "royal_peace_cancelled", "cancelled");
            BellumCivileLogger.Log($"Royal peace closed; case={entry.Id}; reason={reason}.");
        }

        private static TextObject RoyalPeaceLabel(CourtAgendaRecord agenda)
        {
            var text = new TextObject("{=BC_RoyalPeaceAgenda}Enforce peace over {TITLE}");
            var feud = RoyalFeud(agenda.ObjectiveData.TargetId);
            var entry = Current?._royalPeaceCases.FirstOrDefault(c => c.Agenda == agenda);
            var title = FeudalTitleBehavior.Instance?.GetTitle(feud?.TargetTitleId ?? entry?.TitleId);
            text.SetTextVariable("TITLE", title == null ? new TextObject("{=BC_RoyalPeaceDispute}the disputed title") : new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title)));
            return text;
        }

        private void RoyalPeaceNotice(CourtRoyalPeaceCase entry, string kind, string suffix)
        {
            var behavior = ConflictOutcomeBehavior.Current;
            var notice = behavior?.Begin("royal_peace:" + entry.Id + ":" + suffix, entry.Realm, null);
            if (notice == null) return;
            var war = RoyalWars?.GetActiveWars().FirstOrDefault(w => w.FeudRecordId == entry.FeudId);
            if (Clan.PlayerClan != null && war != null
                && (Clan.PlayerClan.Kingdom?.StringId == war.ClaimantKingdomId || Clan.PlayerClan.Kingdom?.StringId == war.HolderKingdomId))
            { notice.PlayerInvolved = true; notice.ChatAllowed = true; }
            ConflictOutcomeBehavior.Capture(notice, "RULER", entry.Ruler?.Name);
            ConflictOutcomeBehavior.Capture(notice, "REALM", entry.Realm?.Name);
            ConflictOutcomeBehavior.Capture(notice, "ENEMY", entry.Attacker?.Name);
            ConflictOutcomeBehavior.Capture(notice, "DATE", new TextObject("{=!}" + entry.Agenda?.SessionDate.ToString()));
            var title = FeudalTitleBehavior.Instance?.GetTitle(entry.TitleId);
            ConflictOutcomeBehavior.Capture(notice, "TITLE", title == null ? new TextObject("{=BC_RoyalPeaceDispute}the disputed title") : new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title)));
            ConflictOutcomeBehavior.Capture(notice, "CLAIMANT_HOUSE", entry.Claimant?.Name);
            ConflictOutcomeBehavior.Capture(notice, "HOLDER_HOUSE", entry.Holder?.Name);
            string reason = entry.Result == "feud_ended" ? "{=BC_RoyalPeaceEndFeud}The private war has already ended. There is no further fighting for this decree to halt."
                : entry.Result == "interrupted_execution" ? "{=BC_RoyalPeaceInterrupted}The Crown's proceedings were interrupted. This summons will not be issued again."
                : entry.Result == "foreign_peace" ? "{=BC_RoyalPeaceEndForeign}Peace has been made with the foreign enemy. The emergency no longer calls for this intervention."
                : entry.Result == "threat_receded" ? "{=BC_RoyalPeaceEndThreat}The foreign threat has receded, and the Crown has withdrawn its emergency intervention."
                : entry.Result == "war_or_crown_changed" ? "{=BC_RoyalPeaceEndCrown}The war or the authority under which this decree was announced has changed. The old summons no longer stands."
                : "{=BC_RoyalPeaceEndChanged}The circumstances required to carry out the decree no longer hold. No new settlement has been imposed by this summons.";
            ConflictOutcomeBehavior.Capture(notice, "REASON", new TextObject(reason));
            behavior.Publish(notice, kind);
        }
    }
}
