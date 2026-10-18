$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$flow = Read 'Behaviors/CourtRoyalPeaceAgendas.cs'
$main = Read 'Behaviors/CourtAgendaBehavior.cs'
Check ($main.Contains('OnRoyalPeaceInvasion') -and $main.Contains('OnRoyalPeaceEnded') -and $main.Contains('MaintainRoyalPeaceCases();')) 'Declaration, peace and daily maintenance are wired.'
Check ($flow.Contains('ReviewWartimeRoyalPeace(attacker)') -and $flow.Contains('ReviewWartimeRoyalPeace(defender)') -and $flow.Contains('realm.RulingClan == Clan.PlayerClan')) 'Automatic review covers both sides of foreign wars but leaves player announcements voluntary.'
Check ($flow.Contains('HasPendingRoyalPeace(realm)') -and $flow.Contains('c.Realm == realm && !c.Closed')) 'Only one outstanding decree may be announced per realm.'
Check ($flow.Contains('_royalPeaceReviewAfter[realm.StringId] = now + CourtRoyalPeaceRules.ReviewDays') -and $main.Contains('BC_RoyalPeaceReviewAfter')) 'NPC review cooldown is persisted instead of rerolling every day.'
Check ($flow.Contains('CourtRoyalPeaceRules.RealmChance') -and $flow.Contains('GetRealmPreview') -and $flow.Contains('PeacetimeRoyalPeaceWeight')) 'NPC decisions evaluate the realm-wide preview in both pricing modes.'
Check ($flow.Contains('TryScheduleRoyalPeace(Kingdom realm, Clan ruler') -and $flow.Contains('NewPlayerCrownBusiness(realm)')) 'Players and NPCs share the notice-based scheduler.'
Check ($flow.Contains('agenda.IsFiled || agenda.IsOngoingObjective') -and $flow.Contains('agenda.PaidInfluence > 0') -and $flow.Contains('PendingDeposition') -and $flow.Contains('IsPending(entry.Realm)')) 'Paid business, ongoing objectives and succession are protected.'
Check ((Read 'Behaviors/CourtExecutiveAgendas.cs').Contains('!IsRoyalPeace(agenda)') -and (Read 'Behaviors/CourtCouncilAgendas.cs').Contains('IsRoyalPeace(agenda)')) 'Other priority agendas cannot overwrite announced royal peace.'
Check ($flow.Contains('CourtRoyalPeaceRules.ExecutionDay(now, BellumCivileOptions.PoliticalDeliberationDays)')) 'Notice uses the configured deliberation window.'
Check ($flow.Contains('QuoteStillValid(entry.Wartime, entry.QuotedCost, preview.Wartime, preview.InfluenceCost)') -and $flow.Contains('TryEnforceRealmPeace(entry.Realm, entry.Realm.RulingClan, out report)')) 'Assignment and execution revalidate the saved quote before invoking the shared executor.'
Check ($flow.Contains('if (!preview.SettlementReady)') -and $flow.Contains('"unsafe_settlement"')) 'Unsafe settlement defers with a bounded cancellation window.'
Check ($flow.Contains('if (!entry.RealmWide)') -and $flow.Contains('"legacy_decree"')) 'Legacy single-feud cases cancel without acquiring realm scope or a new price.'
Check ($flow.Contains('if (!entry.TryBegin()) return;') -and $flow.Contains('if (entry.Started)') -and $flow.Contains('will not replay')) 'Saved execution receipts prevent retrying interrupted settlements.'
Check ($flow.Contains('RoyalPeaceNotice(entry, "royal_peace_warning"') -and $flow.Contains('"royal_peace_cancelled"') -and $flow.Contains('notice.PlayerInvolved = true')) 'Warnings and cancellations reach players inside feud shells.'
Check ((Read 'CourtAgendaPresentation.cs').Contains('BC_RoyalPeaceDate') -and $main.Contains('BC_RoyalPeaceStatus')) 'Agenda date and tooltip describe a decree, not a ballot.'
Check ($main.Contains('BC_CourtRoyalPeaceCases') -and (Read 'BellumCivileSaveDefiner.cs').Contains('typeof(CourtRoyalPeaceCase), 109')) 'Case collection and type are registered for persistence.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $flow + $main + (Read 'ConflictOutcomeText.cs') + (Read 'CourtAgendaPresentation.cs') + (Read 'Behaviors/RealmPeaceEnforcementBehavior.cs') + (Read 'Behaviors/CourtPlayerRoyalPeace.cs')
$ids = @([regex]::Matches($texts, '\{=(BC_(?:RoyalPeace|RealmPeace)[^}]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($id in $ids) { Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Unique English localization: $id" }
