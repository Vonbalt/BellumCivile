using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile
{
    public enum FactionType { Independence = 0, Abdication = 1, InstallRuler = 2, Royalists = 4, Glory = 5, Nobility = 6, Liberty = 7 }

    public enum UltimatumResolution
    {
        Failed,
        PendingPlayerChoice,
        DemandsAccepted,
        RebellionStarted
    }

    /// <summary>
    /// Why did I do this file?
    /// To serve as the fundamental data structure of the mod, representing both armed rebellions and permanent ideological parties. It handles rosters, power calculations, and the execution of demands.
    /// </summary>
    public class FactionObject
    {
        private const int AcceptedUltimatumPacifiedDays = 30;

        [SaveableField(1)] private string _name;
        [SaveableField(2)] private Kingdom _parentKingdom;
        [SaveableField(3)] private Clan _leader;
        [SaveableField(4)] private List<Clan> _members;
        [SaveableField(5)] private FactionType _type;
        [SaveableField(6)] private float _discontent;
        [SaveableField(7)] private float _mood; 
        [SaveableField(8)] private Clan _loyalClan; 
        [SaveableField(9)] private CampaignTime _creationDate;
        [SaveableField(10)] private Kingdom _rebelKingdom;
        [SaveableField(11)] private string _rebelKingdomStringId;
        [SaveableField(12)] private List<Clan> _civilWarStartFiefClans;
        [SaveableField(13)] private List<int> _civilWarStartFiefCounts;
        [SaveableField(14)] private List<Clan> _civilWarStartInfluenceClans;
        [SaveableField(15)] private List<float> _civilWarStartInfluenceValues;
        [SaveableField(16)] private bool _isGrandCoalition;
        [SaveableField(17)] private FactionType _grandCoalitionSourceIdeology;
        [SaveableField(18)] private bool _hasGrandCoalitionSourceIdeology;
        [SaveableField(19)] private bool _ultimatumPending;
        [SaveableField(20)] private bool _ultimatumResolved;
        [SaveableField(21)] private bool _solidarityRecruitmentApplied;
        [SaveableField(22)] private bool _rebellionCreationStarted;
        [SaveableField(23)] private bool _rebellionCreationCompleted;
        [SaveableField(24)] private float _peakDiscontent;
        [SaveableField(25)] private Clan _declaredLoyalist;
        [SaveableField(26)] private Hero _abdicationMonarch;
        [SaveableField(27)] private bool _hereditaryAbdication;
        [SaveableField(28)] private HouseSuccessionLaw _abdicationHouseLaw;
        [SaveableField(29)] private GenderSuccessionLaw _abdicationGenderLaw;
        [SaveableField(30)] private string _abdicationCauseId;
        [SaveableField(31)] private string _successionChallengeId;
        [SaveableField(32)] private bool _challengeShellInitialized;
        [SaveableField(33)] private bool _challengeWarRegistered;
        [SaveableField(34)] private bool _challengeWarAnnounced;
        [SaveableField(35)] private string _civilWarOriginShellPrefix;
        [SaveableField(36)] internal CourtAppeasementRecord CrownAccommodation;
        public bool IsChallengeStartupPending => !string.IsNullOrEmpty(_successionChallengeId) && !_rebellionCreationCompleted;
        internal void BindSuccessionChallenge(string id)
        {
            if (_successionChallengeId != null && _successionChallengeId != id) throw new InvalidOperationException("Challenge identity changed");
            _successionChallengeId = id;
            _ultimatumResolved = true;
            _solidarityRecruitmentApplied = true;
        }
        internal bool StartFrozenSuccessionRebellion(out string failure)
        {
            failure = "Missing Install Ruler challenge identity";
            return Type == FactionType.InstallRuler && !string.IsNullOrEmpty(_successionChallengeId)
                && StartOpenRebellion(null, false, out failure, resumeChallenge: true);
        }
        public string AbdicationCauseId => _abdicationCauseId;
        public Hero AbdicationMonarch => _abdicationMonarch;
        public bool IsHereditaryAbdication => Type == FactionType.Abdication && _hereditaryAbdication;
        public SuccessionLawSet AbdicationLaws => new SuccessionLawSet(_abdicationGenderLaw, _abdicationHouseLaw);

        internal bool HasLoyaltyDeclaration(Clan clan) => clan != null && _declaredLoyalist == clan && clan.Kingdom == ParentKingdom;
        internal void RecordLoyaltyDeclaration(Clan clan)
        {
            if (clan != null && clan == Clan.PlayerClan && clan.Kingdom == ParentKingdom) _declaredLoyalist = clan;
        }

        [System.NonSerialized] private RebellionPowerProjection _cachedPowerProjection;
        [System.NonSerialized] private int _cachedPowerProjectionDay = int.MinValue;
        [System.NonSerialized] private int _cachedPowerProjectionKingdomClanCount = -1;
        [System.NonSerialized] private bool _cachedPowerProjectionIncludesSupport;

        public string Name { get => _name; private set => _name = value; }
        public Kingdom ParentKingdom { get => _parentKingdom; private set => _parentKingdom = value; }
        public Clan Leader { get => _leader; set => _leader = value; }
        public List<Clan> Members { get => _members; private set => _members = value; }
        public FactionType Type { get => _type; private set => _type = value; }
        public float Discontent { get => _discontent; private set => _discontent = value; }
        public float PeakDiscontent => TaleWorlds.Library.MathF.Max(_peakDiscontent, Discontent);
        public CampaignTime CreationDate { get => _creationDate; private set => _creationDate = value; }
        public bool HasTrackedRebelKingdom => !string.IsNullOrEmpty(_rebelKingdomStringId) || _rebelKingdom != null;
        public bool IsUltimatumPending => _ultimatumPending && !_ultimatumResolved;
        public bool IsGrandCoalition => _isGrandCoalition || IsLegacyGrandCoalitionName(Name);

        public bool IsIdeology => Type == FactionType.Glory || Type == FactionType.Nobility || Type == FactionType.Liberty;
        internal float UnderlyingMood { get => _mood; set => _mood = value; }
        internal float AccommodationBonus => CrownAccommodation?.Active == true ? CourtAppeasementRules.Bonus : 0;
        public float Mood
        {
            get => AccommodationBonus == 0 ? _mood : CourtAppeasementRules.Effective(_mood, AccommodationBonus);
            set => _mood = CourtAppeasementRules.SetEffective(_mood, value, AccommodationBonus);
        }
        internal bool IsCoalitionFrom(FactionType source) => IsGrandCoalition
            && _hasGrandCoalitionSourceIdeology && _grandCoalitionSourceIdeology == source;
        
        // Field 8 and enum value 4 remain reserved for existing saves.
        // Court affiliation no longer stores or confers dynastic legitimacy.
        internal void NormalizeCourtFactionLegacyState()
        {
            if (Type == FactionType.Royalists)
                Type = FactionType.Nobility;
            if (_loyalClan != null)
                _loyalClan = null;

            if (Type == FactionType.InstallRuler && IsGrandCoalition
                && ((_hasGrandCoalitionSourceIdeology && _grandCoalitionSourceIdeology == FactionType.Royalists)
                    || (!_hasGrandCoalitionSourceIdeology && Name?.EndsWith("Grand Restoration") == true)))
            {
                Type = FactionType.Abdication;
                MarkAsGrandCoalition(FactionType.Nobility);
                Name = "Nobility Grand Coalition";
            }
            if (_hasGrandCoalitionSourceIdeology && _grandCoalitionSourceIdeology == FactionType.Royalists)
                _grandCoalitionSourceIdeology = FactionType.Nobility;
        }

        public FactionObject(string name, Kingdom parentKingdom, Clan leader, FactionType type)
        {
            Name = name; ParentKingdom = parentKingdom; Leader = leader; Type = type;
            Members = new List<Clan> { leader };
            Discontent = 0f;
            Mood = 0f;
            CreationDate = CampaignTime.Now;
            if (type == FactionType.Abdication)
                _abdicationMonarch = RegencyBehavior.Instance?.GetLegalClanHead(parentKingdom?.RulingClan)
                    ?? parentKingdom?.Leader;
            if (type == FactionType.Abdication && CrownAccessionBehavior.IsHereditaryRealm(parentKingdom))
            {
                _hereditaryAbdication = true;
                _abdicationCauseId = Guid.NewGuid().ToString("N");
                var laws = SuccessionLawHelper.GetLawsForKingdom(parentKingdom);
                _abdicationHouseLaw = laws.SuccessionLaw;
                _abdicationGenderLaw = laws.GenderLaw;
            }
            _civilWarStartFiefClans = new List<Clan>();
            _civilWarStartFiefCounts = new List<int>();
            _civilWarStartInfluenceClans = new List<Clan>();
            _civilWarStartInfluenceValues = new List<float>();
            

        }

        public void MarkAsGrandCoalition(FactionType sourceIdeology)
        {
            _isGrandCoalition = true;
            _grandCoalitionSourceIdeology = sourceIdeology;
            _hasGrandCoalitionSourceIdeology = true;
        }

        public TextObject GetDisplayName()
        {
            if (IsGrandCoalition)
                return GetGrandCoalitionDisplayName();

            TextObject name;
            switch (Type)
            {
                case FactionType.Independence:
                    name = new TextObject("{=BC_FacName_Indep}{CLAN_NAME} Secessionists");
                    break;
                case FactionType.Abdication:
                    name = new TextObject("{=BC_FacName_Abdic}{CLAN_NAME} Coalition");
                    break;
                case FactionType.InstallRuler:
                    name = new TextObject("{=BC_FacName_Install}{CLAN_NAME} Claimants");
                    break;
                case FactionType.Royalists:
                    return GetIdeologyDisplayName(FactionType.Royalists, ParentKingdom);
                case FactionType.Glory:
                    return GetIdeologyDisplayName(FactionType.Glory, ParentKingdom);
                case FactionType.Nobility:
                    return GetIdeologyDisplayName(FactionType.Nobility, ParentKingdom);
                case FactionType.Liberty:
                    return GetIdeologyDisplayName(FactionType.Liberty, ParentKingdom);
                default:
                    return new TextObject(Name ?? string.Empty);
            }

            name.SetTextVariable("CLAN_NAME", Leader?.Name ?? new TextObject(""));
            return name;
        }

        private TextObject GetGrandCoalitionDisplayName()
        {
            if (!_hasGrandCoalitionSourceIdeology)
            {
                TextObject legacyName = new TextObject("{=BC_FacName_GrandCoalitionLegacy}{KINGDOM_NAME} Grand Coalition");
                legacyName.SetTextVariable("KINGDOM_NAME", ParentKingdom?.Name ?? new TextObject(""));
                return legacyName;
            }

            TextObject localizedName = new TextObject("{=BC_FacName_GrandCoalition}{KINGDOM_NAME} {FACTION_NAME} Grand Coalition");
            localizedName.SetTextVariable("KINGDOM_NAME", ParentKingdom?.Name ?? new TextObject(""));
            localizedName.SetTextVariable("FACTION_NAME", GetIdeologyDisplayName(_grandCoalitionSourceIdeology, ParentKingdom));
            return localizedName;
        }

        internal static TextObject GetIdeologyDisplayName(FactionType type, Kingdom kingdom)
        {
            return CourtInstitutionDisplayHelper.GetCourtFactionName(type, kingdom);
        }

        private static bool IsLegacyGrandCoalitionName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && (name.EndsWith("Grand Coalition") || name.EndsWith("Grand Restoration"));
        }

        public Kingdom GetRebelKingdom()
        {
            Kingdom tracked = GetTrackedRebelKingdomIncludingEliminated();
            return tracked != null && !tracked.IsEliminated ? tracked : null;
        }

        public Kingdom GetTrackedRebelKingdomIncludingEliminated()
        {
            if (IsIdeology || HasInvalidRebelKingdomReference) return null;

            if (_rebelKingdom != null)
                return _rebelKingdom;

            if (!string.IsNullOrEmpty(_rebelKingdomStringId))
                return _rebelKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == _rebelKingdomStringId);

            return null;
        }

        public bool IsTrackedRebelKingdom(Kingdom kingdom)
        {
            if (kingdom == null || IsIdeology || HasInvalidRebelKingdomReference) return false;

            if (_rebelKingdom != null && _rebelKingdom == kingdom)
                return true;

            return !string.IsNullOrEmpty(_rebelKingdomStringId) && kingdom.StringId == _rebelKingdomStringId;
        }

        public bool IsTrackedRebelKingdomId(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId) || IsIdeology || HasInvalidRebelKingdomReference) return false;

            return (_rebelKingdom != null && _rebelKingdom.StringId == kingdomId)
                || (!string.IsNullOrEmpty(_rebelKingdomStringId) && _rebelKingdomStringId == kingdomId);
        }

        public void SetRebelKingdom(Kingdom kingdom)
        {
            _rebelKingdom = kingdom;
            _rebelKingdomStringId = kingdom?.StringId ?? string.Empty;
            if (kingdom != null && string.IsNullOrEmpty(_civilWarOriginShellPrefix))
                _civilWarOriginShellPrefix = kingdom.StringId;
            InvalidatePowerProjection();
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.InvalidateFactionLookupCache();
        }

        public void ClearRebelKingdom()
        {
            _rebelKingdom = null;
            _rebelKingdomStringId = string.Empty;
            InvalidatePowerProjection();
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.InvalidateFactionLookupCache();
        }

        public bool TryBackfillRebelKingdomFromLeaderKingdom()
        {
            Kingdom legacyKingdom = Leader?.Kingdom;
            if (!CanBackfillRebelKingdom(legacyKingdom))
                return false;

            SetRebelKingdom(legacyKingdom);
            return true;
        }

        internal bool CanBackfillRebelKingdom(Kingdom kingdom)
        {
            return !IsIdeology && !HasTrackedRebelKingdom
                && kingdom != null && !kingdom.IsEliminated && kingdom != ParentKingdom
                && ParentKingdom != null && !ParentKingdom.IsEliminated
                && Leader != null && !Leader.IsEliminated
                && Leader.Kingdom == kingdom && kingdom.RulingClan == Leader
                && MatchesRebelKingdomId(kingdom.StringId) && kingdom.IsAtWarWith(ParentKingdom)
                && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()
                    ?.IsRebelKingdomClaimedByAnotherFaction(this, kingdom) != true;
        }

        internal bool HasInvalidRebelKingdomReference
        {
            get
            {
                if (IsIdeology || !HasTrackedRebelKingdom) return false;
                string id = _rebelKingdom?.StringId ?? _rebelKingdomStringId;
                if (string.IsNullOrEmpty(id) || id == ParentKingdom?.StringId
                    || (_rebelKingdom != null && !string.IsNullOrEmpty(_rebelKingdomStringId)
                        && id != _rebelKingdomStringId)) return true;
                if (!string.IsNullOrEmpty(_civilWarOriginShellPrefix))
                    return id != _civilWarOriginShellPrefix;

                // Old automatic backfills had no creation receipt. Real wars keep their
                // saved shell even if their founding clan has since been replaced.
                return !_rebellionCreationStarted && !_rebellionCreationCompleted
                    && !MatchesRebelKingdomId(id);
            }
        }

        internal bool DiscardInvalidRebelKingdomReference()
        {
            if (!HasInvalidRebelKingdomReference) return false;
            BellumCivileLogger.Log($"Discarded invalid rebel kingdom reference without war consequences; faction={Name}; leader={Leader?.StringId ?? "none"}; rebel={_rebelKingdom?.StringId ?? _rebelKingdomStringId}; parent={ParentKingdom?.StringId ?? "none"}.");
            ClearRebelKingdom();
            return true;
        }

        internal bool MatchesRebelKingdomId(string kingdomId)
        {
            if (IsIdeology || string.IsNullOrEmpty(kingdomId)) return false;
            if (!string.IsNullOrEmpty(_civilWarOriginShellPrefix))
                return kingdomId == _civilWarOriginShellPrefix;
            string prefix = GetDeterministicRebelKingdomIdPrefix();
            if (string.IsNullOrEmpty(prefix)) return false;
            if (kingdomId == prefix) return true;
            // Creation appends only a numeric collision suffix, never another clan id.
            if (!kingdomId.StartsWith(prefix + "_", StringComparison.Ordinal)) return false;
            string suffix = kingdomId.Substring(prefix.Length + 1);
            return suffix.Length > 0 && suffix.All(c => c >= '0' && c <= '9');
        }

        internal string GetDeterministicRebelKingdomIdPrefix()
        {
            if (!string.IsNullOrEmpty(_civilWarOriginShellPrefix)) return _civilWarOriginShellPrefix;
            if (IsIdeology || ParentKingdom == null || Leader == null)
                return string.Empty;

            return !string.IsNullOrEmpty(_successionChallengeId)
                ? ParentKingdom.StringId + "_rebels_challenge_" + _successionChallengeId
                : ParentKingdom.StringId + "_rebels_" + Leader.StringId;
        }

        internal bool RetargetCivilWarParent(Kingdom expectedParent, Kingdom successor)
        {
            var shell = GetTrackedRebelKingdomIncludingEliminated();
            if (IsIdeology || expectedParent == null || successor?.IsEliminated != false
                || shell == null || shell.IsEliminated || shell == successor) return false;
            if (ParentKingdom == successor) return true;
            if (ParentKingdom != expectedParent) return false;
            // Shell identity belongs to the rebellion, not to its replaceable opponent.
            if (string.IsNullOrEmpty(_civilWarOriginShellPrefix))
                _civilWarOriginShellPrefix = shell.StringId;
            ParentKingdom = successor;
            InvalidatePowerProjection();
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.InvalidateFactionLookupCache();
            return true;
        }

        internal bool TryAdoptExistingRebelKingdom(Kingdom rebelKingdom, out string failureReason)
        {
            failureReason = null;
            if (IsIdeology)
            {
                failureReason = "ideological factions cannot own rebel kingdoms";
                return false;
            }

            if (ParentKingdom == null || ParentKingdom.IsEliminated)
            {
                failureReason = "the parent kingdom is missing or eliminated";
                return false;
            }

            if (Leader == null || Leader.IsEliminated)
            {
                failureReason = "the faction leader is missing or eliminated";
                return false;
            }

            if (rebelKingdom == null || rebelKingdom.IsEliminated || rebelKingdom == ParentKingdom)
            {
                failureReason = "the candidate rebel kingdom is invalid";
                return false;
            }

            if (!MatchesRebelKingdomId(rebelKingdom.StringId))
            {
                failureReason = "the candidate kingdom does not match the faction's deterministic rebel-shell id";
                return false;
            }

            if (Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()
                ?.IsRebelKingdomClaimedByAnotherFaction(this, rebelKingdom) == true)
            {
                failureReason = "the candidate rebel kingdom is already tracked by another faction";
                return false;
            }

            if (!rebelKingdom.IsAtWarWith(ParentKingdom))
            {
                failureReason = "the candidate rebel kingdom is not at war with the parent kingdom";
                return false;
            }

            if (Leader.Kingdom != ParentKingdom && Leader.Kingdom != rebelKingdom)
            {
                failureReason = "the faction leader belongs to an unrelated kingdom";
                return false;
            }

            if (!KingdomCreationSafetyHelper.PrepareRuler(rebelKingdom, Leader, "adopt rebel kingdom"))
            {
                failureReason = "the rebel kingdom has no valid intended ruler";
                return false;
            }
            _rebellionCreationStarted = true;
            SetRebelKingdom(rebelKingdom);
            CaptureCivilWarStartFiefCounts(ParentKingdom.Clans);
            CaptureCivilWarStartFiefCounts(Members);
            CaptureCivilWarStartInfluence(ParentKingdom.Clans);
            CaptureCivilWarStartInfluence(Members);

            if (Leader.Kingdom == ParentKingdom)
                MoveClanToKingdomPreservingCivilWarInfluence(Leader, rebelKingdom, preserveCustomBanner: true, showNotification: false);

            rebelKingdom.RulingClan = Leader;
            foreach (Clan member in Members.ToList())
            {
                if (member != null
                    && member != Leader
                    && !member.IsEliminated
                    && member.Kingdom == ParentKingdom)
                {
                    MoveClanToKingdomPreservingCivilWarInfluence(member, rebelKingdom, preserveCustomBanner: true, showNotification: false);
                }
            }

            if (!TryValidateActiveRebellion(out _, out failureReason))
                return false;

            _ultimatumPending = false;
            _ultimatumResolved = true;
            _solidarityRecruitmentApplied = true;
            _rebellionCreationCompleted = true;
            CivilWarConflictBehavior.Instance?.Observe(this, rebelKingdom,
                Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(rebelKingdom, ParentKingdom));

            Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.RegisterCivilWar(this, rebelKingdom);
            Campaign.Current.GetCampaignBehavior<CivilWarInterventionBehavior>()?.ApplyLock(rebelKingdom, 30);
            Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>()
                ?.CooldownFeudsForCivilWarParticipants(Members, $"reconciled civil war led by {Name}");
            return true;
        }

        internal bool TryCompletePartialRebellionCreation(out string failureReason)
        {
            failureReason = null;
            if (IsIdeology || !_rebellionCreationStarted || _rebellionCreationCompleted)
                return false;

            Kingdom rebelKingdom = GetRebelKingdom();
            if (ParentKingdom == null
                || ParentKingdom.IsEliminated
                || Leader == null
                || Leader.IsEliminated
                || rebelKingdom == null
                || rebelKingdom.IsEliminated)
            {
                failureReason = "the partial transition is missing its parent, leader, or rebel kingdom";
                return false;
            }

            if (Leader.Kingdom != ParentKingdom && Leader.Kingdom != rebelKingdom)
            {
                failureReason = "the faction leader belongs to an unrelated kingdom";
                return false;
            }

            if (!KingdomCreationSafetyHelper.PrepareRuler(rebelKingdom, Leader, "recover partial rebellion"))
            {
                failureReason = "the rebel kingdom has no valid intended ruler";
                return false;
            }
            SetRebelKingdom(rebelKingdom);
            CaptureCivilWarStartFiefCounts(ParentKingdom.Clans);
            CaptureCivilWarStartFiefCounts(Members);
            CaptureCivilWarStartInfluence(ParentKingdom.Clans);
            CaptureCivilWarStartInfluence(Members);

            if (Leader.Kingdom == ParentKingdom)
                MoveClanToKingdomPreservingCivilWarInfluence(Leader, rebelKingdom, preserveCustomBanner: true, showNotification: false);

            rebelKingdom.RulingClan = Leader;
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()
                ?.ValidateTransitionRulerOnce(rebelKingdom, Leader, $"partial civil-war recovery against {ParentKingdom.StringId}");

            foreach (Clan member in (Members ?? new List<Clan>()).ToList())
            {
                if (member != null
                    && member != Leader
                    && !member.IsEliminated
                    && member.Kingdom == ParentKingdom)
                {
                    MoveClanToKingdomPreservingCivilWarInfluence(member, rebelKingdom, preserveCustomBanner: true, showNotification: false);
                }
            }

            if (!rebelKingdom.IsAtWarWith(ParentKingdom))
            {
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(rebelKingdom, ParentKingdom));
            }

            if (!TryValidateActiveRebellion(out _, out failureReason))
                return false;

            _ultimatumPending = false;
            _ultimatumResolved = true;
            _solidarityRecruitmentApplied = true;
            _rebellionCreationCompleted = true;
            Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.RegisterCivilWar(this, rebelKingdom);
            CivilWarConflictBehavior.Instance?.Observe(this, rebelKingdom,
                Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(rebelKingdom, ParentKingdom));
            Campaign.Current.GetCampaignBehavior<CivilWarInterventionBehavior>()?.ApplyLock(rebelKingdom, 30);
            Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>()
                ?.CooldownFeudsForCivilWarParticipants(Members, $"recovered civil war led by {Name}");
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordCivilWarStarted(this, rebelKingdom);
            return true;
        }

        public bool IsCivilWarActive()
        {
            if (IsChallengeStartupPending) return false;
            Kingdom rebelKingdom = GetRebelKingdom();
            return rebelKingdom != null
                && ParentKingdom != null
                && rebelKingdom != ParentKingdom
                && !rebelKingdom.IsEliminated
                && !ParentKingdom.IsEliminated
                && rebelKingdom.IsAtWarWith(ParentKingdom);
        }

        public void AddMember(Clan clan)
        {
            if (clan == null || (IsIdeology && !CourtMembershipEligibility.CanBelong(clan, ParentKingdom))) return;
            if (_declaredLoyalist == clan) _declaredLoyalist = null;
            if (!Members.Contains(clan))
            {
                Members.Add(clan);
                InvalidatePowerProjection();
                Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.InvalidateFactionLookupCache();
            }

            if (HasTrackedRebelKingdom || IsCivilWarActive())
            {
                CaptureCivilWarStartFiefCount(clan);
                CaptureCivilWarStartInfluence(clan);
            }
        }

        public void RemoveMember(Clan clan)
        {
            if (HasTrackedRebelKingdom || IsCivilWarActive())
            {
                CaptureCivilWarStartFiefCount(clan);
                CaptureCivilWarStartInfluence(clan);
            }

            if (Members.Contains(clan))
            {
                Members.Remove(clan);
                InvalidatePowerProjection();
                Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.InvalidateFactionLookupCache();
                if (clan == Leader && Members.Count > 0)
                    Leader = Members.Where(c => !c.IsEliminated).OrderByDescending(c => c.Influence).FirstOrDefault()
                             ?? Members.First();
            }
        }

        internal RebellionPowerProjection CalculatePowerProjection(bool forceRefresh = false)
        {
            bool includeProjectedSupport = !IsCivilWarActive();
            int currentDay = Campaign.Current != null ? (int)CampaignTime.Now.ToDays : int.MinValue;
            int kingdomClanCount = ParentKingdom?.Clans?.Count ?? 0;

            if (!forceRefresh
                && _cachedPowerProjection != null
                && _cachedPowerProjectionDay == currentDay
                && _cachedPowerProjectionKingdomClanCount == kingdomClanCount
                && _cachedPowerProjectionIncludesSupport == includeProjectedSupport)
            {
                return _cachedPowerProjection;
            }

            _cachedPowerProjection = RebellionPowerHelper.CalculateProjectedConflictPower(
                ParentKingdom,
                Members,
                Leader,
                this,
                includeProjectedSupport);
            _cachedPowerProjectionDay = currentDay;
            _cachedPowerProjectionKingdomClanCount = kingdomClanCount;
            _cachedPowerProjectionIncludesSupport = includeProjectedSupport;
            return _cachedPowerProjection;
        }

        private void InvalidatePowerProjection()
        {
            _cachedPowerProjection = null;
            _cachedPowerProjectionDay = int.MinValue;
            _cachedPowerProjectionKingdomClanCount = -1;
        }

        public float CalculateFactionPower() => CalculatePowerProjection().FactionPower;
        
        // What does this complex formula do?
        // Calculates the defensive power of the kingdom using "Feudal Apathy." Non-rebel lords contribute only a fraction of their true military strength to the King's defense depending on their ideological mood.
        public float CalculateLoyalistPower()
        {
            return CalculatePowerProjection().LoyalistPower;
        }

        public string GetDemandDescription()
        {
            switch (Type)
            {
                case FactionType.Independence: return new TextObject("{=BC_DemandDesc_Indep}Independence from the realm").ToString();
                case FactionType.Abdication: TextObject abd = new TextObject("{=BC_DemandDesc_Abdic}The abdication of {RULER_NAME}"); abd.SetTextVariable("RULER_NAME", ParentKingdom.RulingClan?.Leader?.Name ?? new TextObject("?")); return abd.ToString();
                case FactionType.InstallRuler: TextObject inst = new TextObject("{=BC_DemandDesc_Install}Crown {LEADER_NAME} as ruler"); inst.SetTextVariable("LEADER_NAME", Leader?.Leader?.Name ?? new TextObject("?")); return inst.ToString();
                case FactionType.Royalists: return new TextObject("{=BC_DemandDesc_Roy}Centralize power to the crown").ToString();
                case FactionType.Glory: return new TextObject("{=BC_DemandDesc_Mil}Prioritize military campaigns and conquest").ToString();
                case FactionType.Nobility: return new TextObject("{=BC_DemandDesc_Ari}Protect the rights of the nobility").ToString();
                case FactionType.Liberty: return new TextObject("{=BC_DemandDesc_Pop}Empower the common folk").ToString();
                default: return new TextObject("{=BC_DemandDesc_Unknown}Unknown demands").ToString();
            }
        }

        // What does this complex formula do?
        // Evaluates an armed rebellion's faction power ratio against the loyalist power. If it exceeds a dynamic threshold modified by the leader's traits (Calculating/Valor), discontent rises daily until it hits 100.
        public void DailyTick()
        {
            if (IsChallengeStartupPending) return;
            if (IsIdeology) return;

            if (IsCivilWarActive() || IsUltimatumPending || HasTrackedRebelKingdom)
                return;

            RebellionPowerProjection projection = CalculatePowerProjection(forceRefresh: true);
            float factionPower = projection.FactionPower;
            float loyalistPower = projection.LoyalistPower;

            Hero factionLeader = Leader?.Leader;
            if (factionLeader == null) return;

            float dynamicThreshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(factionLeader);

            if (factionPower >= loyalistPower * dynamicThreshold)
            {
                float powerRatio = loyalistPower > 0f ? factionPower / (loyalistPower * dynamicThreshold) : 1f;
                float conspiracyGrowth = O.DiscontentGainBase * powerRatio;
                PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
                conspiracyGrowth *= council?.GetRebelConspiracyGrowthMultiplier(ParentKingdom) ?? 1f;
                Discontent += conspiracyGrowth;
                _peakDiscontent = TaleWorlds.Library.MathF.Max(_peakDiscontent, Discontent);

                if (Discontent >= C.DiscontentTrigger)
                {
                    Discontent = C.DiscontentTrigger;
                    _peakDiscontent = TaleWorlds.Library.MathF.Max(_peakDiscontent, Discontent);
                    TriggerUltimatum();
                }
            }
            else
            {
                Discontent -= O.DiscontentDecayPerDay;
                if (Discontent < 0f) Discontent = 0f;
            }
        }

        // What does this complex formula do?
        // Resolves an ultimatum by either forcing the player to choose, or having an AI King evaluate the probability of folding out of fear vs fighting back, based heavily on the power ratio and their personality traits.
        private string _lastUltimatumPreflightFailure;

        public bool TriggerUltimatum()
        {
            return TriggerUltimatum(null, suppressAutomaticPlayerChoice: false);
        }

        internal bool TriggerUltimatum(CivilWarPlayerCallContext playerCallContext, bool suppressAutomaticPlayerChoice)
        {
            return TriggerUltimatumDetailed(playerCallContext, suppressAutomaticPlayerChoice) != UltimatumResolution.Failed;
        }

        internal UltimatumResolution TriggerUltimatumDetailed(
            CivilWarPlayerCallContext playerCallContext,
            bool suppressAutomaticPlayerChoice,
            Action<UltimatumResolution> deferredCompletion = null)
        {
            if (!CanTriggerUltimatum(out Settlement unsafeSettlement))
            {
                if (unsafeSettlement != null)
                {
                    Discontent = C.DiscontentTrigger;
                    _peakDiscontent = TaleWorlds.Library.MathF.Max(_peakDiscontent, Discontent);
                }

                string failure = $"leader={Leader?.StringId ?? "none"}; parent={ParentKingdom?.StringId ?? "none"}; reason={GetUltimatumPreflightFailure(unsafeSettlement)}";
                if (_lastUltimatumPreflightFailure != failure)
                    BellumCivileLogger.Log($"Ultimatum delayed at preflight; faction={Name}; {failure}.");
                _lastUltimatumPreflightFailure = failure;

                return UltimatumResolution.Failed;
            }

            _lastUltimatumPreflightFailure = null;
            _ultimatumPending = true;
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordUltimatumIssued(this);

            if (ParentKingdom.RulingClan == Clan.PlayerClan)
            {
                NotificationHelper.ShowUltimatumToPlayer(
                    this,
                    () => CompleteDeferredUltimatum(AcceptDemands(), deferredCompletion),
                    () => CompleteDeferredUltimatum(
                        RefuseDemandsAndRebel(playerCallContext, suppressAutomaticPlayerChoice),
                        deferredCompletion));
                return UltimatumResolution.PendingPlayerChoice;
            }
            else
            {
                Hero ruler = ParentKingdom.RulingClan?.Leader;
                if (ruler == null)
                    return RefuseDemandsAndRebel(playerCallContext, suppressAutomaticPlayerChoice);

                RebellionPowerProjection projection = CalculatePowerProjection(forceRefresh: true);
                float factionStrength = projection.FactionPower;
                float liegeStrength = projection.LoyalistPower;

                if (factionStrength < liegeStrength)
                    return RefuseDemandsAndRebel(playerCallContext, suppressAutomaticPlayerChoice);

                double acceptanceChance = UltimatumAcceptanceRules.Calculate(factionStrength, liegeStrength,
                    Kingdom.All.Any(k => k != ParentKingdom && k.IsAtWarWith(ParentKingdom)),
                    ruler.GetTraitLevel(DefaultTraits.Calculating), ruler.GetTraitLevel(DefaultTraits.Valor),
                    ruler.GetTraitLevel(DefaultTraits.Mercy));

                if (MBRandom.RandomFloat < acceptanceChance)
                    return AcceptDemands();

                return RefuseDemandsAndRebel(playerCallContext, suppressAutomaticPlayerChoice);
            }
        }

        private static void CompleteDeferredUltimatum(
            UltimatumResolution resolution,
            Action<UltimatumResolution> deferredCompletion)
        {
            deferredCompletion?.Invoke(resolution);
        }

        private string GetUltimatumPreflightFailure(Settlement unsafeSettlement)
        {
            if (IsIdeology) return "ideological factions cannot issue armed ultimatums";
            if (IsCivilWarActive()) return "the faction already has an active civil war";
            if (HasTrackedRebelKingdom) return "the faction already tracks a rebel kingdom";
            if (_ultimatumPending) return "an ultimatum is already pending";
            if (_ultimatumResolved) return "the ultimatum was already resolved";
            if (_rebellionCreationStarted) return "rebellion creation already started";
            if (_rebellionCreationCompleted) return "rebellion creation already completed";
            if (Leader == null) return "the faction has no leader";
            if (ParentKingdom == null) return "the faction has no parent kingdom";
            if (Leader.Kingdom != ParentKingdom) return "the faction leader no longer belongs to the parent kingdom";
            if (unsafeSettlement != null) return $"the faction leader is in settlement {unsafeSettlement.StringId}, which would become hostile upon rebellion";
            return "an unknown preflight condition failed";
        }

        internal bool CanTriggerUltimatum(out Settlement unsafeSettlement)
        {
            unsafeSettlement = null;

            if (IsIdeology
                || IsCivilWarActive()
                || HasTrackedRebelKingdom
                || _ultimatumPending
                || _ultimatumResolved
                || _rebellionCreationStarted
                || _rebellionCreationCompleted)
            {
                return false;
            }

            if (Leader == null || ParentKingdom == null || Leader.Kingdom != ParentKingdom)
                return false;

            return !ShouldDelayUltimatumForUnsafeLeaderLocation(out unsafeSettlement);
        }

        private bool ShouldDelayUltimatumForUnsafeLeaderLocation(out Settlement unsafeSettlement)
        {
            return IsLeaderUnsafeForRebellion(Leader, ParentKingdom, Members, out unsafeSettlement);
        }

        internal static bool IsLeaderUnsafeForRebellion(
            Clan leader,
            Kingdom parentKingdom,
            IEnumerable<Clan> rebelMembers,
            out Settlement unsafeSettlement)
        {
            unsafeSettlement = null;

            Hero leaderHero = leader?.Leader;
            if (leaderHero == null || leaderHero == Hero.MainHero)
                return false;

            Settlement settlement = leaderHero.CurrentSettlement;
            if (settlement == null || !settlement.IsFortification)
                return false;

            if (settlement.MapFaction != parentKingdom)
                return false;

            Clan ownerClan = settlement.OwnerClan;
            if (ownerClan != null && (rebelMembers ?? Enumerable.Empty<Clan>()).Contains(ownerClan))
                return false;

            unsafeSettlement = settlement;
            return true;
        }

        private float CalculateLiegePower()
        {
            return CalculatePowerProjection().LoyalistPower;
        }

        private UltimatumResolution AcceptDemands()
        {
            if (!KingdomCreationSafetyHelper.IsValidFounder(Leader))
                return UltimatumResolution.Failed;

            if (!TryResolveUltimatum("accepted"))
                return UltimatumResolution.Failed;

            List<Clan> pacifiedMembers = Members
                .Where(c => c != null && !c.IsEliminated && !c.IsMinorFaction && !c.IsUnderMercenaryService)
                .ToList();

            TextObject acceptedDemandDetail = new TextObject("{=BC_Msg_AvoidedWar_Generic}to settle the crisis");

            switch (Type)
            {
                case FactionType.Independence:
                    IndependentKingdomProfile independenceProfile = IndependentKingdomProfileHelper.Create(Leader, ParentKingdom);
                    TextObject newName = independenceProfile.Name;

                    string indepStringId = ParentKingdom.StringId + "_indep_" + Leader.StringId;
                    Kingdom independentKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == indepStringId && !k.IsEliminated)
                        ?? KingdomCreationSafetyHelper.CreateKingdom(indepStringId, Leader);
                    if (!KingdomCreationSafetyHelper.PrepareRuler(independentKingdom, Leader, "accepted independence"))
                        return UltimatumResolution.Failed;

                    Settlement indSettlement = Leader.Settlements.FirstOrDefault() ?? ParentKingdom.Settlements.FirstOrDefault() ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);

                    var independentVisuals = KingdomVisualHelper.ResolveBreakawayKingdomVisuals(ParentKingdom, Leader, indepStringId);

                    independentKingdom.InitializeKingdom(
                        independenceProfile.Name,
                        independenceProfile.Name,
                        independenceProfile.Culture ?? ParentKingdom.Culture,
                        independentVisuals.Banner,
                        independentVisuals.PrimaryColor,
                        independentVisuals.SecondaryColor,
                        indSettlement,
                        independenceProfile.EncyclopediaText,
                        independenceProfile.EncyclopediaTitle,
                        independenceProfile.RulerTitle);
                    KingdomVisualHelper.ApplyKingdomPalette(independentKingdom, independentVisuals);
                    RegisterIndependentRealmSourceTitle(independentKingdom, independenceProfile, "accepted independence ultimatum");
                    RebelPolicyHelper.ApplyIndependencePolicyProfile(this, independentKingdom);

                    bool preserveIndependenceClanBanners = !independentVisuals.RecolorMemberClans;
                    Dictionary<Clan, Banner> preservedIndependenceBanners = preserveIndependenceClanBanners
                        ? KingdomVisualHelper.CaptureClanBannersToPreserve(Members)
                        : new Dictionary<Clan, Banner>();
                    KingdomVisualHelper.ApplyJoinToKingdomWithBannerPolicy(Leader, independentKingdom, preserveIndependenceClanBanners, showNotification: false);
                    independentKingdom.RulingClan = Leader;
                    Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()
                        ?.ClearTemporaryKingdomRepair(independentKingdom);

                    foreach (Clan member in Members.ToList())
                    {
                        if (member != Leader && !member.IsEliminated)
                            KingdomVisualHelper.ApplyJoinToKingdomWithBannerPolicy(member, independentKingdom, preserveIndependenceClanBanners, showNotification: false);
                    }

                    if (Leader?.Kingdom == independentKingdom && independentKingdom.RulingClan != Leader)
                        independentKingdom.RulingClan = Leader;

                    if (preserveIndependenceClanBanners)
                        KingdomVisualHelper.RestoreClanBanners(preservedIndependenceBanners);
                    else
                        KingdomVisualHelper.ReapplyKingdomPaletteAfterClanChanges(independentKingdom, independentVisuals);

                    Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(independentKingdom);

                    acceptedDemandDetail = new TextObject("{=BC_Msg_AvoidedWar_Independence}to recognize the independence of the {REALM_NAME}");
                    acceptedDemandDetail.SetTextVariable("REALM_NAME", independentKingdom.Name);
                    break;

                case FactionType.Abdication:
                    Clan formerRulingClan = ParentKingdom.RulingClan;
                    CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
                    if (IsHereditaryAbdication)
                    {
                        bool recorded = CrownAccessionBehavior.Instance?.BeginForcedAbdication(
                            ParentKingdom, formerRulingClan, AbdicationMonarch, AbdicationLaws, AbdicationCauseId) == true;
                        if (!recorded)
                        {
                            _ultimatumResolved = false;
                            return UltimatumResolution.Failed;
                        }
                        acceptedDemandDetail = new TextObject("{=BC_Msg_AvoidedWar_Abdication}to abdicate the throne");
                        break;
                    }
                    bool successionStarted = resolutionBehavior?.TryBeginAcceptedAbdicationVote(
                        ParentKingdom,
                        Leader,
                        formerRulingClan) ?? false;

                    if (!successionStarted && ElectiveSuccessionBehavior.UsesElection(ParentKingdom))
                    {
                        _ultimatumResolved = false;
                        return UltimatumResolution.Failed;
                    }

                    // Accepted abdication must never become a purely cosmetic outcome. If the
                    // election machinery cannot retain a valid decision, honor the concession
                    // through a direct legal handoff to the faction leader.
                    if (!successionStarted
                        && Leader != null
                        && Leader != formerRulingClan
                        && NobleClanEligibilityHelper.IsValidRulingClan(Leader, ParentKingdom))
                    {
                        resolutionBehavior?.ClearSuccessionStateForKingdom(
                            ParentKingdom,
                            "accepted abdication election fallback");
                        ParentKingdom.RulingClan = Leader;
                        Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                            ?.TrySetKingdomTitleRuler(
                                ParentKingdom,
                                Leader,
                                legalTransfer: true,
                                reason: "accepted abdication fallback");
                        BellumCivileLogger.Log(
                            $"Applied direct accepted-abdication fallback; kingdom={ParentKingdom.StringId}; former={formerRulingClan?.StringId ?? "none"}; successor={Leader.StringId}.");
                    }

                    if (!successionStarted && ParentKingdom.RulingClan != Leader)
                    {
                        BellumCivileLogger.Log(
                            $"Could not honor accepted abdication; kingdom={ParentKingdom.StringId}; former={formerRulingClan?.StringId ?? "none"}; candidate={Leader?.StringId ?? "none"}. The ultimatum will proceed as refused instead of reporting a false abdication.");
                        _ultimatumResolved = false;
                        return RefuseDemandsAndRebel(null, suppressAutomaticPlayerChoice: false);
                    }

                    acceptedDemandDetail = new TextObject("{=BC_Msg_AvoidedWar_Abdication}to abdicate the throne");
                    break;

                case FactionType.InstallRuler:
                    Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.MarkAsUsurper(ParentKingdom);
                    Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()
                        ?.ClearSuccessionStateForKingdom(ParentKingdom, "accepted install-ruler ultimatum");
                    ParentKingdom.RulingClan = Leader;
                    Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                        ?.TrySetKingdomTitleRuler(ParentKingdom, Leader, legalTransfer: true, reason: "accepted install-ruler ultimatum");
                    acceptedDemandDetail = new TextObject("{=BC_Msg_AvoidedWar_InstallRuler}to surrender the crown to {LEADER_NAME} of the {CLAN_NAME}");
                    acceptedDemandDetail.SetTextVariable("LEADER_NAME", Leader?.Leader?.Name ?? Leader?.Name);
                    acceptedDemandDetail.SetTextVariable("CLAN_NAME", Leader?.Name ?? new TextObject(""));
                    break;

                case FactionType.Royalists:
                case FactionType.Glory:
                case FactionType.Nobility:
                case FactionType.Liberty:
                    break;
            }

            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()
                ?.ApplyAcceptedUltimatumPacification(ParentKingdom, pacifiedMembers, AcceptedUltimatumPacifiedDays);

            TextObject msg = new TextObject("{=BC_Msg_AvoidedWar}{KINGDOM_NAME} has avoided civil war by accepting the demands of the {FACTION_NAME} {DEMAND_DETAIL}.");
            msg.SetTextVariable("KINGDOM_NAME", ParentKingdom.Name);
            msg.SetTextVariable("FACTION_NAME", GetDisplayName());
            msg.SetTextVariable("DEMAND_DETAIL", acceptedDemandDetail);
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Warning, primaryKingdom: ParentKingdom, primaryClan: Leader);
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(this, RebellionSummaryOutcome.AcceptedUltimatum);
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(this);
            return UltimatumResolution.DemandsAccepted;
        }

        private static void RegisterIndependentRealmSourceTitle(Kingdom independentKingdom, IndependentKingdomProfile profile, string reason)
        {
            if (independentKingdom == null || !profile.UsesExistingSourceTitle)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord sourceTitle = titleBehavior?.GetTitle(profile.SourceTitleId);
            if (sourceTitle != null)
                titleBehavior.RegisterIndependentRealmShell(independentKingdom, sourceTitle, reason);
        }

        private UltimatumResolution RefuseDemandsAndRebel(CivilWarPlayerCallContext explicitPlayerCall, bool suppressAutomaticPlayerChoice)
        {
            if (!TryResolveUltimatum("refused"))
                return UltimatumResolution.Failed;

            bool playerWasRebelFactionMember = Clan.PlayerClan != null
                && Clan.PlayerClan != Leader
                && Members.Contains(Clan.PlayerClan);
            if (playerWasRebelFactionMember)
                RemoveMember(Clan.PlayerClan);

            RallyAiSupportersOnce();
            CivilWarPlayerCallContext playerCall = suppressAutomaticPlayerChoice
                ? null
                : CivilWarSolidarityHelper.BuildPlayerCallContext(this, playerWasRebelFactionMember, explicitPlayerCall);

            if (StartOpenRebellion(playerCall, clearStandaloneLeaderWar: false, out string failureReason))
                return UltimatumResolution.RebellionStarted;

            BellumCivileLogger.Log(
                $"Ultimatum refusal failed to create a rebellion; faction={Name}; leader={Leader?.StringId ?? "none"}; parent={ParentKingdom?.StringId ?? "none"}; reason={failureReason ?? "unknown"}.");

            // A clean failure remains retryable. Partial transitions retain their flags so the
            // daily legacy-state reconciler can finish adopting the rebel kingdom safely.
            if (!HasTrackedRebelKingdom && Leader?.Kingdom == ParentKingdom)
            {
                _ultimatumPending = false;
                _ultimatumResolved = false;
                _rebellionCreationStarted = false;
                _rebellionCreationCompleted = false;
            }

            return UltimatumResolution.Failed;
        }

        /// <summary>
        /// Starts a landed player's secession directly from the kingdom-menu choice, before
        /// vanilla detaches the clan into a clan-level war. Keeping the parent membership intact
        /// until supporters are assessed lets feudal and personal calls to arms resolve normally.
        /// </summary>
        internal bool StartImmediateSecessionFromParentRealm()
        {
            if (IsIdeology
                || IsCivilWarActive()
                || HasTrackedRebelKingdom
                || Leader == null
                || Leader.IsEliminated
                || ParentKingdom == null
                || ParentKingdom.IsEliminated
                || Leader.Kingdom != ParentKingdom
                || ParentKingdom.RulingClan == Leader)
            {
                return false;
            }

            RallyAiSupportersOnce();
            return StartOpenRebellion(playerCall: null, clearStandaloneLeaderWar: false, out _);
        }

        /// <summary>
        /// Adopts Bannerlord's clan-versus-kingdom war after a landed clan leaves its realm
        /// with its holdings. The player's departure choice already serves as the ultimatum,
        /// so this enters the armed independence phase directly.
        /// </summary>
        internal bool StartImmediateSecessionAfterVanillaDeparture()
        {
            if (IsIdeology
                || IsCivilWarActive()
                || HasTrackedRebelKingdom
                || Leader == null
                || Leader.IsEliminated
                || ParentKingdom == null
                || ParentKingdom.IsEliminated
                || Leader.Kingdom != null
                || !Leader.IsAtWarWith(ParentKingdom))
            {
                return false;
            }

            RallyAiSupportersOnce();
            CivilWarPlayerCallContext playerCall = CivilWarSolidarityHelper.BuildPlayerCallContext(
                this,
                playerWasRebelFactionMember: false,
                explicitContext: null);

            return StartOpenRebellion(playerCall, clearStandaloneLeaderWar: true, out _);
        }

        private bool StartOpenRebellion(
            CivilWarPlayerCallContext playerCall,
            bool clearStandaloneLeaderWar,
            out string failureReason,
            bool resumeChallenge = false)
        {
            failureReason = null;
            if (!KingdomCreationSafetyHelper.IsValidFounder(Leader))
            {
                failureReason = "the faction has no living clan leader";
                return false;
            }

            if (ParentKingdom == null)
            {
                failureReason = "the faction has no parent kingdom";
                return false;
            }

            if (_rebellionCreationStarted && !resumeChallenge)
            {
                failureReason = "rebellion creation was already started";
                return false;
            }

            if (_rebellionCreationCompleted)
            {
                if (resumeChallenge) return TryValidateActiveRebellion(out _, out failureReason);
                failureReason = "rebellion creation was already completed";
                return false;
            }

            if (IsCivilWarActive() && !resumeChallenge)
            {
                failureReason = "the civil war is already active";
                return false;
            }

            if (HasTrackedRebelKingdom && !resumeChallenge)
            {
                failureReason = "the faction already tracks a rebel kingdom";
                return false;
            }

            TextObject rebelName;

            if (IsGrandCoalition)
            {
                rebelName = GetDisplayName();
            }
            else
            {
                switch (Type)
                {
                    case FactionType.Independence: rebelName = new TextObject("{=BC_FacName_Indep}{CLAN_NAME} Secessionists"); break;
                    case FactionType.InstallRuler: rebelName = new TextObject("{=BC_FacName_Install}{CLAN_NAME} Claimants"); break;
                    case FactionType.Abdication:
                    default: rebelName = new TextObject("{=BC_FacName_Abdic}{CLAN_NAME} Coalition"); break;
                }
            }

            rebelName.SetTextVariable("CLAN_NAME", Leader.Name);

            string uniqueRebelId = resumeChallenge ? ParentKingdom.StringId + "_rebels_challenge_" + _successionChallengeId
                : ParentKingdom.StringId + "_rebels_" + Leader.StringId;
            Kingdom rebelKingdom = resumeChallenge ? GetTrackedRebelKingdomIncludingEliminated() : null;
            rebelKingdom = rebelKingdom ?? Kingdom.All.FirstOrDefault(k => k.StringId == uniqueRebelId && (!k.IsEliminated || resumeChallenge));
            if (rebelKingdom?.IsEliminated == true) { failureReason = "the recorded challenge shell was eliminated"; return false; }
            if (rebelKingdom == null)
            {
                string rebelId = uniqueRebelId;
                int idSuffix = 0;
                while (Kingdom.All.Any(k => k.StringId == rebelId))
                    rebelId = uniqueRebelId + "_" + (++idSuffix);
                rebelKingdom = KingdomCreationSafetyHelper.CreateKingdom(rebelId, Leader);
            }
            if (resumeChallenge) SetRebelKingdom(rebelKingdom);

            if (!KingdomCreationSafetyHelper.PrepareRuler(rebelKingdom, Leader, "start rebellion"))
            {
                failureReason = "the rebel kingdom has no valid intended ruler";
                return false;
            }
            _rebellionCreationStarted = true;

            Settlement rebelSettlement = Leader.Settlements.FirstOrDefault() ?? ParentKingdom.Settlements.FirstOrDefault() ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);

            var rebelVisuals = KingdomVisualHelper.ResolveBreakawayKingdomVisuals(ParentKingdom, Leader, rebelKingdom.StringId);

            if (!resumeChallenge || !_challengeShellInitialized)
            {
                rebelKingdom.InitializeKingdom(rebelName, rebelName, Leader?.Culture ?? ParentKingdom.Culture, rebelVisuals.Banner, rebelVisuals.PrimaryColor, rebelVisuals.SecondaryColor, rebelSettlement, new TextObject(""), new TextObject(""), new TextObject(""));
                if (resumeChallenge) _challengeShellInitialized = true;
            }
            KingdomVisualHelper.ApplyKingdomPalette(rebelKingdom, rebelVisuals);
            RebelPolicyHelper.ApplyIndependencePolicyProfile(this, rebelKingdom);
            SetRebelKingdom(rebelKingdom);
            CaptureCivilWarStartFiefCounts(ParentKingdom.Clans);
            CaptureCivilWarStartFiefCounts(Members);
            CaptureCivilWarStartInfluence(ParentKingdom.Clans);
            CaptureCivilWarStartInfluence(Members);
            Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>()
                ?.CooldownFeudsForCivilWarParticipants(Members, $"civil war started by {Name}");

            bool preserveRebelClanBanners = !rebelVisuals.RecolorMemberClans;
            Dictionary<Clan, Banner> preservedRebelBanners = preserveRebelClanBanners
                ? KingdomVisualHelper.CaptureClanBannersToPreserve(Members)
                : new Dictionary<Clan, Banner>();

            if (clearStandaloneLeaderWar && Leader.IsAtWarWith(ParentKingdom))
            {
                // This is a representation change, not a negotiated peace. Clear the short-lived
                // clan stance silently before the same conflict is declared between realm shells.
                FactionManager.SetNeutral(Leader, ParentKingdom);
                FactionHelper.FinishAllRelatedHostileActionsOfFactionToFaction(Leader, ParentKingdom);
                FactionHelper.FinishAllRelatedHostileActionsOfFactionToFaction(ParentKingdom, Leader);
            }

            if (Leader.Kingdom != rebelKingdom)
                MoveClanToKingdomPreservingCivilWarInfluence(Leader, rebelKingdom, preserveRebelClanBanners, showNotification: false);
            rebelKingdom.RulingClan = Leader;
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()
                ?.ValidateTransitionRulerOnce(rebelKingdom, Leader, $"civil war creation against {ParentKingdom.StringId}");

            foreach (Clan member in Members.ToList())
            {
                if (member != Leader && !member.IsEliminated && member.Kingdom == ParentKingdom)
                    MoveClanToKingdomPreservingCivilWarInfluence(member, rebelKingdom, preserveRebelClanBanners, showNotification: false);
            }

            if (Leader?.Kingdom == rebelKingdom && rebelKingdom.RulingClan != Leader)
                rebelKingdom.RulingClan = Leader;

            if (preserveRebelClanBanners)
                KingdomVisualHelper.RestoreClanBanners(preservedRebelBanners);
            else
                KingdomVisualHelper.ReapplyKingdomPaletteAfterClanChanges(rebelKingdom, rebelVisuals);

            if (!rebelKingdom.IsAtWarWith(ParentKingdom))
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(rebelKingdom, ParentKingdom));

            if (!TryValidateActiveRebellion(out _, out failureReason))
            {
                BellumCivileLogger.Log(
                    $"Rebellion creation failed postcondition validation; faction={Name}; rebel={rebelKingdom?.StringId ?? "none"}; parent={ParentKingdom?.StringId ?? "none"}; reason={failureReason ?? "unknown"}.");
                return false;
            }

            if (!resumeChallenge || !_challengeWarRegistered)
            {
                Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.RegisterCivilWar(this, rebelKingdom);
                Campaign.Current.GetCampaignBehavior<CivilWarInterventionBehavior>()?.ApplyLock(rebelKingdom, 30);
                Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordCivilWarStarted(this, rebelKingdom);
                if (resumeChallenge) _challengeWarRegistered = true;
            }
            _rebellionCreationCompleted = true;

            TextObject msg2 = new TextObject("{=BC_Msg_RealmFractures}The realm fractures. {LEADER_NAME} has raised their banners in open rebellion against {KINGDOM_NAME}.");
            CivilWarConflictBehavior.Instance?.Observe(this, rebelKingdom,
                Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(rebelKingdom, ParentKingdom));
            msg2.SetTextVariable("LEADER_NAME", Leader.Name);
            msg2.SetTextVariable("KINGDOM_NAME", ParentKingdom.Name);
            if (!resumeChallenge || !_challengeWarAnnounced)
            {
                if (resumeChallenge) _challengeWarAnnounced = true;
                BellumCivileNotifications.Show(msg2, BellumNotificationColors.Rebellion, primaryKingdom: ParentKingdom, secondaryKingdom: rebelKingdom, primaryClan: Leader, isMajorEvent: true);
            }

            if (playerCall != null)
                NotificationHelper.ShowCivilWarSolidarityChoiceToPlayer(this, playerCall);

            return true;
        }

        internal bool TryValidateActiveRebellion(out Kingdom rebelKingdom, out string failureReason)
        {
            rebelKingdom = GetRebelKingdom();
            failureReason = null;

            if (ParentKingdom == null)
            {
                failureReason = "the parent kingdom is missing";
                return false;
            }

            if (Leader == null || Leader.IsEliminated)
            {
                failureReason = "the faction leader is missing or eliminated";
                return false;
            }

            if (rebelKingdom == null || rebelKingdom.IsEliminated)
            {
                failureReason = "the rebel kingdom is missing or eliminated";
                return false;
            }

            if (!IsTrackedRebelKingdom(rebelKingdom))
            {
                failureReason = "the faction does not track the rebel kingdom";
                return false;
            }

            if (Leader.Kingdom != rebelKingdom)
            {
                failureReason = "the faction leader was not transferred to the rebel kingdom";
                return false;
            }

            if (rebelKingdom.RulingClan != Leader)
            {
                failureReason = "the faction leader is not the rebel kingdom's ruling clan";
                return false;
            }

            if (!rebelKingdom.IsAtWarWith(ParentKingdom))
            {
                failureReason = "the rebel kingdom is not at war with its parent kingdom";
                return false;
            }

            return true;
        }

        private bool TryResolveUltimatum(string outcome)
        {
            if (_ultimatumResolved)
            {
                BellumCivileLogger.Log($"Ignored duplicate ultimatum callback; faction={Name}; outcome={outcome ?? "unknown"}.");
                return false;
            }

            _ultimatumPending = false;
            _ultimatumResolved = true;
            return true;
        }

        private void RallyAiSupportersOnce()
        {
            if (_solidarityRecruitmentApplied)
                return;

            _solidarityRecruitmentApplied = true;
            CivilWarSolidarityHelper.RallyAiSupporters(this);
        }

        internal static int CalculateDesiredFiefs(Clan clan)
        {
            return ClanFiefDesireHelper.CalculateDesiredFiefs(clan);
        }

        public void CaptureCivilWarStartFiefCounts(IEnumerable<Clan> clans)
        {
            EnsureCivilWarFiefSnapshotInitialized();

            foreach (Clan clan in clans ?? Enumerable.Empty<Clan>())
                CaptureCivilWarStartFiefCount(clan);
        }

        public void CaptureCivilWarStartInfluence(IEnumerable<Clan> clans)
        {
            EnsureCivilWarInfluenceSnapshotInitialized();

            foreach (Clan clan in clans ?? Enumerable.Empty<Clan>())
                CaptureCivilWarStartInfluence(clan);
        }

        public void CaptureCivilWarStartFiefCount(Clan clan)
        {
            if (clan == null) return;

            EnsureCivilWarFiefSnapshotInitialized();
            if (_civilWarStartFiefClans.Contains(clan))
                return;

            _civilWarStartFiefClans.Add(clan);
            _civilWarStartFiefCounts.Add(clan.Fiefs.Count);
        }

        public void CaptureCivilWarStartInfluence(Clan clan)
        {
            if (clan == null) return;

            EnsureCivilWarInfluenceSnapshotInitialized();
            if (_civilWarStartInfluenceClans.Contains(clan))
                return;

            _civilWarStartInfluenceClans.Add(clan);
            _civilWarStartInfluenceValues.Add(TaleWorlds.Library.MathF.Max(0f, clan.Influence));
        }

        public void MoveClanToKingdomPreservingCivilWarInfluence(Clan clan, Kingdom targetKingdom, bool preserveCustomBanner = true, bool showNotification = false)
        {
            if (clan == null || targetKingdom == null || clan.IsUnderMercenaryService)
                return;

            CaptureCivilWarStartInfluence(clan);

            if (preserveCustomBanner)
                KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, targetKingdom, showNotification);
            else
                KingdomVisualHelper.ApplyJoinToKingdomWithBannerPolicy(clan, targetKingdom, preserveCustomBanner: false, showNotification: showNotification);

            RestoreCivilWarInfluenceSnapshot(clan);
        }

        public void RestoreCivilWarInfluenceSnapshots(IEnumerable<Clan> clans)
        {
            EnsureCivilWarInfluenceSnapshotInitialized();

            foreach (Clan clan in clans ?? Enumerable.Empty<Clan>())
                RestoreCivilWarInfluenceSnapshot(clan);
        }

        public void RestoreCivilWarInfluenceSnapshot(Clan clan)
        {
            if (clan == null)
                return;

            EnsureCivilWarInfluenceSnapshotInitialized();
            int index = _civilWarStartInfluenceClans.IndexOf(clan);
            if (index < 0 || index >= _civilWarStartInfluenceValues.Count)
                return;

            float snapshot = TaleWorlds.Library.MathF.Max(0f, _civilWarStartInfluenceValues[index]);
            if (clan.Influence < snapshot)
                clan.Influence = snapshot;
        }

        public string ExportCivilWarStartFiefSnapshot()
        {
            EnsureCivilWarFiefSnapshotInitialized();

            List<string> entries = new List<string>();
            int count = TaleWorlds.Library.MathF.Min(_civilWarStartFiefClans.Count, _civilWarStartFiefCounts.Count);
            for (int i = 0; i < count; i++)
            {
                Clan clan = _civilWarStartFiefClans[i];
                if (clan == null || string.IsNullOrEmpty(clan.StringId))
                    continue;

                entries.Add(clan.StringId + ":" + _civilWarStartFiefCounts[i]);
            }

            return string.Join(",", entries);
        }

        private void EnsureCivilWarFiefSnapshotInitialized()
        {
            if (_civilWarStartFiefClans == null) _civilWarStartFiefClans = new List<Clan>();
            if (_civilWarStartFiefCounts == null) _civilWarStartFiefCounts = new List<int>();
        }

        private void EnsureCivilWarInfluenceSnapshotInitialized()
        {
            if (_civilWarStartInfluenceClans == null) _civilWarStartInfluenceClans = new List<Clan>();
            if (_civilWarStartInfluenceValues == null) _civilWarStartInfluenceValues = new List<float>();
        }

        public void HandleClanDestroyed(Clan clan)
        {
            _loyalClan = null;
        }
    }
}
