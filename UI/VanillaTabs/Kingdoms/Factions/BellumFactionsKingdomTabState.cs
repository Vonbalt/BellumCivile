using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Factions
{
    public static class BellumFactionsKingdomTabState
    {
        private sealed class ClearHandle
        {
            public Action Clear { get; set; }
            public Action Select { get; set; }
            public Func<IReadOnlyList<string>> Diagnose { get; set; }
        }

        private static readonly ConditionalWeakTable<object, ClearHandle> ClearHandlers =
            new ConditionalWeakTable<object, ClearHandle>();

        public static void Register(object kingdomManagementVm, Action clear)
        {
            Register(kingdomManagementVm, clear, null);
        }

        public static void Register(object kingdomManagementVm, Action clear, Action select)
        {
            Register(kingdomManagementVm, clear, select, null);
        }

        public static void Register(
            object kingdomManagementVm,
            Action clear,
            Action select,
            Func<IReadOnlyList<string>> diagnose)
        {
            if (kingdomManagementVm == null || (clear == null && select == null && diagnose == null))
                return;

            ClearHandlers.Remove(kingdomManagementVm);
            ClearHandlers.Add(kingdomManagementVm, new ClearHandle
            {
                Clear = clear,
                Select = select,
                Diagnose = diagnose
            });
        }

        public static void Clear(object kingdomManagementVm)
        {
            if (kingdomManagementVm == null)
                return;

            if (ClearHandlers.TryGetValue(kingdomManagementVm, out ClearHandle handle))
                handle.Clear?.Invoke();
        }

        public static bool Select(object kingdomManagementVm)
        {
            if (kingdomManagementVm == null)
                return false;

            if (!ClearHandlers.TryGetValue(kingdomManagementVm, out ClearHandle handle) || handle.Select == null)
                return false;

            handle.Select.Invoke();
            return true;
        }

        public static bool TryDiagnose(object kingdomManagementVm, out IReadOnlyList<string> failures)
        {
            failures = Array.Empty<string>();
            if (kingdomManagementVm == null
                || !ClearHandlers.TryGetValue(kingdomManagementVm, out ClearHandle handle))
            {
                return false;
            }

            if (handle.Diagnose != null)
                failures = handle.Diagnose.Invoke() ?? Array.Empty<string>();
            return true;
        }
    }
}
