using Orangebeard.VSTest.TestLogger.ClientExecution;
using Orangebeard.VSTest.TestLogger.ClientExecution.Logging;
using System;

namespace Orangebeard.VSTest.TestLogger
{
    public class NewTestContext
    {
        public NewTestContext(NewTestContext parent, Guid? testUuid)
        {
            TestUuid = testUuid;
            Parent = parent;

            string scopeName = " ";
            if (parent == null)
            {
                ILogContext launchContext = new LaunchContext { Log = null };
                Log = new LogScope(logContext: launchContext, root: null, parent: null, name: scopeName);
                launchContext.Log = Log;
            }
            else
            {
                Log = new LogScope(parent.Log.Context, root: null, parent: null, name: scopeName);
            }
        }

        public Guid? TestUuid { get; private set; }

        public ILogScope Log { get; private set; }

        public NewTestContext Parent { get; private set; }
    }
}
