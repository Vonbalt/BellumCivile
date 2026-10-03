using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

internal static class CourtTitleGrantTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int rank in new[] {1,2,3})
        foreach (int claim in new[] {10,25,35})
        foreach (int relation in new[] {-200,-100,0,100,200})
        foreach (int generosity in new[] {-2,-1,0,1,2})
        foreach (int honor in new[] {-2,-1,0,1,2})
        foreach (bool favored in new[] {false,true})
        foreach (int rights in new[] {1,2,3})
        {
            bool practical = rights != 1, full = rights == 3;
            float expected = 40 + claim + Math.Max(-15, Math.Min(15, relation * .15f)) + generosity * 8 + honor * 5
                + (favored ? 15 : 0) - (rank-1) * (practical ? 10 : 5) - (full ? 5 : 0);
            expected = Math.Max(0, Math.Min(100, expected));
            check(Math.Abs(CourtTitleGrantRules.Score(rank,claim,relation,generosity,honor,favored,practical,full)-expected)<.001,
                "Production title willingness matches approved matrix");
        }
        foreach (int rank in new[] {1,2,3})
        foreach (bool full in new[] {false,true})
            check(CourtTitleGrantRules.Reward(rank,full)==15+rank*5+(full?5:0),"Claimed title goodwill scales with rank and rights");
        foreach (int rights in new[] {1,2,3})
        {
            var p=new CourtTitleGrantRecord {Recipient=new Clan {StringId="recipient"},OldLegal="crown",OldPractical="crown",Legal=rights!=2,Practical=rights!=1};
            foreach (string legal in new[] {"crown","recipient","outsider"})
            foreach (string practical in new[] {"crown","recipient","outsider"})
            {
                check(CourtTitleGrantRules.Delivered(p,legal,practical)==(legal==(p.Legal?"recipient":"crown") && practical==(p.Practical?"recipient":"crown")),"Receipt requires exact authorized rights");
                check(CourtTitleGrantRules.Recoverable(p,legal,practical)==((legal=="crown" || p.Legal && legal=="recipient") && (practical=="crown" || p.Practical && practical=="recipient")),"Recovery never overwrites unrelated ownership");
            }
        }
        var ids=typeof(CourtTitleGrantRecord).GetFields().Select(f=>f.GetCustomAttribute<SaveableFieldAttribute>()?.Id).ToArray();
        check(ids.Length==27 && ids.All(x=>x.HasValue) && ids.Distinct().Count()==27,"Title delivery saved fields are unique");
        check(CourtTitleGrantRules.Score(1,25,0,0,0,false,true,true)==60,"Neutral strong-claim county grant meets threshold");
        check(CourtTitleGrantRules.Score(3,25,0,0,0,false,true,true)==40,"Kingdom grant remains more reluctant");
    }
}
