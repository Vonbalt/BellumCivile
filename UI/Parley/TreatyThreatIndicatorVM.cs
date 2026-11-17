using System;
using System.Globalization;
using Bannerlord.UIExtenderEx.Attributes;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyThreatIndicatorVM : ViewModel
    {
        private readonly HintViewModel _hint;
        private bool _hintVisible;

        internal TreatyThreatIndicatorVM(TextObject realm, double current, DefensivePactTreatyThreat impact)
        {
            double before = Math.Round(current, 2, MidpointRounding.AwayFromZero);
            double after = Math.Round(impact?.Project(current) ?? current, 2, MidpointRounding.AwayFromZero);
            double change = Math.Round(after - before, 2, MidpointRounding.AwayFromZero);
            string sign = change >= 0 ? "+" : "-";
            ValueText = before.ToString("0.##", CultureInfo.CurrentCulture) + " " + sign
                + Math.Abs(change).ToString("0.##", CultureInfo.CurrentCulture);
            _hint = new HintViewModel(DefensivePactThreatHint.Build(TreatyTermVM.BuildThreatForecast(realm, current, impact)));
        }

        internal TreatyThreatIndicatorVM()
        {
            ValueText = "?";
            _hint = new HintViewModel(new TextObject("{=BC_DefensivePact_ForecastUnavailable}Treaty threat forecast unavailable."));
        }

        [DataSourceProperty] public string ValueText { get; }

        [DataSourceMethod]
        public void ExecuteBeginHint()
        {
            if (_hintVisible) return;
            _hintVisible = true;
            _hint.ExecuteBeginHint();
        }

        [DataSourceMethod]
        public void ExecuteEndHint()
        {
            if (!_hintVisible) return;
            _hintVisible = false;
            _hint.ExecuteEndHint();
        }
    }
}
