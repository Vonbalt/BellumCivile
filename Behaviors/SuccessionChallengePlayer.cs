using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        internal bool IsPlayerChallengeTitle(FeudalTitleRecord title)
        {
            var realm = Clan.PlayerClan?.Kingdom;
            return realm != null && title?.IsActive == true
                && Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetRealmSovereignTitle(realm)?.TitleId == title.TitleId;
        }

        internal TextObject PlayerChallengeBlock(FeudalTitleRecord title)
        {
            var realm = Clan.PlayerClan?.Kingdom;
            if (!IsPlayerChallengeTitle(title) || !CrownAccessionBehavior.IsHereditaryRealm(realm)
                || realm.IsEliminated || SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) != realm)
                return new TextObject("{=BC_PlayerChallenge_Realm}Select the sovereign title of your hereditary realm.");
            if (Hero.MainHero == null || Clan.PlayerClan.Leader != Hero.MainHero || Clan.PlayerClan == realm.RulingClan)
                return new TextObject("{=BC_PlayerChallenge_House}You must lead your own vassal house, not the ruling house.");
            if (!Available(Hero.MainHero))
                return new TextObject("{=BC_PlayerChallenge_Available}You must be an adult, free and available to issue a challenge.");
            if (HereditaryLoyaltyBehavior.Instance?.GetLine(realm).Contains(Hero.MainHero) != true)
                return new TextObject("{=BC_PlayerChallenge_Line}You must be a lawful heir in this realm's line of succession. A claim alone is not enough.");
            if (CrownAccessionBehavior.Instance?.IsPending(realm) == true || _records.Any(r =>
                (r.WarRealm == realm || r.OutcomeRealm == realm) && (r.IsOpen || r.RealmBlockedUntil > Day
                    || r.Challenger == Hero.MainHero && r.PersonalBlockedUntil > Day)))
                return new TextObject("{=BC_PlayerChallenge_Pause}An accession, another challenge or a challenge cooldown prevents this action.");
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null || manager.IsClanPacified(Clan.PlayerClan) || manager.GetRebelFaction(Clan.PlayerClan) != null
                || manager.GetFactionsInKingdom(realm).Any(f => f.IsCivilWarActive()))
                return new TextObject("{=BC_PlayerChallenge_Conflict}Your house is bound by a settlement, belongs to a rebel faction, or the realm is already in civil war.");
            return null;
        }

        internal bool StartPlayerChallenge(FeudalTitleRecord title)
        {
            ReconcileSovereigns();
            var reason = PlayerChallengeBlock(title);
            if (reason != null) { InformationManager.DisplayMessage(new InformationMessage(reason.ToString())); return false; }
            var record = TryBegin(Clan.PlayerClan.Kingdom, Hero.MainHero, 0, playerInitiated: true);
            if (record == null) return false;
            ResolveAppeal(record);
            BellumCivileLogger.Log($"Player succession challenge {record.Id}; realm={record.Realm.StringId}; phase={record.Phase}.");
            return true;
        }

        private void AnswerPlayerUltimatum(SuccessionChallengeRecord record, bool send)
        {
            _inquiry = null;
            ReconcileSovereigns();
            if (record.Phase != SuccessionChallengePhase.AwaitingResponse || record.Challenger != Hero.MainHero) return;
            if (send)
            {
                if (!ValidParticipants(record) || !RefreshResponseBacking(record)) return;
            }
            else WithdrawForInsufficientBacking(record);
            record.ReportPending = false;
        }
    }
}
