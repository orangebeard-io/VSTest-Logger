using Orangebeard.VSTest.TestLogger.ClientExecution;

namespace Orangebeard.VSTest.TestLogger.ClientExtensibility.Commands.CommandArgs
{
    public class LogScopeCommandArgs
    {
        public LogScopeCommandArgs(ILogScope logScope)
        {
            LogScope = logScope;
        }

        public ILogScope LogScope { get; }
    }
}
