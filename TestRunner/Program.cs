using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NDesk.Options;
using NDesk.Options.Extensions;

namespace TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var os = new OptionSet();

            var entryPointVar= os.AddVariable<string>("entrypoint", "Custom entrypoint as fully qualified type name");
            var entryTypeVar = os.AddVariable<string>("entryclass", "Custom entrypoint class as fully qualified type name");
            var testFolderVar = os.AddVariable<string>("testFolder", "Folder containing testcases");
            var inputFolderVar = os.AddVariable<string>("inputFolder", "Subfolder of test folder for input files");
            var outputFolderVar = os.AddVariable<string>("outputFolder", "Subfolder of test folder for output files");
            var inputFileMatchVar = os.AddVariable<string>("inputFileMatch", "Expression used when looking for input test files");
            var outputFileMatchVar = os.AddVariable<string>("outputFileMatch", "Expression used when looking for output test files");
            var warmupRunsVar = os.AddVariable<int>("warmupRuns");
            var testRunsVar = os.AddVariable<int>("testRuns");

            var exe = string.Join(" ", os.Parse(args)).Trim();

            if (string.IsNullOrWhiteSpace(exe))
            {
                os.WriteOptionDescriptions(Console.Out);
                return;
            }

            var module = Assembly.LoadFrom(exe);

            var testFolder = testFolderVar.Value ?? Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(testFolder)) testFolder = Environment.CurrentDirectory;
            var inputFolder = Path.GetFullPath(inputFolderVar.Value != null ? Path.Combine(testFolder, inputFolderVar) : testFolder);
            var outputFolder = Path.GetFullPath(outputFolderVar.Value != null ? Path.Combine(testFolder, outputFolderVar) : testFolder);
            var inputFileMatch = inputFileMatchVar.Value ?? "input";
            var outputFileMatch = outputFileMatchVar.Value ?? "output";

            (Type returnType, Type[] parameterTypes, Func<MethodInfo, Action> adaptor)[] entryAdaptors = {
                (typeof(void), new Type[0], mi => (Action)mi.CreateDelegate(typeof(Action))),
                (typeof(void), new[] {typeof(string).MakeArrayType()}, mi => () => ((Action<string[]>) mi.CreateDelegate(typeof(Action<string[]>)))(Array.Empty<string>())),
                (typeof(int), new Type[0], mi => () => ((Func<int>)mi.CreateDelegate(typeof(Func<int>)))()),
                (typeof(int), new[] {typeof(string).MakeArrayType()}, mi => () => ((Func<string[], int>) mi.CreateDelegate(typeof(Func<string[], int>)))(Array.Empty<string>())),
            };

            Action entrypoint;
            if (entryPointVar.Value != null || entryTypeVar.Value != null)
            {
                var entryType = entryTypeVar == null ? module.EntryPoint.DeclaringType : module.GetType(entryTypeVar);
                var entryMethodName = entryPointVar.Value ?? "Main";
                var methods = from method in entryType.GetMethods(BindingFlags.Static)
                    where method.Name == entryMethodName
                    select BindMethodEntryPoint(method);

                entrypoint = methods.FirstOrDefault();
                if (entrypoint == null)
                {
                    Console.Error.WriteLine("Entrypoint not found, invalid entry point defined.");
                    os.WriteOptionDescriptions(Console.Out);
                    return;
                }
            }

            entrypoint = BindMethodEntryPoint(module.EntryPoint);

            // Repeat runs for more accurate timings
            var testRuns = Math.Max(testRunsVar.Value, 1);
            var warmupRuns = warmupRunsVar.Value == 0 ? testRuns : warmupRunsVar.Value;

            var stopwatch = new Stopwatch();
            var totalElapsed = TimeSpan.Zero;
            var passed = 0;
            var failed = 0;
            foreach (var fileNameIn in Directory.GetFiles(Path.GetFullPath(inputFolder), $"*{inputFileMatch}*"))
            {
                var fileNameOut = Path.Combine(outputFolder, Path.GetFileName(fileNameIn).Replace(inputFileMatch, outputFileMatch));
                if (!File.Exists(fileNameOut)) continue;

                string[] outputLines;
                string[] intendedLines;

                int run;
                for (run = 0; run < warmupRuns; run++)
                {
                    RunTest(fileNameIn, fileNameOut, entrypoint, out outputLines, out intendedLines);
                }

                TimeSpan elapsed = TimeSpan.Zero;
                run = 0;
                do
                {
                    elapsed += RunTest(fileNameIn, fileNameOut, entrypoint, out outputLines, out intendedLines);
                } while (++run < testRuns);
                elapsed = new TimeSpan(elapsed.Ticks / testRuns);

                totalElapsed += elapsed;

                if (intendedLines.Zip(outputLines, (i, o) => i == o).All(b => b))
                {
                    passed++;
                    Console.WriteLine($"Pass {elapsed.TotalMilliseconds:F5}ms: {Path.GetFileNameWithoutExtension(fileNameIn)}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"Fail {elapsed.TotalMilliseconds:F5}ms: {Path.GetFileNameWithoutExtension(fileNameIn)}:");

                    var padWith = (Console.BufferWidth - 12) / 2;

                    Console.WriteLine("n".PadLeft(5) + " │ " + "Expected".PadRight(padWith) + " │ " + "Returned".PadRight(padWith));
                    var lines = Math.Max(outputLines.Length, intendedLines.Length);
                    for (var i = 0; i < lines; i++)
                    {
                        var intendedLine = i < intendedLines.Length ? intendedLines[i] : "";
                        var outputLine = i < outputLines.Length ? outputLines[i] : "";
                        var intendedLineParts = Chunkify(intendedLine, padWith);
                        var outputLineParts = Chunkify(outputLine, padWith);
                        var chunks = Math.Max(outputLineParts.Length, intendedLineParts.Length);
                        for (var j = 0; j < chunks; j++)
                        {
                            var intendedLinePart = j < intendedLineParts.Length ? intendedLineParts[j] : "";
                            var outputLinePart = j < outputLineParts.Length ? outputLineParts[j] : "";
                            Console.WriteLine((j == 0 ? i.ToString() : "").PadLeft(5) + " │ " + intendedLinePart.PadRight(padWith) + " │ " + outputLinePart.PadRight(padWith));
                        }
                    }
                }
            }

            if(failed == 0 && passed == 0)
                Console.WriteLine($"No tests found in {inputFolder}");
            else if (failed == 0)
                Console.WriteLine($"Passed all {totalElapsed.TotalMilliseconds:F5}ms");
            else if (passed == 0)
                Console.WriteLine($"Failed all {totalElapsed.TotalMilliseconds:F5}ms");
            else
                Console.WriteLine($"Failed some {totalElapsed.TotalMilliseconds:F5}ms");

            Console.WriteLine();
            Console.WriteLine("Press a key to continue...");
            Console.ReadKey(true);
        }

        private static string[] Chunkify(string intendedLine, int chunkWidth)
        {
            var chunks = new string[(intendedLine.Length + chunkWidth - 1) / chunkWidth];
            for (var i = 0; i < chunks.Length; i++)
            {
                chunks[i] = intendedLine.Substring(i * chunkWidth, Math.Min(chunkWidth, intendedLine.Length - i * chunkWidth));
            }
            return chunks;
        }


        private static TimeSpan RunTest(string fileNameIn, string fileNameOut, Action testMethod, out string[] outputLines,
    out string[] intendedLines)
        {
            TimeSpan elapsed;
            using (var inStream = File.OpenRead(fileNameIn))
            using (var inStreamReader = new StreamReader(inStream))
            using (var outStream = new MemoryStream())
            using (var outStreamWriter = new StreamWriter(outStream) { AutoFlush = true })
            {
                var outStreamReader = new StreamReader(outStream);

                Console.SetIn(inStreamReader);
                Console.SetOut(outStreamWriter);

                var stopwatch = new Stopwatch();
                stopwatch.Reset();
                stopwatch.Start();
                try
                {
                    testMethod();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                stopwatch.Stop();
                elapsed = stopwatch.Elapsed;

                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetIn(new StreamReader(Console.OpenStandardInput()));
                Console.OutputEncoding = Encoding.UTF8;

                outStream.Position = 0;
                var output = outStreamReader.ReadToEnd();
                var intended = File.ReadAllText(fileNameOut); // Open outputFile
                outputLines = Regex.Split(output, "(\r\n|\r|\n)");
                intendedLines = Regex.Split(intended, "(\r\n|\r|\n)");
            }
            return elapsed;
        }


        static (Type returnType, Type[] parameterTypes, Func<MethodInfo, Action> adaptor)[] entryAdaptors = {
            (typeof(void), new Type[0], mi => (Action)mi.CreateDelegate(typeof(Action))),
            (typeof(void), new[] {typeof(string).MakeArrayType()}, mi => () => ((Action<string[]>) mi.CreateDelegate(typeof(Action<string[]>)))(Array.Empty<string>())),
            (typeof(int), new Type[0], mi => () => ((Func<int>)mi.CreateDelegate(typeof(Func<int>)))()),
            (typeof(int), new[] {typeof(string).MakeArrayType()}, mi => () => ((Func<string[], int>) mi.CreateDelegate(typeof(Func<string[], int>)))(Array.Empty<string>())),
        };
        private static Action BindMethodEntryPoint(MethodInfo methodInfo)
        {

            var parameterTypes = from param in methodInfo.GetParameters()
                select param.ParameterType;
            var methodInfos = from adaptor in entryAdaptors
                where methodInfo.ReturnType == adaptor.returnType
                where parameterTypes.SequenceEqual(adaptor.parameterTypes)
                select adaptor.adaptor(methodInfo);
            return methodInfos.FirstOrDefault();
        }

    }
}
