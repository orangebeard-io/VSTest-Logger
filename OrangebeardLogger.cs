using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Orangebeard.Client.V3;
using Orangebeard.Client.V3.Entity;
using Orangebeard.Client.V3.Entity.Attachment;
using Orangebeard.Client.V3.Entity.Log;
using Orangebeard.Client.V3.Entity.Suite;
using Orangebeard.Client.V3.Entity.Test;
using Orangebeard.Client.V3.Entity.TestRun;
using Orangebeard.Client.V3.MimeTypes;
using Orangebeard.Client.V3.OrangebeardConfig;

namespace Orangebeard.VSTest.TestLogger
{
    [ExtensionUri("logger://Orangebeard")]
    [FriendlyName("Orangebeard")]
    public class OrangebeardLogger : ITestLoggerWithParameters
    {
        private OrangebeardConfiguration _config;
        private OrangebeardAsyncV3Client _orangebeard;

        private readonly Dictionary<string, Guid> _suites = new Dictionary<string, Guid>();

        private static readonly Dictionary<TestOutcome, TestStatus> StatusMapping =
            new Dictionary<TestOutcome, TestStatus>
            {
                { TestOutcome.Passed, TestStatus.PASSED },
                { TestOutcome.Failed, TestStatus.FAILED },
                { TestOutcome.None, TestStatus.SKIPPED },
                { TestOutcome.Skipped, TestStatus.SKIPPED },
                { TestOutcome.NotFound, TestStatus.SKIPPED }
            };

        public void Initialize(TestLoggerEvents events, string testRunDirectory)
        {
            try
            {
                var listenerVersion = typeof(OrangebeardLogger).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    .InformationalVersion.Split('+')[0];

                _config = new OrangebeardConfiguration()
                    .WithListenerIdentification(
                        "VSTest Logger/" + listenerVersion
                    );
                _orangebeard = new OrangebeardAsyncV3Client(_config);

                events.TestRunStart += HandleTestRunStart;
                events.TestResult += HandleTestResult;
                events.TestRunComplete += HandleTestRunComplete;
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
            _orangebeard.StartTestRun(
                new StartTestRun()
                {
                    StartTime = DateTime.UtcNow,
                    TestSetName = _config.TestSetName,
                    Description = _config.Description ?? "",
                    Attributes = _config.Attributes,
                }
            );
        }

        private void HandleTestRunComplete(object sender, TestRunCompleteEventArgs e)
        {
            // finish run
            Task.Run(() => _orangebeard.FinishTestRun(_orangebeard.TestRunContext().TestRun, new FinishTestRun()))
                .Wait();
            Console.WriteLine("Orangebeard Report finished");
        }

        private void HandleTestResult(object sender, TestResultEventArgs e)
        {
            try
            {
                var innerResultsCountProperty = e.Result.Properties.FirstOrDefault(p => p.Id == "InnerResultsCount");
                if (innerResultsCountProperty != null &&
                    (int)e.Result.GetPropertyValue(innerResultsCountProperty) != 0) return;

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

                    var classNameProperty =
                        e.Result.TestCase.Properties.FirstOrDefault(p =>
                            p.Id == "MSTestDiscoverer.TestClassName");
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
                        testName = e.Result.TestCase.DisplayName.Length >= 3
                            ? e.Result.TestCase.DisplayName
                            : e.Result.DisplayName;
                        className = fullName.Substring(0, fullName.Length - testName.Length - 1);
                        testName = e.Result.DisplayName ?? e.Result.TestCase.DisplayName;
                    }
                }
                else
                {
                    testName = e.Result.TestCase.DisplayName.Length >= 3
                        ? e.Result.TestCase.DisplayName
                        : fullName.Split('.').Last();
                    className = fullName.Substring(0, fullName.Length - testName.Length - 1);
                }

                GetOrStartSuite(className);

                // find description
                var testDescription = e.Result.TestCase.Traits.FirstOrDefault(x => x.Name == "Description")
                    ?.Value;

                if (e.Result.TestCase.ExecutorUri.ToString().ToLower().Contains("mstest"))
                {
                    var testProperty = e.Result.TestCase.Properties.FirstOrDefault(p => p.Id == "Description");
                    if (testProperty != null)
                    {
                        testDescription = e.Result.TestCase.GetPropertyValue(testProperty).ToString();
                    }
                }

                // start test 
                _orangebeard.StartTest(new StartTest
                {
                    TestRunUUID = _orangebeard.TestRunContext().TestRun,
                    SuiteUUID = _orangebeard.TestRunContext().ActiveSuite(),
                    TestName = testName,
                    TestType = TestType.TEST,
                    Description = testDescription,
                    StartTime = e.Result.StartTime.UtcDateTime
                });
                
                // add log messages
                foreach (var message in e.Result.Messages.Select(message => message.Text))
                {
                    if (message != null)
                    {
                        SendLog(message, LogLevel.INFO);
                    }
                }

                if (e.Result.ErrorMessage != null)
                {
                    SendLog(e.Result.ErrorMessage + "\n" + e.Result.ErrorStackTrace, LogLevel.ERROR);
                }

                // add attachments
                foreach (var attachmentSet in e.Result.Attachments)
                {
                    foreach (var attachmentData in attachmentSet.Attachments)
                    {
                        var filePath = attachmentData.Uri.LocalPath;

                        try
                        {
                            SendAttachment(filePath);
                        }
                        catch (Exception exp)
                        {
                            var error = $"Could not read content of '{filePath}'.\n{exp.Message}";
                            SendLog(error, LogLevel.WARN);
                        }
                    }
                }

                // finish test
                var endTime = e.Result.StartTime.UtcDateTime.Add(e.Result.Duration);
                _orangebeard.FinishTest(_orangebeard.TestRunContext().ActiveTest().Value, new FinishTest
                {
                    TestRunUUID = _orangebeard.TestRunContext().TestRun,
                    Status = StatusMapping[e.Result.Outcome],
                    EndTime = endTime
                });
            }
            catch (Exception ex)
            {
                HandleLoggerException(nameof(HandleTestResult), ex);
            }
        }

