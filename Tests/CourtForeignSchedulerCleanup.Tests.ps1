$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$flow = Read 'Behaviors/ForeignPolicyBehavior.cs'
foreach ($retired in @('RulerProposalCandidate', 'TryRunRulerForeignPolicyProposalCore', 'EnsureRulerForeignPolicySchedules', 'RunDueRulerForeignPolicyChecks', '_nextRulerForeignPolicyCheckDayByKingdomId', 'BellumCivile_NextRulerForeignPolicyCheckDayByKingdom')) {
    Check (!$flow.Contains($retired)) "Retired scheduler component absent: $retired"
}
Check (!(Read 'BellumCivileConstants.cs').Contains('ForeignPolicyRuler')) 'Scheduler-only tuning constants removed.'
Check ($flow.Contains('CampaignEvents.WarDeclared.AddNonSerializedListener') -and $flow.Contains('CampaignEvents.MakePeace.AddNonSerializedListener') -and $flow.Contains('ReconcileActiveWars();')) 'War, peace and load reconciliation hooks retained.'
Check ($flow.Contains('BellumCivile_ActiveForeignWars') -and $flow.Contains('BellumCivile_PendingForeignWarContexts') -and $flow.Contains('PendingContextLifetimeDays')) 'Active/pending war persistence and transient context expiry retained.'
Check ($flow.Contains('CapturePendingWarDeclarationMotive') -and $flow.Contains('GetWarDeclarationContext') -and $flow.Contains('OnKingdomDecisionCancelled')) 'Declaration context capture and cancellation retained.'
Check (!$flow.Contains('GrantPlayerForeignPolicyMandate') -and !$flow.Contains('BellumCivile_PlayerForeignPolicyMandateUntil')) 'Legacy short mandates retired with their meeting writer.'
Check ((Read 'CheatCommands.cs').Contains('behavior.TryRunRulerForeignPolicyProposal(kingdom, force, out string report)')) 'Existing console entry retains harmless compatibility diagnostic.'
