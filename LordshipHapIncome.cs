using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal static class LordshipHapIncome
    {
        private const string Owner = "com.bellumcivile.lordship.hap";
        private delegate bool CollectIncome(Clan clan, ref ExplainedNumber income, bool apply, bool details);
        private static MethodInfo _hapMethod;
        private static CollectIncome _collect;
        private static Func<Clan, float> _rate;
        private static bool _previewFailureLogged;
        [ThreadStatic] private static Collection _current;
        [ThreadStatic] private static PreviewRequest _preview;

        internal sealed class Collection
        {
            internal readonly Dictionary<Village, Share> Shares = new Dictionary<Village, Share>();
            internal Collection Previous;
            internal Clan Clan;
            internal bool Apply, Completed;
            internal float Rate;
            internal Village PendingVillage;
            internal int PendingIncome;

            internal int Gross(Village village) => Completed && Shares.TryGetValue(village, out var share) ? share.Gross : 0;
            internal int Net(Village village, bool contested) => Completed && Shares.TryGetValue(village, out var share)
                ? Math.Max(0, LordshipIncome.Net(share.Gross, contested) - share.Tax) : 0;
        }

        internal struct Share { internal int Gross, Tax; }
        private sealed class PreviewRequest { internal Clan Clan; internal Collection Collection; }

        private static bool Active => _collect != null && Harmony.GetPatchInfo(
            AccessTools.Method(typeof(DefaultClanFinanceModel), "AddSettlementIncome"))?.Prefixes.Any(p => p.PatchMethod == _hapMethod) == true;

        internal static void Initialize()
        {
            if (_collect != null) return;
            var type = AccessTools.TypeByName("HarvestAndProduction.Patches.ClanFinanceModelPatch");
            if (type == null || type.Assembly.GetName().Name != "HarvestAndProduction") return;
            var harmony = new Harmony(Owner);
            try
            {
                _hapMethod = AccessTools.DeclaredMethod(type, "AddSettlementIncome_Prefix",
                    new[] { typeof(Clan), typeof(ExplainedNumber).MakeByRefType(), typeof(bool), typeof(bool) });
                var rate = AccessTools.DeclaredMethod(type, "CalculateTaxInefficiencyRate", new[] { typeof(Clan) });
                if (_hapMethod == null || rate == null) throw new MissingMethodException("HAP settlement income contract changed.");
                if (Harmony.GetPatchInfo(AccessTools.Method(typeof(DefaultClanFinanceModel), "AddSettlementIncome"))?
                    .Prefixes.Any(p => p.PatchMethod == _hapMethod) != true) return;
                _rate = (Func<Clan, float>)Delegate.CreateDelegate(typeof(Func<Clan, float>), rate);
                harmony.Patch(_hapMethod, prefix: new HarmonyMethod(typeof(LordshipHapIncome), nameof(Begin)),
                    transpiler: new HarmonyMethod(typeof(LordshipHapIncome), nameof(Capture)),
                    finalizer: new HarmonyMethod(typeof(LordshipHapIncome), nameof(Finish)));
                _collect = (CollectIncome)Delegate.CreateDelegate(typeof(CollectIncome), _hapMethod);
                BellumCivileLogger.Log("HAP lordship income compatibility enabled.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(Owner);
                _collect = null; _rate = null;
                BellumCivileLogger.Log("HAP lordship income compatibility is unavailable: " + ex.GetBaseException().Message);
            }
        }

        internal static Collection Preview(Clan clan)
        {
            if (clan == null || !Active) return null;
            var previous = _preview;
            var request = new PreviewRequest { Clan = clan };
            _preview = request;
            try
            {
                var income = new ExplainedNumber(0f);
                _collect(clan, ref income, false, false);
                return request.Collection ?? new Collection();
            }
            catch (Exception ex)
            {
                if (!_previewFailureLogged)
                {
                    _previewFailureLogged = true;
                    BellumCivileLogger.Log("HAP lordship income preview failed safely: " + ex.GetBaseException().Message);
                }
                return new Collection();
            }
            finally { _preview = previous; }
        }

        private static void Begin(Clan clan, bool applyWithdrawals, out Collection __state)
        {
            __state = new Collection { Previous = _current, Clan = clan, Apply = applyWithdrawals };
            _current = __state;
            if (_preview?.Clan == clan) _preview.Collection = __state;
            else LordshipIncome.Current(clan)?.BeginHapCollection(__state);
        }

        private static void Finish(Collection __state)
        {
            if (__state != null) _current = __state.Previous;
        }

        private static float TaxRate(Clan clan)
        {
            float rate = _rate(clan);
            if (_current?.Clan == clan) _current.Rate = rate;
            return rate;
        }

        private static int VillageIncome(ClanFinanceModel model, Clan clan, Village village, bool apply)
        {
            int income = model.CalculateVillageIncome(clan, village, apply);
            if (_current?.Clan == clan)
            {
                _current.PendingVillage = village;
                _current.PendingIncome = income;
            }
            return income;
        }

        private static int FiefTotal(int total)
        {
            var collection = _current;
            if (collection?.PendingVillage != null)
            {
                // Marginal shares telescope to HAP's existing per-fief deduction, including its rounding.
                int Tax(int taxable) => taxable > 0 && collection.Rate > 0f ? (int)(taxable * collection.Rate) : 0;
                collection.Shares[collection.PendingVillage] = new Share { Gross = collection.PendingIncome,
                    Tax = Math.Max(0, Tax(total) - Tax(total - collection.PendingIncome)) };
                collection.PendingVillage = null;
            }
            return total;
        }

        private static void Commit()
        {
            if (_current != null && _current.PendingVillage == null && !float.IsNaN(_current.Rate) && !float.IsInfinity(_current.Rate))
                _current.Completed = true;
        }

        private static IEnumerable<CodeInstruction> Capture(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var village = AccessTools.Method(typeof(ClanFinanceModel), nameof(ClanFinanceModel.CalculateVillageIncome));
            int rates = 0, villages = 0, commits = 0, rateLocal = -1, totalLocal = -1;
            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i];
                if (instruction.operand is MethodInfo method && method.DeclaringType == _hapMethod.DeclaringType
                    && method.Name == "CalculateTaxInefficiencyRate")
                {
                    var store = code.Skip(i + 1).FirstOrDefault(c => c.opcode != OpCodes.Nop);
                    if (store == null || !store.IsStloc()) throw new InvalidOperationException("HAP tax-rate storage changed.");
                    rateLocal = Local(store);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LordshipHapIncome), nameof(TaxRate));
                    rates++;
                }
                else if (instruction.Calls(village))
                {
                    // Match the accumulator update semantically, without depending on HAP's local numbers.
                    var next = code.Skip(i + 1).Select((c, offset) => new { Code = c, Index = i + 1 + offset })
                        .Where(c => c.Code.opcode != OpCodes.Nop).Take(5).ToArray();
                    if (next.Length != 5 || !next[0].Code.IsStloc() || !next[1].Code.IsLdloc()
                        || !next[2].Code.IsLdloc() || next[3].Code.opcode != OpCodes.Add || !next[4].Code.IsStloc()
                        || Local(next[0].Code) != Local(next[2].Code) || Local(next[1].Code) != Local(next[4].Code))
                        throw new InvalidOperationException("HAP village taxable-income accumulator changed.");
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LordshipHapIncome), nameof(VillageIncome));
                    totalLocal = Local(next[1].Code);
                    // The helper leaves the accumulator value on the stack for the original store.
                    var capture = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LordshipHapIncome), nameof(FiefTotal)));
                    capture.MoveLabelsFrom(next[4].Code);
                    code.Insert(next[4].Index, capture);
                    villages++;
                }
                else if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field
                    && field.DeclaringType == _hapMethod.DeclaringType && field.Name == "_settlementIncomeText")
                {
                    int call = i + 1;
                    while (call < code.Count && (code[call].opcode == OpCodes.Nop || code[call].opcode == OpCodes.Ldnull)) call++;
                    if (call >= code.Count || !(code[call].operand is MethodInfo add) || add.DeclaringType != typeof(ExplainedNumber)
                        || (add.Name != nameof(ExplainedNumber.Add) && add.Name != nameof(ExplainedNumber.AddFromExplainedNumber)))
                        throw new InvalidOperationException("HAP settlement-income commit changed.");
                    code.Insert(call + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LordshipHapIncome), nameof(Commit))));
                    commits++;
                }
            }
            if (rates != 1 || villages != 1 || commits != 2) throw new InvalidOperationException("HAP settlement-income capture contract changed.");
            var terms = code.Where(c => c.opcode != OpCodes.Nop).ToList();
            int deductions = 0;
            for (int i = 0; i + 5 < terms.Count; i++)
                if (terms[i].IsLdloc() && Local(terms[i]) == totalLocal && terms[i + 1].opcode == OpCodes.Conv_R4
                    && terms[i + 2].IsLdloc() && Local(terms[i + 2]) == rateLocal && terms[i + 3].opcode == OpCodes.Mul
                    && terms[i + 4].opcode == OpCodes.Conv_I4 && terms[i + 5].IsStloc()) deductions++;
            if (deductions != 1) throw new InvalidOperationException("HAP per-fief tax deduction formula changed.");
            return code;
        }

        private static int Local(CodeInstruction instruction)
        {
            if (instruction.operand is LocalBuilder builder) return builder.LocalIndex;
            if (instruction.operand is LocalVariableInfo local) return local.LocalIndex;
            if (instruction.operand != null) return Convert.ToInt32(instruction.operand);
            if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0) return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1) return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2) return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3) return 3;
            return -1;
        }
    }
}
