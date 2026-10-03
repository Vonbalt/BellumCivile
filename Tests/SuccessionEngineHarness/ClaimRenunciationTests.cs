using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class ClaimRenunciationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var titles = new FeudalTitleBehavior();
        var type = typeof(FeudalTitleBehavior);
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(type, "_titlesById").GetValue(titles);
        var claims = (List<FeudalClaimRecord>)AccessTools.Field(type, "_claims").GetValue(titles);
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        clan.StringId = "player";
        FeudalTitleRecord Title(string id, string legal = "other", string actual = "other", bool active = true)
            => new FeudalTitleRecord(id, id, FeudalTitleType.County, legal, actual, "", "", "", 0, 0, active);
        var target = Title("target");
        registry[target.TitleId] = target;
        bool Eligible(FeudalTitleRecord t) => (bool)AccessTools.Method(typeof(FeudalTitlePlayerActionService),
            "CanRenounceUncontrolledTitle").Invoke(null, new object[] { clan.StringId, t });
        check(Eligible(target), "Renunciation permits a title controlled by another house");
        check(!Eligible(Title("legal", "player")), "Renunciation excludes de-jure ownership");
        check(!Eligible(Title("actual", "other", "player")), "Renunciation excludes de-facto ownership");
        check(!Eligible(Title("inactive", active: false)) && !Eligible(null), "Renunciation excludes missing and inactive titles");
        FeudalClaimRecord Claim(string id, string house, string title, FeudalClaimStrength strength)
        {
            var claim = new FeudalClaimRecord(id, house, title, strength, "test", "", "", 0, -1);
            claims.Add(claim);
            return claim;
        }
        var weak = Claim("weak", "player", "target", FeudalClaimStrength.Weak);
        var strong = Claim("strong", "player", "target", FeudalClaimStrength.Strong);
        var otherTitle = Claim("elsewhere", "player", "child", FeudalClaimStrength.Strong);
        var otherHouse = Claim("foreign", "other", "target", FeudalClaimStrength.Strong);
        var revision = titles.RuntimeRevision;
        check(titles.TryRenounceExplicitClaim(clan, target, out var strength, out _), "Existing claims can be renounced");
        check(strength == FeudalClaimStrength.Strong && !weak.IsActive && !strong.IsActive,
            "Renunciation clears both strengths for the selected title");
        check(otherTitle.IsActive && otherHouse.IsActive, "Other titles and other houses keep their claims");
        check(titles.GetActiveClaims(clan, target).Count == 0 && titles.RuntimeRevision > revision,
            "Renunciation refreshes claim indexes and relation-cache revision");
        check(!titles.TryRenounceExplicitClaim(clan, target, out _, out _), "Repeated renunciation is a no-op");
        check(target.DeFactoHolderClanId == "other" && target.DeJureHolderClanId == "other",
            "Renunciation never transfers title ownership");
    }
}
