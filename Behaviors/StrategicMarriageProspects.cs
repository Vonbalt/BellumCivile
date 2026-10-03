using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class StrategicMarriageBehavior
    {
        private List<PendingMarriageProspect> _marriageProspects = new List<PendingMarriageProspect>();
        private Dictionary<Hero, PendingMarriageProspect> _prospectByHero;
        private int _prospectCleanupDay = -1;

        private void SyncMarriageProspects(IDataStore store)
        {
            store.SyncData("BellumCivile_MarriageProspects", ref _marriageProspects);
            _marriageProspects = _marriageProspects ?? new List<PendingMarriageProspect>();
            _prospectByHero = null;
            _prospectCleanupDay = -1;
        }

        private void IndexMarriageProspects()
        {
            if (_prospectByHero != null) return;
            _prospectByHero = new Dictionary<Hero, PendingMarriageProspect>();
            foreach (var p in _marriageProspects)
            {
                if (p?.First != null) _prospectByHero[p.First] = p;
                if (p?.Second != null) _prospectByHero[p.Second] = p;
            }
        }

        internal bool HasPendingProspect(Hero hero)
        {
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic) return false;
            IndexMarriageProspects();
            return hero != null && _prospectByHero.TryGetValue(hero, out var p)
                && CurrentDay <= p.ExpiresDay && p.IdentityValid;
        }

        private void QueueMarriageProspect(Clan clan, BellumMarriageMatch match)
        {
            if (HasPendingProspect(match.Suitor) || HasPendingProspect(match.Candidate)) return;
            _marriageProspects.Add(new PendingMarriageProspect
            {
                Sponsor = clan, First = match.Suitor, Second = match.Candidate,
                FirstHouse = match.Outcome.FirstHouse, SecondHouse = match.Outcome.SecondHouse,
                Destination = match.Outcome.Destination, ExpiresDay = CurrentDay + 30, NextAttemptDay = CurrentDay + 1
            });
            _prospectByHero = null;
            BellumCivileLogger.Log($"Marriage prospect reserved; house={clan.StringId}; first={match.Suitor.StringId}; second={match.Candidate.StringId}; expires_day={CurrentDay + 30}; reason=temporary_unavailability.");
        }

        private void RemoveMarriageProspect(PendingMarriageProspect p, string reason)
        {
            _marriageProspects.Remove(p);
            _prospectByHero = null;
            BellumCivileLogger.Log($"Marriage prospect concluded; house={p.Sponsor?.StringId}; first={p.First?.StringId}; second={p.Second?.StringId}; reason={reason}.");
        }

        private bool ProcessMarriageProspect(Clan clan)
        {
            // No all-hero search or score calculation while an army, siege or recovery blocks execution.
            if (_prospectCleanupDay != CurrentDay)
            {
                _prospectCleanupDay = CurrentDay;
                foreach (var stale in _marriageProspects.Where(p => p == null || !p.IdentityValid || CurrentDay > p.ExpiresDay).ToList())
                {
                    if (stale == null) { _marriageProspects.Remove(null); _prospectByHero = null; }
                    else RemoveMarriageProspect(stale, CurrentDay > stale.ExpiresDay ? "grace_expired" : "identity_changed");
                }
            }
            var prospect = _marriageProspects.FirstOrDefault(p => p.Sponsor == clan);
            if (prospect == null) return false;
            if (!prospect.IdentityValid) { RemoveMarriageProspect(prospect, "identity_changed"); return true; }
            if (CurrentDay < prospect.NextAttemptDay) return true;
            prospect.NextAttemptDay = CurrentDay + 3;
            if (!BellumMarriageStrategyHelper.MarriageProspectEligible(prospect.First)
                || !BellumMarriageStrategyHelper.MarriageProspectEligible(prospect.Second)
                || HasMarriageOfferFor(prospect.First) || HasMarriageOfferFor(prospect.Second)
                || prospect.FirstHouse.IsAtWarWith(prospect.SecondHouse)
                || CourtAgendaBehavior.Current?.HasDynasticReservation(prospect.First, prospect.Second) == true)
            { RemoveMarriageProspect(prospect, "eligibility_changed"); return true; }
            if (!BellumMarriageStrategyHelper.MarriageParticipantReady(prospect.First)
                || !BellumMarriageStrategyHelper.MarriageParticipantReady(prospect.Second)) return true;
            if ((prospect.FirstHouse == Clan.PlayerClan || prospect.SecondHouse == Clan.PlayerClan) && !CanSendPlayerOffer()) return true;
            try
            {
                var match = BellumMarriageStrategyHelper.ReevaluateProspect(prospect.First, prospect.Second);
                if (match == null || match.Outcome.Destination != prospect.Destination)
                { RemoveMarriageProspect(prospect, "consent_or_destination_changed"); return true; }
                // Remove before callbacks so neither a partial marriage nor a player rejection is retried.
                RemoveMarriageProspect(prospect, "ready_for_execution");
                _lastHouseEvaluationYear[clan.StringId] = CurrentDay / GetCampaignDaysInYear();
                CloseMarriageSearch(clan);
                CompleteStrategicMatch(clan, match, match.Suitor, BellumMarriageStrategyHelper.CalculateDynasticNeed(clan));
            }
            catch (Exception ex)
            {
                if (_marriageProspects.Contains(prospect)) RemoveMarriageProspect(prospect, "evaluation_failed");
                BellumCivileLogger.Log($"Marriage prospect evaluation failed; house={clan.StringId}; error={ex}");
            }
            return true;
        }
    }
}
