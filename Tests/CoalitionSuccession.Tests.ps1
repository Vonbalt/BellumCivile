$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$succession = Read 'Behaviors/CivilWarCoalitionSuccession.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$election = Read 'Patches/KingSelectionAIPatch.cs'
$cancel = Read 'Patches/RebelSuccessionDiagnosticsPatch.cs'
$foreign = Read 'Behaviors/CivilWarForeignClosure.cs'
$scores = Read 'Behaviors/WarScoreBehavior.cs'
Check ((Read 'Patches/HereditaryDeathAccessionPatch.cs').Contains('CaptureCoalitionDeath(victim)')) 'Claimant death is captured before native clan succession, including the player death hook.'
Check ($succession.Contains('faction?.Type == FactionType.InstallRuler') -and $succession.Contains('UsesElection(faction.ParentKingdom)')) 'Emergency coalition elections are restricted to elective claimant wars.'
foreach ($key in @('BC_CoalitionDeaths', 'BC_CoalitionNominees', 'BC_CoalitionSuccessionNotices')) {
    Check ($resolution.Contains('dataStore.SyncData("' + $key + '"')) "Saved coalition state: $key"
}
Check ($succession.Contains('candidates.Count == 1') -and $succession.Contains('CompleteCoalitionSuccession(realm, candidates[0])')) 'A sole eligible successor is appointed without a ballot.'
Check ($succession.Contains('candidates.Count == 0') -and $succession.Contains('ResolveLiegeVictory(faction, realm)')) 'An extinct eligible coalition has a terminal fallback.'
Check ($cancel.Contains('KeepCoalitionBallot(__instance)') -and $cancel.Contains('__result = false')) 'Mandatory coalition ballots survive ordinary proposer-opinion cancellation.'
Check ($election.Contains('emergency != null || coalitionSuccession') -and $election.Contains('PinCoalitionNominees')) 'Coalition nominees compete without a dynastic reserved slot and persist across reloads.'
Check ($succession.Contains('faction.Leader = winner') -and $succession.Contains('_captureResolutionLeaderClanIds[key] = winner.StringId')) 'Elected coalition head and queued victory target are synchronized.'
Check ($resolution.Contains('if (rebelVictory && IsCoalitionSuccession(resolvedRebelKingdom)) continue;')) 'A pending election does not discard a queued capture victory.'
Check (!$succession.Contains('CompleteAccession(') -and !$succession.Contains('ResolvePledges(')) 'Coalition leadership does not run another post-election challenge cycle.'
Check ($scores.Contains('return firstParent != secondParent;')) 'Foreign tracking excludes parent-rebel and same-parent rival matchups.'
Check ($scores.Contains('Kingdom.All.Where(IsValidForeignWarParticipant)') -and $scores.Contains('if (!IsForeignWarPair(first, second)) continue;')) 'Existing external rebel wars are recovered without creating foreign trackers for civil wars.'
Check ($resolution.Contains('if (reunification) CloseReunifiedRebelForeignWars(rebelKingdom, fallbackKingdom);')) 'Reunion closure is explicit, not applied to every temporary-realm cleanup.'
Check ($foreign.Contains('ApplyCivilWarPeaceIfNeeded(rebel, enemy)') -and !$foreign.Contains('ApplyCivilWarPeaceIfNeeded(destination, enemy)')) 'Reunification never makes peace on behalf of the surviving Crown.'
Check ($foreign.Contains('successor == parent') -and $foreign.Contains('InheritExternalWars(successor, enemies)')) 'Independent or restored successor realms retain foreign hostility without exporting it on ordinary reunion.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_Coalition_Succession', 'BC_Coalition_Successor', 'BC_RebelForeignWar_Reunited')) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Localized notification exists exactly once: $id"
}
'Source contracts only; native ballot interaction, save/load recovery and foreign-war settlement require campaign testing.'
