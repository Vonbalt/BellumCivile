using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class WarScoreEventRecord
    {
        [SaveableField(1)] private string _eventId;
        [SaveableField(2)] private WarScoreEventType _eventType;
        [SaveableField(3)] private float _day;
        [SaveableField(4)] private float _delta;
        [SaveableField(5)] private float _scoreAfter;
        [SaveableField(6)] private string _actorKingdomId;
        [SaveableField(7)] private string _targetKingdomId;
        [SaveableField(8)] private string _settlementId;
        [SaveableField(9)] private string _heroId;
        [SaveableField(10)] private string _debugText;

        public string EventId => _eventId;
        public WarScoreEventType EventType => _eventType;
        public float Day => _day;
        public float Delta => _delta;
        public float ScoreAfter => _scoreAfter;
        public string ActorKingdomId => _actorKingdomId;
        public string TargetKingdomId => _targetKingdomId;
        public string SettlementId => _settlementId;
        public string HeroId => _heroId;
        public string DebugText => _debugText;

        internal void ReverseScorePerspective()
        {
            _delta = -_delta;
            _scoreAfter = -_scoreAfter;
        }

        public WarScoreEventRecord(
            WarScoreEventType eventType,
            float day,
            float delta,
            float scoreAfter,
            string actorKingdomId,
            string targetKingdomId,
            string settlementId = "",
            string heroId = "",
            string debugText = "")
        {
            _eventId = Guid.NewGuid().ToString("N");
            _eventType = eventType;
            _day = day;
            _delta = delta;
            _scoreAfter = scoreAfter;
            _actorKingdomId = actorKingdomId ?? string.Empty;
            _targetKingdomId = targetKingdomId ?? string.Empty;
            _settlementId = settlementId ?? string.Empty;
            _heroId = heroId ?? string.Empty;
            _debugText = debugText ?? string.Empty;
        }
    }
}
