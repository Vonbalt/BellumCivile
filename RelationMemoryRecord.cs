using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class RelationMemoryRecord
    {
        [SaveableField(1)] private RelationMemoryScope _scope;
        [SaveableField(2)] private string _firstId;
        [SaveableField(3)] private string _secondId;
        [SaveableField(4)] private string _sourceId;
        [SaveableField(5)] private string _contextText;
        [SaveableField(6)] private int _value;
        [SaveableField(7)] private float _startDay;
        [SaveableField(8)] private float _expiryDay;
        [SaveableField(9)] private float _legacyWeeklyDecay;
        [SaveableField(10)] private string _eventId;

        public RelationMemoryScope Scope => _scope;
        public string FirstId => _firstId ?? string.Empty;
        public string SecondId => _secondId ?? string.Empty;
        public string SourceId => _sourceId ?? string.Empty;
        public string ContextText => _contextText ?? string.Empty;
        public int Value => _value;
        public float StartDay => _startDay;
        public float ExpiryDay => _expiryDay;
        public float LegacyWeeklyDecay => _legacyWeeklyDecay;
        public string EventId => _eventId ?? string.Empty;
        public string PairKey => BuildPairKey(FirstId, SecondId);

        public RelationMemoryRecord(
            RelationMemoryScope scope,
            string firstId,
            string secondId,
            string sourceId,
            string contextText,
            int value,
            float startDay,
            float expiryDay,
            float legacyWeeklyDecay = 0f,
            string eventId = null)
        {
            _scope = scope;
            if (string.CompareOrdinal(firstId, secondId) <= 0)
            {
                _firstId = firstId ?? string.Empty;
                _secondId = secondId ?? string.Empty;
            }
            else
            {
                _firstId = secondId ?? string.Empty;
                _secondId = firstId ?? string.Empty;
            }

            _sourceId = sourceId ?? string.Empty;
            _contextText = contextText ?? string.Empty;
            _value = value;
            _startDay = startDay;
            _expiryDay = expiryDay;
            _legacyWeeklyDecay = Math.Max(0f, legacyWeeklyDecay);
            _eventId = eventId ?? string.Empty;
        }

        public int GetCurrentValue(float currentDay)
        {
            if (IsExpired(currentDay))
                return 0;

            if (_legacyWeeklyDecay <= 0f || _value == 0)
                return _value;

            float elapsedWeeks = Math.Max(0f, (currentDay - _startDay) / 7f);
            int faded = (int)Math.Floor(elapsedWeeks * _legacyWeeklyDecay);
            if (_value > 0)
                return Math.Max(0, _value - faded);

            return Math.Min(0, _value + faded);
        }

        public bool IsExpired(float currentDay)
        {
            return _expiryDay >= 0f && currentDay >= _expiryDay;
        }

        public static float NormalizeDurationMultiplier(float multiplier)
        {
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f
                ? 1f : Math.Max(0.25f, Math.Min(5f, multiplier));
        }

        public void RescaleRemainingDuration(float currentDay, float ratio)
        {
            if (_expiryDay < 0f || IsExpired(currentDay) || GetCurrentValue(currentDay) == 0
                || ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio == 1f)
                return;

            _expiryDay = currentDay + (_expiryDay - currentDay) * ratio;
            if (_legacyWeeklyDecay > 0f)
            {
                // Preserve the amount already faded while changing the speed of future fading.
                _startDay = currentDay - Math.Max(0f, currentDay - _startDay) * ratio;
                _legacyWeeklyDecay /= ratio;
            }
        }

        public void Merge(int additionalValue, float expiryDay)
        {
            _value += additionalValue;
            if (_expiryDay < 0f || expiryDay < 0f)
                _expiryDay = -1f;
            else
                _expiryDay = Math.Max(_expiryDay, expiryDay);
        }

        public static string BuildPairKey(string firstId, string secondId)
        {
            firstId = firstId ?? string.Empty;
            secondId = secondId ?? string.Empty;
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }
    }
}
