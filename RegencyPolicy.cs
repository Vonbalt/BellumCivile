using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile
{
    internal static class RegencyPolicy
    {
        internal static bool TryRestoreAdultLeader(Clan clan)
        {
            Hero successor = clan.GetHeirApparents().OrderByDescending(p => p.Value)
                .Select(p => p.Key).FirstOrDefault(h => h.IsAlive && h.Clan == clan
                    && h.Age >= SuccessionLawHelper.GetAgeOfMajority()
                    && h.DeathMark == KillCharacterAction.KillCharacterActionDetail.None);
            if (successor == null) return false;
            if (clan.Leader == null) clan.SetLeader(successor);
            else ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, successor);
            return clan.Leader == successor;
        }

        internal static bool CanReceiveCrown(Hero heir) => heir != null
            && (BellumCivileOptions.AllowNewRegencies
                || heir.Age >= SuccessionLawHelper.GetAgeOfMajority()
                || HasContinuingRegency(heir));

        internal static bool HasContinuingRegency(Hero ward)
        {
            var regencies = RegencyBehavior.Instance;
            Clan clan = ward?.Clan;
            if (clan == null || ward.IsAlive != true || clan.IsEliminated
                || regencies?.GetWard(clan) != ward) return false;
            Hero regent = regencies.GetRegent(clan);
            return regent != null && regent != ward && regent.IsAlive && regent.Clan == clan
                && regent.DeathMark == KillCharacterAction.KillCharacterActionDetail.None
                && regent.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && !regencies.IsRegentDeathInProgress(regent);
        }
    }
}
