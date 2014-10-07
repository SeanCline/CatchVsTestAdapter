using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

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
        /// <param name="testBinary">Where to look for tests to be run.</param>
        /// <param name="runContext">Context in which to run tests.</param>
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

                var tests = CatchTestDiscoverer.listTestsInBinary(testBinary);
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
        /// <param name="runContext">Context in which to run tests.</param>
        /// <param param name="framework">Where results should be stored.</param>
        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle framework)
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
                    var reportDocument = RunOrDebugCatchTest(test.Source, test.FullyQualifiedName, runContext, framework);
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
            return Utility.runExe(testBinary, testSpec, "--reporter", "xml");
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
                var arguments = Utility.escapeArguments(testSpec, "--reporter", "xml", "--break", "--out", outputPath);
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

            if (result.Outcome == TestOutcome.Failed)
            {
                var expressionNode = (from el in testCaseElement.Descendants("Expression")
                                      where el.Attribute("success").Value == "false"
                                      select el).FirstOrDefault();


                if (expressionNode != null)
                {
                    var expr = new FailureExpression(expressionNode);
                    result.ErrorMessage = expr.ToString();
                }
                else
                {
                    result.ErrorMessage = "Unknown error.";
                }
            }

            return result;
        }


        /// <summary>
        /// Returns the &lt;TestCase&gt; element with the fiven name in a report document.
        /// Throws if that test case does not exist in the document.
        /// </summary>
        internal static XElement GetTestCaseElement(XDocument reportDocument, string testName)
        {
            var testCaseElememt =
                from el in reportDocument.Descendants("TestCase")
                where el.Attribute("name").Value == testName
                select el;

            return testCaseElememt.First<XElement>();
        }


        /// <summary>
        /// Returns the outcome of a given test based on its xml element.
        /// </summary>
        internal static TestOutcome GetTestOutcome(XElement testCaseElememt)
        {
            var status = testCaseElememt.Descendants("OverallResult").First().Attribute("success").Value;

            return (status.ToLower() == "true") ? TestOutcome.Passed : TestOutcome.Failed;
        }

        #endregion
    }
}
