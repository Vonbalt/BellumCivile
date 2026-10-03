using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class TreatyRatificationService
    {
        private static readonly IReadOnlyDictionary<string, string> LocalizedReasonTemplates =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Court peace initiative"] = "{=BC_Parley_Reason_CourtPeaceInitiative}Our faction has called for an end to this war.",
                ["Court rally"] = "{=BC_RallyTreatyReason}The court's call to victory strengthens my resolve to continue this campaign.",
                ["Crown-backed peace initiative"] = "{=BC_Parley_Reason_CrownPeaceInitiative}I have lent my support to those calling for an end to this war.",
                ["Court clientage initiative"] = "{=BC_Parley_Reason_CourtClientageInitiative}These terms would fulfill the submission our faction has sought.",
                ["Crown-backed clientage initiative"] = "{=BC_Parley_Reason_CrownClientageInitiative}These terms would fulfill the ambition to which I have lent the Crown's support.",
                ["Low Enthusiasm"] = "{=BC_Parley_Reason_LowEnthusiasm}I am exhausted by this conflict.",
                ["High Enthusiasm"] = "{=BC_Parley_Reason_HighEnthusiasm}I am still eager to carry on the fight.",
                ["Early war reluctance"] = "{=BC_Parley_Reason_EarlyWar}We have only just taken up arms; we should see this campaign through.",
                ["Inconclusive campaign"] = "{=BC_Parley_Reason_InconclusiveWar}This war has not yet yielded a decisive result.",
                ["Prolonged exhaustion"] = "{=BC_Parley_Reason_ProlongedExhaustion}We are completely exhausted.",
                ["Enemy making unreasonable demands"] = "{=BC_Parley_Reason_UnreasonableDemands}The enemy is making unreasonable demands.",
                ["Unrealized war advantage"] = "{=BC_Parley_Reason_UnrealizedWarAdvantage}We have earned harsher terms than these.",
                ["Concessions received"] = "{=BC_Parley_Reason_ConcessionsReceived}Their concessions make this peace worthwhile.",
                ["Military pressure to settle"] = "{=BC_Parley_Reason_MilitaryPressureToSettle}We are hard pressed by this war.",
                ["You abstain"] = "{=BC_Parley_Reason_PlayerAbstains}I withhold my voice from this treaty.",
                ["You back the treaty"] = "{=BC_Parley_Reason_PlayerBacksTreaty}I stand behind these terms.",
                ["You oppose the treaty"] = "{=BC_Parley_Reason_PlayerOpposesTreaty}I cannot support these terms.",
                ["Additional foreign wars"] = "{=BC_Parley_Reason_AdditionalForeignWars}We cannot sustain so many wars at once.",
                ["Civil war at home"] = "{=BC_Parley_Reason_CivilWarAtHome}We must restore order within our own realm.",
                ["Feuds at home"] = "{=BC_Parley_Reason_FeudsAtHome}Our domestic quarrels demand our attention.",
                ["Occupation retained"] = "{=BC_Parley_Reason_OccupationRetained}We should keep what our armies already hold.",
                ["Client territorial gains secured"] = "{=BC_Parley_ClientGains}Our clients should keep the lands they have taken in our common cause.",
                ["Personal claim passed over for client"] = "{=BC_Parley_ClientClaimPassedOver}My claim to those lands should not be set aside for a client realm.",
                ["Strong claim recognized"] = "{=BC_Parley_Reason_StrongClaimRecognized}This treaty recognizes my rightful claim.",
                ["Weak claim advanced"] = "{=BC_Parley_Reason_WeakClaimAdvanced}This treaty advances my claim.",
                ["Realm territory expanded"] = "{=BC_Parley_Reason_RealmTerritoryExpanded}This treaty expands our realm.",
                ["Would formalize loss of personal fief"] = "{=BC_Parley_Reason_FormalizePersonalFiefLoss}I will not accept the loss of my fief.",
                ["Would surrender unoccupied personal fief"] = "{=BC_Parley_Reason_SurrenderPersonalFief}I will not surrender my fief without a fight.",
                ["Strong claim abandoned"] = "{=BC_Parley_Reason_StrongClaimAbandoned}I will not abandon my rightful claim.",
                ["Weak claim abandoned"] = "{=BC_Parley_Reason_WeakClaimAbandoned}I will not abandon my claim so readily.",
                ["Realm territory conceded"] = "{=BC_Parley_Reason_RealmTerritoryConceded}We yield lands already lost to the enemy.",
                ["Unoccupied realm territory surrendered"] = "{=BC_Parley_Reason_UnoccupiedTerritorySurrendered}We should not surrender lands they have not taken.",
                ["Treaty payment burden"] = "{=BC_Parley_Reason_TreatyPaymentBurden}These payments would burden my house.",
                ["Tribute secured"] = "{=BC_Parley_Reason_TributeSecured}Their tribute will enrich our realm.",
                ["Reparations secured"] = "{=BC_Parley_Reason_ReparationsSecured}Their reparations repay part of our losses.",
                ["Clan member recovered"] = "{=BC_Parley_Reason_ClanMemberRecovered}One of my own will return home.",
                ["Captured ruler recovered"] = "{=BC_Parley_Reason_CapturedRulerRecovered}Our ruler will be freed from captivity.",
                ["Realm noble recovered"] = "{=BC_Parley_Reason_RealmNobleRecovered}One of our nobles will return home.",
                ["Personal prisoner surrendered"] = "{=BC_Parley_Reason_PersonalPrisonerSurrendered}I will not surrender a valuable prisoner.",
                ["Realm prisoner surrendered"] = "{=BC_Parley_Reason_RealmPrisonerSurrendered}We surrender a prisoner held by our realm.",
                ["Personal claim renounced"] = "{=BC_Parley_Reason_PersonalClaimRenounced}I will not renounce my claim.",
                ["Realm claim renounced"] = "{=BC_Parley_Reason_RealmClaimRenounced}Our realm should not abandon this claim.",
                ["Claim against personal title extinguished"] = "{=BC_Parley_Reason_PersonalTitleClaimExtinguished}This removes a threat to my title.",
                ["Foreign claim extinguished"] = "{=BC_Parley_Reason_ForeignClaimExtinguished}This extinguishes an enemy claim.",
                ["Ruler publicly humiliated"] = "{=BC_Parley_Reason_RulerHumiliated}I will not endure such public humiliation.",
                ["Ruler publicly discredited"] = "{=BC_Parley_Reason_RulerDiscredited}I will not accept this attack on my standing.",
                ["Disliked ruler rebuked"] = "{=BC_Parley_Reason_DislikedRulerRebuked}A ruler I despise is finally being rebuked.",
                ["Loyal ruler dishonored"] = "{=BC_Parley_Reason_LoyalRulerDishonored}I will not see our ruler dishonored.",
                ["Realm ruler rebuked"] = "{=BC_Parley_Reason_RealmRulerRebuked}This rebuke diminishes our ruler.",
                ["Enemy ruler humbled"] = "{=BC_Parley_Reason_EnemyRulerHumbled}Our enemy's ruler will be humbled.",
                ["Enemy ruler discredited"] = "{=BC_Parley_Reason_EnemyRulerDiscredited}Our enemy's ruler will be discredited.",
                ["Independence secured by treaty"] = "{=BC_Parley_Reason_IndependenceSecured}I will finally rule in my own right.",
                ["Released with title liege"] = "{=BC_Parley_Reason_ReleasedWithTitleLiege}My liege's independence carries us with it.",
                ["Vassal realm torn away"] = "{=BC_Parley_Reason_VassalRealmTornAway}I will not see a vassal realm torn away.",
                ["Realm weakened by vassal release"] = "{=BC_Parley_Reason_RealmWeakenedByVassalRelease}Releasing these vassals will weaken our realm.",
                ["Enemy realm politically weakened"] = "{=BC_Parley_Reason_EnemyRealmWeakened}This release will weaken our enemy.",
                ["Sovereignty surrendered"] = "{=BC_Parley_Reason_SovereigntySurrendered}I will not surrender my sovereignty.",
                ["Realm subjected to a foreign sovereign"] = "{=BC_Parley_Reason_ForeignSovereign}I will not submit our realm to a foreign sovereign.",
                ["Vassal realm restored to the hierarchy"] = "{=BC_Parley_Reason_VassalRealmRestored}This restores a vassal realm to its rightful place.",
                ["Sovereignty offered for protection"] = "{=BC_Parley_Reason_SovereigntyOffered}We trade some freedom for much-needed protection.",
                ["Sovereignty constrained by foreign clientage"] = "{=BC_Parley_Reason_SovereigntyConstrained}I will not accept the chains of foreign clientage.",
                ["Realm enters a foreign sphere"] = "{=BC_Parley_Reason_ForeignSphere}Our realm would fall into a foreign sphere.",
                ["Realm subjected to foreign diplomacy"] = "{=BC_Parley_Reason_ForeignDiplomacy}Our diplomacy would no longer be our own.",
                ["Trusted prospective suzerain"] = "{=BC_Parley_Reason_TrustedSuzerain}I trust the sovereign who would stand above us.",
                ["Distrusted prospective suzerain"] = "{=BC_Parley_Reason_DistrustedSuzerain}I will not submit to a sovereign I distrust.",
                ["Foreign realm submits to the crown"] = "{=BC_Parley_Reason_RealmSubmitsToCrown}A foreign realm will submit to my crown.",
                ["Client realm enlarges the realm's influence"] = "{=BC_Parley_Reason_ClientRealmInfluence}This client will extend our realm's influence.",
                ["Rightful suzerainty restored"] = "{=BC_Parley_Reason_RightfulSuzerainty}This restores a rightful bond of suzerainty.",
                ["Foreign-culture suzerainty"] = "{=BC_Parley_Reason_ForeignCultureSuzerainty}I will not bow easily to a foreign court.",
                ["Foreign royal spouse joins the dynasty"] = "{=BC_Parley_Reason_RoyalSpouseJoins}A foreign royal will join my dynasty.",
                ["Royal marriage cements the peace"] = "{=BC_Parley_Reason_RoyalMarriagePeace}This marriage will secure the peace.",
                ["Royal houses bound by marriage"] = "{=BC_Parley_Reason_RoyalHousesBound}This union will bind our royal houses.",
                ["Favored royal match"] = "{=BC_Parley_Reason_FavoredRoyalMatch}I welcome the proposed royal match.",
                ["Disfavored royal match"] = "{=BC_Parley_Reason_DisfavoredRoyalMatch}I distrust the spouse chosen for this match.",
                ["Foreign alliance sacrificed"] = "{=BC_Parley_Reason_AllianceSacrificed}We should not abandon a valuable alliance.",
                ["Trade relationship sacrificed"] = "{=BC_Parley_Reason_TradeSacrificed}We should not abandon a valuable trade agreement.",
                ["Ruler compelled to break a diplomatic pledge"] = "{=BC_Parley_Reason_DiplomaticPledgeBroken}I will not stain my word by breaking this pledge.",
                ["Loyalty to abandoned partner"] = "{=BC_Parley_Reason_LoyaltyToAbandonedPartner}I will not betray a realm I trust.",
                ["Hostility toward abandoned partner"] = "{=BC_Parley_Reason_HostilityToAbandonedPartner}I owe that realm no loyalty.",
                ["Marriage ties with abandoned partner"] = "{=BC_Parley_Reason_AbandonedMarriageTies}Our marriage bonds should not be cast aside.",
                ["Enemy alliance dismantled"] = "{=BC_Parley_Reason_EnemyAllianceDismantled}Their alliance will be broken.",
                ["Enemy trade network weakened"] = "{=BC_Parley_Reason_EnemyTradeWeakened}Their trade network will be weakened.",
                ["Concern for isolated realm"] = "{=BC_Parley_Reason_ConcernForIsolatedRealm}I will not help isolate a friendly realm.",
                ["Hostility toward isolated realm"] = "{=BC_Parley_Reason_HostilityToIsolatedRealm}I am content to see that realm isolated.",
                ["Enemy partner isolated"] = "{=BC_Parley_Reason_EnemyPartnerIsolated}One of our enemies will stand isolated.",
                ["Victory publicly acknowledged"] = "{=BC_Parley_Reason_VictoryAcknowledged}Our victory will be acknowledged before all.",
                ["Realm concedes defeat"] = "{=BC_Parley_Reason_RealmConcedesDefeat}We should not publicly confess defeat.",
                ["Client realm relinquished"] = "{=BC_Parley_Reason_ClientRelinquished}We should not relinquish our client realm.",
                ["Client realm liberated"] = "{=BC_Parley_Reason_ClientLiberated}We will free a realm from foreign subordination.",
                ["Crown surrendered to rebels"] = "{=BC_Parley_Reason_CrownSurrenderedToRebels}We will not surrender our crown to rebels.",
                ["Enemy rebellion enforced"] = "{=BC_Parley_Reason_EnemyRebellionEnforced}Enforcing their rebellion will cripple our enemy.",
                ["We are beaten"] = "{=BC_Parley_Reason_WeAreBeaten}We are beaten and cannot refuse these terms.",
                ["We dictate the peace"] = "{=BC_Parley_Reason_WeDictatePeace}We are victorious and will dictate the peace."
            };

        public TreatyCouncilEvaluation EvaluateCouncil(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom kingdom, bool winnerSide, TreatyCouncilSnapshot snapshot = null)
        {
            WarPeaceRevampBehavior warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            List<TreatyCouncilMemberEvaluation> members = new List<TreatyCouncilMemberEvaluation>();
            int yayInfluence = 0;
            int nayInfluence = 0;
            int totalVotingCapacity = 0;
            List<Clan> eligibleClans = GetEligibleClans(kingdom).ToList();
            bool soleRulerDecision = eligibleClans.Count == 1
                && eligibleClans[0] == kingdom?.RulingClan;
            int warScoreBudget = Math.Max(0, proposal?.WarScoreBudget ?? 0);
            int usedWarScore = proposal?.UsedWarScore ?? 0;
            bool overBudget = proposal?.IsOverBudget == true;
            int unusedTolerance = Math.Max(
                C.TreatyUnusedLeverageToleranceMinimum,
                (int)Math.Ceiling(warScoreBudget * C.TreatyUnusedLeverageToleranceShare));
            int unusedLeverage = Math.Max(0, warScoreBudget - usedWarScore - unusedTolerance);
            TreatyWarScoreSummary accounting = TreatyWarScoreAccounting.Calculate(
                proposal?.Terms,
                proposal?.WinnerKingdomId,
                warScoreBudget);
            if (winnerSide && unusedLeverage > 0)
            {
                Kingdom winner = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == proposal?.WinnerKingdomId);
                Kingdom loser = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == proposal?.LoserKingdomId);
                unusedLeverage = Math.Min(unusedLeverage, TreatyDraftService.GetActionableWinnerDemandCap(
                    war,
                    proposal,
                    winner,
                    loser,
                    warScoreBudget - usedWarScore));
            }

            foreach (Clan clan in eligibleClans)
            {
                float enthusiasm = snapshot?.GetEnthusiasm(clan, warWill?.GetWarWill(clan) ?? 50f) ?? (warWill?.GetWarWill(clan) ?? 50f);
                List<TreatyCouncilReason> reasons = new List<TreatyCouncilReason>();
                float utility = 0f;

                float days = war == null ? 0f : Math.Max(0f, (float)CampaignTime.Now.ToDays - war.StartedDay);
                var readiness = PeaceReadiness.Evaluate(days, war?.Score ?? warScoreBudget, enthusiasm, BellumCivileOptions.WarDurationReluctanceDays);
                Add(reasons, ref utility, readiness.Exhaustion, readiness.Exhaustion >= 0f ? "Low Enthusiasm" : "High Enthusiasm");
                Add(reasons, ref utility, readiness.EarlyWarReluctance, "Early war reluctance");
                Add(reasons, ref utility, readiness.InconclusiveWar, "Inconclusive campaign");
                Add(reasons, ref utility, readiness.ProlongedExhaustion, "Prolonged exhaustion");
                float effectiveWill = CourtAgendaBehavior.EffectiveWarWill(clan, war, enthusiasm);
                if (effectiveWill != enthusiasm)
                {
                    var rallied = PeaceReadiness.Evaluate(days, war?.Score ?? warScoreBudget, effectiveWill, BellumCivileOptions.WarDurationReluctanceDays);
                    Add(reasons, ref utility, rallied.Total - readiness.Total, "Court rally");
                }
                ApplyRealmOverextension(reasons, ref utility, kingdom);

                if (winnerSide)
                {
                    Add(reasons, ref utility,
                        -unusedLeverage * C.TreatyUnusedLeverageAssessmentPerWarScore,
                        "Unrealized war advantage");
                }
                else
                {
                    Add(reasons, ref utility, warScoreBudget * 0.6f, "Military pressure to settle");
                }

                if (kingdom?.StringId == proposal?.LoserKingdomId && accounting.OfferingValue > 0)
                {
                    float offeringAssessment = Math.Min(
                        C.TreatyOfferingAssessmentMaximum,
                        accounting.OfferingValue * C.TreatyOfferingAssessmentPerWarScore);
                    Add(reasons, ref utility, offeringAssessment, "Concessions received");
                }

                foreach (TreatyTermRecord term in proposal?.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                    ApplyTermUtility(reasons, ref utility, war, term, clan, winnerSide);

                float courtPeace = CourtAgendaBehavior.Current?.PeaceInitiativeBonus(kingdom, war, clan) ?? 0;
                if (courtPeace != 0) Add(reasons, ref utility, courtPeace,
                    clan == kingdom?.RulingClan ? "Crown-backed peace initiative" : "Court peace initiative");
                float courtClientage = CourtAgendaBehavior.Current?.SubjugationTreatyBonus(kingdom, war, clan, proposal?.Terms) ?? 0;
                if (courtClientage != 0) Add(reasons, ref utility, courtClientage,
                    clan == kingdom?.RulingClan ? "Crown-backed clientage initiative" : "Court clientage initiative");
                float courtClaim = CourtAgendaBehavior.Current?.ClaimTreatyBonus(kingdom, war, clan, proposal?.Terms) ?? 0;
                if (courtClaim != 0) Add(reasons, ref utility, courtClaim,
                    clan == kingdom?.RulingClan ? "Crown-backed claim initiative" : "Court claim initiative");

                if (overBudget && !winnerSide)
                    Add(reasons, ref utility, -1000f, "Enemy making unreasonable demands");

                if (proposal?.IsForced == true && !overBudget)
                {
                    // Capitulation is no longer an ordinary proposal. Ensure AI councillors visibly
                    // recognize the decisive result even when individual concessions would normally
                    // make them reject the draft. A player vassal may still record a personal protest.
                    float decisivePressure = Math.Max(50f, 200f - utility);
                    Add(reasons, ref utility, decisivePressure,
                        winnerSide ? "We dictate the peace" : "We are beaten");
                }

                int influenceCommitment = 0;
                if (clan == Clan.PlayerClan && kingdom != null && kingdom.RulingClan != Clan.PlayerClan)
                {
                    influenceCommitment = proposal?.PlayerInfluenceCommitment ?? 0;
                    ApplyPlayerVote(reasons, ref utility, proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain, influenceCommitment);
                }

                TreatyCouncilVoteStance stance = utility >= 10f
                    ? TreatyCouncilVoteStance.Yay
                    : utility <= -10f
                        ? TreatyCouncilVoteStance.Nay
                        : TreatyCouncilVoteStance.Abstain;
                if (clan == Clan.PlayerClan && kingdom != null && kingdom.RulingClan != Clan.PlayerClan)
                    stance = proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain;
                float influence = snapshot?.GetInfluence(clan, clan.Influence) ?? clan.Influence;
                bool playerRuler = clan == Clan.PlayerClan && kingdom?.RulingClan == Clan.PlayerClan;
                if (!playerRuler && !soleRulerDecision)
                    totalVotingCapacity += PoliticalInfluenceVoteHelper.GetAffordableCommitment(
                        C.TreatyCouncilStrongCommitment,
                        clan,
                        influence);
                if (soleRulerDecision)
                {
                    // A sovereign with no vassal council decides from the treaty assessment itself.
                    // Influence commitments and a participation quorum have no meaning in a one-clan realm.
                    stance = utility >= 10f
                        ? TreatyCouncilVoteStance.Yay
                        : utility <= -10f
                            ? TreatyCouncilVoteStance.Nay
                            : TreatyCouncilVoteStance.Abstain;
                    influenceCommitment = 0;
                }
                else if (playerRuler)
                {
                    stance = TreatyCouncilVoteStance.Abstain;
                    influenceCommitment = 0;
                }
                else if (clan != Clan.PlayerClan || kingdom == null)
                    influenceCommitment = PoliticalInfluenceVoteHelper.GetCommitment(
                        utility,
                        clan,
                        out stance,
                        influence);

                members.Add(new TreatyCouncilMemberEvaluation(clan, enthusiasm, influence, utility, stance, influenceCommitment, reasons));
                if (stance == TreatyCouncilVoteStance.Yay)
                    yayInfluence += influenceCommitment;
                else if (stance == TreatyCouncilVoteStance.Nay)
                    nayInfluence += influenceCommitment;
            }

            // Peace has no participation floor: committed opposition, not absent voters,
            // determines whether a sovereign needs to override the council.
            PoliticalInfluenceVoteTally tally = new PoliticalInfluenceVoteTally(
                yayInfluence,
                nayInfluence,
                0);

            TreatyCouncilMemberEvaluation soleRuler = soleRulerDecision ? members.FirstOrDefault() : null;
            bool soleRulerAccepts = soleRuler?.Utility >= 10f;
            float? soleRulerSupport = soleRulerDecision ? (float?)soleRuler?.Utility : null;
            TreatyCouncilMemberEvaluation rulerAssessment = members.FirstOrDefault(member => member?.Clan == kingdom?.RulingClan);

            return new TreatyCouncilEvaluation(
                yayInfluence,
                nayInfluence,
                tally.Quorum,
                tally.RatificationOverrideCost,
                tally.RejectionOverrideCost,
                members,
                soleRulerDecision,
                soleRulerAccepts,
                soleRulerSupport,
                isBudgetBlocked: overBudget,
                votingCapacity: totalVotingCapacity,
                unopposedRulerSupport: rulerAssessment?.Utility);
        }

        private static void ApplyPlayerVote(List<TreatyCouncilReason> reasons, ref float utility, TreatyCouncilVoteStance stance, int influenceCommitment)
        {
            if (stance == TreatyCouncilVoteStance.Abstain)
            {
                Add(reasons, ref utility, -utility, "You abstain");
                return;
            }

            float commitmentEffect = 10f + Math.Max(0, influenceCommitment) / 4f;
            float target = stance == TreatyCouncilVoteStance.Yay
                ? Math.Max(commitmentEffect, utility + commitmentEffect)
                : Math.Min(-commitmentEffect, utility - commitmentEffect);
            Add(reasons, ref utility, target - utility, stance == TreatyCouncilVoteStance.Yay ? "You back the treaty" : "You oppose the treaty");
        }

        private static void ApplyRealmOverextension(List<TreatyCouncilReason> reasons, ref float utility, Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return;

            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            IReadOnlyList<WarScoreRecord> wars = warScore?.GetActiveWars() ?? new List<WarScoreRecord>();
            bool InvolvesRealm(WarScoreRecord record) => record != null
                && (record.AttackerKingdomId == kingdom.StringId || record.DefenderKingdomId == kingdom.StringId);

            int foreignWars = wars.Count(record => InvolvesRealm(record)
                && record.ConflictType == WarScoreConflictType.ForeignWar);
            int civilWars = wars.Count(record => InvolvesRealm(record)
                && record.ConflictType == WarScoreConflictType.CivilWar);
            int realmFeuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?
                .GetActiveWars()
                .Count(record => record != null && record.ParentKingdomId == kingdom.StringId) ?? 0;

            Add(reasons, ref utility,
                Math.Max(0, foreignWars - 1) * C.TreatyAdditionalForeignWarUtility,
                "Additional foreign wars");
            Add(reasons, ref utility,
                civilWars * C.TreatyActiveCivilWarUtility,
                "Civil war at home");
            Add(reasons, ref utility,
                realmFeuds * C.TreatyActiveRealmFeudUtility,
                "Feuds at home");
        }

        private static void ApplyTermUtility(List<TreatyCouncilReason> reasons, ref float utility, WarScoreRecord war, TreatyTermRecord term, Clan clan, bool winnerSide)
        {
            if (term == null)
                return;

            if (ClientWarTerritory.IsTerritorial(term.Type))
            {
                Settlement settlement = ResolveSettlement(term.SettlementId);
                WarScoreFiefSnapshotRecord snapshot = war?.GetSnapshot(term.SettlementId);
                Clan originalOwner = Clan.All.FirstOrDefault(candidate => candidate?.StringId == snapshot?.OwnerClanId);
                Clan surrenderingOwner = term.WasOccupiedAtDrafting ? originalOwner : settlement?.OwnerClan;
                FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                FeudalTitleRecord title = null;
                if (settlement != null)
                    titles?.TryGetBarony(settlement, out title);
                bool strongClaim = title != null && titles?.HasActiveClaim(clan, title, FeudalClaimStrength.Strong) == true;
                bool weakClaim = !strongClaim && title != null && titles?.HasActiveClaim(clan, title, FeudalClaimStrength.Weak) == true;

                bool clanReceives = clan?.Kingdom?.StringId == term.ToKingdomId;
                bool clanConcedes = clan?.Kingdom?.StringId == term.FromKingdomId;
                if (clanReceives)
                {
                    if (term.Type == TreatyTermType.RecognizeClientOccupation)
                    {
                        Add(reasons, ref utility, 3f, "Client territorial gains secured");
                        if (strongClaim || weakClaim)
                            Add(reasons, ref utility, strongClaim ? -20f : -10f, "Personal claim passed over for client");
                    }
                    else if (settlement?.OwnerClan == clan)
                        Add(reasons, ref utility, 20f, "Occupation retained");
                    else if (strongClaim)
                        Add(reasons, ref utility, 20f, "Strong claim recognized");
                    else if (weakClaim)
                        Add(reasons, ref utility, 10f, "Weak claim advanced");
                    else
                        Add(reasons, ref utility, 5f, "Realm territory expanded");
                }
                else if (clanConcedes && surrenderingOwner == clan)
                {
                    Add(reasons, ref utility, term.WasOccupiedAtDrafting ? -70f : -140f,
                        term.WasOccupiedAtDrafting ? "Would formalize loss of personal fief" : "Would surrender unoccupied personal fief");
                }
                else if (clanConcedes && strongClaim)
                {
                    Add(reasons, ref utility, -30f, "Strong claim abandoned");
                }
                else if (clanConcedes && weakClaim)
                {
                    Add(reasons, ref utility, -15f, "Weak claim abandoned");
                }
                else if (clanConcedes)
                {
                    Add(reasons, ref utility, term.WasOccupiedAtDrafting ? -10f : -20f,
                        term.WasOccupiedAtDrafting ? "Realm territory conceded" : "Unoccupied realm territory surrendered");
                }
            }
            else if (term.Type == TreatyTermType.Reparations || term.Type == TreatyTermType.Tribute)
            {
                bool clanPays = clan?.Kingdom?.StringId == term.FromKingdomId;
                bool clanReceives = clan?.Kingdom?.StringId == term.ToKingdomId;
                if (clanPays)
                {
                    int totalPayment = term.GoldAmount + term.DailyGold * term.DurationDays;
                    int realmGold = Math.Max(1, clan.Kingdom?.Clans?.Where(candidate => candidate?.Leader != null).Sum(candidate => Math.Max(0, candidate.Leader.Gold)) ?? 1);
                    float clanShare = Math.Max(0, clan.Leader?.Gold ?? 0) / (float)realmGold;
                    float personalPayment = totalPayment * clanShare;
                    float burden = personalPayment / Math.Max(1f, clan.Leader?.Gold ?? 0);
                    Add(reasons, ref utility, -Math.Min(30f, burden * 20f), "Treaty payment burden");
                }
                else if (clanReceives)
                {
                    float benefit = Math.Min(20f, Math.Max(5f, term.WarScoreCost * 0.40f));
                    Add(reasons, ref utility, benefit,
                        term.Type == TreatyTermType.Tribute ? "Tribute secured" : "Reparations secured");
                }
            }
            else if (term.Type == TreatyTermType.ReleasePrisoner)
            {
                Hero prisoner = Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == term.HeroId);
                bool clanReceives = clan?.Kingdom?.StringId == term.ToKingdomId;
                bool clanReleases = clan?.Kingdom?.StringId == term.FromKingdomId;
                if (clanReceives)
                {
                    if (prisoner?.Clan == clan)
                        Add(reasons, ref utility, 30f, "Clan member recovered");
                    else if (prisoner?.Clan?.Kingdom == clan?.Kingdom)
                        Add(reasons, ref utility, prisoner == clan.Kingdom?.Leader ? 15f : 3f,
                            prisoner == clan.Kingdom?.Leader ? "Captured ruler recovered" : "Realm noble recovered");
                }
                else if (clanReleases)
                {
                    Clan captorClan = ResolveCaptorClan(prisoner);
                    Add(reasons, ref utility, captorClan == clan ? -15f : -2f,
                        captorClan == clan ? "Personal prisoner surrendered" : "Realm prisoner surrendered");
                }
            }
            else if (term.Type == TreatyTermType.RenounceClaim)
            {
                FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                Clan claimant = Clan.All.FirstOrDefault(candidate => candidate?.StringId == term.ClanId);
                FeudalTitleRecord title = titles?.GetTitle(term.TitleId);
                IEnumerable<FeudalClaimRecord> activeClaims = titles?.GetActiveClaimsByClan(claimant)
                    ?? Enumerable.Empty<FeudalClaimRecord>();
                FeudalClaimStrength strength = activeClaims
                    .Where(claim => claim.TargetTitleId == term.TitleId)
                    .Select(claim => claim.Strength)
                    .DefaultIfEmpty(FeudalClaimStrength.Weak)
                    .Max();
                bool claimantSide = clan?.Kingdom?.StringId == term.FromKingdomId;
                bool protectedSide = clan?.Kingdom?.StringId == term.ToKingdomId;
                if (clan == claimant)
                    Add(reasons, ref utility, strength == FeudalClaimStrength.Strong ? -45f : -25f, "Personal claim renounced");
                else if (claimantSide)
                    Add(reasons, ref utility, strength == FeudalClaimStrength.Strong ? -6f : -3f, "Realm claim renounced");
                else if (protectedSide && title?.DeJureHolderClanId == clan?.StringId)
                    Add(reasons, ref utility, strength == FeudalClaimStrength.Strong ? 25f : 15f, "Claim against personal title extinguished");
                else if (protectedSide)
                    Add(reasons, ref utility, strength == FeudalClaimStrength.Strong ? 8f : 4f, "Foreign claim extinguished");
            }
            else if (term.Type == TreatyTermType.DiscreditRuler || term.Type == TreatyTermType.HumiliateRuler)
            {
                Kingdom targetRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Clan targetRulingClan = targetRealm?.RulingClan;
                bool severe = term.Type == TreatyTermType.HumiliateRuler;
                if (clan == targetRulingClan)
                {
                    Add(reasons, ref utility, severe ? -90f : -50f,
                        severe ? "Ruler publicly humiliated" : "Ruler publicly discredited");
                }
                else if (clan?.Kingdom == targetRealm)
                {
                    int relation = clan.Leader?.GetRelation(targetRulingClan?.Leader) ?? 0;
                    if (relation <= -25)
                        Add(reasons, ref utility, severe ? 15f : 8f, "Disliked ruler rebuked");
                    else if (relation >= 25)
                        Add(reasons, ref utility, severe ? -25f : -15f, "Loyal ruler dishonored");
                    else
                        Add(reasons, ref utility, severe ? -10f : -5f, "Realm ruler rebuked");
                }
                else if (clan?.Kingdom?.StringId == term.ToKingdomId)
                {
                    Add(reasons, ref utility, severe ? 10f : 5f,
                        severe ? "Enemy ruler humbled" : "Enemy ruler discredited");
                }
            }
            else if (term.Type == TreatyTermType.ReleaseVassal)
            {
                Kingdom sourceRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Clan releasedLeader = Clan.All.FirstOrDefault(candidate => candidate?.StringId == term.ClanId);
                TreatyVassalReleaseCandidate release = TreatyRealmTransitionService.GetReleaseCandidates(sourceRealm)
                    .FirstOrDefault(candidate => candidate.LeaderClan == releasedLeader && candidate.RootTitle?.TitleId == term.TitleId);
                if (clan == releasedLeader)
                    Add(reasons, ref utility, 100f, "Independence secured by treaty");
                else if (release?.ClusterClans.Contains(clan) == true)
                    Add(reasons, ref utility, 50f, "Released with title liege");
                else if (clan == sourceRealm?.RulingClan)
                    Add(reasons, ref utility, -60f, "Vassal realm torn away");
                else if (clan?.Kingdom == sourceRealm)
                    Add(reasons, ref utility, -10f, "Realm weakened by vassal release");
                else if (clan?.Kingdom?.StringId == term.ToKingdomId)
                    Add(reasons, ref utility, 10f, "Enemy realm politically weakened");
            }
            else if (term.Type == TreatyTermType.ForceVassalization)
            {
                Kingdom defeatedRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom victorRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                if (clan == defeatedRealm?.RulingClan)
                    Add(reasons, ref utility, -100f, "Sovereignty surrendered");
                else if (clan?.Kingdom == defeatedRealm)
                    Add(reasons, ref utility, -50f, "Realm subjected to a foreign sovereign");
                else if (clan?.Kingdom == victorRealm)
                    Add(reasons, ref utility, clan == victorRealm.RulingClan ? 50f : 25f,
                        "Vassal realm restored to the hierarchy");
            }
            else if (term.Type == TreatyTermType.MakeClientKingdom)
            {
                Kingdom clientRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom suzerainRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                bool clientSide = clan?.Kingdom == clientRealm;
                bool suzerainSide = clan?.Kingdom == suzerainRealm;
                bool voluntary = term.WasVoluntaryOffering;

                if (clientSide)
                {
                    if (clan == clientRealm?.RulingClan)
                        Add(reasons, ref utility, voluntary ? -45f : -90f,
                            voluntary ? "Sovereignty offered for protection" : "Sovereignty constrained by foreign clientage");
                    else
                        Add(reasons, ref utility, voluntary ? -25f : -50f,
                            voluntary ? "Realm enters a foreign sphere" : "Realm subjected to foreign diplomacy");

                    int relation = clan?.Leader?.GetRelation(suzerainRealm?.RulingClan?.Leader) ?? 0;
                    float relationPressure = Math.Max(-15f, Math.Min(15f, relation * 0.15f));
                    if (relationPressure != 0f)
                        Add(reasons, ref utility, relationPressure,
                            relationPressure > 0f ? "Trusted prospective suzerain" : "Distrusted prospective suzerain");
                }
                else if (suzerainSide)
                {
                    Add(reasons, ref utility, clan == suzerainRealm?.RulingClan ? 60f : 25f,
                        clan == suzerainRealm?.RulingClan ? "Foreign realm submits to the crown" : "Client realm enlarges the realm's influence");
                }

                ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
                if (clientSide && clients?.IsLawfulClientage(clientRealm, suzerainRealm) == true)
                    Add(reasons, ref utility, 15f, "Rightful suzerainty restored");
                if (clientSide && clientRealm?.Culture != null && suzerainRealm?.Culture != null
                    && clientRealm.Culture != suzerainRealm.Culture)
                    Add(reasons, ref utility, -10f, "Foreign-culture suzerainty");
            }
            else if (term.Type == TreatyTermType.HostagePeace)
            {
                if (clan?.StringId == term.ClanId && clan.Leader != null && term.HostageTier >= 1 && term.HostageTier <= 4)
                {
                    Hero hostage = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == term.HeroId);
                    Add(reasons, ref utility, -(float)HostagePactRules.GetHouseReluctance(term.HostageTier,
                        clan.Leader.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy), hostage == null ? 0 : clan.Leader.GetRelation(hostage)),
                        "Our kin must stand hostage for the peace");
                }
            }
            else if (term.Type == TreatyTermType.ArrangeRoyalMarriage)
            {
                Kingdom concedingRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom receivingRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                Hero concedingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term.HeroId);
                Hero receivingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term.SecondaryHeroId);
                if (clan == receivingRealm?.RulingClan)
                    Add(reasons, ref utility, 25f, "Foreign royal spouse joins the dynasty");
                else if (clan == concedingRealm?.RulingClan)
                    Add(reasons, ref utility, 10f, "Royal marriage cements the peace");
                else if (clan?.Kingdom == receivingRealm || clan?.Kingdom == concedingRealm)
                    Add(reasons, ref utility, 5f, "Royal houses bound by marriage");

                if (clan?.Leader != null)
                {
                    Hero foreignSpouse = clan.Kingdom == concedingRealm ? receivingSpouse : concedingSpouse;
                    if (foreignSpouse != null)
                    {
                        int relation = clan.Leader.GetRelation(foreignSpouse);
                        if (relation != 0)
                        {
                            float relationPressure = Math.Max(-10f, Math.Min(10f, relation * 0.1f));
                            Add(reasons, ref utility, relationPressure,
                                relationPressure > 0f ? "Favored royal match" : "Disfavored royal match");
                        }
                    }
                }
            }
            else if (term.Type == TreatyTermType.EndTradeAgreement || term.Type == TreatyTermType.EndAlliance)
            {
                Kingdom sourceRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom beneficiaryRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                Kingdom thirdRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ThirdKingdomId);
                bool alliance = term.Type == TreatyTermType.EndAlliance;
                bool sourceSide = clan?.Kingdom == sourceRealm;
                bool beneficiarySide = clan?.Kingdom == beneficiaryRealm;

                if (sourceSide)
                {
                    Add(reasons, ref utility, alliance ? -15f : -8f,
                        alliance ? "Foreign alliance sacrificed" : "Trade relationship sacrificed");
                    if (clan == sourceRealm?.RulingClan)
                        Add(reasons, ref utility, alliance ? -10f : -5f, "Ruler compelled to break a diplomatic pledge");

                    int relation = clan?.Leader?.GetRelation(thirdRealm?.Leader) ?? 0;
                    float relationPressure = Math.Max(-15f, Math.Min(15f, -relation * 0.15f));
                    if (relationPressure != 0f)
                        Add(reasons, ref utility, relationPressure,
                            relationPressure > 0f ? "Hostility toward abandoned partner" : "Loyalty to abandoned partner");
                    if (MarriageAllianceHelper.HasMarriageAlliance(clan, thirdRealm?.RulingClan))
                        Add(reasons, ref utility, alliance ? -20f : -10f, "Marriage ties with abandoned partner");
                }
                else if (beneficiarySide)
                {
                    Add(reasons, ref utility, alliance ? 8f : 4f,
                        alliance ? "Enemy alliance dismantled" : "Enemy trade network weakened");
                    int relation = clan?.Leader?.GetRelation(thirdRealm?.Leader) ?? 0;
                    float relationPressure = Math.Max(-10f, Math.Min(10f, -relation * 0.10f));
                    if (relationPressure != 0f)
                        Add(reasons, ref utility, relationPressure,
                            relationPressure > 0f ? "Hostility toward isolated realm" : "Concern for isolated realm");
                    if (beneficiaryRealm?.IsAtWarWith(thirdRealm) == true)
                        Add(reasons, ref utility, alliance ? 15f : 8f, "Enemy partner isolated");
                }
            }
            else if (term.Type == TreatyTermType.ConcedeDefeat)
            {
                Kingdom defeatedRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom victoriousRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                if (clan?.Kingdom == victoriousRealm)
                    Add(reasons, ref utility, clan == victoriousRealm.RulingClan ? 30f : 15f, "Victory publicly acknowledged");
                else if (clan?.Kingdom == defeatedRealm)
                    Add(reasons, ref utility, clan == defeatedRealm.RulingClan ? -35f : -15f, "Realm concedes defeat");
            }
            else if (term.Type == TreatyTermType.ReleaseClientState)
            {
                Kingdom formerSuzerain = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom liberatingRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                if (clan?.Kingdom == formerSuzerain)
                    Add(reasons, ref utility, clan == formerSuzerain.RulingClan ? -30f : -10f, "Client realm relinquished");
                else if (clan?.Kingdom == liberatingRealm)
                    Add(reasons, ref utility, clan == liberatingRealm.RulingClan ? 20f : 8f, "Client realm liberated");
            }
            else if (term.Type == TreatyTermType.EnforceRebelDemands)
            {
                Kingdom parentRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.FromKingdomId);
                Kingdom enforcingRealm = Kingdom.All.FirstOrDefault(candidate => candidate?.StringId == term.ToKingdomId);
                if (clan?.Kingdom == parentRealm)
                    Add(reasons, ref utility, clan == parentRealm.RulingClan ? -100f : -40f, "Crown surrendered to rebels");
                else if (clan?.Kingdom == enforcingRealm)
                    Add(reasons, ref utility, clan == enforcingRealm.RulingClan ? 30f : 15f, "Enemy rebellion enforced");
            }
        }

        private static void Add(List<TreatyCouncilReason> reasons, ref float utility, float amount, string label)
        {
            utility += amount;
            if (Math.Abs(amount) < 0.01f)
                return;

            if (LocalizedReasonTemplates.TryGetValue(label ?? string.Empty, out string template))
                label = new TextObject(template).ToString();
            reasons.Add(new TreatyCouncilReason(label, amount));
        }

        private static IEnumerable<Clan> GetEligibleClans(Kingdom kingdom)
        {
            return kingdom?.Clans?.Where(clan => clan != null
                && !clan.IsEliminated
                && clan.Leader != null
                && !clan.IsUnderMercenaryService) ?? Enumerable.Empty<Clan>();
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private static Clan ResolveCaptorClan(Hero prisoner)
        {
            var party = prisoner?.PartyBelongedToAsPrisoner;
            if (party == null)
                return null;
            if (party.IsSettlement)
                return party.Settlement.OwnerClan;
            if (party.IsMobile)
                return party.MobileParty.ActualClan ?? party.Owner?.Clan;
            return party.Owner?.Clan;
        }
    }
}
