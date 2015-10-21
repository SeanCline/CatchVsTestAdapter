using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System.Text;
using System.Globalization;

namespace CatchVsTestAdapter
{
    [ExtensionUri(CatchTestExecutor.ExecutorUriString)]
    public class CatchTestExecutor : ITestExecutor
    {
        public const string ExecutorUriString = "executor://CatchTestExecutor";
        public static readonly Uri ExecutorUri = new Uri(CatchTestExecutor.ExecutorUriString);

        private enum ExecutorState { Stopped, Running, Cancelling, Cancelled }
        private ExecutorState _state = ExecutorState.Stopped;

        #region ITestExecutor

        /// <summary>
        /// Cancel test execution.
        /// </summary>
        public void Cancel()
        {
            _state = ExecutorState.Cancelling;
        }


        /// <summary>
        /// Runs the tests.
        /// </summary>
        /// <param name="testBinaries">Where to look for tests to be run.</param>
        /// <param name="context">Context in which to run tests.</param>
        /// <param param name="framework">Where results should be stored.</param>
        public void RunTests(IEnumerable<string> testBinaries, IRunContext context, IFrameworkHandle framework)
        {
            _state = ExecutorState.Running;

            foreach (var testBinary in testBinaries)
            {
                if (_state == ExecutorState.Cancelling)
                {
                    _state = ExecutorState.Cancelled;
                    return;
                }

                var reportDocument = RunOrDebugCatchTest(testBinary, "*", context, framework);

                var tests = CatchTestDiscoverer.ListTestsInBinary(testBinary);
                foreach (var test in tests)
                {
                    try
                    {
                        var result = GetTestResultFromReport(test, reportDocument);
                        framework.RecordResult(result);
                    }
                    catch (Exception ex)
                    {
                        // Log it and move on. It will show up to the user as a test that hasn't been run.
                        framework.SendMessage(TestMessageLevel.Error, "Exception occured when processing test source: " + test.FullyQualifiedName);
                        framework.SendMessage(TestMessageLevel.Informational, "Message: " + ex.Message + "\nStacktrace:" + ex.StackTrace);
                    }
                }
            }
        }


        /// <summary>
        /// Runs the tests.
        /// </summary>
        /// <param name="tests">Which tests should be run.</param>
        /// <param name="context">Context in which to run tests.</param>
        /// <param param name="framework">Where results should be stored.</param>
        public void RunTests(IEnumerable<TestCase> tests, IRunContext context, IFrameworkHandle framework)
        {
            _state = ExecutorState.Running;

            foreach (var test in tests)
            {
                if (_state == ExecutorState.Cancelling)
                {
                    _state = ExecutorState.Cancelled;
                    return;
                }

                try
                {
                    var reportDocument = RunOrDebugCatchTest(test.Source, test.FullyQualifiedName, context, framework);
                    var result = GetTestResultFromReport(test, reportDocument);
                    framework.RecordResult(result);
                }
                catch (Exception ex)
                {
                    // Log it and move on. It will show up to the user as a test that hasn't been run.
                    framework.SendMessage(TestMessageLevel.Error, "Exception occured when processing test case: " + test.FullyQualifiedName);
                    framework.SendMessage(TestMessageLevel.Informational, "Message: " + ex.Message + "\nStacktrace:" + ex.StackTrace);
                }
            }
        }

        private static XDocument RunOrDebugCatchTest(string testBinary, string testSpec, IRunContext runContext, IFrameworkHandle framework)
        {
            if (runContext.IsBeingDebugged)
            {
                return XDocument.Parse(DebugCatchTest(testBinary, testSpec, framework));
            }
            else
            {
                return XDocument.Parse(RunCatchTests(testBinary, testSpec));
            }
        }

        #endregion

        #region Implementation Details

        /// <summary>
        /// Runs a test in a source (Catch binary) and returns the resulting XML string.
        /// If a testspec is not provided, runs the default set of tests.
        /// </summary>
        internal static string RunCatchTests(string testBinary, string testSpec)
        {
            return Utility.runExe(testBinary, testSpec, "-r", "xml", "-d", "yes");
        }


        /// <summary>
        /// Runs a test in a source (Catch binary) with a debugger attached and returns the resulting XML string.
        /// </summary>
        internal static string DebugCatchTest(string testBinary, string testSpec, IFrameworkHandle framework)
        {
            string output;

            var cwd = Directory.GetCurrentDirectory();
            var exePath = Path.Combine(cwd, testBinary);
            var outputPath = Path.GetTempFileName();
            try
            {
                var arguments = Utility.escapeArguments(testSpec, "-r", "xml", "-b", "-d", "yes", "-o", outputPath);
                var debuggee = Process.GetProcessById(framework.LaunchProcessWithDebuggerAttached(exePath, cwd, arguments, null));
                debuggee.WaitForExit();
                output = File.ReadAllText(outputPath);
                debuggee.Close();
            }
            finally
            {
                File.Delete(outputPath);
            }

            return output;
        }


        /// <summary>
        /// Returns a TestResult for a given test case, populated with the data from the report document.
        /// </summary>
        internal static TestResult GetTestResultFromReport(TestCase test, XDocument report)
        {
            var result = new TestResult(test);

            var testCaseElement = GetTestCaseElement(report, test.FullyQualifiedName);
            result.Outcome = GetTestOutcome(testCaseElement);
            result.ErrorMessage = GetTestMessage(testCaseElement);

            try
            {
                result.Duration = GetTestDuration(testCaseElement);
            }
            catch { } //< Older versions of catch do not include the duration in the xml report.

            return result;
        }


