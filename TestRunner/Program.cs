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
            foreach (var file in Directory.GetFiles(testName, $"*.{inExtension}"))
            {
                using (var inStream = File.OpenRead(file))
                using (var inStreamReader = new StreamReader(inStream))
                using (var outStream = new MemoryStream())
                using (var outStreamWriter = new StreamWriter(outStream) { AutoFlush = true })
                {
                    var outStreamReader = new StreamReader(outStream);

                    Console.SetIn(inStreamReader);
                    Console.SetOut(outStreamWriter);
                    
                    stopwatch.Reset();
                    stopwatch.Start();
                    testMethod();
                    stopwatch.Stop();
                    totalElapsed += stopwatch.Elapsed;

                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.SetIn(new StreamReader(Console.OpenStandardInput()));
                    Console.OutputEncoding = Encoding.UTF8;

                    outStream.Position = 0;
                    var output = outStreamReader.ReadToEnd();
                    var intended = File.ReadAllText(Path.ChangeExtension(file, outExtension)); // Open outputFile
                    var outputLines = Regex.Split(output, "(\r\n|\r|\n)");
                    var intendedLines = Regex.Split(intended, "(\r\n|\r|\n)");

                    if (intendedLines.Zip(outputLines, (i, o) => i == o).All(b => b))
                    {
                        passed++;
                        Console.WriteLine($"Pass {stopwatch.Elapsed.TotalMilliseconds:F5}ms: {Path.GetFileNameWithoutExtension(file)}");
                    }
                    else
                    {
                        failed++;
                        Console.WriteLine($"Fail {stopwatch.Elapsed.TotalMilliseconds:F5}ms: {Path.GetFileNameWithoutExtension(file)}:");

                        var padWith = (Console.BufferWidth - 12)/2;

                        Console.WriteLine("n".PadLeft(5) + " │ " + "Expected".PadRight(padWith) + " │ " + "Returned".PadRight(padWith));
                        foreach (var pair in intendedLines.Zip(outputLines, (i, o) => new { intended = i ?? "", output = o ?? "" }).Select((t, i) => new { t.intended, t.output, i}))
                        {
                            Console.WriteLine(pair.i.ToString().PadLeft(5) + " │ " + pair.intended.PadRight(padWith) + " │ " + pair.output.PadRight(padWith));
                        }
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
    }
}
