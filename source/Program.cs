using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace StackAnalyser
{
    class Program
    {
        static string _fn;
        static string _outputFn;
        static string _outputDetailFn;

        static string GetArg(string[] args, string key, string desc)
        {
            var idx = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase)) + 1;
            if (idx <= 0)
                return null;
            if (idx >= args.Length)
                throw new ArgumentNullException($"{key} must be followed by {desc}.");
            return args[idx];
        }

        static bool ParseArgs(string[] args)
        {
            if (args.Any(a => a.StartsWith("-h") || a.StartsWith("--h")))
                return false;

            if (args.Length == 1 && !args[0].StartsWith("-"))
                _fn = args[0];
            _fn = GetArg(args, "--source", "the assembly to analyse");
            var dir = GetArg(args, "--dir", "the directory that contains the file to analyse");
            if (dir != null)
            {
                if (_fn != null)
                    throw new ArgumentException("Cannot specify both --source and --dir");
                DirectoryInfo info = new DirectoryInfo(dir);
                _fn = info.GetFiles("*.S")
                    .OrderByDescending(fi => fi.CreationTime)
                    .FirstOrDefault()
                    ?.FullName;
                if (_fn == null)
                    throw new ArgumentException($"Cannot find a .S file to analyse in '{dir}'");
            }
            if (_fn == null)
                throw new ArgumentException("No source file specified");

            _outputFn = GetArg(args, "--out", "the output filename") ?? Path.ChangeExtension(_fn, "") + "_maxStacks.txt";
            _outputDetailFn = GetArg(args, "--detailed", "the detailed output filename");
            return true;
        }

        static void PrintHelp()
        {
            Console.WriteLine(
@"AVR gcc stack analyser.

Basic usage:
  StackAnalyser <source file name>

Complete options:
  StackAnalyser --source <source> --out <out> --detailed <detailed>
 OR
  StackAnalyser --dir <dir of source> --out <out> --detailed <detailed>

If --dir is specified, will analyse the most recent file in the directory.
If no output is specified, output filename will be <source>_maxStacks.txt"
            );
        }

        static void Main(string[] args)
        {
            try
            {
                if (!ParseArgs(args))
                    PrintHelp();
            }
            catch
            {
                PrintHelp();
                throw;
            }

            var lines = File.ReadAllLines(_fn);
            var analyser = new Analyser();
            analyser.Analyse(lines);
            var graphGroups = analyser.Roots.Select(f => f.GetCallGraphs(new HashSet<AsmFunction>())).ToList();
            File.WriteAllLines(_outputFn,
                graphGroups.Select(grp => grp.OrderByDescending(graph => graph.stackUsed).FirstOrDefault())
                    .OrderByDescending(graph => graph.stackUsed)
                    .Select(tpl => $"{tpl.stackUsed} : {tpl.desc}"));
            if (!string.IsNullOrEmpty(_outputDetailFn ))
                File.WriteAllLines(_outputDetailFn,
                    graphGroups.SelectMany(g => g).OrderByDescending(g => g.stackUsed).Select(tpl => $"{tpl.stackUsed} : {tpl.desc}"));

            var stackDump = Console.ReadLine();
            if (string.IsNullOrEmpty(stackDump))
                return;
            var tracer = new CrashTracer(analyser.Functions);
            tracer.Trace(stackDump);            
            Console.WriteLine();
            Console.WriteLine(" ================== Stack Trace =================");
            Console.WriteLine($"SP: {tracer.StackPointer:X4} ({tracer.StackPointer})");
            Console.WriteLine();
            foreach (var func in tracer.StackTrace)
                Console.WriteLine($"{func.func.Name} : 0x{func.retAddress:X4}");
            Console.WriteLine();
            int accountedStack = tracer.StackTrace.Sum(f => f.func.StackUsed + 2);
            int totalStack = CrashTracer.StackEnd - tracer.StackPointer - 1;
            if (accountedStack == totalStack)
                Console.WriteLine($"All stack accounted for ({totalStack})");
            else
            {
                Console.WriteLine($"Stack accounted: {accountedStack}");
                Console.WriteLine($"Total stack    : {totalStack}");
                Console.WriteLine($"Unaccounted    : {totalStack - accountedStack}");
            }
            Console.WriteLine();
            Console.WriteLine(" ================== =========== =================");
            Console.WriteLine();
        }
    }
}
