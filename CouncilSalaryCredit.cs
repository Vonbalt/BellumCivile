using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CouncilSalaryCredit
    {
        [SaveableField(1)] public Kingdom Realm;
        [SaveableField(2)] public Clan Recipient;
        [SaveableField(3)] public PrivyCouncilOffice Office;
        [SaveableField(4)] public int Amount;
    }
}
