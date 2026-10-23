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

        private void SyncCivilWarGrace(IDataStore store)
        {
            store.SyncData("BC_CivilWarGrace", ref _civilWarGrace);
            _civilWarGrace = _civilWarGrace ?? new List<CivilWarGraceRecord>();
            foreach (var record in _civilWarGrace)
                record.Realms = record.Realms ?? new List<Kingdom>();
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
                if (!record.Realms.Contains(realm)) record.Realms.Add(realm);
        }

        private static bool PermanentGraceRealm(Kingdom realm) => realm != null && !realm.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);

        private void MaintainCivilWarGrace()
        {
            foreach (var record in _civilWarGrace.Where(r => !r.Settled).ToList())
            {
                if (_activeFactions.Contains(record.Faction)
                    || record.RebelRealm?.IsEliminated == false
                    || record.Realms.Any(r => PermanentGraceRealm(r) && (CivilWarConflictBehavior.IsRealmTransferPending(r)
                        || CrownAccessionBehavior.Instance?.IsPending(r) == true
                        || ElectiveSuccessionBehavior.Instance?.PendingDeposition(r) != null))) continue;
                var challenge = SuccessionChallengeBehavior.Instance?.GetWarRecord(record.Faction);
                if (challenge != null && !challenge.ResolutionReturned) continue;
                record.UntilDay = CampaignTime.Now.ToDays + record.DurationDays;
                record.Settled = true;
                BellumCivileLogger.Log($"Civil-war grace settled; faction={record.Faction?.Name}; realms={string.Join(",", record.Realms.Where(PermanentGraceRealm).Select(r => r.StringId))}; until={record.UntilDay:0.##}; duration={record.DurationDays:0.##}.");
            }
        }

        internal bool HasCivilWarGrace(Kingdom realm) => PermanentGraceRealm(realm)
            && _civilWarGrace.Any(r => r.DurationDays > 0 && r.Realms.Contains(realm)
                && (!r.Settled || r.UntilDay > CampaignTime.Now.ToDays));

        internal TextObject CivilWarGraceBlock(Kingdom realm)
        {
            if (!HasCivilWarGrace(realm)) return null;
            var records = _civilWarGrace.Where(r => r.DurationDays > 0 && r.Realms.Contains(realm)).ToList();
            if (records.Any(r => !r.Settled))
                return new TextObject("{=BC_CivilWarGrace_Settling}The recent civil war is still being settled. No new rebellion against the crown may begin until the settlement and its grace period have ended.");
            int days = Math.Max(1, (int)Math.Ceiling(records.Max(r => r.UntilDay) - CampaignTime.Now.ToDays));
            return new TextObject("{=BC_CivilWarGrace_Blocked}The realm is recovering from civil war. New rebellions against the crown are barred. Days remaining: {DAYS}. Private feuds and resistance to treason judgments are unaffected.")
                .SetTextVariable("DAYS", days);
        }

        internal bool IsCivilWarGraceBlocked(FactionObject faction) => faction != null && !faction.IsIdeology
            && !faction.IsJudgmentResistance && !faction.IsCommittedCrownChallenge && HasCivilWarGrace(faction.ParentKingdom);
    }
}
