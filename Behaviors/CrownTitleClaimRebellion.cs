using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal enum CrownTitleClaimState { Invalid, Valid, Fulfilled }
    internal enum CrownTitleClaimSettlement { None, RebelVictory, LoyalistVictory, WhitePeace }

    public partial class FactionObject
    {
        [SaveableField(39)] private string _crownClaimTitleId;
        [SaveableField(40)] private Clan _crownClaimant;
        [SaveableField(41)] private Clan _crownClaimOriginalHolder;
        [SaveableField(42)] private string _crownClaimLegalOwnerId;
        [SaveableField(43)] private List<WarScoreFiefSnapshotRecord> _crownClaimStartingFiefs;
        [SaveableField(44)] private List<string> _crownClaimOccupations;
        [SaveableField(45)] private int _crownClaimPendingSettlement;
        [SaveableField(46)] private string _crownClaimLastLoyalistStrongholdId;

        internal string CrownClaimTitleId => _crownClaimTitleId;
        internal Clan CrownClaimOriginalHolder => _crownClaimOriginalHolder;
        internal CrownTitleClaimSettlement PendingCrownClaimSettlement
        {
            get => (CrownTitleClaimSettlement)_crownClaimPendingSettlement;
            set => _crownClaimPendingSettlement = (int)value;
        }
        internal FeudalTitleRecord CrownClaimTitle => Behaviors.FeudalTitleBehavior.Instance?.GetTitle(_crownClaimTitleId);
        internal TextObject CrownClaimTitleName() => CrownClaimTitle is FeudalTitleRecord title
            ? new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title))
            : new TextObject("{=BC_CrownTitleClaim_Unknown}the disputed title");

        internal TextObject CrownClaimDemand()
            => new TextObject("{=BC_CrownTitleClaim_Demand}surrender {TITLE} to {HOUSE}, without yielding the throne")
                .SetTextVariable("TITLE", CrownClaimTitleName())
                .SetTextVariable("HOUSE", _crownClaimant?.Name ?? Leader?.Name);

        internal void BindCrownTitleClaim(FeudalTitleRecord title)
        {
            _crownClaimTitleId = title.TitleId;
            _crownClaimLegalOwnerId = title.DeJureHolderClanId;
            _crownClaimOriginalHolder = ParentKingdom.RulingClan;
            _crownClaimant = Leader;
        }

        internal bool CrownClaimIsOccupation(string settlementId) => _crownClaimOccupations?.Contains(settlementId) == true;
        internal bool HasCrownClaimBaseline(string settlementId) => _crownClaimStartingFiefs?.Any(f => f.SettlementId == settlementId) == true;
        internal bool HasDecisiveCrownClaimOccupation(Kingdom rebel)
            => !string.IsNullOrEmpty(_crownClaimLastLoyalistStrongholdId)
                && Behaviors.FeudalTitleBehavior.FindCrownClaimSettlement(_crownClaimLastLoyalistStrongholdId)?.OwnerClan?.Kingdom == rebel;

        internal void CaptureCrownClaimStartingFiefs()
        {
            if (Type != FactionType.CrownTitleClaim || _crownClaimStartingFiefs != null) return;
            _crownClaimStartingFiefs = ParentKingdom.Settlements
                .Where(s => s?.IsFortification == true && s.OwnerClan != null)
                .Select(s => new WarScoreFiefSnapshotRecord(s.StringId, ParentKingdom.StringId,
                    s.OwnerClan.StringId, s.IsTown, s.IsCastle)).ToList();
            _crownClaimOccupations = new List<string>();
        }

        internal void ObserveCrownClaimOccupation(Settlement settlement, Clan oldOwner, Clan newOwner,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail, Kingdom rebel)
        {
            if (oldOwner == newOwner || !HasCrownClaimBaseline(settlement.StringId)) return;
            Kingdom from = oldOwner?.Kingdom, to = newOwner?.Kingdom;
            if ((from != ParentKingdom && from != rebel) || (to != ParentKingdom && to != rebel))
            {
                // Once a third realm intervenes, this war cannot award or restore that fief.
                _crownClaimStartingFiefs.RemoveAll(f => f.SettlementId == settlement.StringId);
                _crownClaimOccupations?.Remove(settlement.StringId);
                if (_crownClaimLastLoyalistStrongholdId == settlement.StringId)
                    _crownClaimLastLoyalistStrongholdId = null;
                return;
            }

            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege || from == to)
                return;
            _crownClaimOccupations = _crownClaimOccupations ?? new List<string>();
            if (!_crownClaimOccupations.Contains(settlement.StringId))
                _crownClaimOccupations.Add(settlement.StringId);
            if (from == ParentKingdom && to == rebel && ParentKingdom.Settlements?.Any(s =>
                s?.IsFortification == true && s.OwnerClan?.Kingdom == ParentKingdom) == false)
                _crownClaimLastLoyalistStrongholdId = settlement.StringId;
        }

        internal IEnumerable<WarScoreFiefSnapshotRecord> CrownClaimOccupiedFiefs()
            => (_crownClaimStartingFiefs ?? Enumerable.Empty<WarScoreFiefSnapshotRecord>())
                .Where(fief => CrownClaimIsOccupation(fief.SettlementId)).ToList();

        internal void NoteCrownClaimRestored(string settlementId) => _crownClaimOccupations?.Remove(settlementId);

        internal CrownTitleClaimState CrownClaimState(Kingdom rebel = null)
        {
            var titles = Behaviors.FeudalTitleBehavior.Instance;
            FeudalTitleRecord title = CrownClaimTitle;
            if (!Behaviors.FactionManagerBehavior.IsCrownClaimSubordinate(titles, ParentKingdom, _crownClaimant, title)
                || _crownClaimant != Leader || !_members.Contains(_crownClaimant)
                || !NobleClanEligibilityHelper.IsLiveNobleClan(_crownClaimant)
                || (_crownClaimant.Kingdom != ParentKingdom && (rebel == null || _crownClaimant.Kingdom != rebel)))
                return CrownTitleClaimState.Invalid;

            bool isBarony = title.TitleType == FeudalTitleType.Barony;
            Settlement holding = isBarony ? Behaviors.FeudalTitleBehavior.FindCrownClaimSettlement(title.CapitalSettlementId) : null;
            if (isBarony && (holding?.OwnerClan == null || holding.OwnerClan.StringId != title.DeFactoHolderClanId))
                return CrownTitleClaimState.Invalid;
            bool occupied = isBarony && CrownClaimIsOccupation(holding.StringId);
            if (!occupied && title.DeJureHolderClanId == _crownClaimant.StringId
                && title.DeFactoHolderClanId == _crownClaimant.StringId)
                return CrownTitleClaimState.Fulfilled;

            if (_crownClaimOriginalHolder != ParentKingdom.RulingClan || title.DeJureHolderClanId != _crownClaimLegalOwnerId
                || Behaviors.FactionManagerBehavior.GetCrownClaimStrength(titles, _crownClaimant, title) == null
                || (rebel != null && isBarony && !HasCrownClaimBaseline(holding.StringId)))
                return CrownTitleClaimState.Invalid;
            if (title.DeFactoHolderClanId == _crownClaimOriginalHolder.StringId
                || (rebel != null && occupied && (holding.OwnerClan.Kingdom == rebel || holding.OwnerClan.Kingdom == ParentKingdom)))
                return CrownTitleClaimState.Valid;
            return CrownTitleClaimState.Invalid;
        }

        private bool TrySettleAcceptedCrownClaim()
            => CrownClaimState() == CrownTitleClaimState.Valid
                && Behaviors.FeudalTitleBehavior.Instance.TryAwardCrownClaim(Leader, CrownClaimOriginalHolder, CrownClaimTitle);
    }
}

