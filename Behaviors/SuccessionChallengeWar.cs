using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        private void DispatchRefusedChallenge(SuccessionChallengeRecord record)
        {
            if (!_settling.Add(record)) return;
            try
            {
                var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                if (manager == null || titles == null || record.WarOutcome != SuccessionChallengeOutcome.None) return;
                if (!record.WarDispatchStarted)
                {
                    if (!ValidParticipants(record) || !RefreshResponseBacking(record)) return;
                    if (record.CrownEstate != null && record.WarEstate == null)
                    {
                        record.WarEstate = PreviewConcession(record, lawfulOnly: true, hostile: true);
                        if (record.WarEstate == null) return;
                    }
                    if (record.MilitaryEstate != null && !CrownEstateAvailable(record.MilitaryEstate)) return;
                    if (record.Backers.Any(c => manager.GetRebelFaction(c)?.IsCivilWarActive() == true)) return;
                    foreach (string id in record.MilitaryEstate?.EndowmentTitles ?? Enumerable.Empty<string>())
                    {
                        var title = titles.GetTitle(id);
                        if (title == null || !title.IsActive) return;
                        record.PrewarLegalOwners[id] = title.DeJureHolderClanId;
                        record.PrewarLegalParents[id] = title.ParentTitleId;
                    }
                    foreach (string id in record.MilitaryEstate?.EndowmentFiefs ?? Enumerable.Empty<string>())
                    {
                        if (!titles.TryGetBarony(Settlement.Find(id), out var title)) return;
                        record.PrewarLegalOwners[title.TitleId] = title.DeJureHolderClanId;
                        record.PrewarLegalParents[title.TitleId] = title.ParentTitleId;
                    }
                    record.WarDispatchStarted = true;
                }

                // Once mutations start, recover this transaction, never redraw a demand or pledge.
                record.WarShell = record.WarShell ?? record.WarFaction?.GetTrackedRebelKingdomIncludingEliminated();
                if (record.WarFaction?.TryValidateActiveRebellion(out _, out _) == true
                    && !record.WarFaction.IsChallengeStartupPending)
                {
                    record.Phase = SuccessionChallengePhase.ActiveWar;
                    record.ReportPending = false;
                    return;
                }
                if (record.Realm?.IsEliminated != false || record.Sovereign?.IsAlive != true
                    || Sovereign(record.Realm) != record.Sovereign || record.Challenger?.IsAlive != true)
                {
                    AbortUnstartedWar(record, "The ruler or challenger changed during preparation");
                    return;
                }
                if (!Available(record.Challenger)) return;
                if (!DispatchPledgesValid(record))
                {
                    AbortUnstartedWar(record, "A pledged house or speaker changed during preparation");
                    return;
                }
                var estate = record.MilitaryEstate;
                if (estate != null && (!estate.CadetAnnounced || estate.Cadet?.Leader != record.Challenger))
                {
                    if (!CrownEstateAvailable(estate)) return;
                    if (Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>()?.PrepareAbdicationCadet(estate) != true) return;
                }
                Clan house = record.Challenger.Clan;
                if (house?.Leader != record.Challenger || (house.Kingdom != record.Realm && house.Kingdom != record.WarShell)) return;
                if (record.WarFaction == null)
                {
                    record.WarFaction = new FactionObject("Succession challenge " + record.Id, record.Realm, house, FactionType.InstallRuler);
                    record.WarFaction.BindSuccessionChallenge(record.Id);
                    foreach (Clan backer in record.Backers.Where(c => c != house)) record.WarFaction.Members.Add(backer);
                    // Capture before seizure: a stolen royal estate is not prewar rebel property.
                    record.WarFaction.CaptureCivilWarStartFiefCounts(record.Realm.Clans);
                    record.WarFaction.CaptureCivilWarStartInfluence(record.Realm.Clans);
                }
                var faction = record.WarFaction;
                foreach (Clan member in faction.Members)
                    foreach (var previous in manager.GetFactionsInKingdom(record.Realm)
                        .Where(f => f != faction && !f.IsIdeology && f.Members.Contains(member)).ToList())
                    {
                        if (previous.IsCivilWarActive() || previous.HasTrackedRebelKingdom) return;
                        previous.RemoveMember(member);
                        if (previous.Members.Count == 0) manager.RemoveFaction(previous);
                    }
                manager.RegisterNewFaction(faction);
                if (estate != null)
                {
                    foreach (string id in estate.EndowmentFiefs)
                    {
                        if (record.SeizedFiefs.Contains(id)) continue;
                        var holding = Settlement.Find(id);
                        if (holding == null || holding.SiegeEvent != null || holding.Party?.MapEvent != null) return;
                        if (holding.OwnerClan != estate.EndowmentHouse && holding.OwnerClan != house)
                        {
                            AbortUnstartedWar(record, "A promised inheritance holding changed hands");
                            return;
                        }
                        if (!record.SeizureIntents.Contains(id)) record.SeizureIntents.Add(id);
                        if (holding.OwnerClan != house && !titles.TransferChallengePossession(estate.EndowmentHouse, house, holding)) return;
                        if (!titles.TryGetBarony(holding, out var barony) || !LegalSnapshotMatches(record, barony)) return;
                        record.SeizedFiefs.Add(id);
                    }
                }
                if (!faction.Members.Any(c => c.Fiefs.Any(f => f.IsTown || f.IsCastle)))
                {
                    AbortUnstartedWar(record, "The coalition no longer has a stronghold");
                    return;
                }
                // After household/holding moves, bypass the cached clan strength. Native
                // party membership now accounts for the heir on exactly one side.
                record.BackingPower = faction.Members.Sum(c => (double)RebellionPowerHelper.CalculateLiveClanPower(c));
                record.LoyalistPower = record.Loyalists.Sum(c => (double)RebellionPowerHelper.CalculateLiveClanPower(c));
                record.RequiredRatio = RebellionPowerHelper.CalculateRebellionPowerThreshold(record.Challenger);
                if (!SuccessionChallengeRules.CanProceed(record.BackingPower, record.LoyalistPower, record.RequiredRatio,
                    record.Challenger == Hero.MainHero, faction.Members.Any(c => c.Fiefs.Any(f => f.IsTown || f.IsCastle))))
                {
                    AbortUnstartedWar(record, "The coalition no longer meets the required power ratio");
                    return;
                }
                if (!faction.StartFrozenSuccessionRebellion(out string failure))
                {
                    record.Failure = failure;
                    record.WarShell = faction.GetTrackedRebelKingdomIncludingEliminated();
                    return;
                }
                record.WarShell = faction.GetTrackedRebelKingdomIncludingEliminated();
                record.Phase = SuccessionChallengePhase.ActiveWar;
                record.ReportPending = false;
                record.Failure = null;
            }
            catch (Exception ex)
            {
                record.WarShell = record.WarShell ?? record.WarFaction?.GetTrackedRebelKingdomIncludingEliminated();
                record.Failure = ex.Message;
                BellumCivileLogger.Log($"Succession war dispatch pending; crisis={record.Id}; error={ex}");
            }
            finally { _settling.Remove(record); }
        }

        private static bool DispatchPledgesValid(SuccessionChallengeRecord record) => record.Pledges.All(p =>
            p.Clan?.IsEliminated == false && p.Clan.Leader == p.Speaker
            && (p.Clan.Kingdom == record.Realm || record.Backers.Contains(p.Clan) && p.Clan.Kingdom == record.WarShell))
            && record.Realm.Clans.Where(c => NobleParticipant(c, record.Realm) && c != record.MilitaryEstate?.Cadet)
                .All(c => record.Pledges.Any(p => p.Clan == c));

        private static bool LegalSnapshotMatches(SuccessionChallengeRecord record, FeudalTitleRecord title) => title != null
            && record.PrewarLegalOwners.TryGetValue(title.TitleId, out string owner) && title.DeJureHolderClanId == owner
            && record.PrewarLegalParents.TryGetValue(title.TitleId, out string parent) && title.ParentTitleId == parent;

        private void AbortUnstartedWar(SuccessionChallengeRecord record, string reason)
        {
            record.Failure = reason;
            // A created shell may already have moved clans. Let the ordinary white-peace
            // drain settle that shell, rather than abandoning or deleting its members.
            if (record.WarShell != null)
            {
                Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?.ResolveWhitePeace(record.WarFaction, record.WarShell);
                return;
            }
            if (!RestoreSeizedProperty(record)) return;
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(record.WarFaction);
            WithdrawForInsufficientBacking(record);
        }

        private bool RestoreSeizedProperty(SuccessionChallengeRecord record)
        {
            var estate = record.MilitaryEstate;
            if (estate == null) return true;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return false;
            foreach (string id in record.SeizureIntents.Except(record.ReconciledFiefs).ToList())
            {
                var holding = Settlement.Find(id);
                Clan source = estate.EndowmentHouse;
                if (holding?.OwnerClan == estate.Cadet && source?.IsEliminated == false && source.Leader?.IsAlive == true)
                {
                    if (!titles.TryGetBarony(holding, out var title)) return false;
                    // Later treaties, grants and third-party possession are not ours to undo.
                    if (LegalSnapshotMatches(record, title)
                        && !titles.TransferChallengePossession(estate.Cadet, source, holding)) return false;
                }
                record.ReconciledFiefs.Add(id);
            }
            return true;
        }

        internal bool CanWinChallenge(FactionObject faction)
        {
            var record = GetWarRecord(faction);
            return record == null || record.Challenger?.IsAlive == true && faction.Leader?.Leader == record.Challenger;
        }

        internal SuccessionChallengeRecord GetWarRecord(FactionObject faction) =>
            faction == null ? null : _records.FirstOrDefault(r => r.WarFaction == faction);
        internal bool HasPendingWarOutcome(Kingdom realm) => _records.Any(r => (r.WarRealm == realm || r.OutcomeRealm == realm)
            && r.Phase == SuccessionChallengePhase.ResolvingWar && !r.ResolutionReturned);

        internal bool PrepareWarOutcome(FactionObject faction, SuccessionChallengeOutcome outcome, Kingdom destination,
            CivilWarRivalDefeatRecord ownedRivalDefeat = null)
        {
            bool owned = ownedRivalDefeat != null && outcome == SuccessionChallengeOutcome.Defeat
                && ownedRivalDefeat.Loser?.Faction == faction && ownedRivalDefeat.Winner?.Realm == destination
                && Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?.OwnsRivalDefeat(ownedRivalDefeat) == true;
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction) && !owned) return false;
            var record = GetWarRecord(faction);
            if (record == null) return true;
            if (record.ResolutionReturned) return true;
            if (record.WarOutcome != SuccessionChallengeOutcome.None && record.WarOutcome != outcome) return false;
            if (record.WarOutcome == SuccessionChallengeOutcome.None)
            {
                record.OutcomeLosers = (outcome == SuccessionChallengeOutcome.Defeat ? faction.Members
                    : record.WarRealm.Clans.Where(c => !faction.Members.Contains(c))).Where(c => c != null && !c.IsEliminated).Distinct().ToList();
                record.OutcomeVictor = outcome == SuccessionChallengeOutcome.Victory ? faction.Leader : destination?.RulingClan;
            }
            record.WarShell = record.WarShell ?? faction.GetTrackedRebelKingdomIncludingEliminated();
            record.WarOutcome = outcome;
            record.OutcomeRealm = destination;
            record.Phase = SuccessionChallengePhase.ResolvingWar;
            if (record.EstateResolved) return true;
            try
            {
                if (outcome != SuccessionChallengeOutcome.Victory && !RestoreSeizedProperty(record)) return false;
            }
            catch (Exception ex)
            {
                record.Failure = ex.Message;
                BellumCivileLogger.Log($"Succession restitution pending; crisis={record.Id}; error={ex}");
                return false;
            }
            // Victory legalization is deferred until the actual Crown transfer completes.
            if (outcome != SuccessionChallengeOutcome.Victory) record.EstateResolved = true;
            return true;
        }

        internal void CompleteWarOutcome(FactionObject faction, Kingdom destination)
        {
            var record = GetWarRecord(faction);
            if (record == null || record.ResolutionReturned || record.WarOutcome == SuccessionChallengeOutcome.None) return;
            if (record.WarOutcome != SuccessionChallengeOutcome.WhitePeace && (!record.TribunalQueued || !record.EstateResolved)) return;
            if (record.WarShell?.IsEliminated == false && record.WarShell.Clans.Any(c => !c.IsEliminated)) return;
            record.OutcomeRealm = destination;
            record.ResolutionReturned = true;
            record.OutcomeDay = Day;
            record.SubmissionRuler = record.WarOutcome == SuccessionChallengeOutcome.Defeat ? Sovereign(destination) : null;
            record.SubmissionUntil = record.OutcomeDay + CampaignTime.Years(3).ToDays;
            FinishWarOutcome(record);
        }

        private void FinishWarOutcome(SuccessionChallengeRecord record)
        {
            if (record.Phase != SuccessionChallengePhase.ResolvingWar) return;
            if (!record.ResolutionReturned)
            {
                Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?.ResumeChallengeOutcome(record);
                return;
            }
            if (record.WarShell?.IsEliminated == false && record.WarShell.Clans.Any(c => !c.IsEliminated)) return;
            if (record.WarOutcome == SuccessionChallengeOutcome.Victory)
            {
                var realm = record.OutcomeRealm;
                if (!LegalizeChallengeVictory(record.WarFaction, realm)) return;
                if (realm?.IsEliminated == false) HereditaryLoyaltyBehavior.Instance?.Observe(realm, false);
            }
            record.PersonalBlockedUntil = record.OutcomeDay + CampaignTime.Years(SuccessionChallengeRules.PersonalPauseYears(record.WarOutcome)).ToDays;
            record.RealmBlockedUntil = record.OutcomeDay + CampaignTime.Years(2).ToDays;
            record.Phase = SuccessionChallengePhase.Settled;
            record.ReportPending = false;
            record.Failure = null;
            HereditaryLoyaltyBehavior.Instance?.Invalidate();
        }

        internal bool LegalizeChallengeVictory(FactionObject faction, Kingdom realm)
        {
            var record = GetWarRecord(faction);
            if (record == null || record.EstateResolved) return true;
            var house = record.OutcomeVictor;
            if (house?.IsEliminated != false || realm == null) return false;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var crown = titles?.GetKingdomPoliticalTitle(realm);
            if (record.WarOutcome != SuccessionChallengeOutcome.Victory || titles == null) return false;
            // The winning house is frozen before transfers can change the faction leader.
            // A completed Crown transfer remains valid after a later death or deposition.
            if (!record.HasWarCrownReceipt && (realm.RulingClan != house
                || crown?.DeJureHolderClanId != house.StringId || crown.DeFactoHolderClanId != house.StringId)) return false;
            record.WarCrownTransferred = true;
            if (record.MilitaryEstate != null)
            {
                var estate = record.MilitaryEstate;
                foreach (string id in estate.EndowmentTitles.Except(estate.DeliveredTitles).ToList())
                {
                    var title = titles.GetTitle(id);
                    if (title?.IsActive != true) continue;
                    if (LegalSnapshotMatches(record, title))
                        titles.LegalizeTitleInheritance(house, title, "dynastic challenge victory",
                            preserveDeFacto: title.DeFactoHolderClanId != estate.EndowmentHouse.StringId
                                && title.DeFactoHolderClanId != house.StringId);
                    if (title.DeJureHolderClanId == house.StringId) estate.DeliveredTitles.Add(id);
                }
                foreach (string id in record.SeizedFiefs.Except(estate.DeliveredFiefs).ToList())
                    if (Settlement.Find(id)?.OwnerClan == house && titles.TryGetBarony(Settlement.Find(id), out var title)
                        && title.DeJureHolderClanId == house.StringId) estate.DeliveredFiefs.Add(id);
                estate.EndowmentSettled = true;
            }
            record.EstateResolved = true;
            return true;
        }

        private void MaintainActiveChallenge(SuccessionChallengeRecord record)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(record.WarFaction)) return;
            var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolution == null || record.WarFaction == null) return;
            try
            {
                if (!CanWinChallenge(record.WarFaction) && record.WarShell?.IsEliminated == false)
                    resolution.ResolveWhitePeace(record.WarFaction, record.WarShell);
                else if (record.WarShell == null || record.WarShell.IsEliminated || !record.WarShell.IsAtWarWith(record.WarRealm))
                    resolution.ResolveUnscriptedPeace(record.WarFaction, record.WarShell);
            }
            catch (Exception ex)
            {
                record.Failure = ex.Message;
                BellumCivileLogger.Log($"Succession war maintenance pending; crisis={record.Id}; error={ex}");
            }
        }
    }
}
