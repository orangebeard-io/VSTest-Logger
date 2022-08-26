using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Orangebeard.Client;
using Orangebeard.Client.Abstractions.Models;
using Orangebeard.Client.Abstractions.Requests;
using Orangebeard.Client.Converters;
using Orangebeard.Client.OrangebeardProperties;
using Orangebeard.Shared.Configuration;
using Orangebeard.Shared.Execution.Logging;
using Orangebeard.Shared.Extensibility;
using Orangebeard.Shared.Internal.Logging;
using Orangebeard.Shared.MimeTypes;
using Orangebeard.Shared.Reporter;
using Orangebeard.VSTest.TestLogger.Configuration;
using Orangebeard.VSTest.TestLogger.LogHandler.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Orangebeard.VSTest.TestLogger
{
    [ExtensionUri("logger://Orangebeard")]
    [FriendlyName("Orangebeard")]
    public class OrangebeardLogger : ITestLoggerWithParameters
    {
        private ITraceLogger TraceLogger { get; }

        public static Dictionary<TestOutcome, Status> StatusMapping => _statusMapping;

        private readonly IConfigurationBuilder _configBuilder;
        private IConfiguration _configuration;
        private OrangebeardConfiguration _config;

        private OrangebeardClient _orangebeard;

        private ILaunchReporter _launchReporter;
        private readonly Dictionary<string, ITestReporter> _suites = new Dictionary<string, ITestReporter>();

        private static readonly Dictionary<TestOutcome, Status> _statusMapping = new Dictionary<TestOutcome, Status>
            {
                { TestOutcome.Passed, Status.Passed },
                { TestOutcome.Failed, Status.Failed },
                { TestOutcome.None, Status.Skipped },
                { TestOutcome.Skipped, Status.Skipped },
                { TestOutcome.NotFound, Status.Skipped }
            };

        public OrangebeardLogger()
        {
            var testLoggerDirectory = Path.GetDirectoryName(new Uri(typeof(OrangebeardLogger).Assembly.CodeBase).LocalPath);

            TraceLogger = TraceLogManager.Instance.WithBaseDir(testLoggerDirectory).GetLogger(typeof(OrangebeardLogger));

            var jsonPath = Path.Combine(testLoggerDirectory, "Orangebeard.config.json");
            _configBuilder = new ConfigurationBuilder().AddJsonFile(jsonPath).AddEnvironmentVariables();           
        }

        public void Initialize(TestLoggerEvents events, string testRunDirectory)
        {
            try
            {
                _configuration = _configBuilder.Build();
                if (_configuration.GetValue("Enabled", true))
                {
                    _config = new OrangebeardConfiguration(_configuration).WithListenerIdentification(
                            "VSTest Logger/" +
                            typeof(OrangebeardLogger).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion
                            );
                    _orangebeard = new OrangebeardClient(_config);

                    events.TestRunStart += HandleTestRunStart;
                    events.TestResult += HandleTestResult;
                    events.TestRunComplete += HandleTestRunComplete;
                }
            }
            catch (Exception e)
            {
                HandleLoggerException(nameof(Initialize), e);
                throw;
            }
        }

        public void Initialize(TestLoggerEvents events, Dictionary<string, string> parameters)
        {
            try
            {
                _configBuilder.Add(new ConfigProvider(parameters));
                Initialize(events, parameters.Single(p => p.Key == "TestRunDirectory").Value);
            }
            catch (Exception ex)
            {
                HandleLoggerException(nameof(Initialize), ex);
                throw;
            }
        }
        private void HandleTestRunComplete(object sender, TestRunCompleteEventArgs e)
        {
            if (_launchReporter != null)
            {
                try
                {                    
                    while (_suites.Count != 0)
                    {
                        var deepestSuite = _suites.Keys.OrderBy(s => s.Split('.').Length).Last();

                        TraceLogger.Verbose($"Finishing suite '{deepestSuite}'");
                        var deeperSuite = _suites[deepestSuite];

                        var finishSuiteRequest = new FinishTestItemRequest
                        {
                            EndTime = DateTime.UtcNow,
                            //TODO: identify correct suite status based on inner nodes
                            Status = Status.Passed
                        };

                        deeperSuite.Finish(finishSuiteRequest);
                        _suites.Remove(deepestSuite);
                    }

                    // finish launch
                    var requestFinishLaunch = new FinishLaunchRequest
                    {
                        EndTime = DateTime.UtcNow
                    };

                    _launchReporter.Finish(requestFinishLaunch);

                    try
                    {
                        _launchReporter.Sync();
                    }
                    catch (Exception ex)
                    {
                        HandleLoggerException(nameof(HandleTestRunComplete), ex);
                        throw;
                    }

                   
                }
                catch (Exception ex)
                {
                    HandleLoggerException(nameof(HandleTestRunComplete), ex);
                }
            }
        }

        private void HandleTestResult(object sender, TestResultEventArgs e)
        {
            if (_launchReporter != null)
            {
                try
                {
                    var innerResultsCountProperty = e.Result.Properties.FirstOrDefault(p => p.Id == "InnerResultsCount");
                    if (innerResultsCountProperty == null || (innerResultsCountProperty != null && (int)e.Result.GetPropertyValue(innerResultsCountProperty) == 0))
                    {
                        string className;
                        string testName;
                        var fullName = e.Result.TestCase.FullyQualifiedName;
                        if (e.Result.TestCase.ExecutorUri.Host == "xunit")
                        {
                            var testMethodName = fullName.Split('.').Last();
                            var testClassName = fullName.Substring(0, fullName.LastIndexOf('.'));
                            var displayName = e.Result.TestCase.DisplayName;

                            testName = displayName == fullName
                                ? testMethodName
                                : displayName.Replace($"{testClassName}.", string.Empty);

                            className = testClassName;
                        }
                        else if (e.Result.TestCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
                        {
                            testName = e.Result.DisplayName ?? e.Result.TestCase.DisplayName;

                            var classNameProperty = e.Result.TestCase.Properties.FirstOrDefault(p => p.Id == "MSTestDiscoverer.TestClassName");
                            if (classNameProperty != null)
                            {
                                className = e.Result.TestCase.GetPropertyValue(classNameProperty).ToString();
                            }
                            // else get classname from FQN (mstestadapter/v1)
                            else
                            {
                                // and temporary consider testname from TestCase object instead of from Result object
                                // Result.DisplayName: Test1 (Data Row 0)
                                // TestCase.DisplayName Test1
                                // the first name is better in report, but consider the second name to identify 'className'
                                testName = e.Result.TestCase.DisplayName ?? e.Result.DisplayName;
                                className = fullName.Substring(0, fullName.Length - testName.Length - 1);
                                testName = e.Result.DisplayName ?? e.Result.TestCase.DisplayName;
                            }
                        }
                        else
                        {
                            testName = e.Result.TestCase.DisplayName ?? fullName.Split('.').Last();
                            className = fullName.Substring(0, fullName.Length - testName.Length - 1);
                        }

                        TraceLogger.Info($"ClassName: {className}, TestName: {testName}");

                        var rootNamespaces = _configuration.GetValues<string>("rootNamespaces", null);
                        if (rootNamespaces != null)
                        {
                            var rootNamespace = rootNamespaces.FirstOrDefault(rns => className.StartsWith(rns));
                            if (rootNamespace != null)
                            {
                                className = className.Substring(rootNamespace.Length + 1);
                                TraceLogger.Verbose($"Cutting '{rootNamespace}'... New ClassName is '{className}'.");
                            }
                        }

                        var suiteReporter = GetOrStartSuiteNode(className, e.Result.StartTime.UtcDateTime);

                        // find description
                        var testDescription = e.Result.TestCase.Traits.FirstOrDefault(x => x.Name == "Description")?.Value;

                        if (e.Result.TestCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
                        {
                            var testProperty = e.Result.TestCase.Properties.FirstOrDefault(p => p.Id == "Description");
                            if (testProperty != null)
                            {
                                testDescription = e.Result.TestCase.GetPropertyValue(testProperty).ToString();
                            }
                        }

                        // find categories
                        var testCategories = e.Result.TestCase.Traits.Where(t => t.Name.ToLower() == "category").Select(x => x.Value).ToList();

                        if (e.Result.TestCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
                        {
                            var testProperty = e.Result.TestCase.Properties.FirstOrDefault(p => p.Id == "MSTestDiscoverer.TestCategory");
                            if (testProperty != null)
                            {
                                testCategories.AddRange((string[])e.Result.TestCase.GetPropertyValue(testProperty));
                            }
                        }
                        else if (e.Result.TestCase.ExecutorUri.ToString().ToLower().Contains("nunit"))
                        {
                            var testProperty = e.Result.TestCase.Properties.FirstOrDefault(p => p.Id == "NUnit.TestCategory");
                            if (testProperty != null)
                            {
                                testCategories.AddRange((string[])e.Result.TestCase.GetPropertyValue(testProperty));
                            }
                        }

                        // start test node
                        
                        var startTestRequest = new StartTestItemRequest
                        {
                            Name = testName,
                            Description = testDescription,
                            Attributes = (testCategories.Select(category => new ItemAttribute { Value = category })).ToList(),
                            StartTime = e.Result.StartTime.UtcDateTime,
                            Type = TestItemType.Test
                        };

                        var testReporter = suiteReporter.StartChildTestReporter(startTestRequest);

                        // add log messages
                        if (e.Result.Messages != null)
                        {
                            foreach (var message in e.Result.Messages)
                            {
                                if (message.Text == null) continue;
                                foreach (var line in message.Text.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    var handled = false;

                                    var textMessage = line;

                                    try
                                    {
                                        // SpecRun adapter adds this for output messages, just trim it for internal text messages
                                        if (line.StartsWith("-> "))
                                        {
                                            textMessage = line.Substring(3);
                                        }

                                        var baseCommunicationMessage = ModelSerializer.Deserialize<BaseCommunicationMessage>(textMessage);

                                        switch (baseCommunicationMessage.Action)
                                        {
                                            case CommunicationAction.AddLog:
                                                var addLogCommunicationMessage = ModelSerializer.Deserialize<AddLogCommunicationMessage>(textMessage);
                                                handled = HandleAddLogCommunicationAction(testReporter, addLogCommunicationMessage);
                                                break;
                                            case CommunicationAction.BeginLogScope:
                                                var beginLogScopeCommunicationMessage = ModelSerializer.Deserialize<BeginScopeCommunicationMessage>(textMessage);
                                                handled = HandleBeginLogScopeCommunicationAction(testReporter, beginLogScopeCommunicationMessage);
                                                break;
                                            case CommunicationAction.EndLogScope:
                                                var endLogScopeCommunicationMessage = ModelSerializer.Deserialize<EndScopeCommunicationMessage>(textMessage);
                                                handled = HandleEndLogScopeCommunicationMessage(endLogScopeCommunicationMessage);
                                                break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        HandleLoggerException(nameof(HandleTestResult), ex);
                                    }

                                    if (!handled)
                                    {
                                        // consider line output as usual user's log message

                                        testReporter.Log(new CreateLogItemRequest
                                        {
                                            Time = DateTime.UtcNow,
                                            Level = LogLevel.Info,
                                            Text = line
                                        });
                                    }
                                }
                            }
                        }

                        if (e.Result.ErrorMessage != null)
                        {
                            testReporter.Log(new CreateLogItemRequest
                            {
                                Time = e.Result.EndTime.UtcDateTime,
                                Level = LogLevel.Error,
                                Text = e.Result.ErrorMessage + "\n" + e.Result.ErrorStackTrace
                            });
                        }

                        // add attachments
                        if (e.Result.Attachments != null)
                        {
                            foreach (var attachmentSet in e.Result.Attachments)
                            {
                                foreach (var attachmentData in attachmentSet.Attachments)
                                {
                                    var filePath = attachmentData.Uri.LocalPath;

                                    try
                                    {
                                        var attachmentLogRequest = new CreateLogItemRequest
                                        {
                                            Level = LogLevel.Info,
                                            Text = Path.GetFileName(filePath),
                                            Time = e.Result.EndTime.UtcDateTime
                                        };

                                        var fileExtension = Path.GetExtension(filePath);

                                        byte[] bytes;

                                        using (var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                                        {
                                            using (var memoryStream = new MemoryStream())
                                            {
                                                fileStream.CopyTo(memoryStream);
                                                bytes = memoryStream.ToArray();
                                            }
                                        }

                                        attachmentLogRequest.Attach = new LogItemAttach(MimeTypeMap.GetMimeType(fileExtension), bytes);

                                        testReporter.Log(attachmentLogRequest);
                                    }
                                    catch (Exception exp)
                                    {
                                        var error = $"Cannot read a content of '{filePath}' file: {exp.Message}";

                                        testReporter.Log(new CreateLogItemRequest
                                        {
                                            Level = LogLevel.Warning,
                                            Time = e.Result.EndTime.UtcDateTime,
                                            Text = error
                                        });

                                        TraceLogger.Error(error);
                                    }
                                }
                            }
                        }

                        // finish test

                        // adjust end time, fixes https://github.com/reportportal/agent-net-vstest/issues/49
                        var endTime = e.Result.StartTime.UtcDateTime.Add(e.Result.Duration);

                        var finishTestRequest = new FinishTestItemRequest
                        {
                            EndTime = endTime,
                            Status = _statusMapping[e.Result.Outcome]
                        };

                        testReporter.Finish(finishTestRequest);
                    }
                }
                catch (Exception ex)
                {
                    HandleLoggerException(nameof(HandleTestResult), ex);                   
                }
            }
        }

        private void HandleTestRunStart(object sender, TestRunStartEventArgs e)
        {
            try
            {
                var startLaunch = new StartLaunchRequest
                {
                    Name = _config.TestSetName,
                    Description = _config.Description,
                    StartTime = DateTime.UtcNow
                };

                startLaunch.Attributes = _config.Attributes.ToList();

                _launchReporter = new LaunchReporter(_orangebeard, _configuration, null, new ExtensionManager());
                _launchReporter.Start(startLaunch);
            }
            catch (Exception ex)
            {
                HandleLoggerException(nameof(HandleTestRunStart), ex);
                throw;
            }
        }

        private ITestReporter GetOrStartSuiteNode(string fullName, DateTime startTime)
        {
            if (_suites.ContainsKey(fullName))
            {
                return _suites[fullName];
            }
            else
            {
                var parts = fullName.Split('.');

                if (parts.Length == 1)
                {
                    if (_suites.ContainsKey(parts[0]))
                    {
                        return _suites[parts[0]];
                    }
                    else
                    {
                        // create root
                        var startSuiteRequest = new StartTestItemRequest
                        {
                            Name = fullName,
                            StartTime = startTime,
                            Type = TestItemType.Suite
                        };

                        var rootSuite = _launchReporter.StartChildTestReporter(startSuiteRequest);

                        _suites[fullName] = rootSuite;
                        return rootSuite;
                    }
                }
                else
                {
                    var parent = GetOrStartSuiteNode(string.Join(".", parts.Take(parts.Length - 1)), startTime);

                    // create
                    var startSuiteRequest = new StartTestItemRequest
                    {
                        Name = parts.Last(),
                        StartTime = startTime,
                        Type = TestItemType.Suite
                    };

                    var suite = parent.StartChildTestReporter(startSuiteRequest);

                    _suites[fullName] = suite;

                    return suite;
                }
            }
        }

        private Dictionary<string, ITestReporter> _nestedSteps = new Dictionary<string, ITestReporter>();

        private bool HandleAddLogCommunicationAction(ITestReporter testReporter, AddLogCommunicationMessage message)
        {
            var logRequest = new CreateLogItemRequest
            {
                Level = message.Level,
                Time = message.Time,
                Text = message.Text
            };

            if (message.Attach != null)
            {
                logRequest.Attach = new LogItemAttach
                {
                    MimeType = message.Attach.MimeType,
                    Data = message.Attach.Data
                };
            }

            if (message.ParentScopeId != null)
            {
                testReporter = _nestedSteps[message.ParentScopeId];
            }

            testReporter.Log(logRequest);

            return true;
        }

        private bool HandleBeginLogScopeCommunicationAction(ITestReporter testReporter, BeginScopeCommunicationMessage message)
        {
            var startTestItemRequest = new StartTestItemRequest
            {
                Name = message.Name,
                StartTime = message.BeginTime,
                Type = TestItemType.Step,
                HasStats = false
            };

            if (message.ParentScopeId != null)
            {
                testReporter = _nestedSteps[message.ParentScopeId];
            }

            var nestedStep = testReporter.StartChildTestReporter(startTestItemRequest);

            _nestedSteps[message.Id] = nestedStep;

            return true;
        }

        private readonly Dictionary<LogScopeStatus, Status> _nestedStepStatusMap = new Dictionary<LogScopeStatus, Status> {
            { LogScopeStatus.InProgress, Status.InProgress },
            { LogScopeStatus.Passed, Status.Passed },
            { LogScopeStatus.Failed, Status.Failed },
            { LogScopeStatus.Skipped,Status.Skipped }
        };

        private bool HandleEndLogScopeCommunicationMessage(EndScopeCommunicationMessage message)
        {
            var nestedStep = _nestedSteps[message.Id];

            nestedStep.Finish(new FinishTestItemRequest
            {
                EndTime = message.EndTime,
                Status = _nestedStepStatusMap[message.Status]
            });

            _nestedSteps.Remove(message.Id);

            return true;
        }    

    private void HandleLoggerException(string caller, Exception e)
        {
            var msg = $"Exception in {caller}: {e}";
            TraceLogger.Error(msg);
            Console.WriteLine(msg);
        }

    }
}
