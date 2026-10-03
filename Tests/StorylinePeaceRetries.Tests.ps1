$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($ok, $text) { if (!$ok) { throw $text }; "PASS: $text" }
$will = Read 'Behaviors/WarPeaceRevampBehavior.cs'
$score = Read 'Behaviors/WarScoreBehavior.cs'
$treaties = Read 'Behaviors/ForeignTreatyBehavior.cs'
$entry = Read 'Patches/PeaceParleyEntryPointPatches.cs'
Check ($will.Contains('GetConflictLoad(clan.Kingdom, enthusiasmOnly: true)') -and
    $will.Contains('if (enthusiasmOnly && StorylineWarProtectionHelper.IsPeaceBlocked(kingdom, opponent)) continue;')) 'Passive strain excludes exactly the protected storyline pairs.'
Check ($will.Contains('if (!liberationProposal && conflictLoad.ForeignWarCount >= 2)') -and
    $will.Contains('WarWillConflictLoad conflictLoad = GetConflictLoad(clan.Kingdom);')) 'Offensive readiness retains all real fronts.'
Check ($score.Contains('.Where(war => war.CanReconsiderPeace(CurrentDay))')) 'All clan parley candidates share the war retry window.'
Check ($treaties.Contains('if (!forced && war != null && !war.CanReconsiderPeace')) 'Automatic queue checks cooldown without blocking forced settlements.'
Check ($treaties.Contains('if (!proposal.IsForced) war?.RecordPeaceRejection')) 'Rejected voluntary treaties start the saved window.'
Check ($treaties.Contains('if (change < 0 && !treatyConcluded)') -and $treaties.Contains('TryRecordRefusalReaction(ruler.StringId, voter.Clan.Leader.StringId, proposal.Terms)')) 'Duplicate rejection penalties are checked per ruler, voter and terms.'
Check (([regex]::Matches($entry, 'RetireDuringRetryWindow\(peaceDecision, war\)')).Count -eq 3) 'Notification creation, inspection and post-add cleanup retire premature NPC proposals.'
Check ($entry.Contains('decision.ProposerClan == Clan.PlayerClan || war.ParleyPending')) 'Player initiatives and existing parleys are not retired.'
'Storyline/retry source contracts passed; live campaign testing remains.'
