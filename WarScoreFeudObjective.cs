using System;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public partial class WarScoreRecord
    {
        [SaveableField(35)] private int _feudObjectiveVerifiedController;
        [SaveableField(36)] private float _feudObjectiveControlSinceDay;

        internal bool UpdateFeudObjectiveControl(string claimantKingdomId, bool? claimantControls, float day)
        {
            if (!_isActive || _conflictType != WarScoreConflictType.ClaimFeud
                || _resolutionPending || _parleyPending && _parleyForced
                || float.IsNaN(day) || float.IsInfinity(day)
                || string.IsNullOrWhiteSpace(claimantKingdomId)
                || claimantKingdomId != _attackerKingdomId && claimantKingdomId != _defenderKingdomId)
                return false;

            // Zero is uninitialized (including legacy saves); 1 claimant, -1 holder, 2 neutral.
            int controller = claimantControls.HasValue ? (claimantControls.Value ? 1 : -1) : 2;
            if (_feudObjectiveVerifiedController != controller
                || day < _feudObjectiveControlSinceDay)
            {
                _feudObjectiveVerifiedController = controller;
                _feudObjectiveControlSinceDay = day;
            }

            float completedDays = (float)Math.Floor(Math.Max(0d, (double)day - _feudObjectiveControlSinceDay));
            float direction = claimantControls.HasValue ? (claimantControls.Value ? 1f : -1f) : 0f;
            if (claimantKingdomId == _defenderKingdomId) direction = -direction;
            _objectiveScore = direction * Math.Min(C.WarScoreFeudObjectiveCap,
                completedDays * C.WarScoreFeudObjectiveDailyGain);
            // Feuds use only objective time pressure, including when migrating old saves.
            _tickingScore = 0f;
            _lastTickDay = day;
            RecalculateReversibleScore();
            return true;
        }
    }
}
