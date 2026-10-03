using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public static class RelationMemorySources
    {
        public const string PriorHistory = "prior_history";
        public const string BrokeHostagePeace = "broke_hostage_peace";
        public const string BetrayedHostagePledge = "betrayed_hostage_pledge";
        public const string SparedTreatyHostage = "spared_treaty_hostage";
        public const string ReleasedTreatyHostageEarly = "released_treaty_hostage_early";
        public const string PriorPersonalHistory = "prior_personal_history";
        public const string ProtectedSettlement = "protected_settlement";
        public const string ProtectedVillagers = "protected_villagers";
        public const string ProtectedCaravan = "protected_caravan";
        public const string PlunderedSettlement = "plundered_settlement";
        public const string DevastatedSettlement = "devastated_settlement";
        public const string InterferedInAffairs = "interfered_in_affairs";
        public const string CelebratedMarriage = "celebrated_marriage";
        public const string FulfilledRequest = "fulfilled_request";
        public const string FailedRequest = "failed_request";
        public const string BetrayedTrust = "betrayed_trust";
        public const string HelpedCommunity = "helped_community";
        public const string FailedCommunity = "failed_community";
        public const string KeptPeace = "kept_peace";
        public const string AllowedLawlessness = "allowed_lawlessness";
        public const string LocalGoodwill = "local_goodwill";
        public const string RaidedLands = "raided_lands";
        public const string DisregardedAncestralClaims = "disregarded_ancestral_claims";
        public const string SatisfiedSuccessionDemand = "satisfied_succession_demand";
        public const string RecentFavor = "recent_favor";
        public const string CourtTitleGrant = "court_title_grant";
        public const string ClientLandGrant = "client_land_grant";
        public const string DeliveredNoblePrisoners = "delivered_noble_prisoners";
        public const string RecentGrievance = "recent_grievance";
        public const string KilledKinsman = "killed_kinsman";
        public const string KilledCloseFamily = "killed_close_family";
        public const string ExecutedCloseFamily = "executed_close_family";
        public const string MurderedCloseFamily = "murdered_close_family";
        public const string MurderedKinsman = "murdered_kinsman";
        public const string ExecutedKinsman = "executed_kinsman";
        public const string KilledFriend = "killed_friend";
        public const string KilledEnemy = "killed_enemy";
        public const string SharedBattle = "shared_battle";
        public const string BlamedForDefeat = "blamed_for_defeat";
        public const string AbandonedInCaptivity = "abandoned_in_captivity";
        public const string LostMyFief = "lost_my_fief";
        public const string VoteAlignment = "vote_alignment";
        public const string PolicyMandateOutsideAgenda = "policy_mandate_outside_agenda";
        public const string PolicyMandateBetrayal = "policy_mandate_betrayal";
        public const string BackedCrownPolicyVote = "backed_crown_policy_vote";
        public const string OpposedCrownPolicyVote = "opposed_crown_policy_vote";
        public const string BackedCrownFiefVote = "backed_crown_fief_vote";
        public const string OpposedCrownFiefVote = "opposed_crown_fief_vote";
        public const string BetrayedFactionAgenda = "betrayed_faction_agenda";
        public const string BackedMySuccession = "backed_my_succession";
        public const string OpposedMySuccession = "opposed_my_succession";
        public const string HonoredCallToArms = "honored_call_to_arms";
        public const string RefusedCallToArms = "refused_call_to_arms";
        public const string AbandonedMyCause = "abandoned_my_cause";
        public const string FeudOathbreaker = "feud_oathbreaker";
        public const string DefectedFromMyCause = "defected_from_my_cause";
        public const string FeudalService = "feudal_service";
        public const string TitleUsurpation = "title_usurpation";
        public const string CivilWarSettlement = "civil_war_settlement";
        public const string WarOfIndependence = "war_of_independence";
        public const string CourtPolitics = "court_politics";
        public const string CourtCooperation = "court_cooperation";
        public const string CourtObstruction = "court_obstruction";
        public const string UnfulfilledFactionAgenda = "unfulfilled_faction_agenda";
        public const string AllowedMyDispossession = "allowed_my_dispossession";
        public const string SeizedMyFief = "seized_my_fief";
        public const string ClaimedFiefForCrown = "claimed_fief_for_crown";
        public const string ExpelledMyFriend = "expelled_my_friend";
        public const string AgreedOnTreatyPosition = "agreed_on_treaty_position";
        public const string OverruledTreatyPosition = "overruled_treaty_position";
        public const string SharedTreatyPosition = "shared_treaty_position";
        public const string OpposedTreatyPosition = "opposed_treaty_position";
        public const string PardonedMyHouse = "pardoned_my_house";
        public const string ConfiscatedMyHouse = "confiscated_my_house";
        public const string ExiledMyHouse = "exiled_my_house";
        public const string EnforcedPeaceOnMyFeud = "enforced_peace_on_my_feud";
        public const string AppointedMeToCouncil = "appointed_me_to_council";
        public const string DismissedMeFromCouncil = "dismissed_me_from_council";
        public const string RelievedDuringCaptivity = "relieved_during_captivity";
        public const string OverruledMyCouncilVote = "overruled_my_council_vote";
        public const string OverruledMyFaction = "overruled_my_faction";
        public const string ReleasedMyKinsman = "released_my_kinsman";
        public const string LiberatedMyRealm = "liberated_my_realm";
        public const string BrokeTreatyOrAlliance = "broke_treaty_or_alliance";
        public const string MercenaryService = "mercenary_service";
        public const string LongMercenaryService = "long_mercenary_service";
        public const string MercenaryContractSettled = "mercenary_contract_settled";
        public const string MercenaryCompanyDismissed = "mercenary_company_dismissed";
        public const string MercenaryWagesWithheld = "mercenary_wages_withheld";
        public const string ChangedSuccessionLaws = "changed_succession_laws";
        public const string EnfeoffedMyHouse = "enfeoffed_my_house";
        public const string SupportedMyMercenaryVenture = "supported_my_mercenary_venture";
        public const string BlessedMyMercenaryDeparture = "blessed_my_mercenary_departure";
        public const string AskedMeToAbandonMercenaryVenture = "asked_me_to_abandon_mercenary_venture";
        public const string ForbadeMyMercenaryDeparture = "forbade_my_mercenary_departure";
        public const string PatronizedMyMercenaryCompany = "patronized_my_mercenary_company";
        public const string OpposedMyCompanyRecruitment = "opposed_my_company_recruitment";

        public static string GetDisplayText(string sourceId, string contextText)
        {
            TextObject text;
            switch (sourceId ?? string.Empty)
            {
                case BrokeHostagePeace: text = new TextObject("{=BC_RelationMemory_BrokeHostagePeace}Broke our pledge of peace"); break;
                case BetrayedHostagePledge: text = new TextObject("{=BC_RelationMemory_BetrayedHostagePledge}Betrayed the pledge and executed our hostage"); break;
                case ReleasedTreatyHostageEarly: text = new TextObject("{=BC_RelationMemory_ReleasedTreatyHostageEarly}Honorably returned our hostage before the pledge expired"); break;
                case SparedTreatyHostage: text = new TextObject("{=BC_RelationMemory_SparedTreatyHostage}Returned our hostage despite our breach"); break;
                case ProtectedSettlement: text = new TextObject("{=BC_RelationMemory_ProtectedSettlement}Defended our village from raiders"); break;
                case ProtectedVillagers: text = new TextObject("{=BC_RelationMemory_ProtectedVillagers}Came to the aid of our villagers"); break;
                case ProtectedCaravan: text = new TextObject("{=BC_RelationMemory_ProtectedCaravan}Came to the aid of my caravan"); break;
                case PlunderedSettlement: text = new TextObject("{=BC_RelationMemory_PlunderedSettlement}Plundered our settlement"); break;
                case DevastatedSettlement: text = new TextObject("{=BC_RelationMemory_DevastatedSettlement}Laid waste to our settlement"); break;
                case InterferedInAffairs: text = new TextObject("{=BC_RelationMemory_InterferedInAffairs}Interfered in my affairs"); break;
                case KilledCloseFamily: text = new TextObject("{=BC_RelationMemory_KilledCloseFamily}Killed a close member of my family"); break;
                case ExecutedCloseFamily: text = new TextObject("{=BC_RelationMemory_ExecutedCloseFamily}Executed a close member of my family"); break;
                case MurderedCloseFamily: text = new TextObject("{=BC_RelationMemory_MurderedCloseFamily}Murdered a close member of my family"); break;
                case MurderedKinsman: text = new TextObject("{=BC_RelationMemory_MurderedKinsman}Murdered a member of our house"); break;
                case CelebratedMarriage: text = new TextObject("{=BC_RelationMemory_CelebratedMarriage}Celebrated a marriage"); break;
                case FulfilledRequest: text = new TextObject("{=BC_RelationMemory_FulfilledRequest}Fulfilled my request"); break;
                case FailedRequest: text = new TextObject("{=BC_RelationMemory_FailedRequest}Failed my request"); break;
                case BetrayedTrust: text = new TextObject("{=BC_RelationMemory_BetrayedTrust}Betrayed my trust"); break;
                case HelpedCommunity: text = new TextObject("{=BC_RelationMemory_HelpedCommunity}Helped our community"); break;
                case FailedCommunity: text = new TextObject("{=BC_RelationMemory_FailedCommunity}Failed our community in its need"); break;
                case KeptPeace: text = new TextObject("{=BC_RelationMemory_KeptPeace}Kept the peace in our town"); break;
                case AllowedLawlessness: text = new TextObject("{=BC_RelationMemory_AllowedLawlessness}Allowed lawlessness to flourish"); break;
                case LocalGoodwill: text = new TextObject("{=BC_RelationMemory_LocalGoodwill}Earned the goodwill of our village"); break;
                case RaidedLands: text = new TextObject("{=BC_RelationMemory_RaidedLands}Raided our lands"); break;
                case RelievedDuringCaptivity:
                    text = new TextObject("{=BC_RelationMemory_CaptiveDismissal}Relieved me of office during captivity");
                    break;
                case FeudOathbreaker:
                    text = new TextObject("{=BC_RelationMemory_FeudOathbreaker}Oathbreaker");
                    break;
                case DisregardedAncestralClaims:
                    text = new TextObject("{=BC_RelationMemory_DisregardedAncestralClaims}Disregarded our ancestral claims");
                    break;
                case SatisfiedSuccessionDemand:
                    text = new TextObject("{=BC_RelationMemory_SatisfiedSuccessionDemand}Satisfied my succession demand");
                    break;
                case PriorHistory:
                    text = new TextObject("{=BC_RelationMemory_PriorHistory}Prior history");
                    break;
                case PriorPersonalHistory:
                    text = new TextObject("{=BC_Relation_OpeningHistory}Prior personal history");
                    break;
                case ClientLandGrant:
                    text = new TextObject("{=BC_ClientGrantMemory}Enlarged our realm");
                    break;
                case RecentFavor:
                case CourtTitleGrant:
                    text = sourceId == CourtTitleGrant ? new TextObject("{=BC_TitleGrantMemory}Granted me a title")
                        : new TextObject("{=BC_RelationMemory_RecentFavor}Recent favor");
                    break;
                case DeliveredNoblePrisoners:
                    text = new TextObject("{=BC_RelationMemory_DeliveredNoblePrisoners}Delivered noble prisoners");
                    break;
                case RecentGrievance:
                    text = new TextObject("{=BC_RelationMemory_RecentGrievance}Recent grievance");
                    break;
                case KilledKinsman:
                    text = new TextObject("{=BC_RelationMemory_KilledKinsman}Killed my kinsman");
                    break;
                case ExecutedKinsman:
                    text = new TextObject("{=BC_RelationMemory_ExecutedKinsman}Executed my kinsman");
                    break;
                case KilledFriend:
                    text = new TextObject("{=BC_RelationMemory_KilledFriend}Killed my friend");
                    break;
                case KilledEnemy:
                    text = new TextObject("{=BC_RelationMemory_KilledEnemy}Killed my enemy");
                    break;
                case SharedBattle:
                    text = new TextObject("{=BC_RelationMemory_SharedBattle}Fought at my side");
                    break;
                case BlamedForDefeat:
                    text = new TextObject("{=BC_RelationMemory_BlamedForDefeat}Blamed for our defeat");
                    break;
                case AbandonedInCaptivity:
                    text = new TextObject("{=BC_RelationMemory_AbandonedInCaptivity}Left me in captivity");
                    break;
                case LostMyFief:
                    text = new TextObject("{=BC_RelationMemory_LostMyFief}Failed to protect my lands");
                    break;
                case VoteAlignment:
                    text = new TextObject("{=BC_RelationMemory_VoteAlignment}Remembered political support");
                    break;
                case PolicyMandateOutsideAgenda:
                    text = new TextObject("{=BC_RelationMemory_PolicyMandateOutsideAgenda}Used a policy mandate outside the faction agenda");
                    break;
                case PolicyMandateBetrayal:
                    text = new TextObject("{=BC_RelationMemory_PolicyMandateBetrayal}Betrayed the faction's policy mandate");
                    break;
                case BackedCrownPolicyVote:
                    text = new TextObject("{=BC_RelationMemory_BackedCrownPolicyVote}Backed the crown in a policy vote");
                    break;
                case OpposedCrownPolicyVote:
                    text = new TextObject("{=BC_RelationMemory_OpposedCrownPolicyVote}Opposed the crown in a policy vote");
                    break;
                case BackedCrownFiefVote:
                    text = new TextObject("{=BC_RelationMemory_BackedCrownFiefVote}Backed the crown in a fief vote");
                    break;
                case OpposedCrownFiefVote:
                    text = new TextObject("{=BC_RelationMemory_OpposedCrownFiefVote}Opposed the crown in a fief vote");
                    break;
                case BetrayedFactionAgenda:
                    text = new TextObject("{=BC_RelationMemory_BetrayedFactionAgenda}Betrayed the faction agenda in a policy vote");
                    break;
                case BackedMySuccession:
                    text = new TextObject("{=BC_RelationMemory_BackedMySuccession}Backed my succession");
                    break;
                case OpposedMySuccession:
                    text = new TextObject("{=BC_RelationMemory_OpposedMySuccession}Opposed my succession");
                    break;
                case HonoredCallToArms:
                    text = new TextObject("{=BC_RelationMemory_HonoredCallToArms}Honored my call to arms");
                    break;
                case RefusedCallToArms:
                    text = new TextObject("{=BC_RelationMemory_RefusedCallToArms}Refused my call to arms");
                    break;
                case AbandonedMyCause:
                    text = new TextObject("{=BC_RelationMemory_AbandonedMyCause}Abandoned my cause");
                    break;
                case DefectedFromMyCause:
                    text = new TextObject("{=BC_RelationMemory_DefectedFromMyCause}Defected from my cause");
                    break;
                case FeudalService:
                    text = new TextObject("{=BC_RelationMemory_FeudalService}Changed feudal obligations");
                    break;
                case TitleUsurpation:
                    text = new TextObject("{=BC_RelationMemory_TitleUsurpation}Usurped my title");
                    break;
                case CivilWarSettlement:
                    text = new TextObject("{=BC_RelationMemory_CivilWarSettlement}Civil-war settlement");
                    break;
                case WarOfIndependence:
                    text = new TextObject("{=BC_RelationMemory_WarOfIndependence}War of independence");
                    break;
                case CourtPolitics:
                    text = new TextObject("{=BC_RelationMemory_CourtPolitics}Court politics");
                    break;
                case CourtCooperation:
                    text = new TextObject("{=BC_RelationMemory_CourtCooperation}Cooperation at court");
                    break;
                case CourtObstruction:
                    text = new TextObject("{=BC_RelationMemory_CourtObstruction}Obstruction at court");
                    break;
                case UnfulfilledFactionAgenda:
                    text = new TextObject("{=BC_RelationMemory_UnfulfilledFactionAgenda}Failed to deliver the faction's promised agenda");
                    break;
                case AllowedMyDispossession:
                    text = new TextObject("{=BC_RelationMemory_AllowedMyDispossession}Allowed my house to be dispossessed");
                    break;
                case SeizedMyFief:
                    text = new TextObject("{=BC_RelationMemory_SeizedMyFief}Seized my house's fief");
                    break;
                case ClaimedFiefForCrown:
                    text = new TextObject("{=BC_RelationMemory_ClaimedFiefForCrown}Claimed a fief for the crown");
                    break;
                case ExpelledMyFriend:
                    text = new TextObject("{=BC_RelationMemory_ExpelledMyFriend}Expelled my friend from the realm");
                    break;
                case AgreedOnTreatyPosition:
                    text = new TextObject("{=BC_RelationMemory_AgreedOnTreatyPosition}Agreed with my treaty position");
                    break;
                case OverruledTreatyPosition:
                    text = new TextObject("{=BC_RelationMemory_OverruledTreatyPosition}Overruled my treaty position");
                    break;
                case SharedTreatyPosition:
                    text = new TextObject("{=BC_RelationMemory_SharedTreatyPosition}Backed the same treaty position");
                    break;
                case OpposedTreatyPosition:
                    text = new TextObject("{=BC_RelationMemory_OpposedTreatyPosition}Opposed me over treaty terms");
                    break;
                case PardonedMyHouse:
                    text = new TextObject("{=BC_RelationMemory_PardonedMyHouse}Pardoned my house");
                    break;
                case ConfiscatedMyHouse:
                    text = new TextObject("{=BC_RelationMemory_ConfiscatedMyHouse}Confiscated my house's lands");
                    break;
                case ExiledMyHouse:
                    text = new TextObject("{=BC_RelationMemory_ExiledMyHouse}Exiled my house");
                    break;
                case EnforcedPeaceOnMyFeud:
                    text = new TextObject("{=BC_RelationMemory_EnforcedPeaceOnMyFeud}Imposed peace upon my feud");
                    break;
                case AppointedMeToCouncil:
                    text = new TextObject("{=BC_RelationMemory_AppointedMeToCouncil}Appointed me to the council");
                    break;
                case DismissedMeFromCouncil:
                    text = new TextObject("{=BC_RelationMemory_DismissedMeFromCouncil}Dismissed me from the council");
                    break;
                case OverruledMyCouncilVote:
                    text = new TextObject("{=BC_RelationMemory_OverruledMyCouncilVote}Overruled my council vote");
                    break;
                case OverruledMyFaction:
                    text = new TextObject("{=BC_RelationMemory_OverruledMyFaction}Overruled my faction");
                    break;
                case ReleasedMyKinsman:
                    text = new TextObject("{=BC_RelationMemory_ReleasedMyKinsman}Released my kinsman");
                    break;
                case LiberatedMyRealm:
                    text = new TextObject("{=BC_RelationMemory_LiberatedMyRealm}Liberated my realm");
                    break;
                case BrokeTreatyOrAlliance:
                    text = new TextObject("{=BC_RelationMemory_BrokeTreatyOrAlliance}Broke a treaty or alliance");
                    break;
                case MercenaryService:
                    text = new TextObject("{=BC_RelationMemory_MercenaryService}Mercenary service");
                    break;
                case LongMercenaryService:
                    text = new TextObject("{=BC_RelationMemory_LongMercenaryService}Long mercenary service");
                    break;
                case MercenaryContractSettled:
                    text = new TextObject("{=BC_RelationMemory_MercenaryContractSettled}Contract settled honorably");
                    break;
                case MercenaryCompanyDismissed:
                    text = new TextObject("{=BC_RelationMemory_MercenaryCompanyDismissed}Mercenary company dismissed");
                    break;
                case MercenaryWagesWithheld:
                    text = new TextObject("{=BC_RelationMemory_MercenaryWagesWithheld}Mercenary wages withheld");
                    break;
                case ChangedSuccessionLaws:
                    text = new TextObject("{=BC_RelationMemory_ChangedSuccessionLaws}Changed the laws of succession");
                    break;
                case EnfeoffedMyHouse:
                    text = new TextObject("{=BC_RelationMemory_EnfeoffedMyHouse}Founded and enfeoffed my house");
                    break;
                case SupportedMyMercenaryVenture:
                    text = new TextObject("{=BC_RelationMemory_SupportedMyMercenaryVenture}Supported my mercenary venture");
                    break;
                case BlessedMyMercenaryDeparture:
                    text = new TextObject("{=BC_RelationMemory_BlessedMyMercenaryDeparture}Blessed my mercenary departure");
                    break;
                case AskedMeToAbandonMercenaryVenture:
                    text = new TextObject("{=BC_RelationMemory_AskedMeToAbandonMercenaryVenture}Asked me to abandon my mercenary venture");
                    break;
                case ForbadeMyMercenaryDeparture:
                    text = new TextObject("{=BC_RelationMemory_ForbadeMyMercenaryDeparture}Forbade my mercenary departure");
                    break;
                case PatronizedMyMercenaryCompany:
                    text = new TextObject("{=BC_RelationMemory_PatronizedMyMercenaryCompany}Patronized my mercenary company");
                    break;
                case OpposedMyCompanyRecruitment:
                    text = new TextObject("{=BC_RelationMemory_OpposedMyCompanyRecruitment}Opposed my company's recruitment");
                    break;
                default:
                    text = new TextObject("{=BC_RelationMemory_PoliticalMemory}Political memory");
                    break;
            }

            string label = text.ToString();
            return string.IsNullOrWhiteSpace(contextText) ? label : label + ": " + contextText;
        }
    }

    internal sealed class RelationMemoryDescriptor
    {
        public string SourceId { get; }
        public string ContextText { get; }
        public float DurationDays { get; }
        public RelationMemoryScope Scope { get; }
        public string EventId { get; }

        public RelationMemoryDescriptor(string sourceId, string contextText, float durationDays, RelationMemoryScope scope, string eventId = null)
        {
            SourceId = sourceId ?? string.Empty;
            ContextText = contextText ?? string.Empty;
            DurationDays = Math.Max(1f, durationDays);
            Scope = scope;
            EventId = eventId ?? string.Empty;
        }
    }

    public static class RelationMemoryService
    {
        [ThreadStatic] private static RelationMemoryDescriptor _currentDescriptor;
        [ThreadStatic] private static NativeLabelScope _nativeLabels;
        [ThreadStatic] private static Hero _personalChangeFirst;
        [ThreadStatic] private static Hero _personalChangeSecond;

        internal static bool UsesOriginalPersonalPair(Hero first, Hero second) =>
            BellumCivileOptions.EnableDynamicRelationDrift && first != null && second != null
            && first == _personalChangeFirst && second == _personalChangeSecond;

        internal static RelationMemoryDescriptor CurrentDescriptor => _currentDescriptor;

        internal static IDisposable BeginNativeLabels(string positive, string negative)
        {
            var scope = new NativeLabelScope(positive, negative, _nativeLabels);
            _nativeLabels = scope;
            return scope;
        }

        internal static RelationMemoryDescriptor ResolveCapturedDescriptor(int change)
        {
            if (_currentDescriptor != null) return _currentDescriptor;
            var fallback = BuildFallbackDescriptor(change);
            string source = change > 0 ? _nativeLabels?.Positive : change < 0 ? _nativeLabels?.Negative : null;
            return string.IsNullOrEmpty(source) ? fallback : new RelationMemoryDescriptor(
                source, null, fallback.DurationDays, RelationMemoryScope.Personal);
        }

        private sealed class NativeLabelScope : IDisposable
        {
            internal readonly string Positive;
            internal readonly string Negative;
            private readonly NativeLabelScope _previous;
            private bool _disposed;
            internal NativeLabelScope(string positive, string negative, NativeLabelScope previous)
            { Positive = positive; Negative = negative; _previous = previous; }
            public void Dispose()
            {
                if (_disposed) return;
                _nativeLabels = _previous;
                _disposed = true;
            }
        }

        public static void ApplyChange(
            Hero firstHero,
            Hero secondHero,
            int relationChange,
            bool showNotification,
            string sourceId,
            float durationYears,
            RelationMemoryScope scope = RelationMemoryScope.Personal,
            string contextText = null)
        {
            if (firstHero == null || secondHero == null || relationChange == 0)
                return;

            Hero previousFirst = _personalChangeFirst;
            Hero previousSecond = _personalChangeSecond;
            try
            {
                _personalChangeFirst = scope == RelationMemoryScope.Personal ? firstHero : null;
                _personalChangeSecond = scope == RelationMemoryScope.Personal ? secondHero : null;
                using (Begin(sourceId, durationYears, scope, contextText))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(firstHero, secondHero, relationChange, showNotification);
            }
            finally
            {
                _personalChangeFirst = previousFirst;
                _personalChangeSecond = previousSecond;
            }
        }

        public static IDisposable Begin(
            string sourceId,
            float durationYears,
            RelationMemoryScope scope = RelationMemoryScope.Personal,
            string contextText = null)
        {
            int daysInYear = Math.Max(1, CampaignTime.DaysInYear);
            return Push(new RelationMemoryDescriptor(sourceId, contextText, Math.Max(0.01f, durationYears) * daysInYear, scope));
        }

        internal static IDisposable Push(RelationMemoryDescriptor descriptor)
        {
            RelationMemoryDescriptor previous = _currentDescriptor;
            _currentDescriptor = descriptor;
            return new ContextScope(previous);
        }

        internal static RelationMemoryDescriptor BuildFallbackDescriptor(int relationChange)
        {
            int daysInYear = Math.Max(1, CampaignTime.DaysInYear);
            float years = Math.Max(0.25f, Math.Min(10f, Math.Abs(relationChange) / 3f));
            return new RelationMemoryDescriptor(
                relationChange >= 0 ? RelationMemorySources.RecentFavor : RelationMemorySources.RecentGrievance,
                null,
                years * daysInYear,
                RelationMemoryScope.Personal);
        }

        private sealed class ContextScope : IDisposable
        {
            private readonly RelationMemoryDescriptor _previous;
            private bool _disposed;

            public ContextScope(RelationMemoryDescriptor previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _currentDescriptor = _previous;
                _disposed = true;
            }
        }
    }
}
