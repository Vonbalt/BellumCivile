using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To intertwine military campaigns with the political landscape. It organically builds friendships between lords who win battles together, and causes lords to resent the King for military disasters like lost sieges or imprisonment.
    /// </summary>
    public class ComradesInArmsBehavior : CampaignBehaviorBase
    {
        private const int CaptivityResentmentCap = 4;

        private Dictionary<string, CampaignTime> _captivityNextGrievance = new Dictionary<string, CampaignTime>();
        private Dictionary<string, int> _captivityAccumulatedPenalty = new Dictionary<string, int>();

        public override void RegisterEvents()
        {
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, OnDailyTickHero);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_ComradesCaptivityNextGrievance", ref _captivityNextGrievance);
            dataStore.SyncData("BellumCivile_ComradesCaptivityPenalty", ref _captivityAccumulatedPenalty);
            EnsureCollectionsInitialized();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_captivityNextGrievance == null) _captivityNextGrievance = new Dictionary<string, CampaignTime>();
            if (_captivityAccumulatedPenalty == null) _captivityAccumulatedPenalty = new Dictionary<string, int>();
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!mapEvent.IsFieldBattle && !mapEvent.IsSiegeAssault && !mapEvent.IsSiegeOutside) return;
            if (mapEvent.Winner == null) return;

            MapEventSide winningSide = mapEvent.Winner;
            MapEventSide losingSide = (winningSide == mapEvent.AttackerSide) ? mapEvent.DefenderSide : mapEvent.AttackerSide;
            bool isMajorEngagement = mapEvent.IsSiegeAssault || GetNobleHeroesFromSide(winningSide).Count >= 4 || GetNobleHeroesFromSide(losingSide).Count >= 4;

            ProcessPostBattleRelations(winningSide, true, isMajorEngagement);
            ProcessPostBattleRelations(losingSide, false, isMajorEngagement);
            ProcessRulerAccountabilityForDefeat(GetNobleHeroesFromSide(losingSide));
        }

        private void ProcessPostBattleRelations(MapEventSide side, bool isVictory, bool isMajorEngagement)
        {
            List<Hero> heroes = GetNobleHeroesFromSide(side);
            if (heroes.Count < 2) return;

            Hero battleCommander = side.LeaderParty?.LeaderHero;
            List<KeyValuePair<Hero, int>> playerBattleChanges = new List<KeyValuePair<Hero, int>>();

            for (int i = 0; i < heroes.Count; i++)
            {
                for (int j = i + 1; j < heroes.Count; j++)
                {
                    Hero lordA = heroes[i];
                    Hero lordB = heroes[j];

                    bool isCommanderInvolved = (lordA == battleCommander || lordB == battleCommander);
                    int relationChange = CalculateBattleRelationChange(lordA, lordB, isVictory, isCommanderInvolved, isMajorEngagement);

                    if (relationChange != 0)
                        RelationMemoryService.ApplyChange(lordA, lordB, relationChange, false,
                            RelationMemorySources.SharedBattle, 3f, RelationMemoryScope.Personal);

                    if (lordA == Hero.MainHero || lordB == Hero.MainHero)
                    {
                        Hero otherLord = (lordA == Hero.MainHero) ? lordB : lordA;
                        if (isVictory)
                        {
                            if (relationChange != 0)
                                playerBattleChanges.Add(new KeyValuePair<Hero, int>(otherLord, relationChange));
                        }
                        else
                        {
                            if (relationChange <= 0)
                                playerBattleChanges.Add(new KeyValuePair<Hero, int>(otherLord, relationChange));
                        }
                    }
                }
            }

            ShowBattleRelationSummary(playerBattleChanges, isVictory);
        }

        private void ProcessRulerAccountabilityForDefeat(List<Hero> losingHeroes)
        {
            foreach (Hero loser in losingHeroes)
            {
                if (loser.Clan?.Kingdom?.RulingClan == null) continue;
                if (DynamicRelationBehavior.IsRelationshipMercenaryClan(loser.Clan)) continue;
                if (loser.Clan == loser.Clan.Kingdom.RulingClan) continue; 

                Hero ruler = loser.Clan.Kingdom.RulingClan.Leader;
                if (ruler == null) continue;

                int penalty = -1;

                if (loser.GetTraitLevel(DefaultTraits.Honor) > 0) penalty++;
                if (loser.GetTraitLevel(DefaultTraits.Mercy) > 0) penalty++;
                if (loser.GetTraitLevel(DefaultTraits.Calculating) < 0) penalty--;
                if (loser.GetTraitLevel(DefaultTraits.Honor) < 0) penalty--;
                if (loser.GetTraitLevel(DefaultTraits.Valor) < 0) penalty--;
                if (penalty < -3) penalty = -3;
                if (penalty > 0) penalty = 0;

                if (penalty < 0 && IsValidRelationTarget(loser) && IsValidRelationTarget(ruler))
                {
                    RelationMemoryService.ApplyChange(loser, ruler, penalty, false,
                        RelationMemorySources.BlamedForDefeat, 5f, RelationMemoryScope.Personal);

                    if (loser == Hero.MainHero)
                    {
                        TextObject text = new TextObject("{=BC_Comrades_BlameRuler}You blame {RULER_NAME}'s leadership for your recent defeat on the field of battle ({RELATION_CHANGE}).");
                        text.SetTextVariable("RULER_NAME", ruler.Name);
                        text.SetTextVariable("RELATION_CHANGE", penalty);
                        Msg(text.ToString(), Colors.Red);
                    }
                    else if (ruler == Hero.MainHero)
                    {
                        TextObject text = new TextObject("{=BC_Comrades_BlamedByLord}{LORD_NAME} bitterly blames you for their recent defeat on the field of battle ({RELATION_CHANGE}).");
                        text.SetTextVariable("LORD_NAME", loser.Name);
                        text.SetTextVariable("RELATION_CHANGE", penalty);
                        Msg(text.ToString(), Colors.Red);
                    }
                }
            }
        }

        private void OnDailyTickHero(Hero hero)
        {
            EnsureCollectionsInitialized();
            if (hero == null) return;

            if (!hero.IsPrisoner || hero.Clan?.Kingdom?.RulingClan == null || hero.Clan == hero.Clan.Kingdom.RulingClan || hero.IsMinorFactionHero)
            {
                ClearCaptivityState(hero);
                return;
            }

            Hero ruler = hero.Clan.Kingdom.RulingClan.Leader;
            if (ruler == null) return;

            string heroId = hero.StringId;
            if (!_captivityAccumulatedPenalty.ContainsKey(heroId))
                _captivityAccumulatedPenalty[heroId] = 0;

            if (_captivityAccumulatedPenalty[heroId] >= CaptivityResentmentCap)
                return;

            if (!_captivityNextGrievance.TryGetValue(heroId, out CampaignTime nextGrievance) || nextGrievance.IsFuture)
                nextGrievance = ScheduleNextCaptivityGrievance(hero);

            if (nextGrievance.IsFuture)
                return;

            int penalty = -1;

            if (penalty < 0 && IsValidRelationTarget(hero) && IsValidRelationTarget(ruler))
            {
                RelationMemoryService.ApplyChange(hero, ruler, penalty, false,
                    RelationMemorySources.AbandonedInCaptivity, 8f, RelationMemoryScope.Personal);
                _captivityAccumulatedPenalty[heroId] = _captivityAccumulatedPenalty[heroId] + 1;
                ScheduleNextCaptivityGrievance(hero);

                if (hero == Hero.MainHero)
                {
                    TextObject text = new TextObject("{=BC_Comrades_Captivity}Rotting in captivity, your resentment towards {RULER_NAME} for failing to rescue you grows ({RELATION_CHANGE}).");
                    text.SetTextVariable("RULER_NAME", ruler.Name);
                    text.SetTextVariable("RELATION_CHANGE", penalty);
                    Msg(text.ToString(), Colors.Red);
                }
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (BellumTreatyTransferContext.IsTreatyTransfer) return;
            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) return;
            if (oldOwner?.Clan?.Kingdom?.RulingClan == null || oldOwner.Clan == oldOwner.Clan.Kingdom.RulingClan) return;

            Hero ruler = oldOwner.Clan.Kingdom.RulingClan.Leader;
            if (ruler == null) return;

            int penalty = settlement.IsTown ? -12 : -9;

            if (oldOwner.GetTraitLevel(DefaultTraits.Honor) > 0 || oldOwner.GetTraitLevel(DefaultTraits.Mercy) > 0)
                penalty = TaleWorlds.Library.MathF.Round(penalty * 0.6f);
            else
            {
                if (oldOwner.GetTraitLevel(DefaultTraits.Calculating) < 0) penalty -= 4;
                else if (oldOwner.GetTraitLevel(DefaultTraits.Calculating) > 0) penalty -= 2;
                if (oldOwner.GetTraitLevel(DefaultTraits.Honor) < 0) penalty -= 2;
                if (penalty < -18) penalty = -18;
            }

            if (penalty < 0 && IsValidRelationTarget(oldOwner) && IsValidRelationTarget(ruler))
            {
                RelationMemoryService.ApplyChange(oldOwner, ruler, penalty, false,
                    RelationMemorySources.LostMyFief, 10f, RelationMemoryScope.House, settlement.Name?.ToString());

                if (oldOwner == Hero.MainHero)
                {
                    TextObject text = new TextObject("{=BC_Comrades_LossInfuriates}Failing in their duty to protect the realm, {RULER_NAME}'s loss of {SETTLEMENT_NAME} infuriates you ({RELATION_CHANGE}).");
                    text.SetTextVariable("RULER_NAME", ruler.Name);
                    text.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                    text.SetTextVariable("RELATION_CHANGE", penalty);
                    Msg(text.ToString(), Colors.Red);
                }
                else if (ruler == Hero.MainHero)
                {
                    TextObject text = new TextObject("{=BC_Comrades_LossBlamed}Outraged by your failure to protect their lands, {LORD_NAME} blames you for the loss of {SETTLEMENT_NAME} ({RELATION_CHANGE}).");
                    text.SetTextVariable("LORD_NAME", oldOwner.Name);
                    text.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                    text.SetTextVariable("RELATION_CHANGE", penalty);
                    Msg(text.ToString(), Colors.Red);
                }
            }
        }

        private List<Hero> GetNobleHeroesFromSide(MapEventSide side)
        {
            List<Hero> heroes = new List<Hero>();
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                Hero leader = mapEventParty.Party?.MobileParty?.LeaderHero;
                if (leader != null
                    && !leader.IsDead
                    && leader.Clan != null
                    && !leader.Clan.IsBanditFaction
                    && (!leader.IsMinorFactionHero || DynamicRelationBehavior.IsRelationshipMercenaryClan(leader.Clan)))
                {
                    if (!heroes.Contains(leader))
                    {
                        heroes.Add(leader);
                    }
                }
            }
            return heroes;
        }

        private int CalculateBattleRelationChange(Hero lordA, Hero lordB, bool isVictory, bool isCommanderInvolved, bool isMajorEngagement)
        {
            int relationChange;
            if (isVictory)
            {
                relationChange = 1;
                if (isCommanderInvolved || isMajorEngagement)
                    relationChange++;
            }
            else
            {
                relationChange = isCommanderInvolved ? -2 : -1;
                if (isMajorEngagement)
                    relationChange--;
            }

            relationChange += GetBattleAffinityBonus(lordA, lordB);
            relationChange -= GetBattleClashPenalty(lordA, lordB);

            if (isVictory)
            {
                if (lordA.GetTraitLevel(DefaultTraits.Honor) < 0 || lordB.GetTraitLevel(DefaultTraits.Honor) < 0)
                    relationChange--;

                if (relationChange > 3) relationChange = 3;
                if (relationChange < -1) relationChange = -1;
            }
            else
            {
                if (lordA.GetTraitLevel(DefaultTraits.Honor) > 0 || lordB.GetTraitLevel(DefaultTraits.Honor) > 0) relationChange++;
                if (lordA.GetTraitLevel(DefaultTraits.Mercy) > 0 || lordB.GetTraitLevel(DefaultTraits.Mercy) > 0) relationChange++;

                if (relationChange < -4) relationChange = -4;
                if (relationChange > 0) relationChange = 0;
            }

            return relationChange;
        }

        private static int GetBattleAffinityBonus(Hero lordA, Hero lordB)
        {
            int bonus = 0;

            if (ShareTemperamentAlignment(lordA.GetTraitLevel(DefaultTraits.Calculating), lordB.GetTraitLevel(DefaultTraits.Calculating)))
                bonus++;

            if (ShareTemperamentAlignment(lordA.GetTraitLevel(DefaultTraits.Valor), lordB.GetTraitLevel(DefaultTraits.Valor)))
                bonus++;

            if (ShareTemperamentAlignment(lordA.GetTraitLevel(DefaultTraits.Honor), lordB.GetTraitLevel(DefaultTraits.Honor)))
                bonus++;

            if (ShareTemperamentAlignment(lordA.GetTraitLevel(DefaultTraits.Mercy), lordB.GetTraitLevel(DefaultTraits.Mercy)))
                bonus++;

            return bonus;
        }

        private static int GetBattleClashPenalty(Hero lordA, Hero lordB)
        {
            int penalty = 0;

            if (HaveOpposedTemperaments(lordA.GetTraitLevel(DefaultTraits.Calculating), lordB.GetTraitLevel(DefaultTraits.Calculating)))
                penalty++;

            if (HaveOpposedTemperaments(lordA.GetTraitLevel(DefaultTraits.Valor), lordB.GetTraitLevel(DefaultTraits.Valor)))
                penalty++;

            if (HaveOpposedTemperaments(lordA.GetTraitLevel(DefaultTraits.Honor), lordB.GetTraitLevel(DefaultTraits.Honor)))
                penalty++;

            if (HaveOpposedTemperaments(lordA.GetTraitLevel(DefaultTraits.Mercy), lordB.GetTraitLevel(DefaultTraits.Mercy)))
                penalty++;

            return penalty;
        }

        private static bool ShareTemperamentAlignment(int traitA, int traitB)
        {
            return (traitA >= 1 && traitB >= 1) || (traitA <= -1 && traitB <= -1);
        }

        private static bool HaveOpposedTemperaments(int traitA, int traitB)
        {
            return (traitA >= 1 && traitB <= -1) || (traitA <= -1 && traitB >= 1);
        }

        private void ShowBattleRelationSummary(List<KeyValuePair<Hero, int>> entries, bool isVictory)
        {
            if (entries == null || entries.Count == 0)
                return;

            TextObject text = isVictory
                ? new TextObject("{=BC_Comrades_BattleSummaryVictory}After fighting side-by-side in victory, your bonds have changed: {LORD_CHANGES}.")
                : new TextObject("{=BC_Comrades_BattleSummaryDefeat}After the bitter defeat, blame and respect reshape your bonds: {LORD_CHANGES}.");

            text.SetTextVariable("LORD_CHANGES", string.Join(", ", entries.Select(FormatBattleRelationEntry)));
            Msg(text.ToString(), GetBattleRelationSummaryColor(entries));
        }

        private static string FormatBattleRelationEntry(KeyValuePair<Hero, int> entry)
        {
            string relationChange = entry.Value > 0 ? "+" + entry.Value : entry.Value.ToString();
            return entry.Key.Name + " (" + relationChange + ")";
        }

        private static Color GetBattleRelationSummaryColor(List<KeyValuePair<Hero, int>> entries)
        {
            bool hasPositive = entries.Any(e => e.Value > 0);
            bool hasNegative = entries.Any(e => e.Value < 0);

            if (hasPositive && hasNegative)
                return Colors.Yellow;
            if (hasPositive)
                return Colors.Green;
            if (hasNegative)
                return Colors.Red;
            return Colors.White;
        }

        private CampaignTime ScheduleNextCaptivityGrievance(Hero hero)
        {
            int minDays = 1;
            int maxDays = 3;

            if (hero.GetTraitLevel(DefaultTraits.Honor) > 0 || hero.GetTraitLevel(DefaultTraits.Mercy) > 0)
            {
                minDays = 2;
                maxDays = 4;
            }
            else if (hero.GetTraitLevel(DefaultTraits.Calculating) < 0 || hero.GetTraitLevel(DefaultTraits.Honor) < 0 || hero.GetTraitLevel(DefaultTraits.Valor) < 0)
            {
                minDays = 1;
                maxDays = 2;
            }

            int daysUntilNext = MBRandom.RandomInt(minDays, maxDays + 1);
            CampaignTime nextGrievance = CampaignTime.Now + CampaignTime.Days(daysUntilNext);
            _captivityNextGrievance[hero.StringId] = nextGrievance;
            return nextGrievance;
        }

        private void ClearCaptivityState(Hero hero)
        {
            if (hero?.StringId == null) return;

            _captivityNextGrievance.Remove(hero.StringId);
            _captivityAccumulatedPenalty.Remove(hero.StringId);
        }

        private static bool IsValidRelationTarget(Hero h) => h != null && !h.IsDead && !h.IsDisabled;

        private void Msg(string text, Color color)
        {
            BellumCivileNotifications.ShowPersonal(text, color);
        }
    }
}
