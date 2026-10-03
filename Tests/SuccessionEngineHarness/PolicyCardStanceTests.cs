using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PolicyCardStanceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(CourtPolicyStance).Assembly;
        var configType = assembly.GetType("BellumCivile.IdeologyPolicyAgendaConfig");
        var singleton = AccessTools.Field(configType, "_instance");
        var original = singleton.GetValue(null);
        try
        {
            var config = Activator.CreateInstance(configType, true);
            singleton.SetValue(null, config);
            var stances = (Dictionary<FactionType, Dictionary<string, CourtPolicyStance>>)AccessTools.Field(configType, "_stances").GetValue(config);
            stances[FactionType.Nobility]["test_policy"] = CourtPolicyStance.Support;
            stances[FactionType.Liberty]["test_policy"] = CourtPolicyStance.Oppose;
            var policy = (PolicyObject)FormatterServices.GetUninitializedObject(typeof(PolicyObject));
            policy.StringId = "test_policy";
            var mixin = assembly.GetType("BellumCivile.ViewModelMixin.KingdomPolicyFactionAgendaMixin");
            CourtPolicyStance Resolve(bool crown, FactionType? faction) => (CourtPolicyStance)AccessTools.Method(mixin, "PolicyStance")
                .Invoke(null, new object[] { policy, crown, faction, 0f });
            check(Resolve(false, FactionType.Nobility) == CourtPolicyStance.Support, "Supported policy uses faction support stance");
            check(Resolve(false, FactionType.Liberty) == CourtPolicyStance.Oppose, "Changing faction resolves opposition for the same policy");
            check(Resolve(false, FactionType.Glory) == CourtPolicyStance.Neutral, "Unlisted policy is neutral rather than opposed");
            check(Resolve(false, null) == CourtPolicyStance.Neutral, "Leaving court factions clears policy tint");
            check(Resolve(true, FactionType.Liberty) == CourtPolicyStance.Neutral, "Crown does not inherit faction opposition");
            ((HashSet<string>)AccessTools.Field(configType, "_crown").GetValue(config)).Add(policy.StringId);
            check(Resolve(true, FactionType.Liberty) == CourtPolicyStance.Support, "Crown policy highlight retains independent Crown roster");
            foreach (string command in new[] { "ExecuteBeginPolicyStanceHint", "ExecuteEndPolicyStanceHint" })
                check(AccessTools.Method(mixin, command).GetCustomAttributesData()
                    .Any(a => a.AttributeType.Name == "DataSourceMethodAttribute"), "Policy hover command is exported to the UI binding system: " + command);
            foreach (string name in new[] { "ActivePolicyFactionAgendaPrefabExtension", "OtherPolicyFactionAgendaPrefabExtension" })
            {
                var type = assembly.GetType("BellumCivile.ViewModelMixin." + name);
                var doc = (XmlDocument)AccessTools.Method(type, "GetPrefabExtension").Invoke(Activator.CreateInstance(type, true), null);
                var root = doc.DocumentElement;
                check(root.GetAttribute("Command.Click") == "OnSelect" && root.GetAttribute("IsSelected") == "@IsSelected",
                    name + " retains native selection");
                var blue = (XmlElement)root.SelectSingleNode("Children/Widget[@IsVisible='@IsPlayerFactionAgendaPolicy']");
                var red = (XmlElement)root.SelectSingleNode("Children/Widget[@IsVisible='@IsPlayerFactionOpposedPolicy']");
                check(blue?.GetAttribute("Color") == "#315F9BFF" && red?.GetAttribute("Color") == "#9B3131FF",
                    name + " binds separate blue support and red opposition overlays");
                check(blue?.GetAttribute("DoNotAcceptEvents") == "true" && red?.GetAttribute("DoNotAcceptEvents") == "true",
                    name + " overlays cannot intercept card clicks");
            }
        }
        finally { singleton.SetValue(null, original); }
    }
}
