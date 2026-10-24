$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$diagnostic = Read 'RealmLeadershipDiagnostics.cs'
$patch = Read 'Patches/RealmLeadershipDiagnosticPatch.cs'
$manager = Read 'Behaviors/FactionManagerBehavior.cs'
Check (!$diagnostic.Contains('BellumCivileDebug') -and !$diagnostic.Contains('BellumCivileOptions') -and
    $diagnostic.Contains('BellumCivileLogger.Log(') -and $diagnostic.Contains('BellumCivileLogger.Flush()')) 'Leadership diagnostics are unconditional and immediately flushed without reading MCM/debug settings.'
Check ($manager.Contains('OnGameLoadedEvent.AddNonSerializedListener(this, RealmLeadershipDiagnostics.OnGameLoaded)') -and
    $manager.Contains('OnSessionLaunchedEvent.AddNonSerializedListener(this, RealmLeadershipDiagnostics.OnSessionLaunched)') -and
    $manager.Contains('HourlyTickEvent.AddNonSerializedListener(this, RealmLeadershipDiagnostics.OnHourlyTick)')) 'Existing campaign behavior wires initial and periodic observational sweeps.'
Check ($diagnostic.Contains('ConditionalWeakTable<Campaign, Session>') -and !$diagnostic.Contains('SaveableField') -and
    $diagnostic.Contains('if (session.Open.ContainsKey(realm))') -and $diagnostic.Contains('session.Open.Remove(realm)')) 'Incidents deduplicate per campaign session and rearm after recovery without save changes.'
Check ($diagnostic.Contains('if (!IsReady(Campaign.Current)) return Array.Empty<Snapshot>();') -and
    $diagnostic.Contains('OnGameLoaded(CampaignGameStarter starter) => Start("save_loaded")') -and
    $diagnostic.Contains('OnSessionLaunched(CampaignGameStarter starter) => Start("session_launched")') -and
    $diagnostic.Contains('if (!IsReady(Campaign.Current)) return;')) 'Only load/session readiness enables native snapshots and periodic audits; partial startup data is not diagnosed.'
Check ($patch.Contains('[HarmonyFinalizer]') -and $patch.Contains('private static void Prefix(') -and
    $patch.Contains('private static void Finalizer(') -and !$patch.Contains('return false') -and !$patch.Contains('ref Exception')) 'Hooks observe without skipping native methods or replacing their exceptions.'
Check ($diagnostic -notmatch '\bRulingClan\s*=(?!=)' -and !$diagnostic.Contains('.SetLeader(') -and
    !$diagnostic.Contains('DestroyKingdomAction.Apply(') -and !$diagnostic.Contains('MakePeaceAction.Apply(')) 'The diagnostic does not repair leaders, destroy kingdoms or change diplomacy.'
'Source contracts passed; run --realm-leadership-diagnostic against each game installation.'
