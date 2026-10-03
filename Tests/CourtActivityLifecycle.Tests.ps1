$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$scheduler = Read 'Behaviors/CourtAgendaBehavior.cs'
$events = Read 'CourtSessionEvents.cs'
$activity = Read 'Behaviors/CourtActivityAgendas.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($scheduler.Contains('new CourtActivityObjectiveSource()')) 'Activities participate in the shared agenda selector.'
Check (!$scheduler.Contains('CourtSessionEvents.Run') -and !$scheduler.Contains('sessionMoods')) 'Independent session-event pass is removed.'
Check ((Read 'CourtPolicyObjectiveSource.cs').Contains('_activityAdmissions.TryGetValue')) 'Term admission is cached per faction.'
Check ($save.Contains('AddClassDefinition(typeof(CourtActivityRecord), 90)') -and $save.Contains('AddClassDefinition(typeof(CourtActivityTarget), 91)') -and $save.Contains('ConstructContainerDefinition(typeof(List<CourtActivityTarget>))')) 'Activity records and recipient lists are registered for saves.'
$ids = @([regex]::Matches($save, 'AddClassDefinition\(typeof\([^)]+\), (\d+)\)') | ForEach-Object { $_.Groups[1].Value })
Check (@($ids | Sort-Object -Unique).Count -eq $ids.Count) 'Custom class save IDs remain unique.'
Check ($events.Contains('realm.Armies.Contains(target.Army)')) 'Army activity requires the original army reference.'
Check ($events.Contains('realm.RulingClan == plan.RulingClan') -and $events.Contains('realm.RulingClan?.Leader == plan.Ruler')) 'Activity is bound to the original ruler and house.'
Check ($events.IndexOf('!target.TryBegin()') -lt $events.IndexOf('Apply(definition, target, plan)')) 'Recipient receipt is sealed before native mutations.'
$execution = $events.Substring($events.IndexOf('internal static void Execute'))
Check (!$execution.Contains('MBRandom')) 'Execution and reporting do not reroll events or recipients.'
Check ($activity.Contains('!agenda.SessionDate.IsPast') -and $activity.Contains('agenda.ResultApplied = agenda.PaymentSettled = agenda.EventApplied = true')) 'Session execution records a terminal result once.'
Check (!$activity.Contains('Mood =') -and !$activity.Contains('Mood +=')) 'Activity completion adds no generic agenda mood shock.'
Check ((Read 'Behaviors/IdeologyBehavior.cs').Contains('Legacy faction meetings have been retired.')) 'Legacy forced meetings cannot bypass activity selection.'
[xml]$ui = Read 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
Check ($null -ne $ui.SelectSingleNode('//*[@IsVisible="@HasActivityHint" and @Command.HoverBegin="ExecuteBeginActivityHint"]')) 'Activity hover area is conditional, not an empty generic tooltip.'
'Source and XML contracts only; campaign persistence and rendered hints require live testing.'
