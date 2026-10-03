$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$drift = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalDeJureDriftBehavior.cs')
$titles = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs')
$completion = $titles.Substring($titles.IndexOf('public bool TryCompleteDeJureDrift('))
$completion = $completion.Substring(0, $completion.IndexOf('public void RegisterPartitionHouseClaims('))
Check (!$drift.Contains('TryFindHighestSupersedingPackage') -and !$drift.Contains('TryGetContainingPackageDrift')) 'Ancestor eligibility no longer absorbs or masks a child record.'
Check ($drift.Contains('if (controlledTitles < requiredTitles)')) 'Higher titles still require control of their descendants.'
Check ($drift.Contains('IsPermanentKingdom(currentKingdom)')) 'Civil-war/feud pause correction remains in force.'
Check (!$completion.Contains('packageTitle.SetAssociatedKingdom(drift.TargetKingdomId)')) 'Completion cannot bulk-integrate descendants.'
Check ($completion.Contains('GetDriftOriginKingdomId(child)')) 'Inherited child origin is preserved before the parent changes realm.'
Check ($drift.Contains('BellumCivile_IndependentDriftTiming')) 'One-time migration marker is persisted.'
Check ($drift.Contains('_driftByTitleId.ContainsKey(child.TitleId)')) 'Legacy migration protects existing individual records.'
Check ($drift.Contains('existing.UpdateParents(eligibility.OriginalParent.TitleId, eligibility.TargetParent.TitleId)')) 'Changing receiving titles within the same realm preserves the clock.'
