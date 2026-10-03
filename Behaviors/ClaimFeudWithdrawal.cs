using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class ClaimFeudBehavior
    {
        public bool IsPlayerFeudSupporter(ClaimFeudRecord record)
        {
            string id = Clan.PlayerClan?.StringId;
            return record != null && id != null && IsActiveFeudState(record.State)
                && id != record.ClaimantClanId && id != record.HolderClanId && !record.HasWithdrawn(id)
                && (SplitClanIds(record.ClaimantSupporterIds).Contains(id)
                    || SplitClanIds(record.HolderSupporterIds).Contains(id));
        }

        public TextObject PlayerWithdrawalBlock(ClaimFeudRecord record)
        {
            if (!_feuds.Contains(record) || !IsPlayerFeudSupporter(record))
                return new TextObject("{=BC_FeudLeave_NotSupporter}Only a pledged supporting house may leave. The principal houses must settle the dispute.");
            var clan = Clan.PlayerClan;
            if (clan.Leader == null || clan.Leader.IsPrisoner || clan.IsEliminated || clan.IsUnderMercenaryService)
                return new TextObject("{=BC_FeudLeave_Unavailable}Your house cannot withdraw in its present circumstances.");
            var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            if (record.State == ClaimFeudState.WarActive)
                return wars?.WithdrawalBlock(record.RecordId, clan)
                    ?? (wars == null ? new TextObject("{=BC_FeudLeave_Unavailable}Your house cannot withdraw in its present circumstances.") : null);
            if (clan.Kingdom?.StringId != record.ParentKingdomId)
                return new TextObject("{=BC_FeudLeave_Unavailable}Your house cannot withdraw in its present circumstances.");
            return null;
        }

        public bool TryWithdrawPlayerSupport(string recordId, out TextObject reason)
        {
            var record = _feuds.FirstOrDefault(r => r != null && r.RecordId == recordId);
            reason = PlayerWithdrawalBlock(record);
            if (reason != null) return false;
            var player = Clan.PlayerClan;
            bool claimantSide = SplitClanIds(record.ClaimantSupporterIds).Contains(player.StringId);
            string leaderId = claimantSide ? record.ClaimantClanId : record.HolderClanId;
            var formerSide = SplitClanIds(claimantSide ? record.ClaimantSupporterIds : record.HolderSupporterIds)
                .Concat(new[] { leaderId }).Distinct().Where(id => id != player.StringId)
                .Select(ResolveClan).Where(c => c != null && !c.IsEliminated).ToList();
            if (record.State == ClaimFeudState.WarActive
                && !Campaign.Current.GetCampaignBehavior<ClaimFeudWarBehavior>()
                    .TryWithdrawSupporter(record.RecordId, player, out reason)) return false;

            record.RecordWithdrawal(player.StringId);
            var claimant = SplitClanIds(record.ClaimantSupporterIds).Select(ResolveClan).Where(c => c != null).ToList();
            var holder = SplitClanIds(record.HolderSupporterIds).Select(ResolveClan).Where(c => c != null).ToList();
            RefreshRecordedSupporters(record, claimant, holder,
                RebellionPowerHelper.CalculateFactionPower(claimant), RebellionPowerHelper.CalculateFactionPower(holder));
            foreach (var house in formerSide)
                RelationMemoryService.ApplyChange(player.Leader, house.Leader, house.StringId == leaderId ? -20 : -10,
                    false, RelationMemorySources.FeudOathbreaker, 10f, RelationMemoryScope.House,
                    FeudalTitleBehavior.Instance?.GetTitle(record.TargetTitleId)?.Name);
            BellumCivileLogger.Log($"Feud supporter withdrew; feud={record.RecordId}; clan={player.StringId}; side={(claimantSide ? "claimant" : "holder")}; state={record.State}.");
            return true;
        }
    }
}
