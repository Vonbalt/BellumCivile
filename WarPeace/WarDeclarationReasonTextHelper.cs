using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.WarPeace
{
    internal static class WarDeclarationReasonTextHelper
    {
        public static TextObject Build(WarTargetScore assessment, Kingdom targetKingdom)
        {
            WarTargetMotive motive = SelectPrimaryMotive(assessment);
            TextObject result = motive == null
                ? new TextObject("{=BC_WarVoteReason_Generic}The interests of the realm favor a declaration of war.")
                : BuildMotiveText(motive);

            result.SetTextVariable(
                "TARGET_REALM",
                targetKingdom?.InformalName ?? targetKingdom?.Name ?? TextObject.GetEmpty());
            result.SetTextVariable("SUBJECT", motive?.Subject ?? TextObject.GetEmpty());
            return result;
        }

        internal static WarTargetMotive SelectPrimaryMotive(WarTargetScore assessment)
        {
            return SelectPrimaryMotive(assessment?.Motives);
        }

        internal static TextObject BuildPublicJustification(
            WarTargetMotiveType motiveType,
            TextObject subject)
        {
            TextObject result;
            switch (motiveType)
            {
                case WarTargetMotiveType.Liberation:
                    result = new TextObject("{=BC_WarDeclarationReason_Liberation}The declaration invokes the right to cast off foreign domination.");
                    break;
                case WarTargetMotiveType.StrongClaim:
                    result = new TextObject("{=BC_WarDeclarationReason_StrongClaim}The declaration cites a strong lawful claim to {SUBJECT} as justification.");
                    break;
                case WarTargetMotiveType.WeakClaim:
                    result = new TextObject("{=BC_WarDeclarationReason_WeakClaim}The declaration cites a disputed claim to {SUBJECT} as justification.");
                    break;
                case WarTargetMotiveType.DeJureReclamation:
                    result = new TextObject("{=BC_WarDeclarationReason_DeJure}The declaration demands the restoration of {SUBJECT} to its rightful hierarchy.");
                    break;
                case WarTargetMotiveType.LocalLandFrontier:
                    result = new TextObject("{=BC_WarDeclarationReason_LocalLand}The declaration cites mounting tensions along the shared frontier as justification.");
                    break;
                case WarTargetMotiveType.RealmLandFrontier:
                    result = new TextObject("{=BC_WarDeclarationReason_RealmLand}The declaration invokes the security and expansion of the realm's frontier.");
                    break;
                case WarTargetMotiveType.LocalMaritimeFrontier:
                    result = new TextObject("{=BC_WarDeclarationReason_LocalMaritime}The declaration cites the need to secure nearby waters and shores.");
                    break;
                case WarTargetMotiveType.RealmMaritimeReach:
                    result = new TextObject("{=BC_WarDeclarationReason_RealmMaritime}The declaration invokes the realm's ambitions across the sea.");
                    break;
                case WarTargetMotiveType.CulturalUnification:
                    result = new TextObject("{=BC_WarDeclarationReason_Culture}The declaration cites the reclamation of ancestral lands as justification.");
                    break;
                case WarTargetMotiveType.RichFrontierPrize:
                    result = new TextObject("{=BC_WarDeclarationReason_RichFrontier}The declaration cites the wealth and strategic value of {SUBJECT}.");
                    break;
                case WarTargetMotiveType.RealmPlunderOpportunity:
                    result = new TextObject("{=BC_WarDeclarationReason_Plunder}The declaration points to the riches held within {SUBJECT}.");
                    break;
                case WarTargetMotiveType.MilitaryVulnerability:
                    result = new TextObject("{=BC_WarDeclarationReason_Vulnerable}The declaration cites the strategic opportunity presented by the enemy realm's weakness.");
                    break;
                case WarTargetMotiveType.HostileRuler:
                    result = new TextObject("{=BC_WarDeclarationReason_HostileRuler}The declaration seeks redress for the provocations of {SUBJECT}.");
                    break;
                case WarTargetMotiveType.ForeignRival:
                    result = new TextObject("{=BC_WarDeclarationReason_ForeignRival}The declaration cites a bitter rivalry with the {SUBJECT}.");
                    break;
                default:
                    return TextObject.GetEmpty();
            }

            result.SetTextVariable("SUBJECT", subject ?? TextObject.GetEmpty());
            return result;
        }

        private static WarTargetMotive SelectPrimaryMotive(IEnumerable<WarTargetMotive> motives)
        {
            List<WarTargetMotive> valid = motives?
                .Where(motive => motive != null && motive.Score > 0f)
                .ToList() ?? new List<WarTargetMotive>();
            if (valid.Count == 0)
                return null;

            WarTargetMotive liberation = valid.FirstOrDefault(motive => motive.Type == WarTargetMotiveType.Liberation);
            if (liberation != null)
                return liberation;

            return valid
                .GroupBy(motive => motive.Type)
                .Select(group => new WarTargetMotive
                {
                    Type = group.Key,
                    Score = group.Sum(motive => motive.Score),
                    Subject = group.OrderByDescending(motive => motive.Score).First().Subject
                })
                .OrderByDescending(motive => motive.Score)
                .ThenByDescending(motive => GetTiePriority(motive.Type))
                .FirstOrDefault();
        }

        private static int GetTiePriority(WarTargetMotiveType type)
        {
            switch (type)
            {
                case WarTargetMotiveType.StrongClaim: return 14;
                case WarTargetMotiveType.DeJureReclamation: return 13;
                case WarTargetMotiveType.WeakClaim: return 12;
                case WarTargetMotiveType.CulturalUnification: return 11;
                case WarTargetMotiveType.MilitaryVulnerability: return 10;
                case WarTargetMotiveType.RichFrontierPrize: return 9;
                case WarTargetMotiveType.RealmPlunderOpportunity: return 8;
                case WarTargetMotiveType.LocalLandFrontier: return 7;
                case WarTargetMotiveType.LocalMaritimeFrontier: return 6;
                case WarTargetMotiveType.RealmLandFrontier: return 5;
                case WarTargetMotiveType.RealmMaritimeReach: return 4;
                case WarTargetMotiveType.HostileRuler: return 3;
                case WarTargetMotiveType.ForeignRival: return 2;
                default: return 0;
            }
        }

        private static TextObject BuildMotiveText(WarTargetMotive motive)
        {
            switch (motive.Type)
            {
                case WarTargetMotiveType.Liberation:
                    return new TextObject("{=BC_WarVoteReason_Liberation}We must cast off the dominion of the {TARGET_REALM} and reclaim our independence.");
                case WarTargetMotiveType.StrongClaim:
                    return new TextObject("{=BC_WarVoteReason_StrongClaim}Our nobles hold a strong lawful claim to {SUBJECT}. We should press that right by force.");
                case WarTargetMotiveType.WeakClaim:
                    return new TextObject("{=BC_WarVoteReason_WeakClaim}Our nobles possess a lawful claim to {SUBJECT}. War would give us the means to enforce it.");
                case WarTargetMotiveType.DeJureReclamation:
                    return new TextObject("{=BC_WarVoteReason_DeJure}The {TARGET_REALM} occupy {SUBJECT}, which belongs by law within our hierarchy. It must be reclaimed.");
                case WarTargetMotiveType.LocalLandFrontier:
                    return new TextObject("{=BC_WarVoteReason_LocalLand}The lands of the {TARGET_REALM} press directly against our own. Their frontier is the natural place to advance our power.");
                case WarTargetMotiveType.RealmLandFrontier:
                    return new TextObject("{=BC_WarVoteReason_RealmLand}Our shared frontier with the {TARGET_REALM} makes them the natural focus of our ambitions.");
                case WarTargetMotiveType.LocalMaritimeFrontier:
                    return new TextObject("{=BC_WarVoteReason_LocalMaritime}The shores of the {TARGET_REALM} lie within easy reach of our own ports. The sea offers us a path to conquest.");
                case WarTargetMotiveType.RealmMaritimeReach:
                    return new TextObject("{=BC_WarVoteReason_RealmMaritime}Our ships can readily reach the shores of the {TARGET_REALM}, placing their lands within our grasp.");
                case WarTargetMotiveType.CulturalUnification:
                    return new TextObject("{=BC_WarVoteReason_Culture}Our ancestral lands remain under the rule of the {TARGET_REALM}. War would reunite them with our people.");
                case WarTargetMotiveType.RichFrontierPrize:
                    return new TextObject("{=BC_WarVoteReason_RichFrontier}{SUBJECT} is a prosperous prize lying close to our frontier. Its wealth would greatly enrich our realm.");
                case WarTargetMotiveType.RealmPlunderOpportunity:
                    return new TextObject("{=BC_WarVoteReason_Plunder}{SUBJECT} promises rich spoils. A successful campaign would reward the realm handsomely.");
                case WarTargetMotiveType.MilitaryVulnerability:
                    return new TextObject("{=BC_WarVoteReason_Vulnerable}The military weakness of the {TARGET_REALM} offers an opportunity that may not come again.");
                case WarTargetMotiveType.HostileRuler:
                    return new TextObject("{=BC_WarVoteReason_HostileRuler}{SUBJECT} has given us ample cause for hostility. War would answer those provocations.");
                case WarTargetMotiveType.ForeignRival:
                    return new TextObject("{=BC_WarVoteReason_ForeignRival}Our bitter rivalry with the {SUBJECT} gives us good cause to carry the struggle into their realm.");
                default:
                    return new TextObject("{=BC_WarVoteReason_Generic}The interests of the realm favor a declaration of war.");
            }
        }
    }
}
