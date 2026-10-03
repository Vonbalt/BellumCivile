$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$dispatch = Read 'Behaviors/ElectiveContestDispatch.cs'
$rules = Read 'ElectiveContestUltimatumRules.cs'
Check ($dispatch.Contains('var warChallengers = ElectiveContestUltimatumRules.WarChallengers(record);')) 'Dispatch distinguishes issued demands from unsatisfied war claimants.'
Check ($dispatch.IndexOf('CaptureCivilWarStartInfluence(record.Pledges.Select') -lt $dispatch.IndexOf('!SettleElectiveSurrender(record)')) 'Remaining coalition and holding snapshots are saved before the Crown changes.'
Check ($dispatch.IndexOf('!SettleElectiveSurrender(record)') -lt $dispatch.IndexOf('StartFrozenSuccessionRebellion(out string failure)')) 'Remaining claimant starts against the already-established new Crown.'
Check ($dispatch.Contains('if (warChallengers.Count == 0)') -and $rules.Contains('c.Candidate != record.SurrenderTo')) 'Only a surrender satisfying every demand closes the contest peacefully.'
$settlement = $dispatch.Substring($dispatch.IndexOf('private static bool SettleElectiveSurrender'))
Check (!$settlement.Contains('record.Closed = true') -and !$settlement.Contains('record.WarDispatchCompleted = true')) 'Crown-transfer helper cannot silently terminate another claimant.'
Check ($dispatch.Contains('ElectiveContestUltimatumRules.DispatchOpposition') -and (Read 'ElectiveContestPledgeRules.cs').Contains('ElectiveContestUltimatumRules.Opposition')) 'Pledge and dispatch validation share the half-strength rival deterrent.'
Check ($dispatch.Contains('if (!record.WarDispatchStarted)') -and $settlement.Contains('if (!record.MandateStarted)')) 'Resumption retains initial validation and full-mandate receipts.'
'Source contracts only; native Crown transfer and remaining rebel-shell startup require in-game testing.'