namespace BellumCivile.Behaviors
{
    public partial class FactionManagerBehavior
    {
        internal static bool IsCrownClaimSubordinate(FeudalTitleBehavior titles, Kingdom realm, Clan claimant, FeudalTitleRecord title)
        {
            if (titles == null || realm?.IsEliminated != false || claimant == null || claimant == realm.RulingClan
                || title?.IsActive != true || title.IsDeliberatelyDissolved
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) return false;
            var sovereign = titles.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto) ?? titles.GetKingdomPoliticalTitle(realm);
            return sovereign != null && title.TitleType < sovereign.TitleType && !titles.IsCurrentRealmSovereignTitle(claimant, title);
        }

        internal static FeudalClaimStrength? GetCrownClaimStrength(FeudalTitleBehavior titles, Clan claimant, FeudalTitleRecord title)
            => title.DeJureHolderClanId == claimant.StringId ? FeudalClaimStrength.Strong
                : titles.GetStrongestActiveClaim(claimant, title)?.Strength;

        internal static IReadOnlyList<FeudalTitleRecord> GetCrownClaimTargets(Clan claimant)
        {
            FeudalTitleBehavior titles = FeudalTitleBehavior.Instance;
            Clan crown = claimant?.Kingdom?.RulingClan;
            if (titles == null || crown == null || claimant == crown) return new List<FeudalTitleRecord>();
            return titles.GetActiveClaimsByClan(claimant).Select(c => titles.GetTitle(c.TargetTitleId))
                .Concat(titles.GetTitlesHeldByClan(claimant, deJure: true))
                .Where(t => t?.DeFactoHolderClanId == crown.StringId)
                .Distinct().ToList();
        }

