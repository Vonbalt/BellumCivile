using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtTribunalMemberReaction
    {
        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public int Bloc = -1;
        [SaveableField(3)] public int Amount;
        [SaveableField(4)] public bool Settled;
        [SaveableField(5)] public string ExecutionId;
    }

    public sealed class CourtTribunalReactionRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public List<CourtTribunalMemberReaction> Members = new List<CourtTribunalMemberReaction>();
        [SaveableField(4)] public bool Closed;
        [SaveableField(5)] public bool Clemency;
        [SaveableField(6)] public bool Applied;

        internal bool Ready => Closed && !Applied && !Members.Any(m => !string.IsNullOrEmpty(m.ExecutionId));
        internal int MemberTotal(FactionType bloc) => Math.Max(-30, Math.Min(30,
            Members.Where(m => m.Settled && m.Bloc == (int)bloc).Sum(m => m.Amount)));

        internal void Settle(CourtTribunalMemberReaction member, int amount, bool pardon = false)
        {
            if (Applied || member == null || member.Settled) return;
            member.Amount = amount;
            member.Settled = true;
            member.ExecutionId = null;
            Clemency |= pardon;
        }
    }
}
