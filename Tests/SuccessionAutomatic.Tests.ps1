$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$owner = Read 'Behaviors/SuccessionChallengeBehavior.cs'
$scheduler = Read 'Behaviors/SuccessionChallengeInitiation.cs'
$responses = Read 'Behaviors/SuccessionChallengeTesting.cs'
Check ($owner.Contains('store.SyncData("BC_SuccessionChallengeChecks", ref _lastInitiationChecks)') -and
    $scheduler.IndexOf('_lastInitiationChecks[realm.StringId] = day') -lt $scheduler.IndexOf('loyalty.Maintain(realm)')) 'Scheduled check receipts persist before assessment.'
Check ($scheduler.Contains('loyalty.GetLine(realm)') -and $scheduler.Contains('break;') -and
    $scheduler.Contains('TryBegin(realm, heir, MBRandom.RandomFloat)')) 'Natural initiation follows lawful priority and stops after one appeal.'
Check ($scheduler.Contains('ResolvePermanentRealm(k) == k') -and $scheduler.Contains('r.IsOpen || r.RealmBlockedUntil > Day') -and
    $scheduler.Contains('f.IsCivilWarActive()')) 'Temporary realms, open challenges, realm pauses and active civil wars block scheduled initiation.'
Check ($owner.Contains('manager.IsClanPacified(challenger.Clan)') -and $owner.Contains('manager.GetRebelFaction(challenger.Clan) != null') -and
    $owner.Contains('estate == null || !CrownEstateAvailable(estate)')) 'Manual and natural entry share faction, pacification and dependent-household safety gates.'
Check ($responses.Contains('AdvanceRulerResponses') -and $responses.Contains('ShowRulerResponse') -and
    !$responses.Contains('r.ManualTest &&') -and $responses.Contains('RecordNpcResponse(record, record.ResponseRoll)')) 'Manual and natural records use identical ruler responses and saved NPC rolls.'
Check ($owner.Contains('AnnounceConcession(record)') -and $responses.Contains('if (record.ConcessionAnnounced) return;')) 'Successful land settlement has a once-only notification.'
