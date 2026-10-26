# Restored Realm Leadership Audit

Date: 2026-10-24. Branch: `bug-fixes`.

## Scope And Evidence

Investigated the supplied `crash report.txt`, `BellumCivile20261021.log`, and
`bellumcivile_yearlyreports (1).txt`. Compared current Bellum source with the local
stable 1.4.8 backup and 1.5 beta native DLLs using ILSpy. No campaign save or full
Harmony patch list was supplied, so the original loss of leadership is not reproduced.

The crash occurs in native town-rebellion creation, not Bellum's noble-rebellion
creation. The log repeatedly identifies an active restored Vlandian kingdom with
both `RulingClan` and `Leader` null. The native rebellion relation loop calls
`otherKingdom.Leader.SetPersonalRelation(...)` without a null guard. Its exclusion
checks only elimination and culture. This remains true in the beta DLL, although
other parts of native rebellion timing differ between the two versions.

## Lifecycle Comparison

| Stage | Native | Bellum restored realms |
| --- | --- | --- |
| Allocation | `Kingdom.CreateKingdom` registers a new object without a ruler. | `KingdomCreationSafetyHelper` validates a living founder and seeds `RulingClan` immediately after allocation. |
| Initialization | `KingdomManager.CreateKingdom` initializes identity and then calls `ChangeKingdomAction.ApplyByCreateKingdom`. | Restoration initializes inherited identity, culture and capital, then moves the surviving houses. |
| Ruler assignment | `ChangeRulingClanAction` assigns the property and raises `OnRulingClanChanged`. The property setter itself only writes the backing field. | Restoration and validation also write the property directly. These assignments intentionally do not run a native election but do not publish that native ruler-change event. |
| Publication | The high-level native creation method raises `OnKingdomCreated` after membership is installed. | Restored creation and staged collapse now publish it after verified membership, leadership, crown delivery and predecessor retirement, using a saved notification receipt. |
| Movement | `ChangeKingdomAction` changes membership, then dispatches `OnClanChangedKingdom`; direct property assignment does not perform the whole action. | Transfer helpers use native joining actions, preserve banners/influence, and suppress inappropriate old-ruler repair during the move. |
| Destruction | `DestroyKingdomAction` deactivates first, processes remaining clans, removes wars and raises `OnKingdomDestroyed`. | Bellum drains houses first to avoid destroying them. Inheritance/union/conflict protections can defer destruction before the native body runs. |
| Recovery | Native load checks and succession actions address specific circumstances. | Staged collapse and the additional restored-creation journal retain their chosen identity and verify movement/retirement before completion. |

`DestroyKingdomAction`, `ChangeRulingClanAction`, and `ChangeKingdomAction` decompile
identically in the two installations. The relevant setter and load-migration
semantics also match. Allocation/initialization alone must not be confused with the
high-level native kingdom-created event.

## Findings Before The Fix

### 1. Destruction Success Is Assumed Rather Than Verified

`CivilWarResolutionBehavior.DrainAndDestroyRebelKingdom` calls
`DestroyKingdomAction.Apply(rebelKingdom)` and immediately returns true without
checking `rebelKingdom.IsEliminated`. A Harmony prefix can legitimately defer that
void action without throwing. `ProcessPendingRebelCleanups` trusts the returned
true and removes the queued cleanup record.

This is a concrete missing postcondition. It can discard a retry while the old
realm remains active. Whether this occurred in the supplied campaign is unknown.
The shared drain helper now verifies elimination and retains/queues recovery on
failure, respecting existing ownership and transfer protections. Landless-parent
cleanup also retains its retry, and shell-cleanup success logging checks elimination.

### 2. Historical Restored Orphans Can Fall Between Recovery Systems

`BellumKingdomVisibilityHelper.IsSupersededEmptyKingdomShell` explicitly excludes
all Bellum-generated IDs, including `_restored` IDs. The daily leaderless-ruler
repair also requires a current Bellum resolution reference. Consequently, an old
generated realm whose recovery references have been lost can qualify for neither
repair nor the superseded-empty-shell sweep.

This matches the warning's `referenced_by_bc=False`; that flag is not proof that
Bellum never created the object. Do not remove the exclusions indiscriminately:
live restored realms, pending unions and crown partitions also use generated IDs.

