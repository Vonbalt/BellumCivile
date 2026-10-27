$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$faction = Get-Content -Raw -LiteralPath (Join-Path $root 'FactionObject.cs')
$settlement = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/CivilWarResolutionBehavior.cs')
$accepted = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/AcceptedIndependence.cs')
$membership = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/FactionManagerBehavior.cs')
[xml]$strings = Get-Content -Raw -LiteralPath (Join-Path $root 'ModuleData/Languages/EN/strings.xml')
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
function MethodBody($source, $signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    for ($i = $open + 1; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        if ($source[$i] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $source.Substring($open, $i - $open + 1) }
    }
    throw "Unterminated method: $signature"
}
$accept = MethodBody $faction 'private UltimatumResolution AcceptDemands('
Check ($accept.Contains('TryAcceptIndependence(this, out List<Kingdom> independentKingdoms)') -and
    !$accept.Contains('ApplyJoinToKingdomWithBannerPolicy(Leader,') -and
    $accept.Contains('Leader = independenceLeader;')) 'Accepted independence uses a dedicated settlement and preserves the original sponsor for completion.'
$planner = MethodBody $settlement 'BuildIndependencePartitionGroups(FactionObject faction,'
Check ($planner.Contains('BuildIndependencePartitionGroupsForClans(faction?.ParentKingdom, rebelKingdom?.Clans, victoriousLeader)') -and
    $accepted.Contains('BuildIndependencePartitionGroupsForClans(parent, participants, sponsor)')) 'Peaceful acceptance and wartime victory share the explicit-roster partition planner.'
Check ($accepted.IndexOf('var entries = groups.Select') -lt $accepted.IndexOf('KingdomCreationSafetyHelper.CreateKingdom(') -and
    $accepted.Contains('CreateForTitle(g.Leader, parent, g.SourceTitle)')) 'Title profiles and destinations are captured before creation and transfer callbacks.'
Check ($accepted.IndexOf('RegisterIndependentRealmSourceTitle(') -lt $accepted.IndexOf('MoveAcceptedIndependenceClan(founder, parent, entry)')) 'Founding titles are registered before allegiance-change callbacks can create substitute crowns.'
Check ($accepted.Contains('NobleClanEligibilityHelper.IsValidRulingClan(entry.Realm.RulingClan, entry.Realm)') -and
    $accepted.Contains('bool moved = clan.Kingdom == entry.Realm;')) 'Completion checks actual clan membership and a resident living ruler.'
foreach ($forbidden in @('PromoteRebelKingdomToIndependentKingdom(', 'DrainAndDestroyRebelKingdom(',
    'ApplyCivilWarPeaceIfNeeded(', 'ApplyIndependenceMemories(', 'ApplyCivilWarVictoryInfluenceRewards(', 'AddCivilWarGraceRealms(')) {
    Check (!$accepted.Contains($forbidden)) "Peaceful acceptance does not invoke war settlement effects: $forbidden"
}
Check ($accepted.Contains('DestroyKingdomAction.Apply(entry.Realm)') -and
    !$accepted.Contains('DestroyKingdomAction.Apply(parent)')) 'Only newly prepared empty destinations can be retired, never the parent realm.'
$startup = MethodBody $faction 'private bool StartOpenRebellion('
Check ($startup.IndexOf('SetRebelKingdom(rebelKingdom);') -lt $startup.IndexOf('MoveClanToKingdomPreservingCivilWarInfluence(Leader,') -and
    $membership.Contains('if (movingToRebel || returningFromRebel)')) 'Refused ultimatums retain their pre-transfer rebel identity and membership exemption.'
foreach ($signature in @('private void ResolveRecognizedIndependence(', 'internal void ResolveWhitePeace(')) {
    $body = MethodBody $settlement $signature
    Check ($body.IndexOf('RemoveFaction(faction);') -ge 0 -and
        ($body.IndexOf('RemoveFaction(faction);') -lt $body.IndexOf('PromoteRebelKingdomToIndependentKingdom(') -or
         $body.IndexOf('RemoveFaction(faction);') -lt $body.IndexOf('TransferCivilWarClan('))) "Existing terminal faction is removed before member transfers: $signature"
}
$entry = $strings.SelectNodes('//string[@id="BC_Msg_AvoidedWar_IndependenceRealms"]')
Check ($entry.Count -eq 1 -and $entry[0].text.Contains('{REALM_NAMES}')) 'Multi-realm accepted-independence notice is localized once.'
'Accepted independence source contracts passed; run the compiled callback regressions and verify an in-game ultimatum.'
