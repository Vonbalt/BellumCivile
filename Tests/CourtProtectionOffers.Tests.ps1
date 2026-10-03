$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$behavior = Read 'Behaviors/CourtProtectionAgendas.cs'
$player = Read 'Behaviors/CourtProtectionPlayerFlow.cs'
$source = Read 'CourtProtectionObjectiveSource.cs'
$client = Read 'Behaviors/ClientKingdomBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
Check ($source.Contains('o.Faction == null') -and $source.Contains('o.Sponsor == c.Realm.RulingClan')) 'Protection is Crown business, never a member faction motion.'
Check ($source.Contains('context.ManualSelection ? candidates.AsEnumerable() : candidates.Take(1)')) 'Player can choose all eligible protectors; NPCs rank one recipient per threat.'
Check ($behavior.Contains('HasProtectionOffer(p.Client)') -and $behavior.Contains('CampaignTime.Now.ToDays < p.TermEnd')) 'Saved offer receipt limits the Crown to one appeal in the term without blocking the next boundary.'
Check ($behavior.Contains('p.ClientHouse.Leader != p.ClientRuler') -and $behavior.Contains('p.ProtectorHouse.Leader != p.ProtectorRuler') -and $behavior.Contains('WarStartDate.ToDays != p.ThreatWarStart')) 'Rulers and original war identity are revalidated.'
Check ($behavior.Contains('GetActiveWar(p.Client, p.Threat) == p.ThreatWar')) 'A replacement conflict cannot inherit the old offer.'
Check ($behavior.Contains('assessment.Score.WouldAccept') -and !$player.Contains('Score.WouldAccept')) 'NPC acceptance threshold never overrides player choice.'
Check ($player.Contains('ValidProtectionCallback(p, phase)') -and $player.Contains('_protectionOffers.Contains(p)')) 'Stale popup callbacks cannot accept replaced offers.'
Check ($behavior.Contains('CanAlignClient(p.Client, p.Protector)')) 'Interrupted clientage rechecks changed alignment permissions.'
Check (!$behavior.Contains('MakePeaceAction') -and !$behavior.Contains('.Mood =')) 'The executor never rolls back by forcing peace or applies Crown mood shocks.'
Check ((Read 'Behaviors/CourtAgendaBehavior.cs').Contains('"BC_CourtProtectionOffers"') -and (Read 'BellumCivileSaveDefiner.cs').Contains('List<CourtProtectionRecord>')) 'Offer/recovery/report receipts persist outside agenda cleanup.'
Check ($client.Contains('if (existing == null)') -and $client.Contains('ProtectionAlignmentComplete')) 'Native clientage resumes records and verifies alignment before success.'
Check ($picker.Contains('agenda.Protection = prepared.Protection') -and $picker -match 'motion.Kind == CourtProtectionRules.Kind[^\r\n]*\) && identity.HasTermSnapshot') 'Crown picker preserves prepared target and early session date.'
Check ($behavior.Contains('PopupPending') -and $player.Contains('report.PopupPending = false')) 'Player outcome report survives until acknowledged.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$text = $behavior + $player + $picker + (Read 'Behaviors/CourtAgendaBehavior.cs') + (Read 'CourtAgendaPresentation.cs')
$ids = [regex]::Matches($text, '\{=(BC_Protection[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $ids) {
    $nodes = @($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique protection localization: $id"
    $fallback = [regex]::Match($text, ('\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)')).Groups[1].Value.Replace('\n', "`n")
    Check ($nodes[0].text -ceq $fallback) "Matching protection fallback: $id"
}
'Protection offer contracts passed. Live UI, campaign and save/load tests remain pending.'
