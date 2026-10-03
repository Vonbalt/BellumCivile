using System;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtAppeasementTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int houses in new[] { 2, 4, 8, 12 })
            check(CourtAppeasementRules.Cost(houses) == 100 + 25 * houses, "Accommodation price counts houses");
        foreach (float mood in new[] { -100f, -80, -60, -40, -20, 0, 80, 90, 100 })
        {
            float effective = CourtAppeasementRules.Effective(mood, 20);
            check(effective == Math.Min(100, mood + 20), "Accommodation effective mood is capped");
            check(CourtAppeasementRules.SetEffective(mood, effective, 20) == mood, "Reading and reassigning capped mood preserves underlying value");
            check(CourtAppeasementRules.Effective(mood, 0) == mood, "Expiry reveals original underlying mood without subtraction");
        }
        check(CourtAppeasementRules.SetEffective(-60, -45, 20) == -65, "Negative shock adjusts underlying mood while accommodation persists");
        check(CourtAppeasementRules.Effective(-80, 20) <= -60, "Severe discontent can still qualify for crisis");
        check(CourtAppeasementRules.Effective(-60, 20) < -20, "Existing debate is not automatically withdrawn at -40");
        check(CourtAppeasementRules.Effective(-40, 20) >= -20, "Mild discontent can reach neutral");
        var ruler = new Hero();
        var house = new Clan { Leader = ruler };
        var realm = new Kingdom { RulingClan = house };
        var faction = new FactionObject { ParentKingdom = realm };
        var plan = new CourtAppeasementRecord { Realm = realm, RulingClan = house, Ruler = ruler, Target = faction,
            Until = CampaignTime.Days(50) };
        CampaignTime.CurrentDay = 10;
        check(plan.TryBegin() && !plan.TryBegin(), "Interrupted payment cannot be retried");
        check(!plan.Active, "Planned accommodation grants nothing before execution");
        plan.Applied = true;
        check(plan.Active, "Applied accommodation is active before expiry");
        CampaignTime.CurrentDay = 50;
        check(!plan.Active, "Modifier expires at the exact saved deadline");
        CampaignTime.CurrentDay = 10;
        house.Leader = new Hero();
        check(!plan.Active, "Same-house ruler replacement stops accommodation");
        plan.Ended = true;
        house.Leader = ruler;
        check(!plan.Active, "Restoring the former ruler does not restore an ended accommodation");
        plan.Ended = false;
        realm.RulingClan = new Clan { Leader = ruler };
        check(!plan.Active, "Ruling-house replacement stops accommodation");
        realm.RulingClan = house;
        ruler.IsDead = true;
        check(!plan.Active, "Dead ruler cannot sustain accommodation");
        CampaignTime.CurrentDay = 0;
    }
}
