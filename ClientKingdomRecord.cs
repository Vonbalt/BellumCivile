using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class ClientKingdomRecord
    {
        [SaveableField(1)] private string _clientKingdomId;
        [SaveableField(2)] private string _suzerainKingdomId;
        [SaveableField(3)] private float _startedDay;
        [SaveableField(4)] private bool _wasVoluntary;
        [SaveableField(5)] private float _liberationCooldownUntilDay;

        public string ClientKingdomId => _clientKingdomId;
        public string SuzerainKingdomId => _suzerainKingdomId;
        public float StartedDay => _startedDay;
        public bool WasVoluntary => _wasVoluntary;
        public float LiberationCooldownUntilDay => _liberationCooldownUntilDay;

        public ClientKingdomRecord(
            string clientKingdomId,
            string suzerainKingdomId,
            float startedDay,
            bool wasVoluntary,
            float liberationCooldownUntilDay)
        {
            _clientKingdomId = clientKingdomId ?? string.Empty;
            _suzerainKingdomId = suzerainKingdomId ?? string.Empty;
            _startedDay = startedDay;
            _wasVoluntary = wasVoluntary;
            _liberationCooldownUntilDay = liberationCooldownUntilDay;
        }
    }
}
