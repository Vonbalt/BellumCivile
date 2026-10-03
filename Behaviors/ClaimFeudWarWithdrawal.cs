using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class ClaimFeudWarBehavior
    {
        private ClaimFeudWarRecord WithdrawalWar(string feudId) => _wars.FirstOrDefault(w => w != null && w.IsActive && w.FeudRecordId == feudId);

        private static List<KeyValuePair<Settlement, Clan>> WithdrawalEstates(ClaimFeudWarRecord war, Clan clan)
        {
            var result = new List<KeyValuePair<Settlement, Clan>>();
            foreach (string entry in (war.FiefSnapshot ?? "").Split(';'))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                var settlement = Settlement.All.FirstOrDefault(s => s.StringId == parts[0]);
                if (settlement != null && (parts[1] == clan.StringId || settlement.OwnerClan == clan))
                    result.Add(new KeyValuePair<Settlement, Clan>(settlement, ResolveClan(parts[1])));
            }
            return result;
        }

        public TextObject WithdrawalBlock(string feudId, Clan clan)
        {
            var war = WithdrawalWar(feudId);
            if (war == null || clan == null || clan.Kingdom?.RulingClan == clan
                || clan.StringId == war.ClaimantLeaderClanId || clan.StringId == war.HolderLeaderClanId
                || !(DecodeIds(war.ClaimantClanIds).Contains(clan.StringId) || DecodeIds(war.HolderClanIds).Contains(clan.StringId))
                || (clan.Kingdom?.StringId != war.ClaimantKingdomId && clan.Kingdom?.StringId != war.HolderKingdomId)
                || war.PendingResolutionOutcome != ClaimFeudWarOutcome.None || !string.IsNullOrEmpty(war.PendingCaptureWinnerClanId))
                return new TextObject("{=BC_FeudLeave_Unavailable}Your house cannot withdraw in its present circumstances.");
            var parent = ResolveReturnKingdom(war);
            var claimantRealm = ResolveKingdom(war.ClaimantKingdomId);
            var holderRealm = ResolveKingdom(war.HolderKingdomId);
            if (parent == null)
                return new TextObject("{=BC_FeudLeave_NoRealm}There is no surviving parent realm to receive your house.");
            if (claimantRealm == null || holderRealm == null || claimantRealm.IsEliminated || holderRealm.IsEliminated
                || parent.IsAtWarWith(claimantRealm) || parent.IsAtWarWith(holderRealm))
                return new TextObject("{=BC_FeudLeave_Unavailable}Your house cannot withdraw in its present circumstances.");
            if (MobileParty.All.Any(p => p.ActualClan == clan && (p.MapEvent != null || p.SiegeEvent != null
                || p.Army?.LeaderParty?.MapEvent != null || p.Army?.LeaderParty?.SiegeEvent != null))
                || WithdrawalEstates(war, clan).Any(e => e.Key.SiegeEvent != null))
                return new TextObject("{=BC_FeudLeave_Busy}Your parties and affected estates must be clear of battles and sieges before your house can withdraw.");
            var sides = new HashSet<string>(DecodeIds(war.ClaimantClanIds).Concat(DecodeIds(war.HolderClanIds)));
            if (WithdrawalEstates(war, clan).Any(e => e.Value?.Leader == null || e.Value.IsEliminated
                || e.Key.OwnerClan == null || !sides.Contains(e.Key.OwnerClan.StringId)))
                return new TextObject("{=BC_FeudLeave_Estates}Your house's wartime estates cannot presently be restored safely. Settle their occupation before withdrawing.");
            return null;
        }

        internal bool TryWithdrawSupporter(string feudId, Clan clan, out TextObject reason)
        {
            reason = WithdrawalBlock(feudId, clan);
            if (reason != null) return false;
            var war = WithdrawalWar(feudId);
            var parent = ResolveReturnKingdom(war);
            var estates = WithdrawalEstates(war, clan);
            var influence = DecodeInfluenceSnapshot(war.InfluenceSnapshot);
            float retainedInfluence = Math.Max(clan.Influence, influence.TryGetValue(clan.StringId, out float old) ? old : 0);
            foreach (var party in MobileParty.All.Where(p => p.ActualClan == clan && p.Army != null).ToList())
            {
                if (party.Army.LeaderParty == party) DisbandArmyAction.ApplyByUnknownReason(party.Army);
                else party.Army = null;
            }
            foreach (var estate in estates)
                if (estate.Key.OwnerClan != estate.Value)
                    ChangeOwnerOfSettlementAction.ApplyByDefault(estate.Value.Leader, estate.Key);
            KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, parent, showNotification: false);
            clan.Influence = retainedInfluence;
            war.RemoveWithdrawnHouse(clan.StringId);
            UpdateObjectiveScorePressure(war);
            BellumCivileLogger.Log($"Feud supporter returned; war={war.WarId}; clan={clan.StringId}; destination={parent.StringId}; estates={estates.Count}.");
            return true;
        }
    }
}
