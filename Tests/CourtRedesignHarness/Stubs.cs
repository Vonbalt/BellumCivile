using System.Collections.Generic;
namespace TaleWorlds.Localization
{
    public class TextObject { public TextObject(string text) {} }
}
namespace BellumCivile
{
    public class MandateReformDecision { }
    public class WarScoreRecord { }
    public class FactionObject { public TaleWorlds.CampaignSystem.Kingdom ParentKingdom; public bool IsIdeology = true; public FactionType Type; public float Mood; }
    public static class BellumKingdomVisibilityHelper
    {
        public static bool IsTemporaryBellumKingdom(TaleWorlds.CampaignSystem.Kingdom realm) => realm?.Temporary == true;
    }
    public enum FactionType { Independence = 0, Abdication = 1, InstallRuler = 2, Royalists = 4, Glory = 5, Nobility = 6, Liberty = 7 }
    public enum FeudalTitleType { Barony, County, Duchy, Kingdom, Empire }
    public static class BellumCivileLogger { public static void Log(string text) {} }
}
namespace TaleWorlds.Core
{
    public class SkillObject {}
    public static class DefaultSkills
    {
        public static SkillObject OneHanded = new SkillObject(), TwoHanded = new SkillObject(), Polearm = new SkillObject(),
            Bow = new SkillObject(), Crossbow = new SkillObject(), Throwing = new SkillObject(), Steward = new SkillObject(),
            Leadership = new SkillObject(), Tactics = new SkillObject(), Charm = new SkillObject(), Trade = new SkillObject();
    }
}
namespace TaleWorlds.CampaignSystem
{
    public class Army {}
    public class CharacterObject {}
    public class Kingdom { public Clan RulingClan; public bool Temporary, IsEliminated; public string StringId; }
    public class Clan { public string StringId; public static Clan PlayerClan; public Kingdom Kingdom; public Hero Leader; public bool IsEliminated, IsMinorFaction, IsUnderMercenaryService; }
    public struct CampaignTime
    {
        public static double CurrentDay;
        public bool IsFuture => ToDays > CurrentDay;
        public double ToDays { get; private set; }
        public static CampaignTime Days(float days) => new CampaignTime { ToDays = days };
        public static CampaignTime operator +(CampaignTime left, CampaignTime right) => new CampaignTime { ToDays = left.ToDays + right.ToDays };
    }
    public class Hero
    {
        public bool IsDead;
        public Dictionary<TaleWorlds.Core.SkillObject, int> Skills = new Dictionary<TaleWorlds.Core.SkillObject, int>();
        public int GetSkillValue(TaleWorlds.Core.SkillObject skill) => Skills.TryGetValue(skill, out int value) ? value : 0;
    }
}
namespace TaleWorlds.CampaignSystem.Settlements { public class Settlement {} }
namespace TaleWorlds.SaveSystem
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SaveableFieldAttribute : System.Attribute
    {
        public int Id { get; }
        public SaveableFieldAttribute(int id) { Id = id; }
    }
}
