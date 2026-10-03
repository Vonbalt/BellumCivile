$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$collapse = Read 'Behaviors/CivilWarCollapseContinuation.cs'
$promotion = Read 'Behaviors/CivilWarRivalPromotion.cs'
$capture = $promotion.Substring($promotion.IndexOf('internal CivilWarRivalPromotionRecord CaptureRivalCrownVictory'))
$capture = $capture.Substring(0, $capture.IndexOf('internal bool ResumeCollapsePromotion'))
Check ($capture -notmatch '\.PromoteRivalry\(|ResumeRivalPromotion\(|SetNeutral|DeclareWar|MoveClan|_promotions.Add') 'Reservation captures rivalry history without changing the campaign world.'
Check ($collapse.Contains('return TryBeginContinuingCollapse(parent, wars, deferResume: true);')) 'Temporarily deferred supported collapse also blocks premature native destruction.'
Check ($collapse.Contains('hasRivalry && promotion == null) return true;')) 'Unready rivalry cannot fall through into legacy absorption.'
Check ($collapse.Contains('Promotion = promotion') -and $collapse.Contains('promotion.CollapseOwner = record')) 'Collapse records the shared promotion identity before resuming.'
Check ($promotion.Contains('p.CollapseOwner == null') -and $promotion.Contains('prior.CollapseOwner == null')) 'Collapse-owned promotion cannot enter standalone victory recovery.'
Check ($promotion.Contains('promotion.CollapseOwner != owner') -and $promotion.Contains('OwnsContinuingCollapse(owner)')) 'Only the registered collapse owner may resume its promotion.'
Check ($collapse.IndexOf('ResumeCollapsePromotion(record)') -lt $collapse.IndexOf('KingdomCreationSafetyHelper.CreateKingdom')) 'Rival history is promoted before successor creation and household movement.'
Check ($collapse.Contains('CaptureCrownPairs(record.Conflict, record.Winner)') -and $collapse.Contains('BeginCrownTransfer(record.Conflict, record.Successor')) 'After promotion, existing saved Crown-transfer stages attach the war to the restored realm.'
Check ($promotion.Contains('transfer.NativeCounts = counts') -and $promotion.Contains('transfer.WarStarted = live.WarStartDate')) 'Deferred reservations refresh native history only before score promotion begins.'
'Source contracts and score-composition tests only; native outside conquest and save/load require campaign testing.'
