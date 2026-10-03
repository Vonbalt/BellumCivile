namespace BellumCivile
{
    internal static class ConflictOutcomeText
    {
        internal static string[] For(string kind)
        {
            switch (kind)
            {
                case "liberation_preparing": return new[] {
                    "{=BC_LiberationPreparingTitle}The Cause of Self-Rule",
                    "{=BC_LiberationPreparingChat}{RULER} has called upon the houses of {REALM} to prepare to cast off the dominion of {SUZERAIN}.",
                    "{=BC_LiberationPreparingBody}Until {DATE}, the Crown will rally support for liberation. The banners have not yet been raised: the realm must still judge its strength and consent to war." };
                case "liberation_begun": return new[] {
                    "{=BC_LiberationBegunTitle}Banners of Liberation",
                    "{=BC_LiberationBegunChat}{REALM} has taken up arms against {SUZERAIN}. The Crown's preparations have given way to open war.",
                    "{=BC_LiberationBegunBody}The campaign for self-rule has begun. Independence must now be secured on the battlefield and at the negotiating table." };
                case "liberation_released": return new[] {
                    "{=BC_LiberationReleasedTitle}The Bond Lifted",
                    "{=BC_LiberationReleasedChat}{REALM} is no longer bound in clientage to {SUZERAIN}; the Crown's preparations are no longer needed.",
                    "{=BC_LiberationReleasedBody}The former bond has ended without these preparations bringing the realm to war." };
                case "liberation_expired": return new[] {
                    "{=BC_LiberationExpiredTitle}The Banners Remain Furled",
                    "{=BC_LiberationExpiredChat}The court term has closed in {REALM} without a declaration of liberation against {SUZERAIN}.",
                    "{=BC_LiberationExpiredBody}The Crown's preparations lapse. For now, the realm remains bound by its existing obligations." };
                case "liberation_cancelled": return new[] {
                    "{=BC_LiberationCancelledTitle}Preparations Set Aside",
                    "{=BC_LiberationCancelledChat}Changed circumstances have brought {REALM}'s preparations against {SUZERAIN} to an end.",
                    "{=BC_LiberationCancelledBody}The Crown's former plan no longer governs the realm's course. No declaration of liberation is credited to these preparations." };
                case "royal_peace_warning": return new[] {
                    "{=BC_RoyalPeaceWarningTitle}The Crown Demands Unity",
                    "{=BC_RoyalPeaceWarningChat}With {ENEMY} threatening {REALM}, {RULER} has commanded {CLAIMANT_HOUSE} and {HOLDER_HOUSE} to prepare to lay down their private banners.",
                    "{=BC_RoyalPeaceWarningBody}On {DATE}, the Crown will impose peace upon their feud over {TITLE}. Neither house will be awarded the disputed claim. Both must return to the realm and turn their strength against the foreign enemy." };
                case "royal_peace_cancelled": return new[] {
                    "{=BC_RoyalPeaceCancelledTitle}The Decree Withdrawn",
                    "{=BC_RoyalPeaceCancelledChat}The announced decree concerning the feud over {TITLE} in {REALM} will not proceed.",
                    "{=BC_RoyalPeaceCancelledBody}{REASON}" };
                case "election_called": return new[] {
                    "{=BC_Deposition_ElectionCalled}Election Called",
                    "{=BC_Deposition_Deliberation}{DEPOSED} has been cast down. {CARETAKER} will govern {REALM} until its nobles choose a sovereign on {DATE}. The houses now weigh their loyalties and seek support for the coming election.",
                    "{=BC_Deposition_ElectionCalledBody}The Crown now awaits the verdict of the realm's nobles." };
                case "victory": return new[] {
                    "{=BC_Result_VictoryTitle}The Rebels Prevail",
                    "{=BC_Result_VictoryChat}{FACTION} has prevailed over the Crown's forces in {REALM}.",
                    "{=BC_Result_VictoryBody}The fighting between these houses has ended." };
                case "claimant": return new[] {
                    "{=BC_Result_CrownTitle}A Crown Claimed",
                    "{=BC_Result_CrownChat}{LEADER} has defeated {RULER}'s loyalists and taken the throne of {REALM}.",
                    "{=BC_Result_CrownBody}The victorious houses have secured their claimant's accession. {LEADER} now rules in {RULER}'s place." };
                case "loyalist": return new[] {
                    "{=BC_Result_LoyalistTitle}The Crown Prevails",
                    "{=BC_Result_LoyalistChat}The Crown has defeated {LEADER}'s rebellion in {REALM}.",
                    "{=BC_Result_LoyalistBody}The rebels have failed to enforce their demands. {RULER} retains the throne." };
                case "abdication": return new[] {
                    "{=BC_Result_AbdicationTitle}A Ruler Deposed",
                    "{=BC_Result_AbdicationChat}The victorious rebels have forced {RULER} to relinquish the throne of {REALM}.",
                    "{=BC_Result_AbdicationBody}{SUCCESSION}" };
                case "independence": return new[] {
                    "{=BC_Result_IndependenceTitle}Independence Secured",
                    "{=BC_Result_IndependenceChat}{NEW_REALM} has secured its independence from {REALM}.",
                    "{=BC_Result_IndependenceBody}{LEADER} and the houses joining the new realm are no longer bound to the former Crown." };
                case "peace": return new[] {
                    "{=BC_Result_PeaceTitle}An Uneasy Peace",
                    "{=BC_Result_PeaceChat}The Crown and {FACTION} have laid down their arms without enforcing the rebels' demands.",
                    "{=BC_Result_PeaceBody}The rebel houses return to {REALM}. The fighting has ended, but the dispute has not been settled in their favor." };
                case "satisfied": return new[] {
                    "{=BC_Result_SatisfiedTitle}The Demand Fulfilled",
                    "{=BC_Result_SatisfiedChat}With {DEMAND_RESULT}, {FACTION} has ended its rebellion.",
                    "{=BC_Result_SatisfiedBody}Their declared purpose has been fulfilled, and their houses have returned to {REALM}." };
                case "collapse": return new[] {
                    "{=BC_Result_CollapseTitle}The Realm Endures",
                    "{=BC_Result_CollapseChat}With the old government shattered, {LEADER} has assumed the crown of {REALM}.",
                    "{=BC_Result_CollapseBody}The surviving houses now gather under the restored realm's banner." };
                case "rival": return new[] {
                    "{=BC_Result_RivalTitle}A Rival Overcome",
                    "{=BC_Result_RivalChat}{VICTOR} has defeated rival claimant {LEADER}. The struggle against {RULER} continues.",
                    "{=BC_Result_RivalBody}The defeated coalition's surviving houses have been brought under {VICTOR}'s authority. This victory has not settled the contest for the crown." };
                case "feud_claimant": return new[] {
                    "{=BC_Result_FeudClaimTitle}A Claim Enforced",
                    "{=BC_Result_FeudClaimChat}{CLAIMANT_HOUSE} has defeated {HOLDER_HOUSE} in the feud over {TITLE}.",
                    "{=BC_Result_FeudClaimBody}{TITLE} has been awarded to {NEW_HOLDER}. The claimant's demand has been enforced." };
                case "feud_holder": return new[] {
                    "{=BC_Result_FeudHolderTitle}A Claim Repelled",
                    "{=BC_Result_FeudHolderChat}{HOLDER_HOUSE} has defeated the challenge to its possession of {TITLE}.",
                    "{=BC_Result_FeudHolderBody}{HOLDER} retains {TITLE}." };
                case "feud_default": return new[] {
                    "{=BC_Result_FeudDefaultTitle}A Challenge Abandoned",
                    "{=BC_Result_FeudDefaultChat}Unable to press its challenge, {CLAIMANT_HOUSE} has conceded the feud over {TITLE}.",
                    "{=BC_Result_FeudDefaultBody}{HOLDER_HOUSE} retains the title. The claimant's failure has ended this contest." };
                case "feud_peace": return new[] {
                    "{=BC_Result_FeudPeaceTitle}The Feud Suspended",
                    "{=BC_Result_FeudPeaceChat}{CLAIMANT_HOUSE} and {HOLDER_HOUSE} have ended their fighting over {TITLE} without settling the claim.",
                    "{=BC_Result_FeudPeaceBody}No title has been awarded by this settlement. The feud has fallen quiet, though the competing claims remain." };
                case "feud_royal_peace": return new[] {
                    "{=BC_Result_RoyalPeaceTitle}The Crown Imposes Peace",
                    "{=BC_Result_RoyalPeaceChat}With foreign enemies threatening {REALM}, {RULER} has commanded {CLAIMANT_HOUSE} and {HOLDER_HOUSE} to end their private war over {TITLE}.",
                    "{=BC_Result_RoyalPeaceBody}Neither house has prevailed. Their pre-feud holdings are restored and the disputed claim remains unresolved. The Crown now calls upon both houses to turn their arms against the realm's foreign enemies." };
                default: return new[] {
                    "{=BC_Result_EndedTitle}The Contest Ends",
                    "{=BC_Result_EndedChat}The fighting in {REALM} has ended.",
                    "{=BC_Result_EndedBody}{CAUSE}" };
            }
        }
    }
}
