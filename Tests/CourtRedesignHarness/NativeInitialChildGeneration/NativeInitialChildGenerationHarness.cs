using System;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class NativeInitialChildGenerationHarness
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0]))
        { Console.Error.WriteLine("Pass the installation path of the game used to build this harness."); return 1; }
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string file = new AssemblyName(request.Name).Name + ".dll";
            string path = Directory.EnumerateFiles(args[0], file, SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains("Win64_Shipping_Client"));
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try
        {
            int checks = 0;
            InitialChildGenerationAgeTests.Run((condition, message) =>
            {
                if (!condition) throw new Exception(message);
                checks++;
                Console.WriteLine("PASS: " + message);
            });
            Console.WriteLine(checks + " initial child generation portability checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

namespace BellumCivile
{
    // The fixture patches these getters; the production model and transpiler are linked unchanged.
    internal static class BellumCivileOptions
    {
        public static bool EnableCustomAdulthoodAge => true;
        public static int AdulthoodAge => 16;
    }
}
