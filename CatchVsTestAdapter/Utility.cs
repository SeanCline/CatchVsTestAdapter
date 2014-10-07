using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace CatchVsTestAdapter
{
    class Utility
    {
        public static string runExe(string exe, params string[] args)
        {
            var p = new Process { };
            p.StartInfo = new ProcessStartInfo { };
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
            return args.Select(escapeArgument).Aggregate((x, y) => x + " " + y);
        }

        internal static string escapeArgument(string arg)
        {
            var escapedQuotes = arg.Replace("\"", "\\\"");
            return "\"" + Regex.Replace(escapedQuotes, @"(\\+)$", @"$1$1") + "\"";
        }
    }
}
