$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$dispatch = Read 'Behaviors/ElectiveContestDispatch.cs'
$startup = Read 'Behaviors/ElectiveContestStartup.cs'
Check ($dispatch.IndexOf('CaptureCivilWarStartInfluence') -lt $dispatch.IndexOf('StartFrozenSuccessionRebellion')) 'Both coalition snapshots precede the first shell mutation.'
Check ($dispatch.Contains('if (candidate.WarStarted) continue;') -and $dispatch.Contains('candidate.WarStarted = true;')) 'Completed shell startup has a saved receipt and is skipped on retry.'
Check ($dispatch.IndexOf('!StartRivalry(record,') -gt $dispatch.IndexOf('candidate.WarStarted = true;')) 'Rival hostility is created only after both Crown wars are established.'
Check ($startup.IndexOf('conflict.ObserveRivalry') -lt $startup.IndexOf('FactionManager.DeclareWar')) 'Rival-pair identity is recorded before native hostility.'
Check ($startup.Contains('pair != record.Rivalry || pair.Score?.IsActive != true')) 'Dispatch verifies the exact active rivalry tracker before completion.'
Check ((Read 'Behaviors/CivilWarCrownTransfer.cs').Contains('IsDispatchRealmPending') -and (Read 'Behaviors/CivilWarCrownTransfer.cs').Contains('IsDispatchScorePending')) 'Shared cleanup and war-score paths respect pending election startup.'
Check ($startup.Contains('record.StartupAborted = true;') -and $startup.Contains('resolution.ResolveWhitePeace(faction, shell)') -and $startup.Contains('candidate.WarStarted && faction.Leader?.IsEliminated == false')) 'Interrupted startup releases its guard and cleans unfinished shells while retaining viable established wars.'
Check ((Read 'Behaviors/FactionManagerBehavior.cs').Contains('faction.HasTrackedRebelKingdom || faction.IsChallengeStartupPending')) 'Generic faction death cleanup does not remove an active or pending rebellion.'
Check ($dispatch.Contains('!WarPeaceRevampBehavior.IsRevampEnabled()') -and $startup -notmatch 'MBRandom|AnswerUltimata|CapturePledges') 'Three-way startup requires its war engine and never rerolls decisions.'
'Native shell callbacks, candidate death and save/load still require in-game testing.'
