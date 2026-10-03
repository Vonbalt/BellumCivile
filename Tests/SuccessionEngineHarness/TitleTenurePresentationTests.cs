using System;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class TitleTenurePresentationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var helper = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper");
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); clan.StringId = "house";
        FeudalTitleRecord Title(string legal, string actual) => new FeudalTitleRecord(
            "title", "Title", FeudalTitleType.County, legal, actual, "", "", "", 0, 0);
        string Held(FeudalTitleRecord title) => (string)AccessTools.Method(helper, "GetHeldTitleStatus").Invoke(null, new object[] { clan, title });
        string Hierarchy(FeudalTitleRecord title) => (string)AccessTools.Method(helper, "GetHierarchyTenure").Invoke(null, new object[] { title });
        check(Held(Title("house", "other")) == "de jure only", "Legal-only encyclopedia ownership uses explicit de jure suffix");
        check(Held(Title("other", "house")) == "de facto only", "Possession-only encyclopedia ownership uses explicit de facto suffix");
        check(Held(Title("house", "house")) == "", "Full encyclopedia ownership remains unmarked");
        check(Held(Title("other", "other")) == "" && Held(null) == "", "Unheld or missing title gets no ownership suffix");
        check(Hierarchy(Title("house", "house")) == "De facto and de jure", "Hierarchy full tenure uses explicit combined ownership");
        check(Hierarchy(Title("house", "other")) == "de facto only", "Split hierarchy tenure describes the current possessor");
        check(Hierarchy(Title("house", "")) == "de jure only", "Legal holder without a possessor is de jure only");
        check(Hierarchy(Title("", "house")) == "de facto only", "Possessor without a legal holder is de facto only");
        check(Hierarchy(Title("", "")) == "Vacant" && Hierarchy(null) == "Vacant", "Wholly vacant and absent titles remain vacant");
    }
}
