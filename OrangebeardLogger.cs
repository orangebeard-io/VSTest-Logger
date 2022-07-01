﻿using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Orangebeard.Client;
using Orangebeard.Client.Converters;
using Orangebeard.Client.Entities;
using Orangebeard.Client.OrangebeardProperties;
using Orangebeard.Shared.Configuration;
using Orangebeard.Shared.Internal.Logging;
using Orangebeard.Shared.MimeTypes;
using Orangebeard.VSTest.TestLogger.Configuration;
using Orangebeard.VSTest.TestLogger.LogHandler.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Attribute = Orangebeard.Client.Entities.Attribute;

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

        private OrangebeardV2Client _orangebeard;

        //private ILaunchReporter _launchReporter;
        //private readonly Dictionary<string, ITestReporter> _suites = new Dictionary<string, ITestReporter>();
        private Guid? _testRunUuid;
        private readonly Dictionary<string, Guid> _suites = new Dictionary<string, Guid>();

        private static readonly Dictionary<TestOutcome, Status> _statusMapping = new Dictionary<TestOutcome, Status>
            {
                { TestOutcome.Passed, Status.PASSED },
                { TestOutcome.Failed, Status.FAILED },
                { TestOutcome.None, Status.SKIPPED },
                { TestOutcome.Skipped, Status.SKIPPED },
                { TestOutcome.NotFound, Status.SKIPPED }
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
                    _orangebeard = new OrangebeardV2Client(_config.Endpoint, new Guid(_config.AccessToken), _config.ProjectName, _config.TestSetName, true);

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

        private void HandleTestRunStart(object sender, TestRunStartEventArgs e)
        {
            try
            {
                var attributes = new HashSet<Attribute>(_config.Attributes);
                var startTestRun = new StartTestRun(_config.TestSetName, _config.Description, attributes);

                _testRunUuid = _orangebeard.StartTestRun(startTestRun);
            }
            catch (Exception ex)
            {
                HandleLoggerException(nameof(HandleTestRunStart), ex);
                throw;
            }
        }

        private void HandleTestResult(object sender, TestResultEventArgs e)
        {
            if (_testRunUuid != null)
            {
                try
                {
                    var innerResultsCountProperty = e.Result.Properties.FirstOrDefault(p => p.Id == "InnerResultsCount");
                    if (innerResultsCountProperty == null || (innerResultsCountProperty != null && (int)e.Result.GetPropertyValue(innerResultsCountProperty) == 0))
                    {
                        DetermineClassNameAndTestName(e, out string className, out string testName);

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

                        string testDescription = FindDescription(e.Result.TestCase);

                        List<string> testCategories = FindCategories(e.Result.TestCase);

                        // start test node

                        var testAttributesEnumerable = (testCategories.Select(category => new Attribute(value: category)));
                        var testAttributes = new HashSet<Attribute>(testAttributesEnumerable);
                        var startTest = new StartTestItem(_testRunUuid.Value, testName, TestItemType.TEST, testDescription, testAttributes);

                        var testItemUuid = _orangebeard.StartTestItem(suiteReporter, startTest);

                        AddLogMessages(e.Result, testItemUuid.Value);

                        AddAttachments(e, testItemUuid.Value);

                        FinishTest(e, testItemUuid.Value);
                    }
                }
                catch (Exception ex)
                {
                    HandleLoggerException(nameof(HandleTestResult), ex);
                }
            }
        }

        private void HandleTestRunComplete(object sender, TestRunCompleteEventArgs e)
        {
            if (_testRunUuid != null)
            {
                try
                {                    
                    while (_suites.Count != 0)
                    {
                        var deepestSuite = _suites.Keys.OrderBy(s => s.Split('.').Length).Last();

                        TraceLogger.Verbose($"Finishing suite '{deepestSuite}'");
                        var deeperSuite = _suites[deepestSuite];

                        //TODO: identify correct suite status based on inner nodes
                        var finishSuiteRequest = new FinishTestItem(_testRunUuid.Value, Status.PASSED);

                        _orangebeard.FinishTestItem(deeperSuite, finishSuiteRequest);
                        _suites.Remove(deepestSuite);
                    }

                    // finish launch
                    var requestFinishTestRun = new FinishTestRun();
                    _orangebeard.FinishTestRun(_testRunUuid.Value, requestFinishTestRun);
                }
                catch (Exception ex)
                {
                    HandleLoggerException(nameof(HandleTestRunComplete), ex);
                }
            }
        }

        private void DetermineClassNameAndTestName(TestResultEventArgs e, out string className, out string testName)
        {
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
        }

        private Guid GetOrStartSuiteNode(string fullName, DateTime startTime)
        {
            try
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
                            var startSuite = new StartTestItem(_testRunUuid.Value, fullName, TestItemType.SUITE, null, null);
                            var rootSuite = _orangebeard.StartTestItem(null, startSuite);
                            _suites[fullName] = rootSuite.Value;
                            return rootSuite.Value;
                        }
                    }
                    else
                    {
                        var parent = GetOrStartSuiteNode(string.Join(".", parts.Take(parts.Length - 1)), startTime);

                        // create suite
                        var startSuite = new StartTestItem(_testRunUuid.Value, parts.Last(), TestItemType.SUITE, null, null);
                        var suite = _orangebeard.StartTestItem(parent, startSuite);
                        _suites[fullName] = suite.Value;
                        return suite.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                HandleLoggerException(nameof(GetOrStartSuiteNode), ex);
                throw;
            }
        }

        private static string FindDescription(TestCase testCase)
        {
            // find description
            var testDescription = testCase.Traits.FirstOrDefault(x => x.Name == "Description")?.Value;

            if (testCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
            {
                var testProperty = testCase.Properties.FirstOrDefault(p => p.Id == "Description");
                if (testProperty != null)
                {
                    testDescription = testCase.GetPropertyValue(testProperty).ToString();
                }
            }

            return testDescription;
        }

        private static List<string> FindCategories(TestCase testCase)
        {
            // find categories
            var testCategories = testCase.Traits.Where(t => t.Name.ToLower() == "category").Select(x => x.Value).ToList();

            if (testCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
            {
                var testProperty = testCase.Properties.FirstOrDefault(p => p.Id == "MSTestDiscoverer.TestCategory");
                if (testProperty != null)
                {
                    testCategories.AddRange((string[])testCase.GetPropertyValue(testProperty));
                }
            }
            else if (testCase.ExecutorUri.ToString().ToLower().Contains("nunit"))
            {
                var testProperty = testCase.Properties.FirstOrDefault(p => p.Id == "NUnit.TestCategory");
                if (testProperty != null)
                {
                    testCategories.AddRange((string[])testCase.GetPropertyValue(testProperty));
                }
            }

            return testCategories;
        }

        private void AddLogMessages(TestResult testResult, Guid testUuid)
        {
            // add log messages
            if (testResult.Messages != null)
            {
                foreach (var message in testResult.Messages)
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
                                    handled = HandleAddLogCommunicationAction(testUuid, addLogCommunicationMessage);
                                    break;
                                case CommunicationAction.BeginLogScope:
                                    var beginLogScopeCommunicationMessage = ModelSerializer.Deserialize<BeginScopeCommunicationMessage>(textMessage);
                                    handled = HandleBeginLogScopeCommunicationAction(testUuid, beginLogScopeCommunicationMessage);
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

                            Log log = new Log(_testRunUuid.Value, testUuid, LogLevel.info, line, LogFormat.PLAIN_TEXT);
                            _orangebeard.Log(log);
                        }
                    }
                }
            }

            if (testResult.ErrorMessage != null)
            {
                string message = testResult.ErrorMessage + "\n" + testResult.ErrorStackTrace;
                Log log = new Log(_testRunUuid.Value, testUuid, LogLevel.error, message, LogFormat.PLAIN_TEXT);
                _orangebeard.Log(log);
            }
        }

        private void AddAttachments(TestResultEventArgs e, Guid? testUuid)
        {
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
                            FileInfo fileInfo = new FileInfo(filePath);
                            Attachment.AttachmentFile attachmentFile = new Attachment.AttachmentFile(fileInfo);
                            Attachment attachment = new Attachment(_testRunUuid.Value, testUuid.Value, LogLevel.info, fileInfo.Name, attachmentFile);
                            _orangebeard.SendAttachment(attachment);
                        }
                        catch (Exception exp)
                        {
                            var error = $"Cannot read a content of '{filePath}' file: {exp.Message}";

                            Log log = new Log(_testRunUuid.Value, testUuid.Value, LogLevel.warn, error, LogFormat.PLAIN_TEXT);
                            _orangebeard.Log(log);

                            TraceLogger.Error(error);
                        }
                    }
                }
            }
        }

        private void FinishTest(TestResultEventArgs e, Guid testUuid)
        {
            // finish test
            var finishTestItem = new FinishTestItem(_testRunUuid.Value, _statusMapping[e.Result.Outcome]);
            _orangebeard.FinishTestItem(testUuid, finishTestItem);
        }

        private Dictionary<string, Guid> _nestedSteps = new Dictionary<string, Guid>();

        private bool HandleAddLogCommunicationAction(Guid? testUuid, AddLogCommunicationMessage message)
        {
            if (message.ParentScopeId != null)
            {
                testUuid = _nestedSteps[message.ParentScopeId];
            }

            if (message.Attach != null)
            {
                string filename = "dummy"; //TODO!~ Find a PROPER filename!
                Attachment.AttachmentFile attachmentFile = new Attachment.AttachmentFile(filename, message.Attach.MimeType, message.Attach.Data);
                Attachment attachment = new Attachment(_testRunUuid.Value, testUuid.Value, message.Level, filename, attachmentFile);
                _orangebeard.SendAttachment(attachment);
            }
            else
            {
                var log = new Log(_testRunUuid.Value, testUuid.Value, message.Level, message.Text, LogFormat.PLAIN_TEXT);
                _orangebeard.Log(log);
            }

            return true;
        }

        private bool HandleBeginLogScopeCommunicationAction(Guid testReporter, BeginScopeCommunicationMessage message)
        {
            var startTestItem = new StartTestItem(_testRunUuid.Value, message.Name, TestItemType.STEP, null, null);

            if (message.ParentScopeId != null)
            {
                testReporter = _nestedSteps[message.ParentScopeId];
            }

            var nestedStep = _orangebeard.StartTestItem(testReporter, startTestItem);

            _nestedSteps[message.Id] = nestedStep.Value;

            return true;
        }


        /// <summary>
        /// Status of logging scope.
        /// </summary>
        public enum LogScopeStatus
        {
            InProgress,
            Passed,
            Failed,
            Skipped
        }

        private readonly Dictionary<LogScopeStatus, Status> _nestedStepStatusMap = new Dictionary<LogScopeStatus, Status> {
            { LogScopeStatus.InProgress, Status.IN_PROGRESS },
            { LogScopeStatus.Passed, Status.PASSED },
            { LogScopeStatus.Failed, Status.FAILED },
            { LogScopeStatus.Skipped,Status.SKIPPED }
        };

        private bool HandleEndLogScopeCommunicationMessage(EndScopeCommunicationMessage message)
        {
            var nestedStep = _nestedSteps[message.Id];

            var finishTestItem = new FinishTestItem(_testRunUuid.Value, _nestedStepStatusMap[message.Status]);
            _orangebeard.FinishTestItem(nestedStep, finishTestItem);

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
