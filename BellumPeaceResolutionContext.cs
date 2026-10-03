using System;

namespace BellumCivile
{
    public static class BellumPeaceResolutionContext
    {
        private static int _bellumPeaceDepth;

        public static bool IsBellumPeaceResolution => _bellumPeaceDepth > 0;

        public static void RunBellumPeaceResolution(Action action)
        {
            if (action == null)
                return;

            _bellumPeaceDepth++;
            try
            {
                action();
            }
            finally
            {
                _bellumPeaceDepth--;
            }
        }
    }
}
