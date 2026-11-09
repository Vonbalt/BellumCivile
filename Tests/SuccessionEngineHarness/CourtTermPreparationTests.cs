using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtTermPreparationTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsLoading) { if (Data.TryGetValue(key, out var saved)) value = (T)saved; }
            else if (value is Dictionary<string, CourtTermPreparationRecord> terms)
                Data[key] = terms.ToDictionary(p => p.Key, p => p.Value == null ? null : new CourtTermPreparationRecord {
                    RealmId = p.Value.RealmId, StartDay = p.Value.StartDay, EndDay = p.Value.EndDay,
                    TermDays = p.Value.TermDays, DeliberationDays = p.Value.DeliberationDays });
            else Data[key] = value;
            return true;
        }
    }

    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly HashSet<Kingdom> Removed = new HashSet<Kingdom>(), Blocked = new HashSet<Kingdom>();
    private static readonly List<CourtTermPreparationRecord> Opened = new List<CourtTermPreparationRecord>();
    private static Clan _player;
    private static Kingdom _playerRealm;
    private static Hero _head;
    private static double _day;
    private static float _term;
    private static int _deliberation;
    private static bool _claimedBeforeOpen;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object target, string method, params object[] args) =>
        AccessTools.Method(target.GetType(), method).Invoke(target, args);
    private static Dictionary<string, CourtTermPreparationRecord> Queue(CourtAgendaBehavior behavior) =>
        (Dictionary<string, CourtTermPreparationRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_pendingCourtTerms").GetValue(behavior);
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Term(ref float __result) { __result = _term; return false; }
    private static bool Deliberation(ref int __result) { __result = _deliberation; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { __result = __instance == _player ? _playerRealm : null; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool Valid(Kingdom realm, ref bool __result) { __result = realm != null && Rulers.ContainsKey(realm) && !Removed.Contains(realm); return false; }
    private static bool Available(Kingdom realm, out Clan house, out Hero ruler, ref bool __result)
    { house = Rulers[realm]; ruler = _head; __result = !Removed.Contains(realm) && !Blocked.Contains(realm); return false; }
    private static bool Open(CourtAgendaBehavior __instance, Kingdom realm, CourtTermPreparationRecord term)
    {
        _claimedBeforeOpen &= !Queue(__instance).ContainsKey(realm.StringId) && !Blocked.Contains(realm) && !Removed.Contains(realm);
        Opened.Add(term);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.court-term-preparation");
        var type = typeof(CourtAgendaBehavior);
        var options = type.Assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        void Patch(Type target, string member, string prefix, bool getter = false) => harmony.Patch(
            getter ? AccessTools.PropertyGetter(target, member) : AccessTools.Method(target, member),
            prefix: new HarmonyMethod(typeof(CourtTermPreparationTests), prefix));
        void Prepare(CourtAgendaBehavior b) => Call(b, "PrepareQueuedCourtTerms", Realms, null, null);
        try
        {
            ticks.SetValue(null, 24000L);
            _day = 84.5; _term = 84; _deliberation = 5; _claimedBeforeOpen = true;
            Realms.Clear(); Rulers.Clear(); Removed.Clear(); Blocked.Clear(); Opened.Clear();
            _player = Blank<Clan>(); _head = Blank<Hero>();
            Patch(typeof(CampaignTime), "Now", nameof(Now), true);
            Patch(options, "CourtTermDays", nameof(Term), true);
            Patch(options, "PoliticalDeliberationDays", nameof(Deliberation), true);
            Patch(typeof(Clan), "PlayerClan", nameof(Player), true);
            Patch(typeof(Clan), "Kingdom", nameof(Realm), true);
            Patch(typeof(Kingdom), "RulingClan", nameof(Ruler), true);
            Patch(type, "ValidRealm", nameof(Valid));
            Patch(type, "TryGetCourtRuler", nameof(Available));
            Patch(type, "OpenTerm", nameof(Open));
            var behavior = new CourtAgendaBehavior();
            var dates = (Dictionary<string, CampaignTime>)AccessTools.Field(type, "_nextTerms").GetValue(behavior);
            for (int i = 0; i < 60; i++)
            {
                var realm = Blank<Kingdom>(); realm.StringId = "realm_" + i.ToString("D3");
                Realms.Add(realm); Rulers[realm] = Blank<Clan>(); dates[realm.StringId] = CampaignTime.Days(84);
                check((bool)Call(behavior, "QueueCourtTerm", realm), "A due court term is queued without preparing its agendas");
            }
            _playerRealm = Realms.Last(); Rulers[_playerRealm] = _player;
            check(Opened.Count == 0 && Queue(behavior).Count == 60 && dates.Values.All(d => d.ToDays == 168),
                "All official term boundaries advance immediately without synchronous agenda generation");
            var queued = Queue(behavior)[Realms[0].StringId];
            check(!(bool)Call(behavior, "QueueCourtTerm", Realms[0]) && ReferenceEquals(queued, Queue(behavior)[Realms[0].StringId]),
                "Repeated inspection cannot replace or duplicate pending term work");
            Prepare(behavior);
            check(Opened.Count == 5 && Opened[0].RealmId == _playerRealm.StringId && Queue(behavior).Count == 55 && _claimedBeforeOpen,
                "The player court opens first, using one of five slots, and each job is claimed before callbacks");
            Prepare(behavior);
            check(Opened.Count == 5, "Repeated daily ticks cannot drain extra batches on the same day");
            var store = new Store(); behavior.SyncData(store); store.IsLoading = true;
            var restored = new CourtAgendaBehavior(); restored.SyncData(store);
            check(Queue(restored).Count == 55 && Queue(restored).Values.All(t => t.StartDay == 84 && t.EndDay == 168
                && t.TermDays == 84 && t.DeliberationDays == 5), "Pending work and its fixed timing snapshots survive save/load");
            Prepare(restored);
            check(Opened.Count == 5, "Loading a save does not reset the daily batch guard or replay completed openings");
            _day = 85.5; _term = 21; _deliberation = 30;
            Prepare(restored);
            check(Opened.Count == 10 && Opened.All(t => t.EndDay == 168 && t.TermDays == 84 && t.DeliberationDays == 5),
                "New MCM timing settings do not rewrite terms already waiting in the queue");

            var waiting = Realms.First(r => Queue(restored).ContainsKey(r.StringId));
            var faction = Blank<FactionObject>();
            bool Pending(CourtAgendaRecord agenda, FactionObject owner = null) =>
                (bool)Call(restored, "AwaitingTermAnnouncement", waiting, owner, agenda);
            check(Pending(null) && Pending(new CourtAgendaRecord { State = CourtAgendaState.Completed }),
                "Empty and concluded NPC agendas show the pending-announcement placeholder");
            foreach (var state in new[] { CourtAgendaState.Announced, CourtAgendaState.Deliberating, CourtAgendaState.Voting,
                CourtAgendaState.AwaitingPlayerDecision, CourtAgendaState.AwaitingNomination, CourtAgendaState.Crisis,
                CourtAgendaState.PursuingObjective })
                check(!Pending(new CourtAgendaRecord { State = state }), "Live court business remains visible while the next term is queued: " + state);
            check((string)Call(restored, "DescribeAgendaEntry", waiting, faction, true, null)
                == "Current agenda: To be announced in the following days.", "The waiting announcement uses its localized display text");
            var originalRuler = Rulers[waiting]; Rulers[waiting] = _player;
            check(!Pending(null) && Pending(null, faction), "Player Crown actions are not replaced by an automatic agenda placeholder");
            Rulers[waiting] = originalRuler;
            Blocked.Add(waiting); _day = 86.5; Prepare(restored);
            check(Queue(restored).ContainsKey(waiting.StringId), "A realm awaiting a safe succession or transfer remains queued");
            Rulers[waiting] = Blank<Clan>(); Blocked.Clear(); _day = 87.5; Prepare(restored);
            check(!Queue(restored).ContainsKey(waiting.StringId), "Queued work revalidates and follows the current ruler after succession");
            var gone = Realms.First(r => Queue(restored).ContainsKey(r.StringId));
            Removed.Add(gone); _day = 88.5; Prepare(restored);
            check(!Queue(restored).ContainsKey(gone.StringId) && Opened.All(t => t.RealmId != gone.StringId),
                "Eliminated realms are pruned without opening their agendas");
            var unavailable = Realms.First(r => Queue(restored).ContainsKey(r.StringId));
            foreach (var realm in Realms) if (realm != unavailable) Removed.Add(realm);
            _day = 167.5; int before = Opened.Count; Prepare(restored);
            check(Queue(restored).Count == 0 && Opened.Count == before,
                "A court unavailable until its decision window is gone skips ordinary preparation without fabricating an immediate failure");
            var legacy = new CourtAgendaBehavior(); legacy.SyncData(new Store { IsLoading = true });
            check(Queue(legacy).Count == 0, "Old saves without queue data load with an empty preparation queue");
            foreach (var field in typeof(CourtTermPreparationRecord).GetFields())
                check(field.IsDefined(typeof(TaleWorlds.SaveSystem.SaveableFieldAttribute), false), "Pending term field is saveable: " + field.Name);
        }
        finally { harmony.UnpatchAll(harmony.Id); ticks.SetValue(null, oldTicks); }
    }
}
