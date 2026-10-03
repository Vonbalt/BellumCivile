using System;

namespace BellumCivile
{
    public static class BellumTreatyTransferContext
    {
        private static int _depth;

        public static bool IsTreatyTransfer => _depth > 0;

        public static void Run(Action action)
        {
            if (action == null)
                return;

            _depth++;
            try
            {
                action();
            }
            finally
            {
                _depth--;
            }
        }
    }
}