        /// <summary>
        /// Returns the &lt;TestCase&gt; element with the fiven name in a report document.
        /// Throws if that test case does not exist in the document.
        /// </summary>
        internal static XElement GetTestCaseElement(XDocument reportDocument, string testName)
        {
            Func<string, string, bool> isMatch = (value, wildcard) =>
            {
                bool isWildcard = testName.EndsWith("*");

                if (isWildcard)
                {
                    return Regex.IsMatch(value, "^" + Regex.Escape(wildcard.TrimEnd('*')) + ".*$");
                }
                else
                {
                    return value == wildcard;
                }
            };

            var testCaseElememt =
                from el in reportDocument.Descendants("TestCase")
                where isMatch(el.Attribute("name").Value, testName)
                select el;

            return testCaseElememt.First<XElement>();
        }


        /// <summary>
        /// Returns the errer message of a given test based on its xml element.
        /// </summary>
        internal static TestOutcome GetTestOutcome(XElement testElement)
        {
            // If there is an OverallResult, use it to determine outcome.
            var resultElement = testElement.Elements("OverallResult").FirstOrDefault();
            if (resultElement != null)
            {
                var passed = resultElement.Attribute("success").Value.ToLower() == "true";
                return (passed) ? TestOutcome.Passed : TestOutcome.Failed;
            }

            // If there is an OverallResults (with an s), use it to determine outcome.
            var resultsElement = testElement.Elements("OverallResults").FirstOrDefault();
            if (resultsElement != null)
            {
                var numFailures = int.Parse(resultsElement.Attribute("failures").Value);
                var numExpectedFailures = int.Parse(resultsElement.Attribute("expectedFailures").Value);

                var passed = (numFailures == numExpectedFailures); //< All failures were expected.
                return (passed) ? TestOutcome.Passed : TestOutcome.Failed;
            }

            return TestOutcome.NotFound;
        }


        /// <summary>
        /// Returns the outcome of a given test based on its xml element.
        /// </summary>
        internal static string GetTestMessage(XElement testElement)
        {
            var message = new StringBuilder { };

            // If this is a failed section, print a header.
            if (testElement.Name == "Section" && GetTestOutcome(testElement) != TestOutcome.Passed)
            {
                message.AppendFormat("Section \"{0}\":\n", testElement.Attribute("name").Value);
            }

            // First, recurse over all section elements.
            foreach (var sectionNode in testElement.Elements("Section"))
            {
                message.AppendLine(GetTestMessage(sectionNode));
            }

            // Print failed expressions...
            foreach (var expressionElement in testElement.Elements("Expression"))
            {
                if (expressionElement.Attribute("success").Value.Trim().ToLower() == "false")
                {
                    var expr = new FailureExpression(expressionElement);
                    message.AppendLine(expr.ToString());
                }
            }

            // Print unexpected exceptions...
            foreach (var exceptionElement in testElement.Elements("Exception"))
            {
                var location = SourceLocation.FromXElement(exceptionElement);
                message.AppendFormat("Unexpected exception at {0} with message:\n\t{1}\n", location, exceptionElement.Value.Trim());
                message.AppendLine();
            }

            // Print info...
            foreach (var infoElement in testElement.Elements("Info"))
            {
                if (infoElement.Value != null)
                {
                    message.AppendFormat("Info: {0}\n", infoElement.Value.Trim());
                    message.AppendLine();
                }
            }

            // Print warnings...
            foreach (var warningElement in testElement.Elements("Warning"))
            {
                if (warningElement.Value != null)
                {
                    message.AppendFormat("Warning: {0}\n", warningElement.Value.Trim());
                    message.AppendLine();
                }
            }

            // Print failures...
            foreach (var failureElement in testElement.Elements("Failure"))
            {
                if (failureElement.Value != null && failureElement.Value != "")
                {
                    message.AppendFormat("Explictly failed with message: {0}\n", failureElement.Value.Trim());
                    message.AppendLine();
                }
                else
                {
                    message.AppendLine("Explictly failed.\n");
                }
            }


            // Print fatal error conditions...
            foreach (var fatalElement in testElement.Elements("FatalErrorCondition"))
            {
                var location = SourceLocation.FromXElement(fatalElement);
                if (fatalElement.Value != null && fatalElement.Value.Trim() != "")
                {
                    message.AppendFormat("Fatal Error at {0} with message:\n\t{1}\n", location, fatalElement.Value.Trim());
                    message.AppendLine();
                }
                else
                {
                    message.AppendFormat("Fatal Error at {0}\n", location);
                    message.AppendLine();
                }
            }

            return message.ToString();
        }


        /// <summary>
        /// Returns the duration of a given test based on its xml element.
        /// </summary>
        internal static TimeSpan GetTestDuration(XElement testCaseElement)
        {
            var durationAttr = testCaseElement.Descendants("OverallResult").First().Attribute("durationInSeconds");
            return TimeSpan.FromSeconds(double.Parse(durationAttr.Value, CultureInfo.InvariantCulture));
        }


        #endregion
    }
}
