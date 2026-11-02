using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal sealed class LordshipIncome : IDisposable
    {
        [ThreadStatic] private static LordshipIncome _current;
        private readonly LordshipIncome _previous;
        private readonly Clan _clan;
        private readonly bool _apply;
        private readonly bool _nested;
        private readonly ClanFinanceModel _model;
        private bool _redirected;
        private readonly Dictionary<Village, int> _income = new Dictionary<Village, int>();

        internal LordshipIncome(Clan clan, ClanFinanceModel model, bool apply)
        {
            _previous = _current;
            _nested = _previous != null && _previous._clan == clan;
            _clan = clan;
            _model = model;
            _apply = apply;
            if (clan != null && LordshipBehavior.Instance?.Ready == true)
                foreach (var village in clan.Villages)
                    _income[village] = model.CalculateVillageIncome(clan, village, false);
            _current = this;
        }

        internal static void Observe(Clan clan, Village village, int income)
        {
            if (_current != null && _current._clan == clan && village != null) _current._income[village] = income;
        }

        internal int Gross(Village village, ClanFinanceModel fallback) => village != null && _income.TryGetValue(village, out int value)
            ? value : fallback.CalculateVillageIncome(_clan, village, false);

        internal static int Net(int gross, bool contested) => Math.Max(0, gross + (contested
            ? (int)Math.Round(gross * BellumCivileConstants.FeudalTitleContestedTaxFactor) : 0));

        internal void Redirect(ref ExplainedNumber result)
        {
            if (_redirected || _nested || LordshipBehavior.Instance?.Ready != true) return;
            _redirected = true;
            foreach (var pair in _income)
            {
                var grant = LordshipBehavior.Instance.GetGrant(pair.Key.Settlement);
                if (grant == null || !grant.IsValid || grant.GrantingClan != _clan) continue;
                int amount = Net(pair.Value, FeudalTitlePenaltyHelper.IsBoundToContestedLegalTitle(pair.Key, out _));
                if (amount <= 0) continue;
                var label = new TextObject("{=BC_Lordship_IncomeAssigned}{VILLAGE}: revenues assigned to {HERO}")
                    .SetTextVariable("VILLAGE", pair.Key.Name).SetTextVariable("HERO", grant.Holder.Name);
                result.Add(-amount, label);
                if (_apply)
                {
                    if (grant.IsVassalGrant) LordshipBehavior.Instance.FundVassalIncome(grant, amount);
                    else GiveGoldAction.ApplyBetweenCharacters(null, grant.Holder, amount, true);
                }
            }
            LordshipBehavior.Instance.AddVassalIncome(_clan, _model, ref result, _apply);
        }

        public void Dispose()
        {
            _current = _previous;
            if (_nested)
                foreach (var pair in _income) _previous._income[pair.Key] = pair.Value;
        }
    }
}
