using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    // Consumed only after treaty application returns successfully, never from the peace callback.
    internal static class CourtRallySettlement
    {
        internal static void Record(WarScoreRecord war, TreatyProposalRecord proposal, Dictionary<TreatyTermRecord,int> gold, HashSet<TreatyTermRecord> delivered)
        {
            if(war?.ConflictType != WarScoreConflictType.ForeignWar || proposal?.State != TreatyProposalState.Applied) return;
            var court=CourtAgendaBehavior.Current;
            if(court==null) return;
            bool white=proposal.Terms.Any(t=>t?.Type==TreatyTermType.WhitePeace);
            float net=0;
            bool verified=true;
            string victor=null;
            Kingdom Realm(string id) => Kingdom.All.FirstOrDefault(k=>k.StringId==id);
            foreach(var t in proposal.Terms.Where(t=>t!=null && !white))
            {
                bool fromAttacker=t.FromKingdomId==war.AttackerKingdomId;
                bool pair=(fromAttacker && t.ToKingdomId==war.DefenderKingdomId)
                    || (t.FromKingdomId==war.DefenderKingdomId && t.ToKingdomId==war.AttackerKingdomId);
                if(!pair){verified=false;continue;}
                float value=t.WarScoreCost;
                switch(t.Type)
                {
                    case TreatyTermType.ReleasePrisoner:
                    case TreatyTermType.ArrangeRoyalMarriage: continue;
                    case TreatyTermType.ConcedeDefeat:
                        if(Realm(t.FromKingdomId)?.Leader==null || Realm(t.ToKingdomId)?.Leader==null) {verified=false;break;}
                        if(victor!=null && victor!=t.ToKingdomId) {verified=false;break;}
                        victor=t.ToKingdomId; break;
                    case TreatyTermType.TransferFief:
                        if(Settlement.All.FirstOrDefault(s=>s.StringId==t.SettlementId)?.OwnerClan?.Kingdom?.StringId!=t.ToKingdomId) verified=false;
                        break;
                    case TreatyTermType.RecognizeClientOccupation:
                        var owner = Settlement.All.FirstOrDefault(s => s.StringId == t.SettlementId)?.OwnerClan;
                        if (owner?.StringId != t.ClanId || owner?.Kingdom?.StringId != t.ThirdKingdomId
                            || ClientKingdomBehavior.Instance?.IsClientOf(owner?.Kingdom, Realm(t.ToKingdomId)) != true) verified = false;
                        break;
                    case TreatyTermType.MakeClientKingdom:
                    case TreatyTermType.ForceVassalization:
                    case TreatyTermType.ReleaseVassal:
                    case TreatyTermType.ReleaseClientState:
                        if(!delivered.Contains(t)) verified=false;
                        break;
                    case TreatyTermType.HumiliateRuler:
                    case TreatyTermType.DiscreditRuler:
                        if(Realm(t.FromKingdomId)?.Leader==null || Realm(t.ToKingdomId)?.Leader==null) verified=false;
                        break;
                    case TreatyTermType.RenounceClaim:
                        var titles=Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                        var title=titles?.GetTitle(t.TitleId);
                        var claimant=Clan.All.FirstOrDefault(c=>c.StringId==t.ClanId);
                        if(title==null || claimant==null || titles.HasActiveClaim(claimant,title)) verified=false;
                        break;
                    case TreatyTermType.EndAlliance:
                        var alliances=Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                        if(alliances==null || Realm(t.ThirdKingdomId)==null || alliances.IsAllyWithKingdom(Realm(t.FromKingdomId),Realm(t.ThirdKingdomId))) verified=false;
                        break;
                    case TreatyTermType.EndTradeAgreement:
                        var trade=Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                        if(trade==null || Realm(t.ThirdKingdomId)==null || trade.HasTradeAgreement(Realm(t.FromKingdomId),Realm(t.ThirdKingdomId),out _)) verified=false;
                        break;
                    case TreatyTermType.Reparations:
                        value=t.GoldAmount>0 && gold.TryGetValue(t,out int paid)?value*Math.Min(1,paid/(float)t.GoldAmount):0;
                        break;
                    case TreatyTermType.Tribute:
                        // MakePeace installed the signed native obligation before this receipt.
                        if(t.DailyGold<=0 || t.DurationDays<=0) value=0;
                        break;
                    default:
                        // Indirect structural/prestige effects may silently fail. Do not invent delivery.
                        verified=false; break;
                }
                net+=fromAttacker?-value:value;
            }
            if(proposal.IsForced && verified && !white && victor==null) victor=proposal.WinnerKingdomId;
            court.RecordRallyOutcome(war,verified,white,victor,net,"treaty:"+proposal.ProposalId);
        }
    }
}
