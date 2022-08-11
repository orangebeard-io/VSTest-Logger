using Orangebeard.Client.Entities;
using Orangebeard.VSTest.TestLogger.ClientExecution.Logging;
using System;
using System.Collections.Generic;

namespace Orangebeard.VSTest.TestLogger.LogHandler
{
    public class ContextAwareLogHandler
    {
        //TODO?+ private static readonly ITraceLogger _traceLogger = TraceLogManager.Instance.GetLogger<ContextAwareLogHandler>();

        public static void CommandsSource_OnEndLogScopeCommand(ClientExecution.ILogContext logContext, ClientExtensibility.Commands.CommandArgs.LogScopeCommandArgs args)
        {
            var logScope = args.LogScope;

            if (OrangebeardAddIn.LogScopes.ContainsKey(logScope.Id))
            {
                var testRunUuid = OrangebeardAddIn.TestrunUuid;
                var client = OrangebeardAddIn.Client;
                var status = _nestedStepStatusMap[logScope.Status];
                var finishTestItem = new FinishTestItem(testRunUuid.Value, status);
                Guid testItem = OrangebeardAddIn.LogScopes[logScope.Id];
                Context.Current = Context.Current.Parent;
                client.FinishTestItem(testItem, finishTestItem);

                OrangebeardAddIn.LogScopes.TryRemove(logScope.Id, out Guid _);
            }
            else
            {
                //TODO?+ _traceLogger.Warn($"Unknown current step context to end log scope with `{logScope.Id}` ID.");
            }
        }

        //private static readonly AsyncLocal<ScenarioStepContext> _activeStepContext = new AsyncLocal<ScenarioStepContext>();

        //public static ScenarioStepContext ActiveStepContext
        //{
        //    get
        //    {
        //        return _activeStepContext.Value;
        //    }
        //    set
        //    {
        //        _activeStepContext.Value = value;
        //    }
        //}

        //private static readonly AsyncLocal<ScenarioContext> _activeScenarioContext = new AsyncLocal<ScenarioContext>();

        //public static ScenarioContext ActiveScenarioContext
        //{
        //    get
        //    {
        //        return _activeScenarioContext.Value;
        //    }
        //    set
        //    {
        //        _activeScenarioContext.Value = value;
        //    }
        //}

        //private static readonly AsyncLocal<FeatureContext> _activeFeatureContext = new AsyncLocal<FeatureContext>();

        //public static FeatureContext ActiveFeatureContext
        //{
        //    get
        //    {
        //        return _activeFeatureContext.Value;
        //    }
        //    set
        //    {
        //        _activeFeatureContext.Value = value;
        //    }
        //}

        public static readonly Dictionary<LogScopeStatus, Status> _nestedStepStatusMap = new Dictionary<LogScopeStatus, Status> {
            { LogScopeStatus.InProgress, Status.IN_PROGRESS },
            { LogScopeStatus.Passed, Status.PASSED },
            { LogScopeStatus.Failed, Status.FAILED },
            { LogScopeStatus.Skipped,Status.SKIPPED }
        };
    }
}
