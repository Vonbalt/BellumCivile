using System.Collections.Generic;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class ConflictOutcomeNotice
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public bool PlayerInvolved;
        [SaveableField(3)] public bool ChatAllowed;
        [SaveableField(4)] public bool Ready;
        [SaveableField(5)] public bool Acknowledged;
        [SaveableField(6)] public string Title;
        [SaveableField(7)] public string Chat;
        [SaveableField(8)] public string Body;
        [SaveableField(9)] public Dictionary<string, string> Names = new Dictionary<string, string>();
    }
}
