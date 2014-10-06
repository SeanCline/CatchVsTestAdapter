using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CatchVsTestAdapter
{
    class Utility
    {
        public static string runExe(string exe, params string[] args)
        {
            Process p = new Process();
            p.StartInfo = new ProcessStartInfo();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = exe;
            foreach (var arg in args)
            {
                p.StartInfo.Arguments += escapeArgument(arg) + " ";
            }

            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return stdout;
        }

        internal static string escapeArgument(string arg)
        {
            return "\"" + Regex.Replace(arg, @"(\\+)$", @"$1$1") + "\"";
        }
    }
}
