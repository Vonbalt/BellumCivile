$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($file) { Get-Content -Raw -LiteralPath (Join-Path $root $file) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$behavior = Read 'Behaviors/ClientKingdomBehavior.cs'
$patch = Read 'Patches/ClientKingdomDiplomacyPatches.cs'
$start = $behavior.IndexOf('public bool CanDeclareWar(')
$end = $behavior.IndexOf('internal bool ConfirmLiberationWar(')
$eligibility = $behavior.Substring($start,$end-$start)
Check (!$eligibility.Contains('EndClientStatus(') -and !$eligibility.Contains('_clients.Remove')) 'Eligibility cannot remove clientage.'
$commit = $behavior.Substring($end,$behavior.IndexOf('public bool CanMakePeace(')-$end)
Check ($commit.Contains('!client.IsAtWarWith(suzerain)')) 'Commit requires actual hostility.'
Check ($commit.IndexOf('EndProtectedAgreements(') -lt $commit.IndexOf('_clients.Remove(record)')) 'Cleanup failure retains reconciliation record.'
Check ($patch.Contains('typeof(FactionManager), nameof(FactionManager.DeclareWar)') -and $patch.Contains('[HarmonyFinalizer]') -and $patch.Contains('return __exception;')) 'Post-stance commit preserves the original native exception.'
$eventStart = $behavior.IndexOf('private void OnWarDeclared(')
$event = $behavior.Substring($eventStart,$behavior.IndexOf('private void OnMakePeace(')-$eventStart)
Check ($event.Contains('!first.IsAtWarWith(second)')) 'Spurious war events cannot propagate phantom client wars.'
Check ($event.IndexOf('ConfirmLiberationWar(first, second)') -lt $event.IndexOf('RunDiplomaticSync(')) 'Clientage release precedes bloc propagation.'
Check ($behavior.Contains('var otherClients = new HashSet<Kingdom>') -and $behavior.Contains('kingdom != suzerain).Distinct()')) 'Each supporting kingdom is considered once.'
Check ($behavior.Contains('ClientLiberationRules.BlocContribution(') -and $behavior.Contains('ClientLiberationRules.EffectivePowerMultiplier(') -and $behavior.Contains('ClientLiberationRules.Readiness(')) 'Simulations exercise the production power/readiness rules.'
Check ($behavior.Contains('else if (cooldown > 0f)') -and $behavior.Contains('realmDesire < C.ClientRealmLiberationDesireThreshold') -and $behavior.Contains('willingness < BellumCivileOptions.WarWillDeclareThreshold')) 'Cooldown and desire gates remain, with resolve included in willingness.'
