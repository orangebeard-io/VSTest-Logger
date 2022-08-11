using Orangebeard.VSTest.TestLogger.LogHandler;
using System;

namespace Orangebeard.VSTest.TestLogger.ClientExecution.Logging
{
    public class LogScope : BaseLogScope
    {
        public LogScope(ILogContext logContext, ILogScope root, ILogScope parent, string name)
            : base(logContext)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Log scope name cannot be null of empty.", nameof(name));
            }

            Root = root;
            Parent = parent;
            Name = name;
        }

        public override ILogScope Parent { get; }

        public override string Name { get; }

        public override void Dispose()
        {
            base.Dispose();

            ContextAwareLogHandler.CommandsSource_OnEndLogScopeCommand(Context, new ClientExtensibility.Commands.CommandArgs.LogScopeCommandArgs(this));
            Context.Log = Parent;
        }
    }
}
