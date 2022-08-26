using System;
using Orangebeard.VSTest.TestLogger.LogHandler.Messages;
using System.Collections.Generic;
using Orangebeard.Shared.Extensibility;
using Orangebeard.Shared.Extensibility.Commands;
using Orangebeard.Client.Converters;
using Orangebeard.Client.Abstractions.Models;

namespace Orangebeard.VSTest.TestLogger
{
    public class BridgeExtension : ICommandsListener
    {
        public void Initialize(ICommandsSource commandsSource)
        {
            commandsSource.OnBeginLogScopeCommand += CommandsSource_OnBeginLogScopeCommand;
            commandsSource.OnEndLogScopeCommand += CommandsSource_OnEndLogScopeCommand;
            commandsSource.OnLogMessageCommand += CommandsSource_OnLogMessageCommand;
        }

        private void CommandsSource_OnLogMessageCommand(Orangebeard.Shared.Execution.ILogContext logContext, Orangebeard.Shared.Extensibility.Commands.CommandArgs.LogMessageCommandArgs args)
        {
            var logScope = args.LogScope;

            var communicationMessage = new AddLogCommunicationMessage()
            {
                ParentScopeId = logScope?.Id,
                Time = args.LogMessage.Time,
                Text = args.LogMessage.Message,
                Level = _logLevelMap[args.LogMessage.Level]
            };

            if (args.LogMessage.Attachment != null)
            {
                communicationMessage.Attach = new Attach
                {
                    MimeType = args.LogMessage.Attachment.MimeType,
                    Data = args.LogMessage.Attachment.Data
                };
            }

            Console.WriteLine(ModelSerializer.Serialize<AddLogCommunicationMessage>(communicationMessage));
        }

        private Dictionary<Orangebeard.Shared.Execution.Logging.LogMessageLevel, LogLevel> _logLevelMap = new Dictionary<Orangebeard.Shared.Execution.Logging.LogMessageLevel, LogLevel> {
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Debug, LogLevel.Debug },
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Error, LogLevel.Error },
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Fatal, LogLevel.Fatal },
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Info, LogLevel.Info },
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Trace, LogLevel.Trace },
            { Orangebeard.Shared.Execution.Logging.LogMessageLevel.Warning, LogLevel.Warning }
        };

        private void CommandsSource_OnEndLogScopeCommand(Orangebeard.Shared.Execution.ILogContext logContext, Orangebeard.Shared.Extensibility.Commands.CommandArgs.LogScopeCommandArgs args)
        {
            var logScope = args.LogScope;

            var communicationMessage = new EndScopeCommunicationMessage
            {
                Id = logScope.Id,
                EndTime = logScope.EndTime.Value,
                Status = logScope.Status
            };

            Console.WriteLine(ModelSerializer.Serialize<EndScopeCommunicationMessage>(communicationMessage));
        }

        private void CommandsSource_OnBeginLogScopeCommand(Orangebeard.Shared.Execution.ILogContext logContext, Orangebeard.Shared.Extensibility.Commands.CommandArgs.LogScopeCommandArgs args)
        {
            var logScope = args.LogScope;

            var communicationMessage = new BeginScopeCommunicationMessage
            {
                Id = logScope.Id,
                ParentScopeId = logScope.Parent?.Id,
                Name = logScope.Name,
                BeginTime = logScope.BeginTime
            };

            Console.WriteLine(ModelSerializer.Serialize<BeginScopeCommunicationMessage>(communicationMessage));
        }
    }
}
