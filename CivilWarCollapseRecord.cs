using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarCollapseRecord
    {
        [SaveableField(1)] public string SuccessorId;
        [SaveableField(2)] public CivilWarConflictRecord Conflict;
        [SaveableField(3)] public Kingdom Parent;
        [SaveableField(4)] public FactionObject Winner;
        [SaveableField(5)] public Kingdom WinnerRealm;
        [SaveableField(6)] public Clan WinnerHouse;
        [SaveableField(7)] public Kingdom Successor;
        [SaveableField(8)] public bool Initialized;
        [SaveableField(9)] public CivilWarCrownTransferRecord Transfer;
        [SaveableField(10)] public int Stage;
        [SaveableField(11)] public bool Completed;
        [SaveableField(12)] public bool RewardsApplied;
        [SaveableField(13)] public List<Clan> WinnerClans = new List<Clan>();
        [SaveableField(14)] public List<Kingdom> ExternalEnemies = new List<Kingdom>();
        [SaveableField(15)] public string Failure;
        [SaveableField(16)] public bool Announced;
        [SaveableField(17)] public SuccessionChallengeRecord Challenge;
        [SaveableField(18)] public bool InfluenceRestored;
        [SaveableField(19)] public List<CivilWarPairTransferRecord> NativePairs = new List<CivilWarPairTransferRecord>();
        [SaveableField(20)] public CivilWarRivalPromotionRecord Promotion;

        internal bool OwnsChallenge(SuccessionChallengeRecord challenge) => !Completed
            && challenge != null && Challenge == challenge;

        // Stage 3 begins cleanup only after all surviving wars have committed.
        internal bool Protects(Kingdom realm) => !Completed && Stage < 3 && realm != null
            && (realm == Parent || realm == WinnerRealm || realm == Successor
                || !string.IsNullOrEmpty(SuccessorId) && realm.StringId == SuccessorId
                || Conflict.Sides.Exists(s => !s.Closed && s.Realm == realm));
        internal bool ProtectsScore(WarScoreRecord score) => !Completed && Stage < 3 && score != null
            && Conflict.Pairs.Exists(p => !p.Closed && p.Score == score);
    }
}
