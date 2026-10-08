using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class FactionManagerBehavior
    {
        private Clan _playerCourtReturnClan;
        private Kingdom _playerCourtReturnRealm;
        private Kingdom _playerCourtConflictRealm;
        private int _playerCourtReturnType = -1;

        private void SyncPlayerCourtAffiliation(IDataStore store)
        {
            store.SyncData("BellumCivile_PlayerCourtReturnClan", ref _playerCourtReturnClan);
            store.SyncData("BellumCivile_PlayerCourtReturnRealm", ref _playerCourtReturnRealm);
            store.SyncData("BellumCivile_PlayerCourtConflictRealm", ref _playerCourtConflictRealm);
            store.SyncData("BellumCivile_PlayerCourtReturnType", ref _playerCourtReturnType);
        }

        internal void ForgetPlayerCourtAffiliation()
        {
            _playerCourtReturnClan = null;
            _playerCourtReturnRealm = null;
            _playerCourtConflictRealm = null;
            _playerCourtReturnType = -1;
        }

        private bool IsTemporaryCourtRealm(Kingdom realm) => realm != null
            && (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm) || GetFactionByRebelKingdom(realm) != null);

        private void TrackPlayerCourtTransfer(Clan clan, Kingdom oldRealm, Kingdom newRealm)
        {
            if (clan == null || clan != Clan.PlayerClan) return;
            if (_playerCourtReturnClan != null && _playerCourtReturnClan != clan)
                ForgetPlayerCourtAffiliation();

            // Read the old roster before the kingdom-change handler detaches the player's house.
            var affiliation = _activeFactions.FirstOrDefault(f => f.IsIdeology && f.Members.Contains(clan));
            if (_playerCourtReturnClan == null && affiliation != null
                && ((affiliation.ParentKingdom == oldRealm && IsTemporaryCourtRealm(newRealm))
                    || IsTemporaryCourtRealm(oldRealm)))
            {
                _playerCourtReturnClan = clan;
                _playerCourtReturnRealm = affiliation.ParentKingdom;
                _playerCourtReturnType = (int)affiliation.Type;
                _playerCourtConflictRealm = IsTemporaryCourtRealm(newRealm) ? newRealm : oldRealm;
            }

            if (_playerCourtReturnClan == null) return;
            if (IsTemporaryCourtRealm(newRealm)
                && (oldRealm == _playerCourtReturnRealm || oldRealm == _playerCourtConflictRealm))
            {
                _playerCourtConflictRealm = newRealm;
                return;
            }
            if (oldRealm != _playerCourtConflictRealm || !IsPlayerCourtReturnRealm(newRealm))
                ForgetPlayerCourtAffiliation();
        }

        private bool IsPlayerCourtReturnRealm(Kingdom realm)
        {
            if (realm == null || _playerCourtReturnRealm == null) return false;
            if (realm == _playerCourtReturnRealm) return true;
            // Bellum creates permanent successor shells with these IDs when a civil war resolves.
            string origin = _playerCourtReturnRealm.StringId;
            string destination = realm.StringId;
            return !string.IsNullOrEmpty(origin) && !string.IsNullOrEmpty(destination)
                && (destination == origin + "_restored"
                    || destination.StartsWith(origin + "_restored_", StringComparison.Ordinal)
                    || destination.StartsWith(origin + "_indep_", StringComparison.Ordinal));
        }

        internal void RestorePlayerCourtAffiliation()
        {
            Clan player = Clan.PlayerClan;
            Kingdom realm = player?.Kingdom;
            if (player == null || player.IsEliminated || player.IsUnderMercenaryService
                || (_playerCourtReturnClan != null && _playerCourtReturnClan != player))
            {
                ForgetPlayerCourtAffiliation();
                return;
            }
            if (IsTemporaryCourtRealm(realm))
            {
                // Older civil-war saves can still contain the real affiliation; never guess a lost one.
                if (_playerCourtReturnClan == null)
                {
                    var retained = _activeFactions.FirstOrDefault(f => f.IsIdeology && f.Members.Contains(player));
                    if (retained != null) TrackPlayerCourtTransfer(player, retained.ParentKingdom, realm);
                }
                return;
            }
            if (_playerCourtReturnClan == null) return;
            var type = (FactionType)_playerCourtReturnType;
            if (!IsPlayerCourtReturnRealm(realm)
                || (type != FactionType.Glory && type != FactionType.Nobility && type != FactionType.Liberty)
                || !CourtMembershipEligibility.CanBelong(player, realm))
            {
                ForgetPlayerCourtAffiliation();
                return;
            }
            if (!IdeologyBehavior.CanRunCourtPolitics(realm)) return;

            var existing = _activeFactions.FirstOrDefault(f => f.IsIdeology && f.Members.Contains(player));
            if (existing != null)
            {
                // An intact affiliation or a newer player choice always wins over the saved preference.
                ForgetPlayerCourtAffiliation();
                return;
            }
            var target = _activeFactions.FirstOrDefault(f => f.IsIdeology && f.ParentKingdom == realm && f.Type == type);
            if (target == null)
            {
                target = new FactionObject(FactionObject.GetIdeologyDisplayName(type, realm).ToString(), realm, player, type);
                RegisterNewFaction(target);
            }
            else target.AddMember(player);
            if (!target.Members.Contains(player)) return;
            InvalidateFactionLookupCache();
            BellumCivileLogger.Log($"Restored player court affiliation after internal conflict; clan={player.StringId}; realm={realm.StringId}; faction={type}.");
            ForgetPlayerCourtAffiliation();
        }
    }
}
