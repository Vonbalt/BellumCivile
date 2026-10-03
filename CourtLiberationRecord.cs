using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtLiberationRecord
    {
        [SaveableField(1)] public Kingdom Suzerain;
        [SaveableField(2)] public Hero Ruler;
        [SaveableField(3)] public ClientKingdomRecord Clientage;
        [SaveableField(4)] public bool Activated;
        [SaveableField(5)] public double ActivatedDay;
        [SaveableField(6)] public bool WarConfirmed;
        [SaveableField(7)] public bool Initiated;
        [SaveableField(8)] public double WarDay;
    }
}
