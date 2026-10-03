$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read-Source($path) { Get-Content -Raw (Join-Path $root $path) }
function Assert-Source($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}

$ideology = Read-Source 'Behaviors/IdeologyBehavior.cs'
$feudal = Read-Source 'Behaviors/FeudalPoliticalOptionsBehavior.cs'
$constants = Read-Source 'BellumCivileConstants.cs'
$queue = Read-Source 'Behaviors/ExpulsionDeliberationBehavior.cs'
$hub = Read-Source 'Behaviors/CourtExecutiveAgendas.cs'
$menu = Read-Source 'Patches/ExpulsionDeliberationMenuPatch.cs'
$block = Read-Source 'Patches/BlockVanillaExpulsionPatch.cs'
$actions = Read-Source 'FeudalTitlePlayerActionService.cs'

Assert-Source ($ideology -notmatch 'TreasonReview|OnDailyTick_Treason|_treasonReviewDates') 'No independent NPC treason review scheduler or save key.'
Assert-Source ($ideology -notmatch 'TryBuildFiefRevocationMotion|ApplyFiefRevocationMotion|RevocCooldowns|RevocationCooldowns|RevocationClaimTarget') 'No meeting-driven public revocation scheduler, cooldowns or target scorer.'
Assert-Source ($feudal -notmatch 'GrantCooldown|AutonomousGrantEvaluation|GrantCandidate|TryExecuteGrant') 'No autonomous Crown grant scheduler, scoring or direct transfer.'
Assert-Source ($constants -notmatch 'TreasonReview|FiefAmb') 'Obsolete NPC scheduling and target-selection constants are removed.'
Assert-Source ($queue -match 'QueueExpulsionVote\(Kingdom kingdom, Clan targetClan, Clan proposerClan, CampaignTime voteDate\)' -and
    $queue -notmatch 'CampaignTime\.Days\(O\.PoliticalDeliberationDays\)') 'Expulsion execution requires a supplied date and never invents a new countdown.'
Assert-Source ($hub -match 'FileAgendaTreasonVote\(agenda.Realm, ExecutiveTargetClan\(agenda\), agenda.Sponsor, agenda.VoteDate\)' -and
    $queue -match 'ExecutiveVoteDate\(kingdom, target.StringId, proposer\)') 'The hub supplies the vote date, including queue recovery.'
$nomination = [regex]::Match($ideology, '(?s)public bool TryStartTreasonVote\(.*?(?=internal bool FileAgendaTreasonVote)').Value
Assert-Source ($nomination -match 'TryNominateTreason' -and $nomination -notmatch 'AddDecisionAsModAction|QueueExpulsionVote') 'Player nomination has no immediate-vote fallback.'
Assert-Source ($queue.Contains('AddPlayerLine("treason_indict_expel"') -and
    $queue.Contains('AddPlayerLine("treason_schedule_decree"') -and
    $menu -match 'ExecuteExpelCurrentClan' -and $menu -match 'TryStartTreasonVote') 'Player treason dialogue and kingdom-menu interfaces remain wired.'
Assert-Source ($actions -match 'public static bool TryExecuteGrant\(' -and
    $actions -match 'public static bool TryExecuteRevocation\(' -and
    $actions -match 'public static bool TryExecuteRevocationWithResponse\(') 'Player private title grant and revocation services remain available.'
Assert-Source ($block -match 'if \(expelDecision.Kingdom != playerKingdom\) return false;' -and
    $block -match 'if \(proposerClan == Clan.PlayerClan\)' -and
    $block -match 'if \(IdeologyBehavior.IsModAddingDecision\) return true;') 'NPC expulsion proposals cannot bypass the hub; player nominations and scheduled filing remain.'
$scan = [regex]::Match($hub, '(?s)private void ScanDecreeCases\(.*?(?=private void AnnounceDeferredDecree)').Value
$gate = $scan.IndexOf('if (!BellumCivileOptions.EnableAutomaticTreasonIndictments) return;')
Assert-Source ($gate -gt $scan.IndexOf('entry.Closed = true;') -and
    $gate -gt $scan.IndexOf('if (!ideology.CanScheduleTreasonDecree(realm, entry.Accused))') -and
    $gate -lt $scan.IndexOf('foreach (var target in realm.Clans') -and $gate -ge 0) 'The toggle gates new NPC decree detection after existing-case maintenance.'
Assert-Source ($scan.Contains('if (realm.RulingClan == Clan.PlayerClan) return;') -and
    $queue.Contains('TryIssuePlayerTreasonDecree(Clan.PlayerClan.Kingdom, _treasonInductTarget)') -and
    !$hub.Contains('SchedulePlayerDecree')) 'Player decrees target the addressed lord instead of scanning all vassals.'
$source = Read-Source 'CourtExecutiveObjectiveSource.cs'
$selection = [regex]::Match($source, '(?s)public IEnumerable<CourtObjectiveCandidate> FindCandidates\(.*?(?=public CourtObjectiveEvaluation EvaluateCandidate)').Value
$execution = $hub.Substring($hub.IndexOf('private bool AssignPriorityDecree'))
Assert-Source ($selection -match 'EnableAutomaticTreasonIndictments' -and
    $execution -notmatch 'EnableAutomaticTreasonIndictments' -and
    $source.Substring($source.IndexOf('public CourtObjectiveEvaluation EvaluateCandidate')) -notmatch 'EnableAutomaticTreasonIndictments' -and
    $queue -notmatch 'EnableAutomaticTreasonIndictments' -and $ideology -notmatch 'EnableAutomaticTreasonIndictments') 'Existing treason agendas, deferred decrees, filing and queue execution do not consult the toggle.'
Write-Output 'Source-contract checks only; campaign behavior and player UI interaction still require in-game testing.'