A separate conservative sweep now handles empty, leaderless restored predecessors
with a valid living successor. It cross-checks actual clan membership and settlement
ownership, and excludes pending inheritance, accession, deposition, union, conflict
transfers and other Bellum resolution references. It does not remove the generic
visibility exclusions or destroy healthy landless rulers, including shared living rulers.

### 3. Older Restoration Paths Can Publish Before Verifying Completion

`TransferClansToKingdom` returns void and ignores individual failed moves.
`TransferLiveClansFromEliminatedKingdom` returns true after attempting the moves,
without checking that each house arrived. Some callers ignore even that result.
`CreateRestoredSuccessorKingdom` does not check the result of draining the former
rebel kingdom before returning the restored realm/marking challenge readiness.

These are partial-completion risks when another mod blocks a move or destruction,
not proof of the original crash. The staged continuing-collapse path is stronger:
it checks remaining clans and journal stages, but still ultimately relies on the
shared drain helper's destruction result.

Transfer helpers now return verified results, move the founder before followers,
and stop publication when a household or retirement remains pending. A new saved
creation record reserves one successor identity before allocation, retains household
and foreign-war snapshots, and resumes the original owner hourly. Old interrupted
challenge records reuse their exact planned realm rather than creating another suffix.
Restoration's own transfer protection permits its household drain, but permits native
retirement only after the old realm is empty; unrelated transfer protections remain.

### 4. Conditional Native Load Migration Can Clear A Protected Realm's Ruler

`ClanVariablesCampaignBehavior.OnGameLoadFinished` has a migration for saves
older than v1.3.14 while updating game version. When two kingdoms share a ruling
house, the small-kingdom branch clears `RulingClan` before requesting destruction.
If a protection defers that destruction, the cleared ruler is not rolled back by
the native method. The new diagnostic can identify both the clearing call and an
uncompleted destruction request. This migration does not run on every ordinary
load, and its applicability to the reporter's save is unknown.

A scoped patch observes only this pre-v1.3.14 updating-version load migration. If
a previously living ruling house is cleared, the realm remains active, and Bellum
still protects it for a conflict, partition or union transfer, finalization restores
that prior house. It does not overwrite an installed successor, restore a dead or
headless house, undo completed destruction, or suppress the original native exception.

### 5. Lifecycle Event Differences Are A Compatibility Surface

Direct ruler assignments and absent high-level kingdom-created publication can
leave event-driven third-party caches behind native object state. No supplied
evidence identifies such a mod as the cause. Any later event alignment requires
idempotent, post-initialization publication. The restored creation paths and staged
collapse now raise `OnKingdomCreated` after readiness checks. The notification
receipt is saved before invoking listeners: if one throws, retry does not replay
listeners that already ran. This is deliberately at-most-once publication; a throwing
third-party listener can still prevent later listeners in that invocation from running.
Direct ruler seeding is retained; no native election or extra ruler-change event is added.

## Diagnostic Added

`RealmLeadershipDiagnostics` and `RealmLeadershipDiagnosticPatch` observe the
native ruling-clan setter, clan-leader changes, clan kingdom changes, ruler hero
state changes, and kingdom destruction. All hooks are observational and void;
the finalizer preserves original exceptions. Existing load/session events and an
hourly audit catch pre-existing or raw-field/save changes that bypass the hooks.

The logger is unconditional, file-only, and flushed immediately. It records:

- One incident per live realm's missing/dead-ruler episode, previous house/head,
  current identity, detection source and campaign day.
- Bounded house/holding lists, pending collapse stages, cleanup and succession
  references, accession/union records, call stacks with assemblies and Harmony owners.
- Recovery or retirement and elapsed observed duration; one uncompleted-destruction
  marker per episode if an observed destruction request leaves the realm active.

Poll/load reports explicitly say a causal stack is unavailable. Normal temporary
succession or pre-destruction gaps can also be reported; a quick recovery/retirement
is not proof of corruption. Healthy realms and repeated unchanged hourly checks
do not write reports. Session deduplication is weakly scoped to the Campaign object
and is not saved. The observer itself makes no behavioral changes. The separate
lifecycle fixes above add a registered restoration save record/list (class ID 137)
and append notification field 21 to the existing collapse record. Missing restoration
data in older saves initializes empty. No MCM setting or town-rebellion crash
suppression is added.

