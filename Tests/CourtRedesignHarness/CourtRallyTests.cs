using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.SaveSystem;

internal static class CourtRallyTests
{
    internal static void Run(Action<bool,string> check)
    {
        for(int n=0;n<=1000;n++)
        {
            float baseline=n/10f, effective=CourtRallyRules.Effective(baseline,true);
            check(Math.Abs(effective-Math.Min(100,baseline+15))<.0001,"Production rally caps effective enthusiasm");
            check(CourtRallyRules.Effective(baseline,false)==baseline,"Unscoped baseline untouched");
        }
        foreach(bool verified in new[]{false,true})
        foreach(bool white in new[]{false,true})
        foreach(int recognized in new[]{-1,0,1})
        for(int net=-150;net<=150;net++)
        {
            int expected=!verified?0:white?-1:recognized!=0?recognized:net>=10?1:-1;
            check(CourtRallyRules.Classify(verified,white,recognized,net)==expected,"Production outcome precedence and net threshold");
        }
        check(CourtRallyRules.Shock(1)==10 && CourtRallyRules.Shock(-1)==-10 && CourtRallyRules.Shock(-2)==-5 && CourtRallyRules.Shock(0)==0,"Distinct success, defeat, expiry and invalidation consequences");
        foreach(double year in new[]{24d,84,365})
        foreach(double remaining in new[]{1d,5,21,84})
            check(CourtRallyRules.End(20,20+remaining,year)==20+Math.Min(remaining,year/4),"Production season and original term cap");
        var ids=typeof(CourtRallyRecord).GetFields().Select(f=>f.GetCustomAttribute<SaveableFieldAttribute>()?.Id).ToArray();
        check(ids.Length==17 && ids.All(x=>x.HasValue) && ids.Distinct().Count()==17,"Rally fields retain unique save identifiers");
    }
}
