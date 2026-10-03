using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyTermVM : ViewModel
    {
        private string _name;
        private string _costText;

        public TreatyTermVM(TreatyTermRecord term, string favoredKingdomId = "", int? effectiveImpact = null)
        {
            Name = BuildName(term);
            int impact = effectiveImpact ?? term?.GetWarScoreImpact(favoredKingdomId) ?? 0;
            CostText = new TextObject("{=BC_Parley_WarScoreCost}{VALUE} WS")
                .SetTextVariable("VALUE", impact.ToString("+0;-0;0"))
                .ToString();
        }

        [DataSourceProperty]
        public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "Name"); } } }
        [DataSourceProperty]
        public string CostText { get => _costText; set { if (value != _costText) { _costText = value; OnPropertyChangedWithValue(value, "CostText"); } } }

        private static string BuildName(TreatyTermRecord term)
        {
            if (term == null)
                return string.Empty;
            switch (term.Type)
            {
                case TreatyTermType.HostagePeace:
                    return new TextObject("{=BC_Parley_TermHostage}{FROM} pledges {HERO} to {TO} for {DAYS} days of peace")
                        .SetTextVariable("DAYS", term.DurationDays)
                        .SetTextVariable("FROM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("HERO", Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == term.HeroId)?.Name ?? TextObject.GetEmpty()).ToString();
                case TreatyTermType.WhitePeace:
                    return new TextObject("{=BC_Parley_Term_WhitePeace}White Peace").ToString();
                case TreatyTermType.TransferFief:
                    Settlement settlement = Settlement.All.FirstOrDefault(candidate => candidate?.StringId == term.SettlementId);
                    return new TextObject("{=BC_Parley_Term_TransferFief}Transfer {FIEF_NAME}: {FROM_REALM} to {TO_REALM}")
                        .SetTextVariable("FIEF_NAME", settlement?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .ToString();
                case TreatyTermType.RecognizeClientOccupation:
                    return ClientWarTerritory.RecognitionText(
                        Settlement.All.FirstOrDefault(s => s.StringId == term.SettlementId),
                        ClientWarTerritory.Realm(term.ThirdKingdomId)).ToString();
                case TreatyTermType.Reparations:
                    return new TextObject("{=BC_Parley_Term_ReparationsDirectional}{FROM_REALM} pays {TO_REALM} {GOLD} denars in reparations")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("GOLD", term.GoldAmount)
                        .ToString();
                case TreatyTermType.Tribute:
                    return new TextObject("{=BC_Parley_Term_TributeDirectional}{FROM_REALM} pays {TO_REALM} {GOLD} denars/day for {DAYS} days")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("GOLD", term.DailyGold)
                        .SetTextVariable("DAYS", term.DurationDays)
                        .ToString();
                case TreatyTermType.ReleasePrisoner:
                    Hero hero = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term.HeroId);
                    return new TextObject("{=BC_Parley_Term_ReleasePrisoner}{FROM_REALM} releases {HERO_NAME} to {TO_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("HERO_NAME", hero?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.RenounceClaim:
                    Clan claimant = Clan.All.FirstOrDefault(candidate => candidate?.StringId == term.ClanId);
                    FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<Behaviors.FeudalTitleBehavior>()?.GetTitle(term.TitleId);
                    return new TextObject("{=BC_Parley_Term_RenounceClaim}{CLAN_NAME} renounces its claim to {TITLE_NAME}")
                        .SetTextVariable("CLAN_NAME", claimant?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("TITLE_NAME", title == null ? TextObject.GetEmpty() : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(title, claimant)))
                        .ToString();
                case TreatyTermType.DiscreditRuler:
                case TreatyTermType.HumiliateRuler:
                    Kingdom targetRealm = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == term.FromKingdomId);
                    TextObject template = term.Type == TreatyTermType.HumiliateRuler
                        ? new TextObject("{=BC_Parley_Term_HumiliateRuler}{TO_REALM} humiliates {RULER_NAME} of {FROM_REALM}")
                        : new TextObject("{=BC_Parley_Term_DiscreditRuler}{TO_REALM} discredits {RULER_NAME} of {FROM_REALM}");
                    return template
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("RULER_NAME", targetRealm?.RulingClan?.Leader?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.ReleaseVassal:
                    Clan releasedClan = Clan.All.FirstOrDefault(candidate => candidate?.StringId == term.ClanId);
                    FeudalTitleRecord releasedTitle = Campaign.Current?.GetCampaignBehavior<Behaviors.FeudalTitleBehavior>()?.GetTitle(term.TitleId);
                    return new TextObject("{=BC_Parley_Term_ReleaseVassal}{FROM_REALM} releases {CLAN_NAME} as the independent {TITLE_NAME}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("CLAN_NAME", releasedClan?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("TITLE_NAME", releasedTitle == null ? TextObject.GetEmpty() : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(releasedTitle, releasedClan)))
                        .ToString();
                case TreatyTermType.ForceVassalization:
                    return new TextObject("{=BC_Parley_Term_ForceVassalization}{FROM_REALM} submits as a vassal realm of {TO_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .ToString();
                case TreatyTermType.MakeClientKingdom:
                    TextObject clientTemplate = term.WasVoluntaryOffering
                        ? new TextObject("{=BC_Parley_Term_OfferClientKingdom}{FROM_REALM} voluntarily enters the clientage of {TO_REALM}")
                        : new TextObject("{=BC_Parley_Term_MakeClientKingdom}{FROM_REALM} submits as a client kingdom of {TO_REALM}");
                    return clientTemplate
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .ToString();
                case TreatyTermType.ArrangeRoyalMarriage:
                    Hero concedingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidateHero => candidateHero?.StringId == term.HeroId);
                    Hero receivingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidateHero => candidateHero?.StringId == term.SecondaryHeroId);
                    return new TextObject("{=BC_Parley_Term_RoyalMarriage}Arrange the marriage of {FIRST_NAME} and {SECOND_NAME}; {FIRST_NAME} joins the {RECEIVING_CLAN}")
                        .SetTextVariable("FIRST_NAME", concedingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("SECOND_NAME", receivingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("RECEIVING_CLAN", receivingSpouse?.Clan?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.EndTradeAgreement:
                    return new TextObject("{=BC_Parley_Term_EndTradeAgreement}{FROM_REALM} ends its trade agreement with {THIRD_REALM} as part of its settlement with {TO_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("THIRD_REALM", ResolveKingdomName(term.ThirdKingdomId))
                        .ToString();
                case TreatyTermType.EndAlliance:
                    return new TextObject("{=BC_Parley_Term_EndAlliance}{FROM_REALM} ends its alliance with {THIRD_REALM} as part of its settlement with {TO_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("THIRD_REALM", ResolveKingdomName(term.ThirdKingdomId))
                        .ToString();
                case TreatyTermType.ConcedeDefeat:
                    return new TextObject("{=BC_Parley_Term_ConcedeDefeat}{FROM_REALM} concedes defeat to {TO_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .ToString();
                case TreatyTermType.ReleaseClientState:
                    return new TextObject("{=BC_Parley_Term_ReleaseClientState}{FROM_REALM} releases {THIRD_REALM} from clientage")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("THIRD_REALM", ResolveKingdomName(term.ThirdKingdomId))
                        .ToString();
                case TreatyTermType.EnforceRebelDemands:
                    return new TextObject("{=BC_Parley_Term_EnforceRebelDemands}{TO_REALM} enforces the demands of {THIRD_REALM} against {FROM_REALM}")
                        .SetTextVariable("FROM_REALM", ResolveKingdomName(term.FromKingdomId))
                        .SetTextVariable("TO_REALM", ResolveKingdomName(term.ToKingdomId))
                        .SetTextVariable("THIRD_REALM", ResolveKingdomName(term.ThirdKingdomId))
                        .ToString();
                default: return term.Type.ToString();
            }
        }

        private static TextObject ResolveKingdomName(string kingdomId)
        {
            return Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == kingdomId)?.Name ?? TextObject.GetEmpty();
        }
    }
}
