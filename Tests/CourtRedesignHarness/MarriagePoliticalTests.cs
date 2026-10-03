using System;
using BellumCivile.Behaviors;

internal static class MarriagePoliticalTests
{
    internal static void Run(Action<bool, string> check)
    {
        float Foreign(float own, float partner, float enemy, bool common = false, bool allied = false,
            bool room = true, bool isolated = false) => MarriagePoliticalRules.ForeignPartner(
                own, partner, enemy, common, allied, room, isolated, true, 0);
        check(Foreign(1000, 4000, 0) == 80, "A powerful prospective friend is not a threat");
        check(Foreign(1000, 4000, 2000) == 100 && Foreign(4000, 1000, 0) == 80,
            "Only the threatened realm gains protection value");
        check(Foreign(1000, 100, 2000) < Foreign(1000, 4000, 2000), "Protection reflects useful partner strength");
        check(Foreign(1000, 4000, 2000, room: false, isolated: true) == 65,
            "Full slots remove expansion and protection opportunity without vetoing family ties");
        check(Foreign(1000, 4000, 2000, allied: true, room: false) == 100,
            "An existing ally can still provide protection without another slot");
        check(Foreign(1000, 1000, 0, allied: true, isolated: true) == 65,
            "Already allied never receives another prospective-alliance reward");
        check(MarriagePoliticalRules.DomesticPartner(1000, 1000, 100, true)
            - MarriagePoliticalRules.DomesticPartner(1000, 1000, 100, false) == 25,
            "Crown access is a single explicit political benefit, separate from clan tier prestige");
        check(MarriagePoliticalRules.ForeignHousePartner(1000, 1000, 0, false) == 57.5f,
            "Ordinary foreign houses value a useful new family tie");
        check(MarriagePoliticalRules.ForeignHousePartner(1000, 1000, 0, true) == 67.5f,
            "A foreign ruling house offers added political access");
        for (int own = 0; own <= 5000; own += 100)
        for (int partner = 0; partner <= 5000; partner += 100)
        {
            float domestic = MarriagePoliticalRules.DomesticPartner(own, partner, 100, false);
            check(domestic >= 10 && domestic <= 85, "Ordinary-house political utility bounded");
            check(MarriagePoliticalRules.DomesticPartner(own, partner + 100, 100, false) >= domestic,
                "A stronger political partner does not lose utility");
            float foreignHouse = MarriagePoliticalRules.ForeignHousePartner(own, partner, 100, false);
            check(foreignHouse >= 40 && foreignHouse <= 80, "Ordinary foreign house value bounded");
            check(MarriagePoliticalRules.ForeignHousePartner(own, partner + 100, 100, false) >= foreignHouse,
                "A stronger foreign house does not lose political value");
            float foreign = Foreign(own, partner, 3000, common: true, isolated: true);
            check(foreign >= 0 && foreign <= 100, "Foreign political utility bounded");
            check(Foreign(own, partner + 100, 3000, common: true, isolated: true) >= foreign,
                "Useful protection is monotonic in partner strength");
        }
        Console.WriteLine("Political examples: equal domestic houses +29.5 each; equal foreign houses +57.5; royal friend at peace +80; protection-seeking realm +100 / secure partner +80; full slots +65.");
    }
}
