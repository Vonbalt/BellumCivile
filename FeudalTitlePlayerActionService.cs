using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public static class FeudalTitlePlayerActionService
    {
        public const float GrantInfluenceCost = 100f;

        public static TextObject GetRenunciationBlock(FeudalTitleRecord selectedTitle, out bool visible)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var title = titles?.GetTitle(selectedTitle?.TitleId);
            var clan = Clan.PlayerClan;
            visible = CanRenounceUncontrolledTitle(clan?.StringId, title)
                && titles.GetActiveClaims(clan, title).Any();
            if (!visible)
                return new TextObject("{=BC_Renounce_Unavailable}You must hold an existing claim to a title that your house does not control.");

            bool feud = Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>()?.GetActiveFeuds()
                .Any(r => r.TargetTitleId == title.TitleId && r.ClaimantClanId == clan.StringId) == true;
            bool rebellion = Kingdom.All.Any(realm => titles.GetRealmSovereignTitle(realm)?.TitleId == title.TitleId
                && FactionManagerBehavior.Instance?.GetFactionsInKingdom(realm)
                    .Any(f => f.Type == FactionType.InstallRuler && f.Leader == clan) == true);
            if (feud || rebellion
                || SuccessionChallengeBehavior.Instance?.IsPressingTitleClaim(clan, title, titles) == true
                || ElectiveContestBehavior.Instance?.IsPressingTitleClaim(clan, title, titles) == true)
                return new TextObject("{=BC_Renounce_Conflict}Settle or abandon the feud or claimant rebellion pressing this claim before renouncing it.");
            return null;
        }

        internal static bool CanRenounceUncontrolledTitle(string clanId, FeudalTitleRecord title)
            => !string.IsNullOrEmpty(clanId) && title?.IsActive == true
                && title.DeJureHolderClanId != clanId && title.DeFactoHolderClanId != clanId;

        public static bool TryExecuteRenunciation(FeudalTitleRecord selectedTitle, out TextObject reason)
        {
            reason = GetRenunciationBlock(selectedTitle, out bool visible);
            if (!visible || reason != null) return false;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles.TryRenounceExplicitClaim(Clan.PlayerClan, titles.GetTitle(selectedTitle.TitleId), out _, out _))
                return true;
            reason = new TextObject("{=BC_Renounce_Unavailable}You must hold an existing claim to a title that your house does not control.");
            return false;
        }
        public const float RevocationInfluenceCost = BellumCivileConstants.FeudalTitleRevocationInfluenceCost;

        public static FeudalUsurpationPreview GetUsurpationPreview(Clan clan, FeudalTitleRecord title)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleUsurpationAssessment assessment = FeudalTitleUsurpationAssessmentService.Evaluate(
                titleBehavior,
                clan,
                title,
                checkResources: true);
            FeudalUsurpationPreview preview = new FeudalUsurpationPreview
            {
                CanUsurp = assessment.CanUsurp,
                Reason = assessment.Reason,
                HasClaim = assessment.HasClaim,
                IsLawfulAssumption = assessment.IsLawfulAssumption,
                GoldCost = assessment.GoldCost,
                InfluenceCost = assessment.InfluenceCost,
                TotalTitles = assessment.TotalTitles,
                ControlledTitles = assessment.ControlledTitles,
                RequiredTitles = assessment.RequiredTitles,
                ControlShare = assessment.ControlShare
            };
            if (assessment.CanUsurp && titleBehavior != null)
                preview.SovereignElevation = titleBehavior.GetSovereignElevationPreview(clan, title);
            return preview;
        }

        public static FeudalFormationPreview GetFormationPreview(Clan clan, FeudalTitleRecord selectedTitle)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                return new FeudalFormationPreview
                {
                    BlockReason = FeudalTitleFormationBlockReason.Unavailable,
                    Reason = "title formation is unavailable"
                };
            }

            return titleBehavior.GetFormationAssessment(
                clan,
                selectedTitle,
                allowReorganization: true,
                checkResources: true);
        }

        public static FeudalDissolutionPreview GetDissolutionPreview(Clan clan, FeudalTitleRecord title)
        {
            FeudalDissolutionPreview preview = new FeudalDissolutionPreview { Reason = string.Empty };
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || title == null)
            {
                preview.Reason = "title dissolution is unavailable";
                return preview;
            }

            preview.IsVisible = title.TitleType > FeudalTitleType.Barony;
            preview.InfluenceCost = titleBehavior.GetTitleDissolutionInfluenceCost(title.TitleType);
            preview.DeJureChildren = titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeJure).Count;
            preview.DeFactoChildren = titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeFacto).Count;
            preview.CanDissolve = titleBehavior.CanDissolveTitle(clan, title, out string reason);
            preview.Reason = reason ?? string.Empty;
            return preview;
        }

        public static bool TryExecuteDissolution(Clan clan, FeudalTitleRecord title, out string reason)
        {
            reason = null;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title dissolution is unavailable";
                return false;
            }

            return titleBehavior.TryDissolveTitle(clan, title, chargeCost: true, out reason);
        }

        public static FeudalRenamePreview GetRenamePreview(Clan clan, FeudalTitleRecord title)
        {
            FeudalRenamePreview preview = new FeudalRenamePreview { Reason = string.Empty };
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                preview.Reason = "title renaming is unavailable";
                return preview;
            }

            preview.CanRename = titleBehavior.CanRenameTitle(clan, title, out string reason);
            preview.Reason = reason ?? string.Empty;
            return preview;
        }

        public static bool TryExecuteRename(Clan clan, FeudalTitleRecord title, string name, out string reason)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title renaming is unavailable";
                return false;
            }

            return titleBehavior.TryRenameTitle(clan, title, name, out reason);
        }

        public static FeudalServicePreview GetServicePreview(Clan clan, FeudalTitleRecord selectedTitle)
        {
            FeudalServicePreview preview = new FeudalServicePreview { Reason = string.Empty };
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalServiceBehavior serviceBehavior = Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            if (titleBehavior == null || serviceBehavior == null || selectedTitle == null || !selectedTitle.IsActive || selectedTitle.IsDeliberatelyDissolved)
            {
                preview.Reason = "service contracts are unavailable";
                return preview;
            }

            preview.ParentTitle = titleBehavior.GetParentTitle(selectedTitle, FeudalHierarchyMode.DeFacto);
            preview.CurrentLevel = serviceBehavior.GetServiceLevel(selectedTitle, preview.ParentTitle);
            if (clan?.Leader == null)
            {
                preview.Reason = "clan has no living leader";
                return preview;
            }

            if (preview.ParentTitle == null || !preview.ParentTitle.IsActive)
            {
                preview.Reason = "title has no immediate de facto liege";
                return preview;
            }

            if (titleBehavior.IsServiceDecoupledByRank(selectedTitle))
            {
                preview.Reason = "holder outranks or is a peer to the de jure liege";
                return preview;
            }

            if (selectedTitle.DeFactoHolderClanId == clan.StringId)
            {
                preview.Reason = "selected title is held directly by your clan";
                return preview;
            }

            if (selectedTitle.DeFactoHolderClanId == preview.ParentTitle.DeFactoHolderClanId)
            {
                preview.Reason = "selected title and liege title share the same holder";
                return preview;
            }

            if (preview.ParentTitle.DeFactoHolderClanId != clan.StringId)
            {
                preview.Reason = "selected title does not belong to your immediate vassal";
                return preview;
            }

            preview.CanChange = true;
            return preview;
        }

        public static FeudalGrantPreview GetGrantPreview(Clan clan, FeudalTitleRecord selectedTitle)
        {
            FeudalGrantPreview preview = new FeudalGrantPreview
            {
                Reason = string.Empty,
                InfluenceCost = GrantInfluenceCost
            };

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || selectedTitle == null || !selectedTitle.IsActive || selectedTitle.IsDeliberatelyDissolved)
            {
                preview.Reason = "title grant is unavailable";
                return preview;
            }

            if (clan?.Leader == null)
            {
                preview.Reason = "clan has no living leader";
                return preview;
            }

            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null || kingdom.RulingClan != clan)
            {
                preview.Reason = "grantor is not the ruling clan";
                return preview;
            }

            bool heldDeJure = selectedTitle.DeJureHolderClanId == clan.StringId;
            bool heldDeFacto = selectedTitle.DeFactoHolderClanId == clan.StringId;
            if (!heldDeJure && !heldDeFacto)
            {
                preview.Reason = "title is not held directly by your clan";
                return preview;
            }

            if (titleBehavior.IsRealmSovereignTitle(kingdom, selectedTitle))
            {
                preview.Reason = "the sovereign title of the realm cannot be granted";
                return preview;
            }

            List<Clan> recipients = kingdom.Clans
                .Where(candidate => candidate != null
                    && candidate != clan
                    && !candidate.IsEliminated
                    && !candidate.IsUnderMercenaryService
                    && candidate.Leader != null
                    && !candidate.Leader.IsDead)
                .OrderBy(candidate => candidate.Name?.ToString() ?? candidate.StringId)
                .ToList();

            foreach (Clan recipient in recipients)
                preview.Recipients.Add(BuildGrantRecipientPreview(titleBehavior, clan, recipient, selectedTitle));

            if (preview.Recipients.Count == 0)
            {
                preview.Reason = "no settled vassals";
                return preview;
            }

            if (clan.Influence < GrantInfluenceCost)
            {
                preview.Reason = "insufficient influence";
                return preview;
            }

            if (!preview.Recipients.Any(recipient => recipient.CanReceive))
            {
                preview.Reason = "no eligible vassals";
                return preview;
            }

            preview.CanOpen = true;
            return preview;
        }

        public static FeudalRevocationPreview GetRevocationPreview(Clan clan, FeudalTitleRecord selectedTitle)
        {
            FeudalRevocationPreview preview = new FeudalRevocationPreview
            {
                Reason = string.Empty,
                InfluenceCost = RevocationInfluenceCost
            };

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || selectedTitle == null || !selectedTitle.IsActive || selectedTitle.IsDeliberatelyDissolved)
            {
                preview.Reason = "title revocation is unavailable";
                return preview;
            }

            if (clan?.Leader == null || clan.Leader.IsDead)
            {
                preview.Reason = "clan has no living leader";
                return preview;
            }

            FeudalTitleRecord parent = titleBehavior.GetParentTitle(selectedTitle, FeudalHierarchyMode.DeFacto);
            if (parent == null || parent.DeFactoHolderClanId != clan.StringId)
            {
                preview.Reason = "selected title does not belong to your immediate vassal";
                return preview;
            }

            Clan holder = ResolveClan(selectedTitle.DeFactoHolderClanId);
            preview.HolderClan = holder;
            if (holder == null || holder == clan || holder.Leader == null || holder.Leader.IsDead)
            {
                preview.Reason = "title holder is not a valid vassal";
                return preview;
            }

            if (holder.Kingdom == null || clan.Kingdom == null || holder.Kingdom != clan.Kingdom)
            {
                preview.Reason = "title holder is not a settled vassal";
                return preview;
            }

            if (!TryGetActiveClaimStrength(titleBehavior, clan, selectedTitle, out FeudalClaimStrength strength))
            {
                preview.Reason = "no weak or strong claim";
                return preview;
            }

            preview.ClaimStrength = strength;
            if (clan.Influence < preview.InfluenceCost)
            {
                preview.Reason = "insufficient influence";
                return preview;
            }

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (feudBehavior == null)
            {
                preview.Reason = "claim feud behavior unavailable";
                return preview;
            }

            if (feudBehavior.HasActiveDisputeForTitle(selectedTitle.TitleId))
            {
                preview.Reason = "the title is already disputed by an active claim feud";
                return preview;
            }

            if (feudBehavior.IsClanCommittedToArmedClaimFeud(clan)
                || feudBehavior.IsClanCommittedToArmedClaimFeud(holder))
            {
                preview.Reason = "one side is already committed to an armed claim feud";
                return preview;
            }

            preview.HolderLikelyDefies = feudBehavior.WouldHolderDefyRevocation(
                titleBehavior,
                clan,
                holder,
                selectedTitle,
                strength,
                out float revokerPower,
                out float holderPower);
            preview.RevokerPower = revokerPower;
            preview.HolderPower = holderPower;
            preview.CanRevoke = true;
            return preview;
        }

        public static bool TryExecuteUsurpation(Clan clan, FeudalTitleRecord title, out string reason)
        {
            return TryExecuteUsurpation(clan, title, FeudalSovereignElevationChoice.NotApplicable, out reason);
        }

        public static bool TryExecuteUsurpation(
            Clan clan,
            FeudalTitleRecord title,
            FeudalSovereignElevationChoice elevationChoice,
            out string reason)
        {
            reason = null;
            FeudalUsurpationPreview preview = GetUsurpationPreview(clan, title);
            if (!preview.CanUsurp)
            {
                reason = preview.Reason;
                return false;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title behavior unavailable";
                return false;
            }

            FeudalSovereignElevationPreview elevation = preview.SovereignElevation;
            if (elevation?.IsRequired == true)
            {
                if (elevationChoice != FeudalSovereignElevationChoice.PeacefulSeparation
                    && elevationChoice != FeudalSovereignElevationChoice.RetainHoldingsAndRebel)
                {
                    reason = "choose how the new sovereign realm will separate from its former liege";
                    return false;
                }

                if (!elevation.HasExternalHoldings)
                    elevationChoice = FeudalSovereignElevationChoice.PeacefulSeparation;
            }
            else
            {
                elevationChoice = FeudalSovereignElevationChoice.NotApplicable;
            }

            Clan oldHolder = ResolveClan(title.DeJureHolderClanId);
            if (preview.GoldCost > 0)
                GiveGoldAction.ApplyBetweenCharacters(clan.Leader, null, preview.GoldCost, true);
            if (preview.InfluenceCost > 0f
                && !NpcInfluenceBudgetService.TrySpend(
                    clan,
                    preview.InfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "title_usurpation"))
            {
                if (preview.GoldCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, preview.GoldCost, true);
                reason = clan == Clan.PlayerClan ? "insufficient influence" : "insufficient influence reserve";
                return false;
            }

            if (!titleBehavior.TryUsurpTitle(clan, title, "player_hierarchy_action", out reason))
            {
                if (preview.GoldCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, preview.GoldCost, true);
                NpcInfluenceBudgetService.Refund(
                    clan,
                    preview.InfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "title_usurpation");
                return false;
            }

            Kingdom formerKingdom = elevation?.ParentKingdom;
            Kingdom independentKingdom = null;
            if (elevation?.IsRequired == true
                && !titleBehavior.TryPromoteSovereignTitleToIndependentRealm(
                    clan,
                    title,
                    elevationChoice,
                    "sovereign title usurpation",
                    out independentKingdom,
                    out reason))
            {
                BellumCivileLogger.Log(
                    $"Sovereign elevation failed after title usurpation; clan={clan.StringId}; title={title.TitleId}; choice={elevationChoice}; reason={reason ?? "unknown"}.");
                return false;
            }

            FeudalTitleUsurpationBehavior.ApplyUsurpationRelations(clan, oldHolder, title);

            NotificationHelper.ShowFeudalTitleUsurped(clan, oldHolder, title);
            if (independentKingdom != null)
            {
                NotificationHelper.ShowSovereignTitleElevated(
                    clan,
                    formerKingdom,
                    independentKingdom,
                    title,
                    elevationChoice == FeudalSovereignElevationChoice.RetainHoldingsAndRebel);
            }
            BellumCivileLogger.Log($"Player usurped feudal title; clan={clan.StringId}; title={title.TitleId}; gold={preview.GoldCost}; influence={preview.InfluenceCost:0}.");
            return true;
        }

        public static bool TrySetServiceLevel(Clan clan, FeudalTitleRecord title, FeudalServiceLevel level, out string reason)
        {
            reason = null;
            FeudalServicePreview preview = GetServicePreview(clan, title);
            if (!preview.CanChange)
            {
                reason = preview.Reason;
                return false;
            }

            FeudalServiceBehavior serviceBehavior = Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            if (serviceBehavior == null)
            {
                reason = "service contracts are unavailable";
                return false;
            }

            serviceBehavior.SetServiceLevel(title, preview.ParentTitle, level, clan, "player_hierarchy_action");
            BellumCivileLogger.Log($"Player changed feudal service; clan={clan?.StringId ?? "null"}; child={title?.TitleId}; parent={preview.ParentTitle?.TitleId}; level={level}.");
            return true;
        }

        public static bool TryExecuteGrant(Clan grantorClan, FeudalTitleRecord title, Clan recipientClan, out FeudalGrantResult result, out string reason)
        {
            result = null;
            reason = null;

            FeudalGrantPreview preview = GetGrantPreview(grantorClan, title);
            FeudalGrantRecipientPreview recipientPreview = preview.Recipients.FirstOrDefault(recipient => recipient.Clan == recipientClan);
            if (!preview.CanOpen || recipientPreview == null || !recipientPreview.CanReceive)
            {
                reason = recipientPreview?.Reason ?? preview.Reason;
                return false;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title behavior unavailable";
                return false;
            }

            Kingdom oldKingdom = grantorClan.Kingdom;
            if (GrantInfluenceCost > 0f
                && !NpcInfluenceBudgetService.TrySpend(
                    grantorClan,
                    GrantInfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "title_grant"))
            {
                reason = grantorClan == Clan.PlayerClan ? "insufficient influence" : "insufficient influence reserve";
                return false;
            }

            if (!titleBehavior.TryGrantTitleByRuler(
                grantorClan,
                recipientClan,
                title,
                recipientPreview.CreatesIndependentRealm,
                out Kingdom independentKingdom,
                out reason))
            {
                NpcInfluenceBudgetService.Refund(
                    grantorClan,
                    GrantInfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "title_grant");
                return false;
            }

            if (grantorClan.Leader != null && recipientClan.Leader != null && recipientPreview.RelationGain > 0)
                RelationMemoryService.ApplyChange(grantorClan.Leader, recipientClan.Leader, recipientPreview.RelationGain, true,
                    RelationMemorySources.CourtTitleGrant, 10f, RelationMemoryScope.Personal,
                    FeudalTitleDisplayHelper.FormatTitleName(title, recipientClan));

            ApplyGrantMoodFallout(oldKingdom, recipientClan, recipientPreview.RelationGain);
            NotificationHelper.ShowFeudalTitleGranted(grantorClan, recipientClan, title, recipientPreview.CreatesIndependentRealm, recipientPreview.RelationGain);

            result = new FeudalGrantResult
            {
                Title = title,
                RecipientClan = recipientClan,
                NewIndependentKingdom = independentKingdom,
                CreatedIndependentRealm = recipientPreview.CreatesIndependentRealm,
                TransferredDeJure = recipientPreview.TransfersDeJure,
                TransferredDeFacto = recipientPreview.TransfersDeFacto,
                RelationGain = recipientPreview.RelationGain
            };

            BellumCivileLogger.Log($"Player granted feudal title; grantor={grantorClan?.StringId ?? "null"}; recipient={recipientClan?.StringId ?? "null"}; title={title?.TitleId ?? "null"}; independent={independentKingdom?.StringId ?? "no"}; relation={recipientPreview.RelationGain}; influence={GrantInfluenceCost:0}.");
            return true;
        }

        public static bool TryExecuteRevocation(Clan revokerClan, FeudalTitleRecord title, out bool holderDefied, out string reason)
        {
            FeudalRevocationPreview preview = GetRevocationPreview(revokerClan, title);
            return TryExecuteRevocationInternal(revokerClan, title, preview, preview.HolderLikelyDefies, out holderDefied, out reason);
        }

        public static bool TryExecuteRevocationWithResponse(Clan revokerClan, FeudalTitleRecord title, bool holderDefies, out bool holderDefied, out string reason)
        {
            FeudalRevocationPreview preview = GetRevocationPreview(revokerClan, title);
            return TryExecuteRevocationInternal(revokerClan, title, preview, holderDefies, out holderDefied, out reason);
        }

        private static bool TryExecuteRevocationInternal(Clan revokerClan, FeudalTitleRecord title, FeudalRevocationPreview preview, bool holderDefies, out bool holderDefied, out string reason)
        {
            holderDefied = false;
            reason = null;

            if (!preview.CanRevoke)
            {
                reason = preview.Reason;
                return false;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (titleBehavior == null || feudBehavior == null)
            {
                reason = "title revocation is unavailable";
                return false;
            }

            if (preview.InfluenceCost > 0f
                && !NpcInfluenceBudgetService.TrySpend(
                    revokerClan,
                    preview.InfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "claimed_title_revocation"))
            {
                reason = revokerClan == Clan.PlayerClan ? "insufficient influence" : "insufficient influence reserve";
                return false;
            }

            Clan holder = preview.HolderClan;

            if (holderDefies)
            {
                if (!feudBehavior.TryStartImmediateRevocationWar(
                    titleBehavior,
                    revokerClan,
                    holder,
                    title,
                    preview.ClaimStrength,
                    out _,
                    out reason))
                {
                    NpcInfluenceBudgetService.Refund(
                        revokerClan,
                        preview.InfluenceCost,
                        NpcInfluenceExpenseKind.Discretionary,
                        "claimed_title_revocation");
                    return false;
                }

                ApplyRevocationRelationPenalty(revokerClan, holder, preview.ClaimStrength);
                holderDefied = true;
                NotificationHelper.ShowFeudalTitleRevocationDefied(revokerClan, holder, title);
                BellumCivileLogger.Log($"Feudal title revocation defied; revoker={revokerClan.StringId}; holder={holder.StringId}; title={title.TitleId}; strength={preview.ClaimStrength}; influence={preview.InfluenceCost:0}.");
                return true;
            }

            if (!titleBehavior.TryResolveClaimFeudForClaimant(revokerClan, holder, title, "claimed_title_revocation", out reason))
            {
                NpcInfluenceBudgetService.Refund(
                    revokerClan,
                    preview.InfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "claimed_title_revocation");
                return false;
            }

            ApplyRevocationRelationPenalty(revokerClan, holder, preview.ClaimStrength);
            NotificationHelper.ShowFeudalTitleRevoked(revokerClan, holder, title);
            BellumCivileLogger.Log($"Feudal title revoked by claim; revoker={revokerClan.StringId}; holder={holder.StringId}; title={title.TitleId}; strength={preview.ClaimStrength}; influence={preview.InfluenceCost:0}.");
            return true;
        }

        public static TextObject GetServiceLevelName(FeudalServiceLevel level)
        {
            switch (level)
            {
                case FeudalServiceLevel.Exemption:
                    return new TextObject("{=BC_FeudalService_Exemption}Exemption");
                case FeudalServiceLevel.Lessened:
                    return new TextObject("{=BC_FeudalService_Lessened}Lessened");
                case FeudalServiceLevel.Elevated:
                    return new TextObject("{=BC_FeudalService_Elevated}Elevated");
                case FeudalServiceLevel.Extortion:
                    return new TextObject("{=BC_FeudalService_Extortion}Extortion");
                default:
                    return new TextObject("{=BC_FeudalService_Customary}Customary");
            }
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static bool TryGetActiveClaimStrength(FeudalTitleBehavior titleBehavior, Clan clan, FeudalTitleRecord title, out FeudalClaimStrength strength)
        {
            strength = FeudalClaimStrength.Weak;
            if (titleBehavior == null || clan == null || title == null)
                return false;

            if (title.DeJureHolderClanId == clan.StringId)
            {
                strength = FeudalClaimStrength.Strong;
                return true;
            }

            FeudalClaimRecord claim = titleBehavior.GetStrongestActiveClaim(clan, title);
            if (claim == null)
                return false;

            strength = claim.Strength;
            return true;
        }

        private static void ApplyRevocationRelationPenalty(Clan revokerClan, Clan holderClan, FeudalClaimStrength strength)
        {
            if (revokerClan?.Leader == null || holderClan?.Leader == null || revokerClan == holderClan)
                return;

            int penalty = strength == FeudalClaimStrength.Strong
                ? BellumCivileConstants.FeudalTitleRevocationStrongRelationPenalty
                : BellumCivileConstants.FeudalTitleRevocationWeakRelationPenalty;
            RelationMemoryService.ApplyChangeWithDefaultDuration(revokerClan.Leader, holderClan.Leader, penalty, false,
                RelationMemorySources.RevokedMyTitle, RelationMemoryScope.House);
        }

        private static FeudalGrantRecipientPreview BuildGrantRecipientPreview(FeudalTitleBehavior titleBehavior, Clan grantorClan, Clan recipientClan, FeudalTitleRecord title)
        {
            FeudalGrantRecipientPreview preview = new FeudalGrantRecipientPreview
            {
                Clan = recipientClan,
                Reason = string.Empty,
                TransfersDeJure = title?.DeJureHolderClanId == grantorClan?.StringId,
                TransfersDeFacto = title?.DeFactoHolderClanId == grantorClan?.StringId
            };

            if (titleBehavior == null || grantorClan == null || recipientClan == null || title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                preview.Reason = "title grant is unavailable";
                return preview;
            }

            if (recipientClan.Kingdom != grantorClan.Kingdom || recipientClan.IsUnderMercenaryService || recipientClan.IsEliminated)
            {
                preview.Reason = "recipient is not a settled vassal";
                return preview;
            }

            if (title.TitleType > FeudalTitleType.Barony && !RecipientHoldsImmediateChildTitle(titleBehavior, recipientClan, title))
            {
                preview.Reason = "recipient holds no immediate child title";
                return preview;
            }

            FeudalTitleRecord sovereignTitle = titleBehavior.GetRealmSovereignTitle(grantorClan.Kingdom, FeudalHierarchyMode.DeFacto)
                                           ?? titleBehavior.GetKingdomPoliticalTitle(grantorClan.Kingdom);
            preview.CreatesIndependentRealm = sovereignTitle != null
                && title.TitleType == sovereignTitle.TitleType
                && title.TitleId != sovereignTitle.TitleId;
            preview.HadClaim = titleBehavior.HasActiveClaim(recipientClan, title);
            preview.RelationGain = CalculateGrantRelationGain(title.TitleType, preview.HadClaim, preview.TransfersDeJure && preview.TransfersDeFacto, preview.CreatesIndependentRealm);
            preview.CanReceive = true;
            return preview;
        }

        private static bool RecipientHoldsImmediateChildTitle(FeudalTitleBehavior titleBehavior, Clan recipientClan, FeudalTitleRecord title)
        {
            if (titleBehavior == null || recipientClan == null || title == null)
                return false;

            return titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeFacto)
                .Concat(titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeJure))
                .Where(child => child != null && child.IsActive)
                .GroupBy(child => child.TitleId)
                .Select(group => group.First())
                .Any(child => child.DeFactoHolderClanId == recipientClan.StringId || child.DeJureHolderClanId == recipientClan.StringId);
        }

        private static int CalculateGrantRelationGain(FeudalTitleType type, bool recipientHadClaim, bool fullLegalTransfer, bool createsIndependentRealm)
        {
            int gain;
            switch (type)
            {
                case FeudalTitleType.County:
                    gain = 15;
                    break;
                case FeudalTitleType.Duchy:
                    gain = 20;
                    break;
                case FeudalTitleType.Kingdom:
                    gain = 25;
                    break;
                case FeudalTitleType.Empire:
                    gain = 30;
                    break;
                default:
                    gain = 10;
                    break;
            }

            if (recipientHadClaim)
                gain += 5;
            if (fullLegalTransfer)
                gain += 5;
            if (createsIndependentRealm)
                gain *= 2;
            return gain;
        }

        private static void ApplyGrantMoodFallout(Kingdom kingdom, Clan recipientClan, int relationGain)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null || kingdom == null || recipientClan == null)
                return;

            FactionObject recipientFaction = factionManager.GetIdeologicalFaction(recipientClan);
            float happyDelta = Math.Max(5f, relationGain * 0.5f);
            float displeasedDelta = Math.Max(3f, relationGain * 0.25f);
            foreach (FactionObject faction in factionManager.GetFactionsInKingdom(kingdom).Where(faction => faction != null && faction.IsIdeology))
            {
                if (faction == recipientFaction)
                    faction.Mood = ClampMood(faction.Mood + happyDelta);
                else
                    faction.Mood = ClampMood(faction.Mood - displeasedDelta);
            }
        }

        private static float ClampMood(float value)
        {
            if (value < -100f)
                return -100f;
            return value > 100f ? 100f : value;
        }
    }

    public sealed class FeudalServicePreview
    {
        public bool CanChange { get; set; }
        public string Reason { get; set; }
        public FeudalTitleRecord ParentTitle { get; set; }
        public FeudalServiceLevel CurrentLevel { get; set; } = FeudalServiceBehavior.DefaultLevel;
    }
}
