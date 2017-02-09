using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            // Repeat runs for more accurate timings
            var warmupRuns = 1;
            var testRuns = 1;

            // 1. Refer each project containing test programs and reference the main method by key (optional)
            var tests = new Dictionary<string, Action>()
            {
                ["ATest"] = ATest.Program.Main
            };

            // 2. Set test name and pick the main method to run. In case you hardcode the main method you can skip the previous step
            var testName = args[0];
            var testMethod = tests[testName];

            // 3. Set the input/output file extensions
            var inExtension = "in";
            var outExtension = "out";

            // 4. Ensure testfiles are in place. By default looks for %WORKINGDIRECTORY%/${testName}/*.${inExtension}
            // 5. Run TestRunner
            var stopwatch = new Stopwatch();
            var totalElapsed = TimeSpan.Zero;
            var passed = 0;
            var failed = 0;
            foreach (var fileNameIn in Directory.GetFiles(testName, $"*.{inExtension}"))
            {
                var fileNameOut = Path.ChangeExtension(fileNameIn, outExtension);

                string[] outputLines;
                string[] intendedLines;

                int run;
                for (run = 0; run < warmupRuns; run++)
                {
                    RunTest(fileNameIn, fileNameOut, testMethod, out outputLines, out intendedLines);
                }

                TimeSpan elapsed = TimeSpan.Zero;
                run = 0;
                do
                {
                    elapsed += RunTest(fileNameIn, fileNameOut, testMethod, out outputLines, out intendedLines);
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
                    foreach (var pair in intendedLines.Zip(outputLines, (i, o) => new { intended = i ?? "", output = o ?? "" }).Select((t, i) => new { t.intended, t.output, i }))
                    {
                        Console.WriteLine(pair.i.ToString().PadLeft(5) + " │ " + pair.intended.PadRight(padWith) + " │ " + pair.output.PadRight(padWith));
                    }
                }
            }

            if (failed == 0)
                Console.WriteLine($"Passed all {totalElapsed.TotalMilliseconds:F5}ms");
            else if (passed == 0)
                Console.WriteLine($"Failed all {totalElapsed.TotalMilliseconds:F5}ms");
            else
                Console.WriteLine($"Failed some {totalElapsed.TotalMilliseconds:F5}ms");

            Console.WriteLine();
            Console.WriteLine("Press a key to continue...");
            Console.ReadKey(true);
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
                testMethod();
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

    }
}