        internal TextObject GetCrownClaimBlock(Clan claimant, FeudalTitleRecord title, string expectedCrownId)
        {
            var titles = FeudalTitleBehavior.Instance;
            Kingdom realm = claimant?.Kingdom;
            Clan crown = realm?.RulingClan;
            if (crown == null || crown.StringId != expectedCrownId || !NobleClanEligibilityHelper.IsLiveNobleClan(claimant)
                || !IsCrownClaimSubordinate(titles, realm, claimant, title)
                || title.DeFactoHolderClanId != crown.StringId)
                return new TextObject("{=BC_CrownTitleClaim_Unavailable}You need a claim to a subordinate title in the Crown's possession.");
            if (title.DeJureHolderClanId != crown.StringId && title.DeJureHolderClanId != claimant.StringId)
                return new TextObject("{=BC_CrownTitleClaim_ThirdParty}Another house holds this title by law. Its rights cannot be settled by this demand.");
            if (GetCrownClaimStrength(titles, claimant, title) == null)
                return new TextObject("{=BC_CrownTitleClaim_NoClaim}Your house must hold this title de jure or have an active weak or strong claim to it.");
            if (title.TitleType == FeudalTitleType.Barony
                && FeudalTitleBehavior.FindCrownClaimSettlement(title.CapitalSettlementId)?.OwnerClan != crown)
                return new TextObject("{=BC_CrownTitleClaim_Unavailable}You need a claim to a subordinate title in the Crown's possession.");
            if (GetRebelFaction(claimant) != null)
                return new TextObject("{=BC_UI_Err_AlreadyRebel}You are already participating in a rebellion.");
            if (_activeFactions.Any(f => f.Type == FactionType.CrownTitleClaim && f.ParentKingdom == realm && f.CrownClaimTitleId == title.TitleId))
                return new TextObject("{=BC_CrownTitleClaim_Duplicate}A rebel faction is already pressing a claim to this title.");
            return CivilWarGraceBlock(realm);
        }

        internal FactionObject TryFoundCrownClaim(Clan claimant, FeudalTitleRecord title, string crownId, out TextObject block)
        {
            block = GetCrownClaimBlock(claimant, title, crownId);
            if (block != null) return null;
            var faction = new FactionObject("Crown title claim: " + title.TitleId, claimant.Kingdom, claimant, FactionType.CrownTitleClaim);
            faction.BindCrownTitleClaim(title);
            RegisterNewFaction(faction);
            return _activeFactions.Contains(faction) ? faction : null;
        }

