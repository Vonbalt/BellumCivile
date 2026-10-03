using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static class CadetRegencyFoundingTests
{
    private static Clan _clan, _heroClan;
    private static Hero _leader;
    private static int _foundings, _successions;
    private static bool _alive;
    private static bool Head(ref Hero __result) { __result = _leader; return false; }
    private static bool House(ref Clan __result) { __result = _heroClan; return false; }
    private static bool Alive(ref bool __result) { __result = _alive; return false; }
    private static bool Found(Hero leader) { _foundings++; _leader = leader; return false; }
    private static bool Succeed(Clan clan, Hero newLeader)
    {
        _successions++;
        if (_leader == null) throw new NullReferenceException("Native succession requires an old leader");
        _leader = newLeader; return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        _clan = Blank<Clan>(); _clan.StringId = "bc_partition_estate_regression";
        _heroClan = _clan; _leader = null; _alive = true; _foundings = _successions = 0;
        var regent = Blank<Hero>(); regent.StringId = "regent";
        var ward = Blank<Hero>(); ward.StringId = "ward";
        var install = AccessTools.Method(typeof(RegencyBehavior), "TryInstallClanLeader");
        var preserve = AccessTools.Method(typeof(PartitionSuccessionBehavior), "PreservePendingCadetHead");
        bool Install() => (bool)install.Invoke(null, new object[] { _clan, regent, "test founding" });
        void Preserve() => preserve.Invoke(null, new object[] { _clan, ward });
        var h = new Harmony("bellum.test.cadet_regency_founding");
        void Patch(MethodBase target, string name) => h.Patch(target,
            prefix: new HarmonyMethod(typeof(CadetRegencyFoundingTests), name));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            bool nativeFailed = false;
            try { ChangeClanLeaderAction.ApplyWithSelectedNewLeader(_clan, regent); }
            catch (NullReferenceException) { nativeFailed = true; }
            check(nativeFailed, "Actual native succession action reproduces the leaderless founding exception");
            Patch(AccessTools.Method(typeof(Clan), "SetLeader"), nameof(Found));
            Patch(AccessTools.Method(typeof(ChangeClanLeaderAction), "ApplyWithSelectedNewLeader"), nameof(Succeed));
            check(Install() && _leader == regent && _foundings == 1 && _successions == 0,
                "Leaderless cadet installs founding regent without invoking predecessor succession");
            check(Install() && _foundings == 1 && _successions == 0, "Repeated founding is idempotent");
            _leader = ward;
            check(Install() && _successions == 1 && _leader == regent, "Existing leader retains native succession behavior");
            _leader = null;
            Preserve();
            check(_leader == ward, "Failed regent preparation retains ward as provisional native clan head");
            Preserve();
            check(_foundings == 2, "Pending retry does not repeat founding assignment");
            check(Install() && _leader == regent && _successions == 2, "Pending ward head can subsequently yield to a regent");
            Preserve();
            check(_leader == regent, "Pending recovery never overwrites an installed regent");
            _leader = null; _heroClan = Blank<Clan>();
            Preserve();
            check(_leader == null && !Install(), "Recovery never steals an heir or regent from another house");
            _heroClan = _clan; _alive = false;
            Preserve();
            check(_leader == null && !Install(), "Dead heroes cannot become founding or provisional leaders");
            _alive = true;
            var partition = new PartitionSuccessionBehavior();
            var share = new CrossClanEstateShare { CadetPlan = new CrownAccessionRecord { Cadet = _clan, Heir = ward } };
            AccessTools.Field(typeof(PartitionSuccessionBehavior), "_crossClanEstates").SetValue(partition,
                new List<CrossClanEstateRecord> { new CrossClanEstateRecord { Shares = new List<CrossClanEstateShare> { share } } });
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "RestorePendingEstateHeads").Invoke(partition, null);
            check(_leader == ward && !share.Completed && !share.LandedSettled,
                "Loaded pending estate regains a provisional head without marking inheritance delivered");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
