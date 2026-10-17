using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, double> _challengeWindowUntil = new Dictionary<string, double>();
        private Dictionary<string, bool> _challengeWindowAllowed = new Dictionary<string, bool>();

        private void SyncInstabilityData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_CourtChallengeWindowUntil", ref _challengeWindowUntil);
            dataStore.SyncData("BC_CourtChallengeWindowAllowed", ref _challengeWindowAllowed);
            _challengeWindowUntil = _challengeWindowUntil ?? new Dictionary<string, double>();
            _challengeWindowAllowed = _challengeWindowAllowed ?? new Dictionary<string, bool>();
        }

        private bool MayStartAutomaticCourtChallenge(FactionObject faction, Kingdom realm)
        {
            if (faction == null || realm == null) return false;
            if (faction.Leader == Clan.PlayerClan || GetAgenda(realm, faction)?.State == CourtAgendaState.Crisis)
                return true;
            string key = realm.StringId + "|" + (int)faction.Type;
            return InternalInstabilityRules.CourtWindowAllows(key, CampaignTime.Now.ToDays, NextEvaluation(realm).ToDays,
                BellumCivileOptions.CourtChallengeChancePercent, _challengeWindowUntil, _challengeWindowAllowed,
                () => MBRandom.RandomFloat);
        }

        private bool ShouldStartCourtCrisis(FactionObject faction, Kingdom realm)
        {
            if (faction == null) return false;
            if (faction.Leader == Clan.PlayerClan) return faction.Mood <= -60;
            return faction.Mood <= BellumCivileOptions.CourtCrisisMoodThreshold
                && MayStartAutomaticCourtChallenge(faction, realm);
        }

        private void PruneChallengeWindows(HashSet<string> livingRealms)
        {
            foreach (string key in _challengeWindowUntil.Keys.Where(key => key.LastIndexOf('|') < 0
                || !livingRealms.Contains(key.Substring(0, key.LastIndexOf('|')))).ToList())
            {
                _challengeWindowUntil.Remove(key);
                _challengeWindowAllowed.Remove(key);
            }
        }
    }
}
