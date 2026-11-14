using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static class DefensivePactThreatTests
{
    private static bool SkipLog() => false;
    private sealed class MemoryStore : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Data[key] = data;
            else if (Data.TryGetValue(key, out object value)) data = (T)value;
            else return false;
            return true;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var rules = assembly.GetType("BellumCivile.DefensivePactThreatRules", true);
        var ledger = assembly.GetType("BellumCivile.DefensivePactThreatLedger", true);
        double Decay(double rate, int fiefs, double skill = 0) => (double)AccessTools.Method(rules, "WeeklyDecay")
            .Invoke(null, new object[] { rate, fiefs, skill });
        object New(double value, double day, double rate) => Activator.CreateInstance(ledger,
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "realm", value, day, rate }, null);
        double Peek(object value, double day) => (double)AccessTools.Method(ledger, "Peek").Invoke(value, new object[] { day });
        void Rate(object value, double day, double rate) => AccessTools.Method(ledger, "ChangeRate").Invoke(value, new object[] { day, rate });
        void Adjust(object value, double day, double delta) => AccessTools.Method(ledger, "Adjust").Invoke(value, new object[] { day, delta });
        double Property(object value, string name) => (double)AccessTools.Property(ledger, name).GetValue(value);
        bool Near(double a, double b) => Math.Abs(a - b) < .000001;

        var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings", true);
        var optionsType = assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var settings = Activator.CreateInstance(settingsType, true);
        var cachedSettings = AccessTools.Field(optionsType, "_settings");
        object priorSettings = cachedSettings.GetValue(null);
        var configuredDecay = AccessTools.Property(settingsType, "DefensivePactWeeklyThreatDecay");
        var effectiveDecay = AccessTools.Property(optionsType, "DefensivePactWeeklyThreatDecay");
        try
        {
            cachedSettings.SetValue(null, settings);
            check(Near((float)effectiveDecay.GetValue(null), 1), "Threat decay setting defaults to one point per seven days");
            configuredDecay.SetValue(settings, -3f);
            check(Near((float)effectiveDecay.GetValue(null), 0), "Threat decay setting clamps negative values");
            configuredDecay.SetValue(settings, 50f);
            check(Near((float)effectiveDecay.GetValue(null), 10), "Threat decay setting clamps excessive values");
            configuredDecay.SetValue(settings, float.NaN);
            check(Near((float)effectiveDecay.GetValue(null), 1), "Invalid saved threat decay falls back to the default");
        }
        finally { cachedSettings.SetValue(null, priorSettings); }

        check(Near(Decay(1, 0), 1) && Near(Decay(1, 10), .5) && Near(Decay(1, 20), 1d / 3d)
            && Near(Decay(1, 30), .25) && Near(Decay(1, 50), .25), "Fief scaling reaches a fourfold cap at 30 fiefs");
        check(Near(Decay(1, 30, 1), .5) && Near(Decay(1, 0, 1), 2),
            "A fully effective chancellor doubles the size-adjusted decay");
        double Chancellor(bool active, double competence) => (double)AccessTools.Method(rules, "ChancellorEffect")
            .Invoke(null, new object[] { active, competence });
        check(Near(Chancellor(true, 0), 0) && Near(Chancellor(true, 50), .5)
            && Near(Chancellor(true, 100), 1) && Near(Chancellor(true, 150), 1)
            && Near(Chancellor(false, 100), 0) && Near(Chancellor(true, double.NaN), 0),
            "Chancellor competence scales the bonus from zero to 100%, and inactivity or invalid competence grants none");
        var registry = assembly.GetType("BellumCivile.PrivyCouncilAssignmentRegistry", true);
        var definitions = (IEnumerable)AccessTools.Method(registry, "GetAssignments").Invoke(null, new object[] { PrivyCouncilOffice.Chancellor });
        bool shownWithoutFeature = false;
        foreach (var definition in definitions)
            if ((string)definition.GetType().GetProperty("Id").GetValue(definition) == "chancellor_reassure_realms") shownWithoutFeature = true;
        check(!shownWithoutFeature && AccessTools.Method(registry, "GetAssignment").Invoke(null, new object[] { "chancellor_reassure_realms" }) != null,
            "The assignment is registered but hidden while defensive pacts are inactive");
        var aiType = typeof(DefensivePactAiBehavior);
        var offset = AccessTools.Method(aiType, "InitialOffset");
        int firstOffset = (int)offset.Invoke(null, new object[] { "vlandia" });
        check(firstOffset >= 0 && firstOffset < 30 && firstOffset == (int)offset.Invoke(null, new object[] { "vlandia" }),
            "NPC evaluation offsets are stable within a fixed 30-day interval");
        var aiScheduler = new DefensivePactAiBehavior();
        var dueType = aiType.GetNestedType("DueRealm", BindingFlags.NonPublic);
        object schedule = AccessTools.Field(aiType, "_schedule").GetValue(aiScheduler);
        var enqueue = schedule.GetType().GetMethod("Add");
        for (int i = 0; i < 55; i++)
        {
            var realm = (Kingdom)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Kingdom));
            realm.StringId = "realm_" + i.ToString("D2");
            var due = Activator.CreateInstance(dueType, true);
            AccessTools.Field(dueType, "Realm").SetValue(due, realm);
            AccessTools.Field(dueType, "Day").SetValue(due, 0);
            enqueue.Invoke(schedule, new[] { due });
        }
        var takeDue = AccessTools.Method(aiType, "TakeDue");
        int Taken(int day) => ((ICollection)takeDue.Invoke(aiScheduler, new object[] { day })).Count;
        int firstBatch = Taken(0), totalTaken = firstBatch;
        for (int i = 0; i < 13; i++) totalTaken += Taken(0);
        check(firstBatch == 4 && totalTaken == 55 && Taken(0) == 0 && Taken(29) == 0 && Taken(30) == 4,
            "A 55-realm schedule caps daily work and reschedules each realm after 30 fixed days");
        var a = New(50, 0, 1);
        check(Near(Peek(a, 3.5), 49.5) && Near(Peek(a, 14), 48)
            && Near(Property(a, "UpdatedDay"), 0) && Near(Property(a, "Value"), 50),
            "Repeated lazy reads provide continuous decay without mutating the stored snapshot");
        Rate(a, 14, .25);
        check(Near(Peek(a, 42), 47) && Near(Property(a, "Value"), 48),
            "A rate change settles the previous interval before applying the new rate");
        Adjust(a, 42, 30);
        check(Near(Peek(a, 49), 76.75), "An event applies its delta after settling elapsed decay");
        Adjust(a, 49, 100);
        check(Near(Peek(a, 56), 99.75), "Threat caps at 100 before later decay");
        Adjust(a, 56, -200);
        check(Near(Peek(a, 70), 0), "Threat loss and subsequent decay cannot produce a negative value");
        bool rejected = false;
        try { Rate(a, 60, double.NaN); } catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
        check(rejected && Near(Property(a, "UpdatedDay"), 56) && Near(Property(a, "WeeklyDecay"), .25),
            "Invalid rate changes cannot partially modify the ledger");
        rejected = false;
        try { Adjust(a, 55, 10); } catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
        check(rejected && Near(Property(a, "UpdatedDay"), 56), "A backwards timestamp cannot corrupt the ledger");

        var owner = new DefensivePactThreatBehavior();
        var ownerType = typeof(DefensivePactThreatBehavior);
        double Get(DefensivePactThreatBehavior b, string id, double day) => (double)AccessTools.Method(ownerType, "GetThreat")
            .Invoke(b, new object[] { id, day });
        void Add(DefensivePactThreatBehavior b, string id, double day, double rate, double delta)
            => AccessTools.Method(ownerType, "AdjustThreat").Invoke(b, new object[] { id, day, rate, delta });
        var records = AccessTools.Field(ownerType, "_records");
        check(Near(Get(owner, "one", 0), 0) && ((System.Collections.ICollection)records.GetValue(owner)).Count == 0,
            "Reading an unthreatened realm creates no saved snapshot");
        Add(owner, "one", 0, .5, 40);
        Add(owner, "two", 0, 1, 20);
        var store = new MemoryStore { IsSaving = true }; owner.SyncData(store);
        var reloaded = new DefensivePactThreatBehavior(); store.IsSaving = false; reloaded.SyncData(store);
        check(Near(Get(reloaded, "one", 14), 39) && Near(Get(reloaded, "two", 14), 18),
            "The realm-keyed lookup and previous rates are rebuilt from saved snapshots");
        AccessTools.Method(ownerType, "ChangeRate").Invoke(reloaded, new object[] { "one", 14d, 1d });
        check(Near(Get(reloaded, "one", 21), 38) && Near(Get(reloaded, "two", 21), 17),
            "Changing one realm's rate settles only that realm and preserves the other");
        Add(reloaded, "two", 21, .5, 5);
        check(Near(Get(reloaded, "two", 28), 21.5),
            "An event with a new decay rate settles the previous rate before applying its change");
        Add(reloaded, "empty", 21, 1, -10);
        check(((System.Collections.ICollection)records.GetValue(reloaded)).Count == 2,
            "A loss for an unthreatened realm creates no unnecessary record");

        bool Treaty(DefensivePactThreatBehavior b, string proposal, string realm, double day, double rate, double delta)
            => (bool)AccessTools.Method(ownerType, "ApplyTreatyOnce").Invoke(b, new object[] { proposal, realm, day, rate, delta });
        check(Treaty(reloaded, "treaty", "one", 21, 1, 10) && !Treaty(reloaded, "treaty", "one", 21, 1, 10)
            && Treaty(reloaded, "treaty", "two", 21, .5, -5) && Near(Get(reloaded, "one", 21), 48)
            && Near(Get(reloaded, "two", 21), 17), "A treaty changes each realm once even when application is retried");
        store.IsSaving = true; reloaded.SyncData(store);
        var afterReload = new DefensivePactThreatBehavior(); store.IsSaving = false; afterReload.SyncData(store);
        check(!Treaty(afterReload, "treaty", "one", 28, 1, 10) && Near(Get(afterReload, "one", 28), 47),
            "Treaty receipts survive a save/load and prevent duplicate changes");
        check(Treaty(afterReload, "ab", "c", 28, 1, 1) && Treaty(afterReload, "a", "bc", 28, 1, 1),
            "Receipt keys distinguish ambiguous proposal and realm identifier combinations");
        rejected = false;
        try { Treaty(afterReload, "bad", "one", 28, double.NaN, 10); }
        catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
        check(rejected && Treaty(afterReload, "bad", "one", 28, 1, 10),
            "Invalid inputs do not consume a treaty receipt");

        var rateOwner = new DefensivePactThreatBehavior();
        Add(rateOwner, "alpha", 0, 1, 40); Add(rateOwner, "beta", 0, 1, 20); Add(rateOwner, "expired", 0, 1, 1);
        int rateCalls = 0;
        int Refresh(IEnumerable<string> ids, double day, Func<string, double> rates) => (int)AccessTools.Method(ownerType, "RefreshRates")
            .Invoke(rateOwner, new object[] { ids, day, rates });
        int RefreshAll(double day, Func<string, double> rates) => (int)AccessTools.Method(ownerType, "RefreshAllRates")
            .Invoke(rateOwner, new object[] { day, rates });
        check(Refresh(new[] { "alpha", "missing", "alpha", "expired" }, 14, id => { rateCalls++; return .5; }) == 1
            && rateCalls == 1 && Near(Get(rateOwner, "alpha", 21), 37.5) && Near(Get(rateOwner, "beta", 21), 17),
            "A targeted change settles only the affected active realm and skips duplicates, unknown and expired realms");
        rejected = false; rateCalls = 0;
        try { RefreshAll(21, id => { rateCalls++; return id == "beta" ? double.NaN : .25; }); }
        catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
        check(rejected && rateCalls == 2 && Near(Get(rateOwner, "alpha", 28), 37),
            "A bad global rate is rejected before modifying any tracked realm");
        rateCalls = 0;
        check(RefreshAll(21, id => { rateCalls++; return id == "alpha" ? .25 : 2; }) == 2 && rateCalls == 2
            && Near(Get(rateOwner, "alpha", 28), 37.25) && Near(Get(rateOwner, "beta", 28), 15),
            "A settings-wide rate change scans only stored live threat and preserves each previous interval");

        var impactType = assembly.GetType("BellumCivile.DefensivePactTreatyThreat", true);
        object Impact() => Activator.CreateInstance(impactType, true);
        void ImpactCall(object impact, string method, params object[] values)
            => AccessTools.Method(impactType, method).Invoke(impact, values);
        double ImpactValue(object impact, string property) => (double)AccessTools.Property(impactType, property).GetValue(impact);
        double Project(object impact, double current) => (double)AccessTools.Method(impactType, "Project").Invoke(impact, new object[] { current });
        object recipient = Impact(), conceder = Impact();
        ImpactCall(recipient, "GainFief", 40, false);
        ImpactCall(conceder, "LoseFief", 40);
        check(Near(Project(recipient, 30), 50) && Near(Project(conceder, 40), 30),
            "An unclaimed 40-WS fief increases the recipient by 20 and reduces the conceder by 10");
        var termVm = assembly.GetType("BellumCivile.UI.Parley.TreatyTermVM", true);
        var forecastMethod = AccessTools.Method(termVm, "BuildThreatForecast");
        string Forecast(double before, object impact) => (string)forecastMethod.Invoke(null,
            new object[] { new TaleWorlds.Localization.TextObject("Vlandia"), before, impact });
        check(Forecast(30, recipient).Contains("increases from 30 to 50")
            && Forecast(40, conceder).Contains("decreases from 40 to 30")
            && Forecast(3, conceder).Contains("decreases from 3 to 0"),
            "Treaty forecast describes increases and decreases after applying threat bounds");
        check(Forecast(100, recipient).Contains("remains at 100")
            && Forecast(0, null).Contains("remains at 0"),
            "A capped or unaffected treaty forecast reports no change");
        var forecastRow = Activator.CreateInstance(termVm, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { "forecast" }, null);
        check((int)AccessTools.Property(termVm, "RowHeight").GetValue(forecastRow) == 72
            && (string)AccessTools.Property(termVm, "CostText").GetValue(forecastRow) == string.Empty,
            "Forecast rows reserve extra height and do not display a war-score cost");
        ImpactCall(recipient, "GainFief", 40, true);
        ImpactCall(recipient, "GainForcedTerm", 60);
        check(Near(ImpactValue(recipient, "Gain"), 45), "Claimed land and forced terms use the normal quarter-cost rate");
        ImpactCall(recipient, "GainFief", 40, false);
        check(Near(ImpactValue(recipient, "CappedGain"), 50) && Near(Project(recipient, 30), 80),
            "One treaty cannot add more than 50 threat to a realm");
        ImpactCall(recipient, "LoseFief", 80);
        check(Near(Project(recipient, 30), 60) && Near(Project(conceder, 3), 0),
            "Losses are applied alongside capped gains, and the final score remains bounded");
        rejected = false;
        try { ImpactCall(recipient, "LoseFief", -1); }
        catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
        check(rejected && Near(ImpactValue(recipient, "Loss"), 20), "Invalid term costs do not alter the accumulated result");

        var calculator = assembly.GetType("BellumCivile.DefensivePactTreatyCalculator", true);
        var calculate = AccessTools.Method(calculator, "Calculate");
        IDictionary Calculate(IEnumerable<TreatyTermRecord> terms, Func<TreatyTermRecord, bool> effective,
            Func<TreatyTermRecord, bool> concession, Func<TreatyTermRecord, bool> legitimate)
            => (IDictionary)calculate.Invoke(null, new object[] { terms, effective, concession, legitimate });
        var castle = new TreatyTermRecord(TreatyTermType.TransferFief, 40, "castle", fromKingdomId: "battania", toKingdomId: "vlandia");
        var emptyTerms = new TreatyTermRecord[0];
        Func<TreatyTermRecord, bool> yes = _ => true, no = _ => false;
        check(Calculate(emptyTerms, yes, yes, no).Count == 0
            && Calculate(new[] { castle }, no, yes, no).Count == 0
            && Calculate(new[] { castle }, yes, no, no).Count == 0,
            "A returned occupation or failed transfer has no treaty threat impact");
        var signed = Calculate(new[] { castle, castle }, yes, yes, no);
        check(signed.Count == 2 && Near(Project(signed["vlandia"], 30), 50)
            && Near(Project(signed["battania"], 40), 30), "An effective fief concession counts once for both principals");
        var recognized = new TreatyTermRecord(TreatyTermType.RecognizeClientOccupation, 40, "castle",
            fromKingdomId: "battania", toKingdomId: "vlandia", thirdKingdomId: "client");
        check(Near(Project(Calculate(new[] { recognized }, yes, yes, yes)["vlandia"], 30), 40),
            "A confirmed client occupation can be accounted for without an ownership-change event");
        var imposed = new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 60,
            fromKingdomId: "battania", toKingdomId: "vlandia");
        var offered = new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 60,
            fromKingdomId: "battania", toKingdomId: "vlandia", wasVoluntaryOffering: true);
        check(Near(Project(Calculate(new[] { imposed }, yes, yes, no)["vlandia"], 30), 45)
            && Calculate(new[] { offered }, yes, yes, no).Count == 0,
            "Forced clientage adds threat only when actually imposed, not voluntarily offered");
        var white = new TreatyTermRecord(TreatyTermType.WhitePeace, 0);
        check(Calculate(new[] { white, castle }, yes, yes, no).Count == 0,
            "White peace never creates a territorial threat gain, even with a malformed mixed draft");
        var territorialType = assembly.GetType("BellumCivile.DefensivePactTerritorialFacts", true);
        var classify = AccessTools.Method(territorialType, "Classify");
        Func<string, string, bool?> sameSide = (a, b) => a == b
            || (a == "battania_client" && b == "battania") || (a == "vlandia_client" && b == "vlandia");
        string Land(TreatyTermRecord term, IEnumerable<WarScoreFiefSnapshotRecord> snapshots) => classify.Invoke(null,
            new object[] { term, snapshots, sameSide }).ToString();
        var battanianCastle = new WarScoreFiefSnapshotRecord("castle", "battania", "old", false, true);
        var vlandianCastle = new WarScoreFiefSnapshotRecord("castle", "vlandia", "old", false, true);
        var clientCastle = new WarScoreFiefSnapshotRecord("castle", "battania_client", "old", false, true);
        check(Land(castle, new[] { battanianCastle }) == "Concession"
            && Land(castle, new[] { vlandianCastle }) == "ReturnedToOwnSide",
            "The war snapshot distinguishes keeping conquered land from returning the recipient's own fief");
        check(Land(recognized, new[] { clientCastle }) == "Concession"
            && Land(castle, new WarScoreFiefSnapshotRecord[0]) == "Unknown"
            && Land(castle, new[] { battanianCastle, vlandianCastle }) == "Unknown",
            "Client-side ownership is recognized, while missing or conflicting snapshots do not invent a concession");
        var legalType = assembly.GetType("BellumCivile.DefensivePactLegalBasisRules", true);
        var legal = AccessTools.Method(legalType, "Evaluate");
        var titleMap = new Dictionary<string, FeudalTitleRecord> {
            ["barony"] = new FeudalTitleRecord("barony", "Barony", FeudalTitleType.Barony, "", "", "county", "castle", "", 0, 0),
            ["county"] = new FeudalTitleRecord("county", "County", FeudalTitleType.County, "", "", "crown", "", "", 0, 0),
            ["crown"] = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom, "", "", "", "", "", 0, 0)
        };
        Func<string, FeudalTitleRecord> findTitle = id => titleMap.TryGetValue(id, out var found) ? found : null;
        string Basis(string barony, string sovereign, params string[] claims) => legal.Invoke(null,
            new object[] { barony, sovereign, new HashSet<string>(claims), findTitle }).ToString();
        check(Basis("barony", "other", "barony") == "Established"
            && Basis("barony", "other", "county") == "Established"
            && Basis("barony", "crown") == "Established", "A fief claim, ancestor claim or de jure sovereign provides a legal basis");
        check(Basis("barony", "other") == "NotEstablished" && Basis("barony", null) == "Unknown"
            && Basis("missing", "other") == "Unknown", "Missing jurisdiction or title data remains unknown instead of inventing an unjustified conquest");
        titleMap["county"].SetParentTitle("barony");
        check(Basis("barony", "other") == "Unknown", "A cyclic de jure hierarchy cannot trap or misclassify the legal-basis check");
        titleMap["county"].SetParentTitle("crown");
        var warBasis = assembly.GetType("BellumCivile.DefensivePactWarJustification", true);
        var warEvaluate = AccessTools.Method(warBasis, "Evaluate");
        var controlled = new FeudalTitleRecord("disputed", "Disputed", FeudalTitleType.County,
            "attacker_clan", "defender_clan", "", "", "", 0, 0);
        var elsewhere = new FeudalTitleRecord("elsewhere", "Elsewhere", FeudalTitleType.County,
            "third_clan", "third_clan", "", "", "", 0, 0);
        var warTitles = new Dictionary<string, FeudalTitleRecord> { ["disputed"] = controlled, ["elsewhere"] = elsewhere };
        Func<string, FeudalTitleRecord> lookupWarTitle = id => warTitles.TryGetValue(id, out var title) ? title : null;
        string WarBasis(IEnumerable<string> claimants, IEnumerable<string> claims, IEnumerable<FeudalTitleRecord> legalTitles)
            => warEvaluate.Invoke(null, new object[] { claimants, new HashSet<string> { "defender_clan" },
                (Func<string, IEnumerable<string>>)(_ => claims), (Func<string, IEnumerable<FeudalTitleRecord>>)(_ => legalTitles), lookupWarTitle }).ToString();
        check(WarBasis(new[] { "attacker_clan" }, new[] { "disputed" }, Array.Empty<FeudalTitleRecord>()) == "Established"
            && WarBasis(new[] { "attacker_clan" }, Array.Empty<string>(), new[] { controlled }) == "Established",
            "A target-controlled claim or de jure holding justifies a declaration");
        check(WarBasis(new[] { "attacker_clan" }, new[] { "elsewhere" }, Array.Empty<FeudalTitleRecord>()) == "NotEstablished"
            && WarBasis(new[] { "attacker_clan" }, Array.Empty<string>(), new[] { elsewhere }) == "NotEstablished",
            "Claims and titles held by an unrelated realm do not justify attacking the target");
        check(WarBasis(new[] { "attacker_clan" }, new[] { "missing" }, Array.Empty<FeudalTitleRecord>()) == "Unknown"
            && WarBasis(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FeudalTitleRecord>()) == "Unknown",
            "Missing title or claimant data does not invent an unjustified war");
        var projectionType = assembly.GetType("BellumCivile.DefensivePactDraftProjection", true);
        var basisType = assembly.GetType("BellumCivile.DefensivePactLegalBasis", true);
        var builder = AccessTools.Method(projectionType, "Build");
        Delegate BasisDelegate(string name)
        {
            var parameter = Expression.Parameter(typeof(TreatyTermRecord), "term");
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(TreatyTermRecord), basisType),
                Expression.Constant(Enum.Parse(basisType, name), basisType), parameter).Compile();
        }
        var draft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        draft.AddTerm(castle);
        object Preview(IEnumerable<WarScoreFiefSnapshotRecord> snapshots, string basis) => builder.Invoke(null,
            new object[] { draft, snapshots, sameSide, BasisDelegate(basis) });
        bool Reliable(object value) => (bool)AccessTools.Property(projectionType, "IsReliable").GetValue(value);
        IDictionary Impacts(object value) => (IDictionary)AccessTools.Property(projectionType, "Impacts").GetValue(value);
        var validPreview = Preview(new[] { battanianCastle }, "NotEstablished");
        check(Reliable(validPreview) && Near(Project(Impacts(validPreview)["vlandia"], 30), 50)
            && Near(Project(Impacts(validPreview)["battania"], 40), 30),
            "A draft preview combines real treaty terms, occupation snapshots and legal status for both realms");
        var returnedPreview = Preview(new[] { vlandianCastle }, "Unknown");
        check(Reliable(returnedPreview) && Impacts(returnedPreview).Count == 0,
            "Returning a fief to its own side does not require or invent a legal-basis charge");
        var uncertainPreview = Preview(new[] { battanianCastle }, "Unknown");
        check(!Reliable(uncertainPreview) && Impacts(uncertainPreview).Count == 0,
            "Unknown legal facts produce an unavailable forecast rather than a partial or guessed score");
        var missingPreview = Preview(new WarScoreFiefSnapshotRecord[0], "NotEstablished");
        check(!Reliable(missingPreview) && Impacts(missingPreview).Count == 0,
            "A missing ownership snapshot prevents an unreliable territorial forecast");
        var snapshotType = assembly.GetType("BellumCivile.DefensivePactTreatySnapshot", true);
        bool Relevant(TreatyTermRecord term) => (bool)AccessTools.Method(snapshotType, "IsRelevant").Invoke(null, new object[] { term });
        check(Relevant(castle) && !Relevant(null) && !Relevant(new TreatyTermRecord(TreatyTermType.WhitePeace, 0))
            && !Relevant(new TreatyTermRecord(TreatyTermType.Reparations, 10, fromKingdomId: "battania", toKingdomId: "vlandia"))
            && !Relevant(new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 10, fromKingdomId: "battania", toKingdomId: "vlandia", wasVoluntaryOffering: true)),
            "Only threat-relevant treaty terms require a settlement journal");
        var capture = AccessTools.Method(snapshotType, "Capture");
        var evaluateSnapshot = AccessTools.Method(snapshotType, "Evaluate");
        var originalSnapshot = new WarScoreFiefSnapshotRecord("castle", "battania", "old", false, true);
        var snapshot = capture.Invoke(null, new object[] { draft, new[] { originalSnapshot }, sameSide, BasisDelegate("NotEstablished") });
        AccessTools.Method(typeof(WarScoreFiefSnapshotRecord), "RecordVoluntaryGrant")
            .Invoke(originalSnapshot, new object[] { "battania", "old", "vlandia", "new" });
        object Recorded(ISet<TreatyTermRecord> delivered) => evaluateSnapshot.Invoke(snapshot, new object[] { delivered });
        var noneDelivered = Recorded(new HashSet<TreatyTermRecord>());
        var castleDelivered = Recorded(new HashSet<TreatyTermRecord> { castle });
        check(Reliable(noneDelivered) && Impacts(noneDelivered).Count == 0
            && Reliable(castleDelivered) && Near(Project(Impacts(castleDelivered)["vlandia"], 30), 50),
            "A pre-settlement snapshot counts a concession only after its delivery receipt, even if ownership facts later change");
        var journalType = assembly.GetType("BellumCivile.DefensivePactTreatyJournal", true);
        object Prepare(DefensivePactThreatBehavior b, TreatyProposalRecord proposal, object captured, double day = 0,
            double winnerRate = 1, double loserRate = 1)
        {
            object result = AccessTools.Method(ownerType, "PrepareTreaty").Invoke(b, new object[] { proposal, captured, day });
            if (result != null) AccessTools.Method(result.GetType(), "CaptureRates").Invoke(result, new object[] { winnerRate, loserRate });
            return result;
        }
        bool Delivery(object journal, TreatyTermRecord term) => (bool)AccessTools.Method(journalType, "RecordDelivery")
            .Invoke(journal, new object[] { term });
        bool Seal(object journal, TreatyProposalRecord proposal) => (bool)AccessTools.Method(journalType, "Seal")
            .Invoke(journal, new object[] { proposal });
        object FinalImpact(object journal) => AccessTools.Method(journalType, "EvaluateDelivered").Invoke(journal, null);
        var journalOwner = new DefensivePactThreatBehavior();
        var journal = Prepare(journalOwner, draft, snapshot);
        check(journal != null && ReferenceEquals(journal, Prepare(journalOwner, draft, snapshot))
            && Delivery(journal, castle) && !Delivery(journal, castle),
            "A treaty gets one journal and each actual delivery is recorded once");
        check(!Seal(journal, draft) && FinalImpact(journal) == null,
            "An interrupted or merely pending treaty cannot produce final threat");
        var journalStore = new MemoryStore { IsSaving = true }; journalOwner.SyncData(journalStore);
        var resumedOwner = new DefensivePactThreatBehavior(); journalStore.IsSaving = false; resumedOwner.SyncData(journalStore);
        var resumedJournal = AccessTools.Method(ownerType, "FindTreaty").Invoke(resumedOwner, new object[] { draft.ProposalId });
        draft.SetState(TreatyProposalState.Applied, "test", 0);
        check(ReferenceEquals(resumedJournal, journal) && Seal(resumedJournal, draft)
            && !Seal(resumedJournal, draft) && !Delivery(resumedJournal, castle),
            "A resumed applied treaty seals its saved delivery evidence once");
        var recoveredImpact = FinalImpact(resumedJournal);
        check(Reliable(recoveredImpact) && Near(Project(Impacts(recoveredImpact)["vlandia"], 30), 50)
            && Near(Project(Impacts(recoveredImpact)["battania"], 40), 30),
            "The sealed journal reconstructs confirmed threat from pre-settlement facts");
        bool ApplyJournal(DefensivePactThreatBehavior b, object j)
            => (bool)AccessTools.Method(ownerType, "ApplySealedTreaty").Invoke(b, new object[] { j });
        Add(resumedOwner, "battania", 0, 1, 40);
        Add(resumedOwner, "vlandia", 0, 1, 30);
        check(Treaty(resumedOwner, draft.ProposalId, "battania", 0, 1, -10),
            "A treaty may be interrupted after applying only one realm");
        Add(resumedOwner, "battania", 7, 1, 0);
        check(ApplyJournal(resumedOwner, resumedJournal)
            && ApplyJournal(resumedOwner, resumedJournal)
            && Near(Get(resumedOwner, "battania", 7), 29) && Near(Get(resumedOwner, "vlandia", 0), 50),
            "A partially applied treaty resumes even if the already-receipted realm has advanced");
        var ratesJournal = AccessTools.Method(snapshotType, "CreateJournal").Invoke(snapshot, new object[] { "rate-validation", 0d });
        var captureRates = AccessTools.Method(journalType, "CaptureRates");
        check(!(bool)captureRates.Invoke(ratesJournal, new object[] { double.NaN, 1d })
            && (bool)captureRates.Invoke(ratesJournal, new object[] { .5d, 1d })
            && !(bool)captureRates.Invoke(ratesJournal, new object[] { 2d, 2d }),
            "Settlement rates must be finite, nonnegative, and captured only once");
        draft.SetState(TreatyProposalState.ParleyPending, "test");
        var otherFief = new TreatyTermRecord(TreatyTermType.TransferFief, 40, "missing_castle",
            fromKingdomId: "battania", toKingdomId: "vlandia");
        draft.AddTerm(otherFief);
        var mixedSnapshot = capture.Invoke(null, new object[] { draft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var draftMixed = evaluateSnapshot.Invoke(mixedSnapshot, new object[] { null });
        var deliveredOnly = evaluateSnapshot.Invoke(mixedSnapshot, new object[] { new HashSet<TreatyTermRecord> { castle } });
        check(!Reliable(draftMixed) && Reliable(deliveredOnly) && Impacts(deliveredOnly).Count == 2,
            "A failed or undelivered uncertain term cannot invalidate confirmed terms, while the full draft stays uncertain");
        var mixedOwner = new DefensivePactThreatBehavior();
        var mixedJournal = Prepare(mixedOwner, draft, mixedSnapshot);
        Delivery(mixedJournal, castle);
        draft.SetState(TreatyProposalState.Applied, "test", 0);
        var mixedImpact = Seal(mixedJournal, draft) ? FinalImpact(mixedJournal) : null;
        check(mixedImpact != null && Reliable(mixedImpact) && Impacts(mixedImpact).Count == 2,
            "An undelivered term with unknown facts does not spoil a journal's confirmed results");
        draft.SetState(TreatyProposalState.ParleyPending, "test");
        var restoredDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        var savedTerm = new TreatyTermRecord(TreatyTermType.TransferFief, 40, "castle", fromKingdomId: "battania", toKingdomId: "vlandia");
        restoredDraft.AddTerm(savedTerm);
        var savedSnapshot = capture.Invoke(null, new object[] { restoredDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var identityJournal = Prepare(new DefensivePactThreatBehavior(), restoredDraft, savedSnapshot);
        var equivalentTerm = new TreatyTermRecord(TreatyTermType.TransferFief, 40, "castle", fromKingdomId: "battania", toKingdomId: "vlandia");
        restoredDraft.ReplaceTerms(new[] { equivalentTerm });
        check(Delivery(identityJournal, equivalentTerm), "A uniquely matching restored term can confirm delivery without shared object identity");
        restoredDraft.SetState(TreatyProposalState.Applied, "test", 0);
        check(Seal(identityJournal, restoredDraft) && Reliable(FinalImpact(identityJournal)),
            "A reconstructed proposal can seal when its actual terms match the captured terms");
        var changedDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        changedDraft.AddTerm(savedTerm);
        var changedSnapshot = capture.Invoke(null, new object[] { changedDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var changedJournal = Prepare(new DefensivePactThreatBehavior(), changedDraft, changedSnapshot);
        changedDraft.ReplaceTerms(new[] { new TreatyTermRecord(TreatyTermType.TransferFief, 41, "castle", fromKingdomId: "battania", toKingdomId: "vlandia") });
        changedDraft.SetState(TreatyProposalState.Applied, "test", 0);
        check(!Seal(changedJournal, changedDraft), "Changed treaty terms cannot seal a journal captured for different terms");
        var recoveryDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        recoveryDraft.AddTerm(savedTerm);
        var recoverySnapshot = capture.Invoke(null, new object[] { recoveryDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var recoveryOwner = new DefensivePactThreatBehavior();
        var recoveryJournal = Prepare(recoveryOwner, recoveryDraft, recoverySnapshot, 7);
        Delivery(recoveryJournal, savedTerm);
        var reconcile = AccessTools.Method(ownerType, "ReconcileAppliedTreaties");
        int Reconcile() => (int)reconcile.Invoke(recoveryOwner, new object[] { new[] { recoveryDraft } });
        check(Reconcile() == 0 && FinalImpact(recoveryJournal) == null,
            "Load-time reconciliation leaves a treaty that was not applied unsealed");
        recoveryDraft.SetState(TreatyProposalState.Applied, "test", 7);
        check(Reconcile() == 1 && Reconcile() == 0
            && Near((double)AccessTools.Property(journalType, "SettlementDay").GetValue(recoveryJournal), 7)
            && Reliable(FinalImpact(recoveryJournal)),
            "Load-time reconciliation seals an applied treaty once using its original settlement day");
        var chronologicalOwner = new DefensivePactThreatBehavior();
        Add(chronologicalOwner, "vlandia", 0, 1, 30); Add(chronologicalOwner, "battania", 0, 1, 40);
        object JournalAt(float day)
        {
            var proposal = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
            proposal.AddTerm(savedTerm);
            var captured = capture.Invoke(null, new object[] { proposal, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
            var entry = Prepare(chronologicalOwner, proposal, captured, day);
            Delivery(entry, savedTerm);
            proposal.SetState(TreatyProposalState.Applied, "test", day);
            if (!Seal(entry, proposal)) throw new InvalidOperationException("Could not seal chronological fixture");
            return entry;
        }
        JournalAt(14); JournalAt(7);
        var process = AccessTools.Method(ownerType, "ProcessSealedTreaties");
        check((bool)process.Invoke(chronologicalOwner, null) && (bool)process.Invoke(chronologicalOwner, null)
            && Near(Get(chronologicalOwner, "vlandia", 14), 68) && Near(Get(chronologicalOwner, "battania", 14), 18)
            && ((ICollection)AccessTools.Field(ownerType, "_treatyJournals").GetValue(chronologicalOwner)).Count == 0,
            "Pending treaties recover in order, retire detailed journals and remain idempotent");
        const double preciseDay = 1000.123489;
        var preciseDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        preciseDraft.AddTerm(savedTerm);
        var preciseSnapshot = capture.Invoke(null, new object[] { preciseDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var preciseOwner = new DefensivePactThreatBehavior();
        var preciseJournal = Prepare(preciseOwner, preciseDraft, preciseSnapshot, preciseDay, 1, .5);
        Add(preciseOwner, "battania", preciseDay, 1, 40);
        Add(preciseOwner, "vlandia", preciseDay, 1, 30);
        AccessTools.Method(ownerType, "ChangeRate").Invoke(preciseOwner, new object[] { "battania", preciseDay, .5d });
        Delivery(preciseJournal, savedTerm);
        preciseDraft.SetState(TreatyProposalState.Applied, "test", (float)preciseDay);
        check(Seal(preciseJournal, preciseDraft)
            && (double)AccessTools.Property(journalType, "SettlementDay").GetValue(preciseJournal) == preciseDay
            && ApplyJournal(preciseOwner, preciseJournal)
            && Near(Get(preciseOwner, "battania", preciseDay), 30) && Near(Get(preciseOwner, "vlandia", preciseDay), 50),
            "A settlement uses its full-precision time even when a fief-rate change occurred at the same instant");
        var wrongDayDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        wrongDayDraft.AddTerm(savedTerm);
        var wrongDaySnapshot = capture.Invoke(null, new object[] { wrongDayDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var wrongDayJournal = Prepare(new DefensivePactThreatBehavior(), wrongDayDraft, wrongDaySnapshot, 7);
        wrongDayDraft.SetState(TreatyProposalState.Applied, "test", 8);
        check(!Seal(wrongDayJournal, wrongDayDraft), "A treaty applied on a different day cannot use stale pre-settlement facts");
        var invalidDraft = new TreatyProposalRecord("war", "vlandia", "battania", "vlandia", 0, 100, false);
        invalidDraft.AddTerm(savedTerm);
        var invalidSnapshot = capture.Invoke(null, new object[] { invalidDraft, new[] { battanianCastle }, sameSide, BasisDelegate("NotEstablished") });
        var invalidJournal = Prepare(new DefensivePactThreatBehavior(), invalidDraft, invalidSnapshot);
        var unexpectedTerm = new TreatyTermRecord(TreatyTermType.TransferFief, 40, "unknown", fromKingdomId: "battania", toKingdomId: "vlandia");
        check(!Delivery(invalidJournal, unexpectedTerm) && !Delivery(invalidJournal, savedTerm),
            "An unrecognized delivered term invalidates the journal rather than losing evidence silently");
        invalidDraft.SetState(TreatyProposalState.Applied, "test", 0);
        check(!Seal(invalidJournal, invalidDraft) && FinalImpact(invalidJournal) == null,
            "An invalid journal cannot publish a partial threat result");

        var eligibility = assembly.GetType("BellumCivile.DefensivePactEligibilityRules", true);
        int Threshold(int distance) => (int)AccessTools.Method(eligibility, "JoiningThreshold").Invoke(null, new object[] { distance });
        bool Join(double threat, int tier, int fiefs, double strength, double target, int relation,
            bool peace = true, bool member = false, bool ally = false, bool pact = false, bool client = false, bool cooldown = false)
            => (bool)AccessTools.Method(eligibility, "CanJoin").Invoke(null,
                new object[] { threat, tier, fiefs, strength, target, relation, peace, member, ally, pact, client, cooldown });
        bool Leave(double threat, int tier, int relation, bool ally = false, bool pact = false, bool client = false)
            => (bool)AccessTools.Method(eligibility, "MustLeave").Invoke(null,
                new object[] { threat, tier, relation, ally, pact, client });
        check(Threshold(1) == 25 && Threshold(2) == 50 && Threshold(3) == 75
            && Threshold(4) == 95 && Threshold(0) == 95, "Frontier tiers unlock reach at 25, 50, 75 and 95 threat");
        check(Join(50, 50, 24, 79, 100, -1) && !Join(49.99, 50, 24, 79, 100, -1)
            && !Join(50, 50, 25, 79, 100, -1) && !Join(50, 50, 24, 80, 100, -1)
            && !Join(50, 50, 24, 79, 100, 0), "Joining honors strict fief, strength and relationship boundaries");
        check(!Join(50, 50, 1, 1, 100, -1, peace: false)
            && !Join(50, 50, 1, 1, 100, -1, member: true)
            && !Join(50, 50, 1, 1, 100, -1, ally: true)
            && !Join(50, 50, 1, 1, 100, -1, pact: true)
            && !Join(50, 50, 1, 1, 100, -1, client: true)
            && !Join(50, 50, 1, 1, 100, -1, cooldown: true), "War, existing membership, protected ties and cooldown block joining");
        check(!Leave(25, 50, 59) && Leave(24.99, 50, 59) && Leave(50, 50, 60)
            && Leave(50, 50, 0, pact: true), "Members leave below half their joining tier, at +60 relations or on a protected tie");
        bool AutomaticLeave(double threat, int tier, int relation, bool player, bool ally = false, bool pledge = false, bool client = false)
            => (bool)AccessTools.Method(eligibility, "MustLeaveWithoutVote").Invoke(null,
                new object[] { threat, tier, relation, ally, pledge, client, player });
        check(AutomaticLeave(10, 50, 0, false) && AutomaticLeave(50, 50, 60, false)
            && !AutomaticLeave(10, 50, 0, true) && !AutomaticLeave(50, 50, 60, true)
            && AutomaticLeave(50, 50, 0, true, ally: true) && AutomaticLeave(50, 50, 0, true, pledge: true)
            && AutomaticLeave(50, 50, 0, true, client: true),
            "Player realms vote on reduced threat or improved relations, while contradictory protected ties end membership");
        var graph = new Dictionary<string, string[]> {
            ["target"] = new[] { "a", "b" }, ["a"] = new[] { "target", "b", "c" },
            ["b"] = new[] { "target", "a", "c" }, ["c"] = new[] { "a", "b", "d" },
            ["d"] = new[] { "c", "e" }, ["e"] = new[] { "d" }
        };
        int lookups = 0;
        Func<string, IEnumerable<string>> neighbours = id => { lookups++; return graph.TryGetValue(id, out var edges) ? edges : null; };
        var geography = assembly.GetType("BellumCivile.DefensivePactGeography", true);
        var reach = (IReadOnlyDictionary<string, int>)AccessTools.Method(geography, "ReachWithinThree")
            .Invoke(null, new object[] { "target", neighbours });
        check(reach["a"] == 1 && reach["b"] == 1 && reach["c"] == 2 && reach["d"] == 3
            && !reach.ContainsKey("e") && !reach.ContainsKey("target"),
            "Frontier reach uses shortest paths through three degrees without treating the target as a member");
        check(lookups == 4, "Bounded frontier traversal does not expand third-degree or disconnected realms");
        var cacheType = assembly.GetType("BellumCivile.DefensivePactFrontierCache", true);
        object NewFrontier(params string[] ids) => Activator.CreateInstance(cacheType,
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { ids }, null);
        int FrontierStep(object cache, int budget, Func<string, string, bool?> neighborQuery)
            => (int)AccessTools.Method(cacheType, "Step").Invoke(cache, new object[] { budget, neighborQuery });
        bool FrontierDistance(object cache, string first, string second, out int distance)
        {
            object[] args = { first, second, 0 };
            bool result = (bool)AccessTools.Method(cacheType, "TryGetDistance").Invoke(cache, args);
            distance = (int)args[2];
            return result;
        }
        int Pending(object cache) => (int)AccessTools.Property(cacheType, "PendingCount").GetValue(cache);
        void Invalidate(object cache, string id) => AccessTools.Method(cacheType, "Invalidate").Invoke(cache, new object[] { id });
        object frontier = NewFrontier("a", "b", "c", "d", "e");
        bool bcConnected = true;
        int pairCalls = 0;
        Func<string, string, bool?> pairQuery = (first, second) =>
        {
            pairCalls++;
            return first == "a" && second == "b" || first == "b" && second == "c" && bcConnected
                || first == "c" && second == "d";
        };
        check(Pending(frontier) == 10 && FrontierStep(frontier, 2, pairQuery) == 2 && pairCalls == 2
            && FrontierDistance(frontier, "a", "b", out int direct) && direct == 1
            && !FrontierDistance(frontier, "a", "c", out _),
            "Frontier work respects its daily budget and reports incomplete routes as unknown");
        check(FrontierStep(frontier, 100, pairQuery) == 8 && Pending(frontier) == 0
            && FrontierDistance(frontier, "a", "d", out int thirdHop) && thirdHop == 3
            && FrontierDistance(frontier, "a", "e", out int disconnected) && disconnected == 0,
            "A completed frontier cache distinguishes three-hop and disconnected realms");
        bcConnected = false; Invalidate(frontier, "b");
        check(Pending(frontier) == 4 && !FrontierDistance(frontier, "a", "d", out _)
            && FrontierStep(frontier, 100, pairQuery) == 4
            && FrontierDistance(frontier, "a", "d", out int afterChange) && afterChange == 0,
            "A changed realm invalidates only its own pairs and never returns stale reach");
        Invalidate(frontier, "a");
        check(FrontierStep(frontier, 100, (first, second) => null) == 4 && Pending(frontier) == 4
            && !FrontierDistance(frontier, "a", "b", out _),
            "Unavailable geography is retried on a later pass without guessing a distance");
        object reentrantFrontier = NewFrontier("x", "y");
        FrontierStep(reentrantFrontier, 1, (first, second) => { Invalidate(reentrantFrontier, "x"); return true; });
        check(Pending(reentrantFrontier) == 1 && !FrontierDistance(reentrantFrontier, "x", "y", out _),
            "A frontier change during a calculation discards the stale result");
        object prioritizedFrontier = NewFrontier("a", "b", "c", "d", "e");
        AccessTools.Method(cacheType, "Prioritize").Invoke(prioritizedFrontier, new object[] { "d", "e" });
        bool requestedFirst = true, directFirst = false;
        int priorityCalls = 0;
        check(FrontierStep(prioritizedFrontier, 2, (first, second) =>
            { if (priorityCalls++ == 0) directFirst = first == "d" && second == "e";
                requestedFirst &= first == "d" || second == "d" || first == "e" || second == "e"; return false; }) == 2
            && directFirst && requestedFirst && Pending(prioritizedFrontier) == 8,
            "A requested realm pair moves its frontier work forward without exceeding the daily budget");
        var pairIndex = new int[4, 4];
        int pairNumber = 0;
        for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) pairIndex[i, j] = pairIndex[j, i] = pairNumber++;
        bool exhaustiveFrontiersValid = true;
        for (int stateCode = 0; stateCode < 729 && exhaustiveFrontiersValid; stateCode++)
        {
            int value = stateCode, unknown = 0;
            var states = new int[6];
            var unknownBits = new int[6];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = value % 3; value /= 3;
                if (states[i] == 0) unknownBits[i] = 1 << unknown++;
            }
            object partial = NewFrontier("a", "b", "c", "d");
            FrontierStep(partial, 6, (first, second) =>
            {
                int state = states[pairIndex[first[0] - 'a', second[0] - 'a']];
                return state == 0 ? (bool?)null : state == 2;
            });
            var possibleDistances = new HashSet<int>();
            for (int mask = 0; mask < (1 << unknown); mask++)
            {
                var queue = new Queue<(int Node, int Depth)>();
                var visited = new bool[4]; visited[0] = true; queue.Enqueue((0, 0));
                int actual = 0;
                while (queue.Count > 0 && actual == 0)
                {
                    var item = queue.Dequeue();
                    if (item.Depth >= 3) continue;
                    for (int next = 0; next < 4; next++)
                    {
                        if (next == item.Node || visited[next]) continue;
                        int edge = pairIndex[item.Node, next], state = states[edge];
                        if (state != 2 && !(state == 0 && (mask & unknownBits[edge]) != 0)) continue;
                        if (next == 3) { actual = item.Depth + 1; break; }
                        visited[next] = true; queue.Enqueue((next, item.Depth + 1));
                    }
                }
                possibleDistances.Add(actual);
            }
            bool resolved = FrontierDistance(partial, "a", "d", out int reported);
            exhaustiveFrontiersValid = resolved == (possibleDistances.Count == 1)
                && (!resolved || possibleDistances.Contains(reported));
        }
        check(exhaustiveFrontiersValid, "Partial frontier answers agree with every possible completion of four-realm maps");
        var aiRules = assembly.GetType("BellumCivile.DefensivePactAiRules", true);
        double Chance(double realmRatio, double pactRatio) => (double)AccessTools.Method(aiRules, "JoinChance")
            .Invoke(null, new object[] { realmRatio, pactRatio });
        check(Chance(.2, .1) > Chance(.6, .1) && Chance(.2, .1) > Chance(.2, .7)
            && Chance(.2, .7) == Chance(.2, 2), "Weaker eligible realms and understrength pacts receive modest joining bonuses");
        check(Chance(.1, 0) < .05 && Chance(.79, 1) >= .02 && Chance(.8, 0) == 0
            && Chance(double.NaN, 0) == 0, "Provisional monthly joining odds are bounded and invalid inputs are rejected");
        var riskRules = assembly.GetType("BellumCivile.DefensivePactWarRisk", true);
        float TargetPenalty(float source, float target, float extra) => (float)AccessTools.Method(riskRules, "AdditionalTargetPenalty")
            .Invoke(null, new object[] { source, target, extra });
        float CouncilPenalty(float value, float applied = 0) => (float)AccessTools.Method(riskRules, "CouncilPenalty").Invoke(null, new object[] { value, applied });
        float Applied(float score, float penalty) => (float)AccessTools.Method(riskRules, "AppliedToClampedScore").Invoke(null, new object[] { score, penalty });
        float PowerScore(float source, float target) => (float)AccessTools.Method(riskRules, "PowerScore").Invoke(null, new object[] { source, target });
        bool baselineUnchanged = true, monotonicRisk = true;
        foreach (float source in new[] { 1f, 50f, 100f, 125f, 400f, 1000f })
            foreach (float target in new[] { 1f, 50f, 100f, 400f, 1000f })
            {
                float ratio = source / target;
                float expected = ratio >= 1.25f ? Math.Min(35f, (ratio - 1f) * 30f)
                    : ratio <= .85f ? -Math.Min(35f, (1f / Math.Max(.01f, ratio) - 1f) * 30f) : 0f;
                baselineUnchanged &= Math.Abs(PowerScore(source, target) - expected) < .0001f;
                float previous = 0;
                foreach (float extra in new[] { 0f, 25f, 100f, 500f, 5000f })
                {
                    float penalty = TargetPenalty(source, target, extra);
                    monotonicRisk &= penalty + .0001f >= previous && penalty <= 70.0001f;
                    previous = penalty;
                }
            }
        check(baselineUnchanged && monotonicRisk,
            "Ordinary power scores are unchanged and greater defensive strength never reduces risk");
        check(Near(TargetPenalty(100, 100, 0), 0) && Near(TargetPenalty(100, 100, 100), 30)
            && Near(TargetPenalty(100, 100, 400), 35) && Near(TargetPenalty(400, 100, 100), 5),
            "Defensive strength uses the existing capped power score and scales with the balance of power");
        check(Near(CouncilPenalty(TargetPenalty(100, 100, 100)), 10.5)
            && Near(CouncilPenalty(0), 0) && Near(TargetPenalty(float.NaN, 100, 100), 0)
            && Near(TargetPenalty(100, 100, float.PositiveInfinity), 0),
            "Council deterrence is bounded and invalid strength cannot influence a vote");
        check(Near(Applied(60, 30), 30) && Near(CouncilPenalty(30, Applied(60, 30)), 0)
            && Near(Applied(-10, 30), 20) && Near(CouncilPenalty(30, Applied(-10, 30)), 3.5)
            && Near(Applied(110, 30), 0) && Near(CouncilPenalty(30, Applied(110, 30)), 10.5),
            "Council risk fills only the penalty hidden by target-score caps, avoiding double counting");
        var voteRules = assembly.GetType("BellumCivile.DefensivePactVoteRules", true);
        float VoteSupport(double threat, int tier, int relation) => (float)AccessTools.Method(voteRules, "Support")
            .Invoke(null, new object[] { threat, tier, relation });
        check(Near(VoteSupport(25, 25, -10), 52.5) && VoteSupport(75, 25, -10) > VoteSupport(25, 25, -10)
            && VoteSupport(50, 25, -50) > VoteSupport(50, 25, 50)
            && VoteSupport(double.NaN, 25, 0) == 0 && VoteSupport(100, 25, -100) <= 100,
            "Ballot support rises with excess threat and hostility while remaining bounded");
        float LeaveSupport(double threat, int tier, int relation) => (float)AccessTools.Method(voteRules, "LeaveSupport")
            .Invoke(null, new object[] { threat, tier, relation });
        check(LeaveSupport(10, 50, 0) > LeaveSupport(50, 50, 0)
            && LeaveSupport(50, 50, 50) > LeaveSupport(50, 50, -50)
            && LeaveSupport(100, 25, -100) == 0 && LeaveSupport(0, 95, 100) == 100
            && LeaveSupport(double.NaN, 50, 0) == 0,
            "Leaving support grows as threat falls or personal relations improve and remains bounded");
        var vote = (DefensivePactJoinDecision)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DefensivePactJoinDecision));
        check(vote.GetProposalInfluenceCost() == 100 && vote.DetermineInitialCandidates().Count() == 2,
            "The native defensive-pact ballot has two outcomes and costs 100 influence");

        var pactType = assembly.GetType("BellumCivile.DefensivePactRecord", true);
        var callType = assembly.GetType("BellumCivile.DefensivePactCallRecord", true);
        object pact = Activator.CreateInstance(pactType, BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { "target" }, null);
        bool PactCall(string name, params object[] values) => (bool)AccessTools.Method(pactType, name).Invoke(pact, values);
        object PactProp(string name) => AccessTools.Property(pactType, name).GetValue(pact);
        check(PactCall("TryJoin", "first", 25) && PactCall("TryJoin", "second", 50)
            && PactCall("TryJoin", "third", 75) && !PactCall("TryJoin", "first", 25)
            && !PactCall("TryJoin", "target", 25), "Pact membership stores distinct realms and excludes the target");
        check(!PactCall("TryActivate", "outsider", 20d) && PactCall("TryActivate", "first", 20d)
            && !PactCall("TryActivate", "first", 20d) && !PactCall("TryJoin", "late", 25),
            "Only an attacked member activates the pact, once, before any late join");
        var calls = (System.Collections.IList)PactProp("Calls");
        check(calls.Count == 2 && (string)PactProp("AttackedMemberId") == "first"
            && Near((double)PactProp("CooldownUntil"), 120)
            && !(bool)PactProp("AllCallsCompleted"), "Activation freezes the remaining members and starts the 100-day cooldown");
        check(PactCall("IsCooldownActive", 119.99d) && !PactCall("IsCooldownActive", 120d),
            "The reform cooldown expires exactly 100 days after activation");
        bool Call(object receipt, string method) => (bool)AccessTools.Method(callType, method).Invoke(receipt, null);
        check(!Call(calls[0], "TryComplete") && Call(calls[0], "TryStart") && !Call(calls[0], "TryStart")
            && Call(calls[0], "TryComplete") && !Call(calls[0], "TryComplete")
            && !(bool)PactProp("AllCallsCompleted"), "Call receipts record an attempt before completion and cannot replay it");
        check(Call(calls[1], "TryStart") && Call(calls[1], "TryComplete") && (bool)PactProp("AllCallsCompleted"),
            "Activation completes only after every snapshotted obligation is resolved");
        object obsolete = Activator.CreateInstance(callType, BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { "eliminated" }, null);
        check(Call(obsolete, "TrySkip") && !Call(obsolete, "TryStart") && !Call(obsolete, "TrySkip"),
            "An impossible call can be retired once without declaring a war");

        var execution = assembly.GetType("BellumCivile.DefensivePactCallExecution", true);
        var step = AccessTools.Method(execution, "Step");
        (object Pact, object Call) NewCall()
        {
            var p = Activator.CreateInstance(pactType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "target" }, null);
            AccessTools.Method(pactType, "TryJoin").Invoke(p, new object[] { "victim", 25 });
            AccessTools.Method(pactType, "TryJoin").Invoke(p, new object[] { "defender", 50 });
            AccessTools.Method(pactType, "TryActivate").Invoke(p, new object[] { "victim", 20d });
            return (p, ((IList)AccessTools.Property(pactType, "Calls").GetValue(p))[0]);
        }
        bool available = true, atWar = false, mayDeclare = true;
        int declarations = 0;
        Func<string, string, bool> availability = (target, member) => available;
        Func<string, string, bool> war = (target, member) => atWar;
        Func<string, string, bool> permission = (target, member) => mayDeclare;
        Action<string, string> declare = (target, member) => { declarations++; atWar = true; };
        void Execute((object Pact, object Call) entry, Action<string, string> action)
            => step.Invoke(null, new object[] { entry.Pact, entry.Call, availability, war, permission, action });
        string Outcome(object call) => AccessTools.Property(callType, "Outcome").GetValue(call).ToString();
        var normal = NewCall(); Execute(normal, declare); Execute(normal, declare);
        check(declarations == 1 && Outcome(normal.Call) == "WarConfirmed", "A defensive call declares and confirms one separate war once");
        var already = NewCall(); Execute(already, declare);
        check(declarations == 1 && Outcome(already.Call) == "WarConfirmed", "An existing war satisfies the call without a second declaration");
        atWar = false; available = false;
        var missing = NewCall(); Execute(missing, declare);
        check(declarations == 1 && Outcome(missing.Call) == "Unavailable", "An unavailable member is skipped without changing wars");
        available = true; mayDeclare = false;
        var prohibited = NewCall(); Execute(prohibited, declare);
        check(declarations == 1 && Outcome(prohibited.Call) == "Unavailable", "A protected diplomatic relationship prevents an unsafe call");
        mayDeclare = true;
        var h = new Harmony("bellum.test.defensive_pact_call");
        try
        {
            h.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), prefix: new HarmonyMethod(typeof(DefensivePactThreatTests), nameof(SkipLog)));
            var interrupted = NewCall();
            Execute(interrupted, (target, member) => { declarations++; atWar = true; throw new InvalidOperationException(); });
            check(Outcome(interrupted.Call) == "Pending", "An interrupted declaration remains pending for verification");
            Execute(interrupted, declare);
            check(declarations == 2 && Outcome(interrupted.Call) == "WarConfirmed", "Recovery recognizes a war created before an interrupted callback without redeclaring");
            atWar = false;
            var failed = NewCall();
            Execute(failed, (target, member) => { declarations++; throw new InvalidOperationException(); });
            Execute(failed, declare);
            check(declarations == 3 && Outcome(failed.Call) == "DeclarationFailed", "An interrupted attempt that left no war is never replayed");
        }
        finally { h.UnpatchAll(h.Id); }

        var notice = NewCall();
        bool QueueNotice(object entry, string realm) => (bool)AccessTools.Method(pactType, "QueueNotice").Invoke(entry, new object[] { realm });
        bool NoticeFlag(object entry, string name) => (bool)AccessTools.Property(pactType, name).GetValue(entry);
        check(!QueueNotice(notice.Pact, "outsider") && QueueNotice(notice.Pact, "defender")
            && NoticeFlag(notice.Pact, "HasPendingNotice") && !NoticeFlag(notice.Pact, "IsNoticeReady"),
            "A defending player's notice waits until its separate-war call has a result");
        Call(notice.Call, "TryAlreadyAtWar");
        check(NoticeFlag(notice.Pact, "IsNoticeReady") && (bool)AccessTools.Method(pactType, "AcknowledgeNotice").Invoke(notice.Pact, null)
            && !NoticeFlag(notice.Pact, "HasPendingNotice") && !(bool)AccessTools.Method(pactType, "AcknowledgeNotice").Invoke(notice.Pact, null),
            "A confirmed call enables one acknowledgement of the saved notice");
        var victimNotice = NewCall();
        check(QueueNotice(victimNotice.Pact, "victim") && NoticeFlag(victimNotice.Pact, "IsNoticeReady"),
            "An attacked player realm can be notified immediately");

        var pactBehavior = new DefensivePactBehavior();
        var pactBehaviorType = typeof(DefensivePactBehavior);
        object[] invalidProposal = { null, null, null };
        check(!(bool)AccessTools.Method(pactBehaviorType, "CanProposeJoin").Invoke(pactBehavior, invalidProposal)
            && invalidProposal[2] != null, "An invalid player proposal is blocked with an explanation");
        var noticeText = AccessTools.Method(pactBehaviorType, "BuildNotice");
        string Notice(bool victim, bool confirmed, bool ongoing) => noticeText.Invoke(null,
            new object[] { new TaleWorlds.Localization.TextObject("Aggressor"), new TaleWorlds.Localization.TextObject("Signatory"), victim, confirmed, ongoing }).ToString();
        check(Notice(false, false, false).Contains("could not enter") && Notice(false, true, true).Contains("has taken up arms")
            && Notice(false, true, false).Contains("has since ended") && Notice(true, true, true).Contains("attacked our realm"),
            "Call-to-arms messages distinguish a failed call, a current war, a finished war and a direct attack");
        bool JoinPact(DefensivePactBehavior behavior, string member, string target, double day, bool peace = true)
            => (bool)AccessTools.Method(pactBehaviorType, "TryJoin").Invoke(behavior,
                new object[] { member, target, day, 75d, 50, 2, 40d, 100d, -10, peace, false, false, false });
        bool ActivatePact(DefensivePactBehavior behavior, string target, string victim, double day)
            => (bool)AccessTools.Method(pactBehaviorType, "Activate").Invoke(behavior, new object[] { target, victim, day });
        check(JoinPact(pactBehavior, "victim", "target", 0) && JoinPact(pactBehavior, "defender", "target", 0)
            && !JoinPact(pactBehavior, "defender", "other_target", 0)
            && !JoinPact(pactBehavior, "wartime", "target", 0, false),
            "The saved registry enforces one active membership and peacetime entry");
        check((string)AccessTools.Method(pactBehaviorType, "GetMembershipTarget").Invoke(pactBehavior, new object[] { "victim" }) == "target"
            && AccessTools.Method(pactBehaviorType, "GetMembershipTarget").Invoke(pactBehavior, new object[] { "outsider" }) == null,
            "The diplomacy overview can identify a member's target without scanning all pacts");
        var membershipQueue = (System.Collections.ICollection)AccessTools.Field(pactBehaviorType, "_membershipChecks").GetValue(pactBehavior);
        var leavePact = AccessTools.Method(pactBehaviorType, "TryLeave");
        check(membershipQueue.Count == 2 && (bool)leavePact.Invoke(pactBehavior, new object[] { "defender" })
            && JoinPact(pactBehavior, "defender", "target", 0) && membershipQueue.Count == 2,
            "Membership checks are queued once even when a realm leaves and rejoins before its turn");
        var activeStore = new MemoryStore { IsSaving = true }; pactBehavior.SyncData(activeStore);
        var activeReload = new DefensivePactBehavior(); activeStore.IsSaving = false; activeReload.SyncData(activeStore);
        check(((System.Collections.ICollection)AccessTools.Field(pactBehaviorType, "_membershipChecks").GetValue(activeReload)).Count == 2,
            "Loading active pacts rebuilds the bounded membership-check queue");
        check(!ActivatePact(pactBehavior, "target", "outsider", 20) && ActivatePact(pactBehavior, "target", "victim", 20)
            && !ActivatePact(pactBehavior, "target", "victim", 20),
            "An attack activates only the target's pact and removes it from active membership");
        var pactStore = new MemoryStore { IsSaving = true }; pactBehavior.SyncData(pactStore);
        var loadedPacts = new DefensivePactBehavior(); pactStore.IsSaving = false; loadedPacts.SyncData(pactStore);
        check((bool)AccessTools.Field(pactBehaviorType, "_hasPendingCalls").GetValue(loadedPacts)
            && !JoinPact(loadedPacts, "new_member", "target", 119.99)
            && JoinPact(loadedPacts, "new_member", "target", 120)
            && JoinPact(loadedPacts, "defender", "different_target", 120),
            "Reload restores pending calls and the target cooldown while former members can join a different pact");
        AccessTools.Method(pactBehaviorType, "RemoveRealm").Invoke(loadedPacts, new object[] { "target" });
        check(JoinPact(loadedPacts, "new_member", "another_target", 120),
            "Destroying a target retires its active pact and releases its members");
        var initiating = AccessTools.Method(pactBehaviorType, "IsInitiatingAttack");
        bool Attack(DeclareWarAction.DeclareWarDetail detail, bool calling = false, bool sync = false, bool hostile = true)
            => (bool)initiating.Invoke(null, new object[] { detail, calling, sync, hostile });
        check(Attack(DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision)
            && Attack(DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility)
            && !Attack(DeclareWarAction.DeclareWarDetail.Default)
            && !Attack(DeclareWarAction.DeclareWarDetail.CausedByCallToWarAgreement)
            && !Attack(DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision, sync: true)
            && !Attack(DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility, calling: true)
            && !Attack(DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision, hostile: false),
            "Only a confirmed deliberate attack activates a pact, excluding automatic calls and client synchronization");
    }
}
