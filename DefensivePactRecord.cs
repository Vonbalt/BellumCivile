using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal enum DefensivePactCallOutcome { Pending, WarConfirmed, Unavailable, DeclarationFailed }

    internal sealed class DefensivePactMemberRecord
    {
        [SaveableField(1)] private string _realmId;
        [SaveableField(2)] private int _joiningThreshold;
        internal string RealmId => _realmId;
        internal int JoiningThreshold => _joiningThreshold;

        internal DefensivePactMemberRecord(string realmId, int joiningThreshold)
        {
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            if (!DefensivePactEligibilityRules.IsJoiningThreshold(joiningThreshold))
                throw new ArgumentOutOfRangeException(nameof(joiningThreshold));
            _realmId = realmId;
            _joiningThreshold = joiningThreshold;
        }
    }

    internal sealed class DefensivePactCallRecord
    {
        [SaveableField(1)] private string _realmId;
        [SaveableField(2)] private bool _attempted;
        [SaveableField(3)] private bool _completed;
        [SaveableField(4)] private int _outcome;
        internal string RealmId => _realmId;
        internal bool Attempted => _attempted;
        internal bool Completed => _completed;
        internal DefensivePactCallOutcome Outcome => Enum.IsDefined(typeof(DefensivePactCallOutcome), _outcome)
            ? (DefensivePactCallOutcome)_outcome : DefensivePactCallOutcome.Pending;
        internal DefensivePactCallRecord(string realmId) { _realmId = realmId; }
        internal bool TryStart() { if (_attempted || _completed) return false; _attempted = true; return true; }
        internal bool TryComplete() => TryFinish(DefensivePactCallOutcome.WarConfirmed);
        internal bool TryFinish(DefensivePactCallOutcome outcome)
        {
            if (!_attempted || _completed || outcome == DefensivePactCallOutcome.Pending
                || !Enum.IsDefined(typeof(DefensivePactCallOutcome), outcome)) return false;
            _completed = true; _outcome = (int)outcome; return true;
        }
        internal bool TryAlreadyAtWar()
        {
            if (_completed) return false;
            _completed = true; _outcome = (int)DefensivePactCallOutcome.WarConfirmed; return true;
        }
        internal bool TrySkip()
        {
            if (_attempted || _completed) return false;
            _completed = true; _outcome = (int)DefensivePactCallOutcome.Unavailable; return true;
        }
    }

    internal sealed class DefensivePactRecord
    {
        internal const int ReformCooldownDays = 100;
        [SaveableField(1)] private string _targetId;
        [SaveableField(2)] private List<DefensivePactMemberRecord> _members = new List<DefensivePactMemberRecord>();
        [SaveableField(3)] private bool _activated;
        [SaveableField(4)] private string _attackedMemberId;
        [SaveableField(5)] private double _activatedDay;
        [SaveableField(6)] private List<DefensivePactCallRecord> _calls = new List<DefensivePactCallRecord>();
        [SaveableField(7)] private string _noticeRealmId;
        [SaveableField(8)] private bool _noticeAcknowledged;

        internal string TargetId => _targetId;
        internal bool Activated => _activated;
        internal string AttackedMemberId => _attackedMemberId;
        internal double ActivatedDay => _activatedDay;
        internal IReadOnlyList<DefensivePactMemberRecord> Members => _members;
        internal IReadOnlyList<DefensivePactCallRecord> Calls => _calls;
        internal double CooldownUntil => _activated ? _activatedDay + ReformCooldownDays : 0;
        internal bool AllCallsCompleted => _activated && _calls.All(call => call != null && call.Completed);
        internal string NoticeRealmId => _noticeRealmId;
        internal bool HasPendingNotice => _activated && !_noticeAcknowledged && !string.IsNullOrWhiteSpace(_noticeRealmId);
        internal bool IsNoticeReady => HasPendingNotice && (_noticeRealmId == _attackedMemberId
            || _calls.Any(call => call != null && call.RealmId == _noticeRealmId && call.Completed));
        internal bool IsCooldownActive(double now) => _activated && DefensivePactThreatRules.Finite(now)
            && now >= _activatedDay && now < CooldownUntil;

        internal DefensivePactRecord(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException(nameof(targetId));
            _targetId = targetId;
        }

        internal bool TryJoin(string realmId, int threshold)
        {
            if (_activated || string.IsNullOrWhiteSpace(realmId) || realmId == _targetId
                || _members.Any(m => m != null && m.RealmId == realmId)
                || !DefensivePactEligibilityRules.IsJoiningThreshold(threshold)) return false;
            _members.Add(new DefensivePactMemberRecord(realmId, threshold));
            return true;
        }

        internal bool TryLeave(string realmId)
        {
            if (_activated || string.IsNullOrWhiteSpace(realmId)) return false;
            var member = _members.FirstOrDefault(m => m != null && m.RealmId == realmId);
            return member != null && _members.Remove(member);
        }

        internal bool TryActivate(string attackedMemberId, double day)
        {
            if (_activated || !DefensivePactThreatRules.Finite(day) || day < 0
                || !_members.Any(m => m != null && m.RealmId == attackedMemberId)) return false;
            var calls = _members.Where(m => m != null && m.RealmId != attackedMemberId)
                .Select(m => new DefensivePactCallRecord(m.RealmId)).ToList();
            _attackedMemberId = attackedMemberId;
            _activatedDay = day;
            _calls = calls;
            _activated = true;
            return true;
        }

        internal bool QueueNotice(string realmId)
        {
            if (!_activated || _noticeAcknowledged || !string.IsNullOrWhiteSpace(_noticeRealmId)
                || string.IsNullOrWhiteSpace(realmId) || !_members.Any(m => m != null && m.RealmId == realmId)) return false;
            _noticeRealmId = realmId;
            return true;
        }

        internal bool AcknowledgeNotice()
        {
            if (!HasPendingNotice) return false;
            _noticeAcknowledged = true;
            return true;
        }
    }
}