        internal IEnumerable<FactionObject> GetCrownTitleClaimFactions()
            => _activeFactions.Where(f => f.Type == FactionType.CrownTitleClaim);

        private void ObserveCrownClaimFiefChange(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement?.IsFortification != true) return;
            foreach (var faction in _activeFactions.Where(f => f.Type == FactionType.CrownTitleClaim && f.HasTrackedRebelKingdom).ToList())
            {
                Kingdom rebel = faction.GetTrackedRebelKingdomIncludingEliminated();
                if (rebel != null)
                    faction.ObserveCrownClaimOccupation(settlement, oldOwner?.Clan, newOwner?.Clan ?? settlement.OwnerClan, detail, rebel);
            }
        }
    }

    public partial class FeudalTitleBehavior
    {
        internal static Settlement FindCrownClaimSettlement(string id) => ResolveSettlement(id);

        internal bool TryAwardCrownClaim(Clan claimant, Clan crown, FeudalTitleRecord title)
        {
            if (!NobleClanEligibilityHelper.IsLiveNobleClan(claimant) || crown == null || claimant == crown
                || title?.IsActive != true || title.IsDeliberatelyDissolved) return false;
            string oldLegal = title.DeJureHolderClanId, oldActual = title.DeFactoHolderClanId;
            if (oldLegal != crown.StringId && oldLegal != claimant.StringId) return false;

            if (title.TitleType == FeudalTitleType.Barony)
            {
                Settlement holding = FindCrownClaimSettlement(title.CapitalSettlementId);
                Clan current = holding?.OwnerClan;
                if (current == null || current.StringId != oldActual
                    || holding.SiegeEvent != null || holding.Party?.MapEvent != null) return false;
                if (current != claimant && !TransferChallengePossession(current, claimant, holding)) return false;
                if (holding.Town != null) holding.Town.IsOwnerUnassigned = false;
                FiefDeliberationBehavior.Current?.ClearFiefVoteStateForSettlement(holding, "Crown title claim enforced");
            }
            else if (oldActual != crown.StringId && oldActual != claimant.StringId) return false;

            title.SetDeFactoHolder(claimant.StringId);
            if (oldLegal == crown.StringId)
            {
                title.SetDeJureHolder(claimant.StringId);
                RemoveRedundantClaimsForHolder(claimant, title);
                if (NobleClanEligibilityHelper.IsLiveNobleClan(crown))
                    RegisterClaim(crown, title, FeudalClaimStrength.Weak, "crown_title_claim_dispossessed", claimant.Leader,
                        crown, carrierHero: crown.Leader, generationDepth: 1);
            }
            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "Crown title claim enforced");
            ReconcileReorganizedTitles(new[] { title.TitleId });
            BellumCivileLogger.Log($"Crown title claim enforced; title={title.TitleId}; house={claimant.StringId}; former_legal={oldLegal}; former_actual={oldActual}.");
            TitleAcquisitionBehavior.Acquired(claimant, title, oldLegal, oldActual);
            return true;
        }

        internal void RejectCrownTitleClaim(Clan claimant, Clan crown, FeudalTitleRecord title)
        {
            if (claimant == null || crown == null || title?.IsActive != true || title.DeFactoHolderClanId != crown.StringId
                || title.DeJureHolderClanId != crown.StringId) return;
            var claim = GetStrongestActiveClaim(claimant, title);
            if (claim == null) return;
            DeactivateClaimsForTitle(claimant, title);
            if (claim.Strength == FeudalClaimStrength.Strong && NobleClanEligibilityHelper.IsLiveNobleClan(claimant))
                RegisterClaim(claimant, title, FeudalClaimStrength.Weak, "crown_title_claim_defeated", crown.Leader,
                    claimant, carrierHero: claimant.Leader, generationDepth: 2);
        }
    }
}
