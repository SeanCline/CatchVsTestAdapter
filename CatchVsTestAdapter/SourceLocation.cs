using System;
using System.Xml.Linq;

namespace CatchVsTestAdapter
{
    public class SourceLocation
    {
        public SourceLocation(string file, uint line)
        {
            File = file;
            Line = line;
        }

        public static SourceLocation FromXElement(XElement el)
        {
            var file = el.Attribute("filename").Value;
            var line = uint.Parse(el.Attribute("line").Value);
            return new SourceLocation(file, line);
        }

        public string File { get; private set; }
        public uint Line { get; private set; }

        public override string ToString()
        {
            return System.IO.Path.GetFileName(File) + "(" + Line + ")";
        }
    }
}
