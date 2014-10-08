using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace CatchVsTestAdapter
{
    [FileExtension(".exe")]
    [DefaultExecutorUri(CatchTestExecutor.ExecutorUriString)]
    public class CatchTestDiscoverer : ITestDiscoverer
    {
        /// <summary>
        /// Finds tests in Catch unit test binaries. Note: We have to run the binary to enumerate tests.
        /// </summary>
        /// <param name="sources">Binaries to search for tests.</param>
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext context, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            var catchBinaries = sources.Where(isSourceACatchTestBinary);

            var tests = ListTestsInBinaries(catchBinaries);

            foreach (var testCase in tests)
            {
                discoverySink.SendTestCase(testCase);
            }
        }

        internal static bool isSourceACatchTestBinary(string source)
        {
            // This is a dirty hack to detect whether a binary is a catch test.
            // We really need to know since to detect tests, the binary needs to be run.
            var fileContents = File.ReadAllText(source);
            return fileContents.Contains("--list-tests") && fileContents.Contains("--list-tags");
        }

        internal static IEnumerable<TestCase> ListTestsInBinaries(IEnumerable<string> sources)
        {
            var tests = new List<TestCase>();

            foreach (var source in sources)
            {
                tests.AddRange(ListTestsInBinary(source));
            }

            return tests;
        }

        internal static IEnumerable<TestCase> ListTestsInBinary(string source)
        {
            var tests = new List<TestCase>();

            var listOutput = Utility.runExe(source, "--list-tests");

            // Match a test case out of the output.
            const string regexStr = @"\r?\n[ ]{2}(?<name>[^\r\n]*)(?:\r?\n[ ]{4}(?<name>[^ ][^\r\n]*))*(?:\r?\n[ ]{6}(?<tag>\s*\[[^\r\n]*\])*)?";

            foreach (Match match in Regex.Matches(listOutput, regexStr))
            {
                IEnumerable<string> nameLines = match.Groups["name"].Captures.OfType<Capture>().Select(x => x.Value).ToList();
                var fullyQuallifiedName = (nameLines.Count() == 1) ? nameLines.First() : nameLines.First() + "*";
                var test = new TestCase(fullyQuallifiedName, CatchTestExecutor.ExecutorUri, source)
                {
                    DisplayName = nameLines.Aggregate((x, y) => x + " " + y)
                };

                // Add test tags as traits.
                if (test.GetType().GetProperty("Traits") != null) //< Don't populate traits on older versions of VS.
                {
                    foreach (Capture tag in match.Groups["tag"].Captures)
                    {
                        test.Traits.Add("Tags", tag.Value);
                    }
                }

                tests.Add(test);
            }

            return tests;
        }
    }
}