## Verification And Limits

Native-DLL harness checks cover blocked founder/follower/loyalist moves, silent
destruction refusal, retained cleanup queues, save/load continuation, hourly
emergency recovery, legacy challenge adoption, crown-delivery failure, reentrant
and throwing creation listeners, unavailable successors, conservative orphan
retirement, and the scoped native migration guard. Native save definitions include
both the new record and appended collapse field. Source contracts additionally
check dispatch ownership, publication ordering and all cleanup protections.

Release rebuilds pass against stable 1.4.8 (16 existing obsolete-API warnings)
and the local beta DLLs (zero warnings). Each version passes 717 focused checks:
47 restored-realm recovery, 394 civil-war settlement, 91 civil-war grace,
112 integration/save definitions, 22 leadership diagnostics, 9 clan-finance
diagnostics, and 42 Crown household recovery. The beta check uses a detached temporary integration
worktree; it does not merge or change the user's actual branches.

Broader checks exposed existing failures also reproduced on untouched commit
`51d2672`: the inheritance-regression suite has 34 domestic-estate fixture failures
in `IsLandlessHistoricalCrown`, long-run recovery fails its Crown-regent replacement
assertion, and `CivilWarContinuation.Tests.ps1` has a stale parent-retarget source
contract. Those unrelated production paths and fixtures are not changed here.

The original user's save and complete mod set are still unavailable. These fixes
close demonstrated lifecycle weaknesses, not a reproduced causal diagnosis of that
campaign. They do not invent rulers for arbitrary broken realms, delete generated
kingdoms solely by name, or globally suppress native null-reference exceptions.
An interrupted restoration whose recorded founder/successor becomes unavailable
stays deferred and logged instead of silently choosing another realm or dynasty.

Suggested in-game checks on each version:

1. Lose a realm's last stronghold to a foreign conqueror during a crown civil war;
   confirm a single successor, preserved houses/titles and normal remaining wars.
2. Save/reload during unresolved civil-war settlement; confirm no extra restored
   kingdom and no repeated creation/reward notifications.
3. Load an affected old save; confirm empty orphan retirement only when the strict
   successor/ownership checks hold, and retain the always-on leadership diagnostics
   for anything that remains broken.

## October 26 Follow-Up

The supplied Rhodok logs identify a distinct continuing-collapse failure: native
clan-change callbacks pruned the winning coalition during its move to the restored
realm. A later stage-three retry then compared the changed faction leader with the
original challenger and stopped before retiring the empty rebel realm. The saved
winner, outcome and restored kingdom were still available. Registration of the
restored mantle alone also did not deliver the legal Crown required by challenge
victory legalization.

Recovery now preserves captured coalition members only inside the coordinator's
own stage-two source-to-successor transfers. The original challenge and winning
house must agree; an uncommitted claimant must still live, lead that house, and
belong to the recorded realms. Legal Crown delivery is verified against the exact
successor and saved victor before its receipt is recorded. Actual matching legal
ownership can recover an interruption after delivery; existing receipts retain
historical outcomes without deposing a later ruler. Empty predecessors still use
the ordinary guarded drain, including retry when destruction is refused. No new
save fields or broad native-rebellion suppression are introduced.

`--collapse-claimant-recovery` exercises the real faction-change handler and
settlement coordinator with native movement/title/publication endpoints doubled,
including an older empty-coalition stage-three journal, interrupted legal delivery,
dead or defected uncommitted claimants, later rulers, save-data continuation and
blocked retirement. It is not an in-game reproduction of the user's full mod stack.

The second supplied report is separate: native militia ownership throws while
Bellum invalidates party visuals during grand-coalition creation. Both visual
scans now avoid the native hero-owner getters for settlement parties.
`--party-visual-refresh` reproduces the native getter failure and verifies healthy
party updates, ownership changes, absent leaders, missing settlement/binding data,
and the actual palette entry point. Neither fix identifies which mod or earlier
event produced that incomplete militia. The second report's orphaned-feud warning
remains a separate investigation, not a proven cause of its visual-refresh crash.
