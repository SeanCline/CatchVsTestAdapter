using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CatchVsTestAdapter
{
    class Utility
    {
        public static string runExe(string exe, params string[] args)
        {
            var p = new Process{};
            p.StartInfo = new ProcessStartInfo{};
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = exe;
            p.StartInfo.Arguments = escapeArguments(args);

            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return stdout;
        }

        public static String escapeArguments(params string[] args)
        {
            var argumentString = "";
            foreach (var arg in args)
            {
                argumentString += escapeArgument(arg) + " ";
            }

            // Trim the trailing space from above.
            if (argumentString.Length > 0)
            {
                argumentString = argumentString.Remove(argumentString.Length - 1);
            }

            return argumentString;
        }

        internal static string escapeArgument(string arg)
        {
            var escapedQuotes = arg.Replace("\"", "\\\"");
            return "\"" + Regex.Replace(escapedQuotes, @"(\\+)$", @"$1$1") + "\"";
        }
    }
}
