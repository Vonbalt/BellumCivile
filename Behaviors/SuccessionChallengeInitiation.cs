using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        private void InitiateScheduledChallenges()
        {
            var loyalty = HereditaryLoyaltyBehavior.Instance;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (loyalty == null || manager == null) return;
            int day = (int)Day;
            foreach (var realm in Kingdom.All.Where(k => !k.IsEliminated && CrownAccessionBehavior.IsHereditaryRealm(k)
                && SuccessionLawBehavior.Instance?.ResolvePermanentRealm(k) == k).OrderBy(k => k.StringId, StringComparer.Ordinal).ToList())
            {
                int last = _lastInitiationChecks.TryGetValue(realm.StringId, out int saved) ? saved : -SuccessionChallengeSchedule.IntervalDays;
                if (!SuccessionChallengeSchedule.IsDue(realm.StringId, day, last)) continue;
                _lastInitiationChecks[realm.StringId] = day;
                if (CrownAccessionBehavior.Instance?.IsPending(realm) == true
                    || _records.Any(r => (r.WarRealm == realm || r.OutcomeRealm == realm) && (r.IsOpen || r.RealmBlockedUntil > Day))
                    || manager.GetFactionsInKingdom(realm).Any(f => f.IsCivilWarActive())) continue;
                loyalty.Maintain(realm);
                foreach (Hero heir in loyalty.GetLine(realm))
                {
                    if (heir == Hero.MainHero || !Available(heir) || heir.Clan?.Kingdom != realm) continue;
                    if (loyalty.Get(realm, heir)?.IsDisloyal != true) continue;
                    var record = TryBegin(realm, heir, MBRandom.RandomFloat);
                    if (record == null) continue;
                    ResolveAppeal(record);
                    BellumCivileLogger.Log($"Automatic succession challenge {record.Id}: claimant={heir.StringId}; realm={realm.StringId}; demand={record.Demand}; phase={record.Phase}.");
                    break; // No sibling may start another appeal in this scheduled pass.
                }
            }
        }
    }
}
