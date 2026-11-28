using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class FactionManagerBehavior
    {
        private List<CivilWarGraceRecord> _civilWarGrace = new List<CivilWarGraceRecord>();
        private readonly HashSet<Kingdom> _pendingPacificationDisbands = new HashSet<Kingdom>();
        private Dictionary<Kingdom, CampaignTime> _initialCivilWarGrace = new Dictionary<Kingdom, CampaignTime>();
        private bool _initialCivilWarGracePending;
        private CampaignTime _initialCivilWarGraceUntil = CampaignTime.Zero;

        private void SyncCivilWarGrace(IDataStore store)
        {
            store.SyncData("BC_CivilWarGrace", ref _civilWarGrace);
            store.SyncData("BC_InitialCivilWarGrace", ref _initialCivilWarGrace);
            store.SyncData("BC_InitialCivilWarGracePending", ref _initialCivilWarGracePending);
            store.SyncData("BC_InitialCivilWarGraceUntil", ref _initialCivilWarGraceUntil);
            _civilWarGrace = _civilWarGrace ?? new List<CivilWarGraceRecord>();
            _initialCivilWarGrace = _initialCivilWarGrace ?? new Dictionary<Kingdom, CampaignTime>();
            foreach (var record in _civilWarGrace)
                record.Realms = record.Realms ?? new List<Kingdom>();
            if (store.IsLoading)
            {
                _pendingPacificationDisbands.Clear();
                foreach (var record in _civilWarGrace.Where(r => r.Settled && r.DurationDays > 0))
                    _pendingPacificationDisbands.UnionWith(record.Realms);
            }
        }

        private void ArmInitialCivilWarGrace(CampaignGameStarter starter)
        {
            if (_initialCivilWarGraceUntil != CampaignTime.Zero) return;
            _initialCivilWarGraceUntil = CampaignTime.Now + CampaignTime.Days(Math.Max(1, CampaignTime.DaysInYear));
            _initialCivilWarGracePending = true;
        }

        private void ApplyInitialCivilWarGrace(CampaignGameStarter starter)
        {
            if (!_initialCivilWarGracePending) return;
            foreach (var realm in Kingdom.All.Where(PermanentGraceRealm))
                _initialCivilWarGrace[realm] = _initialCivilWarGraceUntil;
            _initialCivilWarGracePending = false;
        }

        internal void QueueCivilWarGrace(FactionObject faction, Kingdom rebel, Kingdom outcome = null)
        {
            if (faction == null || faction.IsIdeology || !faction.HasStartedCivilWar) return;
            var record = _civilWarGrace.FirstOrDefault(r => r.Faction == faction);
            if (record == null)
            {
                record = new CivilWarGraceRecord { Faction = faction, RebelRealm = rebel,
                    DurationDays = BellumCivileOptions.CivilWarGraceYears * Math.Max(1, CampaignTime.DaysInYear) };
                _civilWarGrace.Add(record);
            }
            AddCivilWarGraceRealms(faction, faction.ParentKingdom, outcome);
        }

        internal void AddCivilWarGraceRealms(FactionObject faction, params Kingdom[] realms)
        {
            var record = _civilWarGrace.FirstOrDefault(r => r.Faction == faction);
            if (record == null) return;
            foreach (var realm in realms.Where(PermanentGraceRealm))
            {
                if (record.Realms.Contains(realm)) continue;
                record.Realms.Add(realm);
                if (record.Settled && record.DurationDays > 0)
                    _pendingPacificationDisbands.Add(realm);
            }
        }

        private static bool PermanentGraceRealm(Kingdom realm) => realm != null && !realm.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);

        private static bool IsGraceSettlementPending(Kingdom realm) => CivilWarConflictBehavior.IsRealmTransferPending(realm)
            || CrownAccessionBehavior.Instance?.IsPending(realm) == true
            || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null;

        private void MaintainCivilWarGrace()
        {
            foreach (var record in _civilWarGrace.Where(r => !r.Settled).ToList())
            {
                if (_activeFactions.Contains(record.Faction)
                    || record.RebelRealm?.IsEliminated == false
                    || record.Realms.Any(r => PermanentGraceRealm(r) && IsGraceSettlementPending(r))) continue;
                var challenge = SuccessionChallengeBehavior.Instance?.GetWarRecord(record.Faction);
                if (challenge != null && !challenge.ResolutionReturned) continue;
                record.UntilDay = CampaignTime.Now.ToDays + record.DurationDays;
                record.Settled = true;
                if (record.DurationDays > 0)
                    _pendingPacificationDisbands.UnionWith(record.Realms);
                BellumCivileLogger.Log($"Civil-war grace settled; faction={record.Faction?.Name}; realms={string.Join(",", record.Realms.Where(PermanentGraceRealm).Select(r => r.StringId))}; until={record.UntilDay:0.##}; duration={record.DurationDays:0.##}.");
            }
            DisbandPacifiedConspiracies();
        }

        private void DisbandPacifiedConspiracies()
        {
            if (_pendingPacificationDisbands.Count == 0) return;
            double now = CampaignTime.Now.ToDays;
            _pendingPacificationDisbands.RemoveWhere(realm => !PermanentGraceRealm(realm)
                || !_civilWarGrace.Any(r => r.Settled && r.DurationDays > 0 && r.UntilDay > now && r.Realms.Contains(realm)));
            var ready = new HashSet<Kingdom>(_pendingPacificationDisbands.Where(realm => !IsGraceSettlementPending(realm)));
            if (ready.Count == 0) return;
            _pendingPacificationDisbands.ExceptWith(ready);

            var playerClan = Clan.PlayerClan;
            var summary = Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>();
            foreach (var faction in _activeFactions.Where(f => IsWarmingCrownFaction(f) && ready.Contains(f.ParentKingdom)
                && !f.IsCommittedCrownChallenge && !f.IsJudgmentResistance).ToList())
            {
                var realm = faction.ParentKingdom;
                if (CivilWarConflictBehavior.IsFactionTransferPending(faction))
                {
                    _pendingPacificationDisbands.Add(realm);
                    continue;
                }

                RemoveFaction(faction);
                summary?.RecordFactionDisbanded(faction, "postwar_pacification");
                if (playerClan == null || (faction.Leader != playerClan && !faction.Members.Contains(playerClan))) continue;
                var notice = new TextObject("{=BC_CivilWarGrace_FactionDisbanded}With peace restored in {REALM}, {FACTION} has disbanded. New rebellions against the Crown are barred for {DAYS} days.")
                    .SetTextVariable("REALM", realm.Name)
                    .SetTextVariable("FACTION", NotificationHelper.GetFactionDisplayName(faction))
                    .SetTextVariable("DAYS", GetCivilWarGraceRemainingDays(realm));
                BellumCivileNotifications.ShowPersonal(notice, BellumNotificationColors.Rebellion);
            }
        }

        internal bool HasCivilWarGrace(Kingdom realm) => PermanentGraceRealm(realm)
            && ((_initialCivilWarGrace.TryGetValue(realm, out var initialUntil) && initialUntil.ToDays > CampaignTime.Now.ToDays)
                || _civilWarGrace.Any(r => r.DurationDays > 0 && r.Realms.Contains(realm)
                    && (!r.Settled || r.UntilDay > CampaignTime.Now.ToDays)));

        internal TextObject CivilWarGraceBlock(Kingdom realm)
        {
            if (!HasCivilWarGrace(realm)) return null;
            return new TextObject("{=BC_CivilWarGrace_Blocked}The realm is currently pacified. New rebellions against the crown are barred for {DAYS} days.")
                .SetTextVariable("DAYS", GetCivilWarGraceRemainingDays(realm));
        }

        private int GetCivilWarGraceRemainingDays(Kingdom realm)
        {
            double now = CampaignTime.Now.ToDays;
            double remaining = _civilWarGrace.Where(r => r.DurationDays > 0 && r.Realms.Contains(realm))
                .Select(r => r.Settled ? r.UntilDay - now : r.DurationDays).DefaultIfEmpty(0).Max();
            if (_initialCivilWarGrace.TryGetValue(realm, out var initialUntil))
                remaining = Math.Max(remaining, initialUntil.ToDays - now);
            return Math.Max(1, (int)Math.Ceiling(remaining));
        }

        internal bool IsCivilWarGraceBlocked(FactionObject faction) => faction != null && !faction.IsIdeology
            && !faction.IsJudgmentResistance && !faction.IsCommittedCrownChallenge && HasCivilWarGrace(faction.ParentKingdom);

        internal TextObject CivilWarGraceJoinBlock(FactionObject faction) => faction != null && !faction.IsIdeology
            && !faction.IsJudgmentResistance && !faction.IsCivilWarActive() ? CivilWarGraceBlock(faction.ParentKingdom) : null;
    }
}
