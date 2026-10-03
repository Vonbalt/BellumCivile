using System.Collections.Generic;
using System.Linq;
using BellumCivile.WarPeace;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class ActiveForeignWarRecord
    {
        [SaveableField(1)] private string _warKey;
        [SaveableField(2)] private string _attackerKingdomId;
        [SaveableField(3)] private string _defenderKingdomId;
        [SaveableField(4)] private ForeignPolicyMotive _declaredMotive;
        [SaveableField(5)] private string _sponsorFactionType;
        [SaveableField(6)] private string _sponsorClanId;
        [SaveableField(7)] private List<string> _targetTitleIds;
        [SaveableField(8)] private float _startingAttackerStrength;
        [SaveableField(9)] private float _startingDefenderStrength;
        [SaveableField(10)] private float _startedDay;
        [SaveableField(11)] private float _contextCreatedDay;
        [SaveableField(12)] private int _publicDeclarationMotiveType;
        [SaveableField(13)] private TextObject _publicDeclarationMotiveSubject;
        [SaveableField(14)] private bool _hasPublicDeclarationMotive;

        public string WarKey => _warKey;
        public string AttackerKingdomId => _attackerKingdomId;
        public string DefenderKingdomId => _defenderKingdomId;
        public ForeignPolicyMotive DeclaredMotive => _declaredMotive;
        public string SponsorFactionType => _sponsorFactionType;
        public string SponsorClanId => _sponsorClanId;
        public IReadOnlyList<string> TargetTitleIds => _targetTitleIds ?? (IReadOnlyList<string>)new List<string>();
        public float StartingAttackerStrength => _startingAttackerStrength;
        public float StartingDefenderStrength => _startingDefenderStrength;
        public float StartedDay => _startedDay;
        public float ContextCreatedDay => _contextCreatedDay;
        internal bool HasPublicDeclarationMotive => _hasPublicDeclarationMotive;
        internal WarTargetMotiveType PublicDeclarationMotiveType =>
            (WarTargetMotiveType)_publicDeclarationMotiveType;
        internal TextObject PublicDeclarationMotiveSubject =>
            _publicDeclarationMotiveSubject ?? TextObject.GetEmpty();

        public ActiveForeignWarRecord(
            string warKey,
            string attackerKingdomId,
            string defenderKingdomId,
            ForeignPolicyMotive declaredMotive,
            string sponsorFactionType,
            string sponsorClanId,
            IEnumerable<string> targetTitleIds,
            float startingAttackerStrength,
            float startingDefenderStrength,
            float startedDay,
            float contextCreatedDay)
        {
            _warKey = warKey ?? string.Empty;
            _attackerKingdomId = attackerKingdomId ?? string.Empty;
            _defenderKingdomId = defenderKingdomId ?? string.Empty;
            _declaredMotive = declaredMotive;
            _sponsorFactionType = sponsorFactionType ?? string.Empty;
            _sponsorClanId = sponsorClanId ?? string.Empty;
            _targetTitleIds = targetTitleIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList() ?? new List<string>();
            _startingAttackerStrength = startingAttackerStrength;
            _startingDefenderStrength = startingDefenderStrength;
            _startedDay = startedDay;
            _contextCreatedDay = contextCreatedDay;
        }

        public void MarkStarted(float startedDay, float attackerStrength, float defenderStrength)
        {
            _startedDay = startedDay;
            _startingAttackerStrength = attackerStrength;
            _startingDefenderStrength = defenderStrength;
        }

        internal void SetPublicDeclarationMotive(WarTargetMotive motive)
        {
            if (motive == null)
                return;

            _publicDeclarationMotiveType = (int)motive.Type;
            _publicDeclarationMotiveSubject = motive.Subject ?? TextObject.GetEmpty();
            _hasPublicDeclarationMotive = true;
        }
    }
}
