using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal readonly struct MarriageFundingDiagnostic
    {
        private readonly string _first, _second, _payer, _payee;
        private readonly int _dowry, _dueNow;
        private readonly int? _available;
        private readonly bool _deposit;
        internal float Score { get; }

        internal MarriageFundingDiagnostic(Hero first, Hero second, Clan payer, Clan payee,
            float score, int dowry, int dueNow, bool deposit)
        {
            _first = first?.StringId;
            _second = second?.StringId;
            _payer = payer?.StringId;
            _payee = payee?.StringId;
            _dowry = dowry;
            _dueNow = dueNow;
            _available = MarriagePayments.Wallet(payer)?.Gold;
            _deposit = deposit;
            Score = score;
        }

        internal void Trace(Clan sponsor)
        {
            if (!BellumCivileDebug.ShowInGameMessages) return;
            int? shortfall = _available.HasValue ? System.Math.Max(0, _dueNow - _available.Value) : (int?)null;
            string blocker = !_available.HasValue ? "no_payer_wallet"
                : shortfall > 0 ? "insufficient_gold" : "payee_cannot_receive";
            BellumCivileDebug.Trace("marriage",
                $"willing match blocked by funding; search_house={sponsor?.StringId}; kind={(_deposit ? "betrothal" : "marriage")}; "
                + $"first={_first}; second={_second}; score={Score:0.##}; payer={_payer}; payee={_payee}; dowry={_dowry}; "
                + $"due_now={_dueNow}; payer_gold={_available?.ToString() ?? "unavailable"}; shortfall={shortfall?.ToString() ?? "unavailable"}; blocker={blocker}.",
                requestInGameDisplay: true);
        }
    }
}
