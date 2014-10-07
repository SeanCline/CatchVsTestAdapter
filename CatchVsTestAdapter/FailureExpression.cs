using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CatchVsTestAdapter
{
    class FailureExpression
    {
        public FailureExpression(XElement expressionNode)
        {
            var fileAttr = expressionNode.Attribute("filename");
            var lineAttr = expressionNode.Attribute("line");
            if (fileAttr != null && lineAttr != null)
            {
                Location = new SourceLocation(fileAttr.Value, uint.Parse(lineAttr.Value));
            }

            var originalNode = expressionNode.Descendants("Original").FirstOrDefault();
            if (originalNode != null)
            {
                OriginalExpression = originalNode.Value.Trim();
            }

            var expandedNode = expressionNode.Descendants("Expanded").FirstOrDefault();
            if (expandedNode != null)
            {
                ExpandedExpression = expandedNode.Value.Trim();
            }

            var ExceptionNode = expressionNode.Descendants("Exception").FirstOrDefault();
            if (ExceptionNode != null)
            {
                ExceptionMessage = ExceptionNode.Value.Trim();
            }
        }

        public SourceLocation Location { get; private set; }
        public string OriginalExpression { get; private set; }
        public string ExpandedExpression { get; private set; }
        public string ExceptionMessage { get; private set; }

        /// <summary>
        /// Builds as close of a representation to a catch command line reporter message as possible.
        /// </summary>
        public override string ToString()
        {
            var message = new StringBuilder{};
            string indentation = "\t";

            message.AppendFormat("{0} FAILED:\n", Location);
            
            if (OriginalExpression != null)
            {
                message.Append("Expression:\n");
                message.AppendFormat("{0}{1}\n", indentation, OriginalExpression);
            }

            if (ExpandedExpression != null && ExpandedExpression != OriginalExpression)
            {
                message.Append("With expansion:\n");
                message.AppendFormat("{0}{1}\n", indentation, ExpandedExpression);
            }

            if (ExceptionMessage != null)
            {
                message.Append("due to unexpected exception with message:\n");
                message.AppendFormat("{0}{1}\n", indentation, ExceptionMessage);
            }

            return message.ToString();
        }
    }


    public class SourceLocation
    {
        public SourceLocation(string file, uint line)
        {
            File = file;
            Line = line;
        }

        public string File { get; private set; }
        public uint Line { get; private set; }

        public override string ToString()
        {
            return System.IO.Path.GetFileName(File) + "(" + Line + ")";
        }
    }
}
