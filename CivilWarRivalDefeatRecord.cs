using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarRivalDefeatRecord
    {
        [SaveableField(1)] public WarScoreRecord Score;
        [SaveableField(2)] public CivilWarSideRecord Winner;
        [SaveableField(3)] public CivilWarSideRecord Loser;
        [SaveableField(4)] public Kingdom Crown;
        [SaveableField(5)] public List<Clan> LosingClans = new List<Clan>();
        [SaveableField(6)] public List<Clan> WinningClans = new List<Clan>();
        [SaveableField(7)] public List<Clan> RewardedClans = new List<Clan>();
        [SaveableField(8)] public SuccessionChallengeRecord Challenge;
        [SaveableField(9)] public int Stage;
        [SaveableField(10)] public bool Completed;
        [SaveableField(11)] public bool TribunalQueued;
        [SaveableField(12)] public string FiefSnapshot;
        [SaveableField(13)] public string Failure;
        [SaveableField(14)] public bool Announced;
        [SaveableField(15)] public Clan VictorHouse;

        internal bool Protects(Kingdom realm) => !Completed && Stage < 2 && realm != null
            && (realm == Crown || realm == Winner?.Realm || realm == Loser?.Realm);
        internal bool OwnsChallenge(SuccessionChallengeRecord challenge) => !Completed && challenge != null && Challenge == challenge;
    }
}
