using Orangebeard.VSTest.TestLogger.ClientExecution.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Orangebeard.VSTest.TestLogger.ClientExtensibility.LogFormatter
{
    public class FileLogFormatter : ILogFormatter
    {
        /// <inheritdoc/>
        public int Order => 10;

        /// <inheritdoc/>
        public bool FormatLog(string logMessage, out string newLogMessage, out LogMessageAttachment logMessageAttachment)
        {
            newLogMessage = logMessage;
            logMessageAttachment = null;

            if (logMessage != null)
            {
                var regex = new Regex("{rp#file#(.*)}");
                var match = regex.Match(logMessage);
                if (match.Success)
                {
                    newLogMessage = logMessage.Replace(match.Value, "");

                    var filePath = match.Groups[1].Value;

                    try
                    {
                        var mimeType = Shared.MimeTypes.MimeTypeMap.GetMimeType(Path.GetExtension(filePath));

                        logMessageAttachment = new LogMessageAttachment(mimeType, File.ReadAllBytes(filePath), filePath);

                        return true;
                    }
                    catch (Exception exp)
                    {
                        newLogMessage += $"{Environment.NewLine}{Environment.NewLine}Cannot fetch data by `{filePath}` path.{Environment.NewLine}{exp}";
                    }
                }
            }
            return false;
        }
    }
}
