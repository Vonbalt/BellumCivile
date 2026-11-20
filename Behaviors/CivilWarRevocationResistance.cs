using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class FactionManagerBehavior
    {
        internal FactionObject FindRevocationResistanceFaction(Kingdom realm, Clan holder)
        {
            if (realm == null || holder == null)
                return null;
            return _activeFactions.FirstOrDefault(f => f != null && !f.IsIdeology
                && f.ParentKingdom == realm && f.Members.Contains(holder));
        }

        internal bool TryResistCrownRevocation(Clan crown, Clan holder, Func<bool> demandStillCurrent,
            Action<bool> deferredCompletion, out string reason)
        {
            Kingdom realm = crown?.Kingdom;
            reason = null;
            bool CanResist() => realm?.IsEliminated == false && realm.RulingClan == crown
                && crown.Kingdom == realm && holder != crown && holder?.Kingdom == realm
                && !holder.IsUnderMercenaryService && KingdomCreationSafetyHelper.IsValidFounder(holder)
                && demandStillCurrent();
            if (!CanResist())
            {
                reason = new TextObject("{=BC_RevocationResistance_Stale}The Crown's demand is no longer valid.").ToString();
                return false;
            }

            FactionObject faction = FindRevocationResistanceFaction(realm, holder);
            bool formedForResistance = faction == null;
            if (formedForResistance)
                faction = new FactionObject($"{holder.Name} Coalition", realm, holder, FactionType.Abdication);

            if (!faction.TryPrepareRevocationResistance(out bool wasJudgmentResistance, out string failure))
            {
                BellumCivileLogger.Log($"Crown revocation resistance blocked; realm={realm.StringId}; holder={holder.StringId}; faction={faction.Name}; reason={failure}.");
                reason = new TextObject("{=BC_RevocationResistance_Blocked}The house cannot rally its faction against the Crown at present.").ToString();
                return false;
            }

            if (formedForResistance)
                RegisterNewFaction(faction);

            bool Finish(UltimatumResolution result)
            {
                // A partially started shell belongs to the existing rebellion recovery path.
                if (result != UltimatumResolution.Failed || faction.HasTrackedRebelKingdom || faction.IsRebellionCreationPending)
                {
                    BellumCivileLogger.Log($"Crown revocation resisted; realm={realm.StringId}; holder={holder.StringId}; faction={faction.Name}; type={faction.Type}; fallback={formedForResistance}; result={result}.");
                    return true;
                }

                faction.CancelRevocationResistance(wasJudgmentResistance);
                if (formedForResistance)
                    RemoveFaction(faction);
                return false;
            }

            UltimatumResolution resolution = formedForResistance
                ? faction.StartImmediateRevocationResistance(holder)
                : faction.TriggerUltimatumDetailed(null, suppressAutomaticPlayerChoice: false,
                    deferredCompletion: result =>
                    {
                        bool completed = Finish(result);
                        deferredCompletion?.Invoke(completed);
                    },
                    committedRevocationDefier: holder,
                    revocationStillCurrent: () => CanResist() && _activeFactions.Contains(faction) && faction.Members.Contains(holder));
            if (resolution == UltimatumResolution.PendingPlayerChoice || Finish(resolution))
                return true;

            reason = new TextObject("{=BC_RevocationResistance_Blocked}The house cannot rally its faction against the Crown at present.").ToString();
            return false;
        }
    }
}
