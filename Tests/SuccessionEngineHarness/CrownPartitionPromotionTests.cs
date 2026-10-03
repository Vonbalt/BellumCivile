using System;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionPromotionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var record = new CrownPartitionPromotionRecord();
        bool Call(string name, params object[] args) => (bool)AccessTools.Method(typeof(CrownPartitionPromotionRecord), name).Invoke(record, args);
        check(!Call("TryBeginFounderCreation"), "Founder creation requires reserved identity");
        record.FounderId = "cadet";
        check(Call("TryBeginFounderCreation") && !Call("TryBeginFounderCreation"), "Founder creation starts once");
        var reload = new CrownPartitionPromotionRecord();
        foreach (var field in typeof(CrownPartitionPromotionRecord).GetFields()) field.SetValue(reload, field.GetValue(record));
        record = reload;
        check(!Call("TryBeginFounderCreation"), "Reconstructed started founder receipt cannot repeat creation");
        check(!Call("TryBeginRealmCreation"), "Realm creation requires verified founder");
        record.Founder = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        record.FounderVerified = true;
        check(!Call("TryBeginRealmCreation"), "Realm creation requires reserved shell identity");
        record.SuccessorId = "successor";
        check(Call("TryBeginRealmCreation") && !Call("TryBeginRealmCreation"), "Realm creation cannot repeat after interruption");
        check(!Call("TryComplete", true), "Creation receipts alone cannot complete promotion");
        record.Successor = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        record.Parent = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        record.RetainedHouse = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        record.Heir = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        record.CrownId = "crown";
        AccessTools.Property(typeof(Clan), "StringId").SetValue(record.Founder, "cadet");
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(record.Successor, "successor");
        record.FounderInitializationCompleted = record.RealmInitializationCompleted = true;
        record.TransferDiplomacyPrepared = true;
        record.GovernmentStarted = record.GovernmentReturned = record.GovernmentVerified = true;
        record.CrownRegistrationStarted = record.CrownRegistrationReturned = true;
        record.HierarchyStarted = record.HierarchyReturned = true;
        record.CourtFinalizationStarted = record.CourtFinalizationReturned = true;
        record.PoliticalParentTargets = new System.Collections.Generic.Dictionary<string, string> { { "crown", "" } };
        record.RealmVerified = record.EstateDeliveryStarted = record.EstateVerified = record.HierarchyVerified = record.ObligationsSettled = true;
        check(!Call("TryComplete", true), "Missing membership and title snapshots block completion");
        record.Titles.Add(new RealmUnionTitleRecord { TitleId = "crown" });
        var receipt = new RealmUnionClanRecord { Clan = record.Founder,
            ActionStarted = true, ActionCompleted = true, RestorationStarted = true, RestorationCompleted = true };
        record.Houses.Add(new CrownPartitionHouseRecord { Transfer = receipt, MovementReturned = true,
            RestorationReturned = true, PostMoveBalancesCaptured = true });
        check(!Call("TryComplete", false), "Current-world verification required in addition to receipts");
        receipt.RestorationCompleted = false;
        check(!Call("TryComplete", true), "Unrestored house blocks completion");
        receipt.RestorationCompleted = true; receipt.EndMercenaryContract = true;
        check(!Call("TryComplete", true), "Promotion does not silently terminate a mercenary contract");
        receipt.EndMercenaryContract = false;
        record.EstateTitleIds.Add("crown");
        record.EstateReceipts = new System.Collections.Generic.List<CrownEstateDeliveryRecord>
        {
            new CrownEstateDeliveryRecord { AssetId = "crown", IsTitle = true, Started = true, ActionReturned = true, Verified = true }
        };
        record.CrownRegistrationReturned = false;
        check(!Call("TryComplete", true), "Incomplete Crown registration blocks completion");
        record.CrownRegistrationReturned = true;
        record.HierarchyReturned = false;
        check(!Call("TryComplete", true), "Incomplete hierarchy callback blocks promotion completion");
        record.HierarchyReturned = true;
        record.CourtFinalizationReturned = false;
        check(!Call("TryComplete", true), "Incomplete court callback blocks promotion completion");
        record.CourtFinalizationReturned = true;
        record.PoliticalParentTargets.Clear();
        check(!Call("TryComplete", true), "Missing final political manifest blocks promotion completion");
        record.PoliticalParentTargets.Add("crown", "");
        check(Call("TryComplete", true), "Verified promotion can complete");
        var identity = new CrownPartitionPromotionRecord
        {
            FounderId = "cadet", SuccessorId = "successor", Parent = record.Parent, Heir = record.Heir,
            RetainedHouse = record.RetainedHouse, FounderCreationStarted = true
        };
        AccessTools.Field(typeof(Clan), "_leader").SetValue(record.Founder, record.Heir);
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(record.Founder, record.Parent);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(record.Heir, record.Founder);
        bool Recover(string method, object candidate) => (bool)AccessTools.Method(typeof(CrownPartitionPromotionRecord), method)
            .Invoke(identity, new[] { candidate });
        check(!Recover("TryRecoverFounder", record.Founder), "Registered founder without initialization receipt is not adopted");
        identity.FounderInitializationCompleted = true;
        check(Recover("TryRecoverFounder", record.Founder) && identity.Founder == record.Founder,
            "Fully initialized reserved founder can be adopted without repeating events");
        check(Recover("TryRecoverFounder", record.Founder), "Founder recovery is repeatable without side effects");
        identity.FounderId = "other";
        check(!Recover("TryRecoverFounder", record.Founder), "Wrong founder identity cannot be adopted");
        identity.FounderId = "cadet";
        record.Successor.RulingClan = record.Founder;
        identity.RealmCreationStarted = true;
        check(!Recover("TryRecoverRealm", record.Successor), "Bare successor shell cannot be mistaken for initialized kingdom");
        identity.RealmInitializationCompleted = true;
        check(Recover("TryRecoverRealm", record.Successor) && identity.Successor == record.Successor,
            "Initialized reserved shell is recovered without native initialization replay");
        record.Successor.RulingClan = record.RetainedHouse;
        check(!Recover("TryRecoverRealm", record.Successor), "Wrong ruling house blocks shell adoption");
        record.Houses.Add(record.Houses[0]);
        check(!Call("TryComplete", true), "Duplicate house receipts cannot prove completion");
        var pending = (PendingPartitionSuccessionRecord)FormatterServices.GetUninitializedObject(typeof(PendingPartitionSuccessionRecord));
        check(pending.CrownPromotions == null, "Older partition records do not invent promotion journals");
        var attr = typeof(PendingPartitionSuccessionRecord).GetField("CrownPromotions").GetCustomAttributesData()[0];
        check(Convert.ToInt32(attr.ConstructorArguments[0].Value) == 10, "Promotion journals append pending partition field 10");
    }
}
