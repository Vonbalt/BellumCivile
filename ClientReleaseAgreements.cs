using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    // Change pact dates in place: ending/restarting an alliance also discards its call-to-war obligations.
    internal sealed class ClientReleaseAgreements
    {
        private static readonly FieldInfo Alliances = typeof(AllianceCampaignBehavior).GetField("_alliances", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Trades = typeof(TradeAgreementsCampaignBehavior).GetField("_tradeAgreements", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly IList _alliances;
        private readonly List<TradeAgreementsCampaignBehavior.TradeAgreement> _trades;
        private readonly int _allianceIndex, _tradeIndex;
        private readonly object _oldAlliance, _newAlliance;
        private readonly TradeAgreementsCampaignBehavior.TradeAgreement _oldTrade, _newTrade;

        private ClientReleaseAgreements(IList alliances, List<TradeAgreementsCampaignBehavior.TradeAgreement> trades,
            int allianceIndex, int tradeIndex, object oldAlliance, object newAlliance,
            TradeAgreementsCampaignBehavior.TradeAgreement oldTrade, TradeAgreementsCampaignBehavior.TradeAgreement newTrade)
        {
            _alliances = alliances; _trades = trades; _allianceIndex = allianceIndex; _tradeIndex = tradeIndex;
            _oldAlliance = oldAlliance; _newAlliance = newAlliance; _oldTrade = oldTrade; _newTrade = newTrade;
        }

        internal static bool TryPrepare(Kingdom first, Kingdom second, out ClientReleaseAgreements change)
        {
            change = null;
            try
            {
                var campaign = Campaign.Current;
                var allianceBehavior = campaign?.GetCampaignBehavior<IAllianceCampaignBehavior>() as AllianceCampaignBehavior;
                var tradeBehavior = campaign?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>() as TradeAgreementsCampaignBehavior;
                if (allianceBehavior == null || tradeBehavior == null) return false;
                var alliances = Alliances?.GetValue(allianceBehavior) as IList;
                var trades = Trades?.GetValue(tradeBehavior)
                    as List<TradeAgreementsCampaignBehavior.TradeAgreement>;
                if (first == null || second == null || alliances == null || trades == null) return false;
                int allianceIndex = -1, tradeIndex = -1;
                FieldInfo end = null;
                bool Pair(Kingdom a, Kingdom b) => a == first && b == second || a == second && b == first;
                for (int i = 0; i < alliances.Count; i++)
                {
                    object item = alliances[i];
                    var type = item?.GetType();
                    var a = type?.GetField("Kingdom1"); var b = type?.GetField("Kingdom2");
                    if (a == null || b == null) return false;
                    if (!Pair(a.GetValue(item) as Kingdom, b.GetValue(item) as Kingdom)) continue;
                    if (allianceIndex >= 0) return false;
                    allianceIndex = i; end = type.GetField("EndTime");
                }
                for (int i = 0; i < trades.Count; i++)
                    if (Pair(trades[i].Kingdom1, trades[i].Kingdom2))
                    {
                        if (tradeIndex >= 0) return false;
                        tradeIndex = i;
                    }
                if (allianceIndex < 0 || tradeIndex < 0 || end?.FieldType != typeof(CampaignTime) || end.IsInitOnly) return false;
                var now = CampaignTime.Now;
                var allianceEnd = now + campaign.Models.AllianceModel.MaxDurationOfAlliance;
                var tradeEnd = now + campaign.Models.TradeAgreementModel.GetTradeAgreementDurationInYears(first, second);
                if (allianceEnd.ToDays <= now.ToDays || tradeEnd.ToDays <= now.ToDays) return false;
                object oldAlliance = alliances[allianceIndex];
                object newAlliance = RuntimeHelpers.GetObjectValue(oldAlliance);
                var oldEnd = (CampaignTime)end.GetValue(oldAlliance);
                end.SetValue(newAlliance, oldEnd.ToDays > allianceEnd.ToDays ? oldEnd : allianceEnd);
                var oldTrade = trades[tradeIndex];
                var newTrade = new TradeAgreementsCampaignBehavior.TradeAgreement(oldTrade.Kingdom1, oldTrade.Kingdom2, tradeEnd)
                {
                    Kingdom1GoldGained = oldTrade.Kingdom1GoldGained, Kingdom2GoldGained = oldTrade.Kingdom2GoldGained,
                    Kingdom1GoldGainedTotal = oldTrade.Kingdom1GoldGainedTotal, Kingdom2GoldGainedTotal = oldTrade.Kingdom2GoldGainedTotal
                };
                change = new ClientReleaseAgreements(alliances, trades, allianceIndex, tradeIndex, oldAlliance, newAlliance, oldTrade, newTrade);
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Could not prepare friendly client release agreements: " + ex);
                return false;
            }
        }

        internal bool TryApply()
        {
            if (_allianceIndex >= _alliances.Count || _tradeIndex >= _trades.Count
                || !Equals(_alliances[_allianceIndex], _oldAlliance) || !_trades[_tradeIndex].Equals(_oldTrade)) return false;
            try
            {
                _alliances[_allianceIndex] = _newAlliance;
                _trades[_tradeIndex] = _newTrade;
                return true;
            }
            catch
            {
                Restore();
                throw;
            }
        }

        internal void Restore()
        {
            if (_allianceIndex < _alliances.Count && Equals(_alliances[_allianceIndex], _newAlliance)) _alliances[_allianceIndex] = _oldAlliance;
            if (_tradeIndex < _trades.Count && _trades[_tradeIndex].Equals(_newTrade)) _trades[_tradeIndex] = _oldTrade;
        }
    }
}
