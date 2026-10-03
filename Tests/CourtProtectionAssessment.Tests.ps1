$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($file) { Get-Content -LiteralPath (Join-Path $root $file) -Raw }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$assessment = Read 'CourtProtectionAssessment.cs'
$commands = Read 'CheatCommands.cs'
$command = $commands.Substring($commands.IndexOf('public static string ProtectionPreview('))
$command = $command.Substring(0, $command.IndexOf('[CommandLineFunctionality.'))
Check ($command.Contains('CourtProtectionAssessmentService.DebugReport') -and !$command.Contains('TryEstablishClientKingdom')) 'Console inspection invokes only the read-only assessment.'
Check (!$assessment.Contains('DeclareWarAction') -and !$assessment.Contains('MakePeaceAction') -and !$assessment.Contains('TryEstablishClientKingdom') -and !$assessment.Contains('CanDeclareWar(')) 'Assessment cannot establish clientage, change war stances or invoke mutating liberation checks.'
Check ($assessment.Contains('PeekWarWill') -and !$assessment.Contains('.GetWarWill(')) 'Preview does not initialize saved war enthusiasm.'
Check ($assessment.Contains('new HashSet<Kingdom>') -and $assessment.Contains('realms.Distinct()') -and $assessment.Contains('otherEnemies.ExceptWith(hostile)')) 'Client blocs are deduplicated and the named enemy is excluded from other-front burden.'
Check ($assessment.Contains('TryReadDiplomacyNonAggressionPact') -and $assessment.Contains('TryReadDiplomacyWarCooldown')) 'Both loaded Diplomacy obligations are checked read-only.'
Check ($assessment.Contains('IsWarDecisionAllowedBetweenKingdoms') -and $assessment.Contains('IsPeaceDecisionAllowedBetweenKingdoms')) 'Preview checks new declarations and required client-only peace alignment.'
Check ($assessment.Contains('GetClientKingdomCandidate') -and $assessment.Contains('territory.WarScoreCost')) 'Territorial eligibility reuses real clientage valuation.'
Check ($assessment.Contains('ThenBy(r => r.Protector.StringId, StringComparer.Ordinal)')) 'Protector ranking has a stable realm-ID tie break.'
Check ((Read 'Behaviors/CourtAgendaBehavior.cs').Contains('new CourtProtectionObjectiveSource')) 'Protection motion is registered in the shared agenda selector.'
'Protection assessment contracts passed; live campaign and installed-mod checks remain pending.'