        private void SendAttachment(string filePath)
        {
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

            var logId = SendLog(Path.GetFileName(filePath), LogLevel.INFO);

            var attachment = new Attachment
            {
                File = new AttachmentFile
                {
                    Name = Path.GetFileName(filePath),
                    Content = bytes,
                    ContentType = MimeTypeMap.GetMimeType(fileExtension),
                },
                MetaData = new AttachmentMetaData
                {
                    TestRunUUID = _orangebeard.TestRunContext().TestRun,
                    TestUUID = _orangebeard.TestRunContext().ActiveTest().Value,
                    StepUUID = _orangebeard.TestRunContext().ActiveStep(),
                    LogUUID = logId,
                    AttachmentTime = DateTime.UtcNow
                }
            };
            _ = _orangebeard.SendAttachment(attachment);
        }

        private Guid SendLog(string message, LogLevel level)
        {
            var logItem = new Log
            {
                TestRunUUID = _orangebeard.TestRunContext().TestRun,
                TestUUID = _orangebeard.TestRunContext().ActiveTest().Value,
                StepUUID = _orangebeard.TestRunContext().ActiveStep(),
                LogTime = DateTime.UtcNow,
                LogLevel = level,
                Message = message,
                LogFormat = LogFormat.PLAIN_TEXT
            };

            return _orangebeard.Log(logItem);
        }


        private void GetOrStartSuite(string fullName)
        {
            if (_suites.ContainsKey(fullName)) return;

            var suiteNames = fullName.Split('.');
            var suite = _orangebeard.StartSuite(new StartSuite
            {
                TestRunUUID = _orangebeard.TestRunContext().TestRun,
                SuiteNames = new List<string>(suiteNames)
            })[0];

            _suites[fullName] = suite;
        }
        
        private void HandleLoggerException(string caller, Exception e)
        {
            var msg = $"Exception in {caller}: {e}";
            Console.WriteLine(msg);
        }
    }
}