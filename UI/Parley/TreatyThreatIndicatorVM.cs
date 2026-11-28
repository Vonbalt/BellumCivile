using Bannerlord.UIExtenderEx.Attributes;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyThreatIndicatorVM : ViewModel
    {
        private readonly BasicTooltipViewModel _hint;
        private bool _hintVisible;

        internal TreatyThreatIndicatorVM(TextObject realm, double current, DefensivePactTreatyThreat impact)
        {
            double after = impact?.Project(current) ?? current;
            ValueText = DefensivePactThreatHint.FormatValue(current) + " " + DefensivePactThreatHint.FormatChange(current, after);
            _hint = new BasicTooltipViewModel(() => DefensivePactThreatHint.Build(realm, current, after));
        }

        internal TreatyThreatIndicatorVM()
        {
            ValueText = "?";
            _hint = new BasicTooltipViewModel(DefensivePactThreatHint.Unavailable);
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
