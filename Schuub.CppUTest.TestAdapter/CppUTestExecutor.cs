//
// This is free and unencumbered software released into the public domain.
//
// Anyone is free to copy, modify, publish, use, compile, sell, or
// distribute this software, either in source code form or as a compiled
// binary, for any purpose, commercial or non-commercial, and by any
// means.
//
// For more information, please refer to <https://unlicense.org>
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Schuub.CppUTest.TestAdapter
{
  /// <summary>
  /// Launch the specified process with the debugger attached.
  /// </summary>
  /// <param name="filePath">File path to the exe to launch.</param>
  /// <param name="workingDirectory">Working directory that process should use.</param>
  /// <param name="arguments">Command line arguments the process should be launched with.</param>
  /// <param name="environmentVariables">Environment variables to be set in target process</param>
  /// <returns>Process ID of the started process.</returns>
  public delegate int LaunchProcessWithDebuggerAttachedDelegate(string filePath, string workingDirectory, string arguments, IDictionary<string, string> environmentVariables);

  [ExtensionUri(ExecutorUriString)]
  public class CppUTestExecutor : ITestExecutor
  {
    ///<summary>
    /// The Uri used to identify the NUnitExecutor
    ///</summary>
    public const string ExecutorUriString = "executor://SchuubCppUTestTestAdapter";

    public static readonly Uri ExecutorUri = new Uri(ExecutorUriString);

    private TaskCompletionSource<object> _cancelSource = new TaskCompletionSource<object>();

    public void Cancel()
    {
      _cancelSource.TrySetCanceled();
    }

    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
      RunTests(tests.ToList(), frameworkHandle.SendMessage, runContext.IsBeingDebugged,
        frameworkHandle.LaunchProcessWithDebuggerAttached,
        frameworkHandle.RecordStart,
        frameworkHandle.RecordEnd,
        frameworkHandle.RecordResult);
    }

    public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
      var testCases = new List<TestCase>();

      foreach (var sourceAssembly in sources)
      {
        if (!CppUTestDiscoverer.IsCppUTestExe(sourceAssembly))
        {
          continue;
        }

        using (var cancelSource = new CancellationTokenSource())
        {
          _cancelSource.Task.ContinueWith(_ => cancelSource.Cancel());
          
          CppUTestDiscoverer.LearnTestCases(sourceAssembly,
            frameworkHandle.SendMessage,
            (testCase) => testCases.Add(testCase),
            cancelToken: cancelSource.Token);
        }

        if (testCases.Count == 0)
        {
          continue;
        }
      }

      RunTests(testCases, frameworkHandle.SendMessage, runContext.IsBeingDebugged,
        frameworkHandle.LaunchProcessWithDebuggerAttached,
        frameworkHandle.RecordStart,
        frameworkHandle.RecordEnd,
        frameworkHandle.RecordResult);
    }

    /// <summary>
    /// Runs the given test cases, which might be from different assemblies.
    /// </summary>
    public void RunTests(
      List<TestCase> allTests, 
      Action<TestMessageLevel, string> logger,
      bool isBeingDebugged,
      LaunchProcessWithDebuggerAttachedDelegate launchProcessWithDebuggerAttached,
      Action<TestCase> recordStart, 
      Action<TestCase, TestOutcome> recordEnd, 
      Action<TestResult> recordResult)
    {
      foreach (var testsInSource in allTests.GroupBy(x => x.Source))
      {
        var assemblyFilePath = testsInSource.Key;
        var testCases = testsInSource.ToList();
        var testCasesByFullName = testCases.ToDictionary(x => x.FullyQualifiedName);
        var testCasesNeedingReporting = new HashSet<TestCase>(testCases);
        foreach (var testCase in testCases)
        {
          recordStart(testCase);
        }
        try
        {
          // make a temporary directory to serve as CWD
          var tempDirPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
          Directory.CreateDirectory(tempDirPath);
          try
          {
            RunProcess(
              launchProcessWithDebuggerAttached: isBeingDebugged ? launchProcessWithDebuggerAttached : null,
              filePath: assemblyFilePath,
              workingDirectory: tempDirPath,
              arguments: "-ojunit");

            foreach (var xmlFilePath in Directory.EnumerateFiles(tempDirPath, "*.xml"))
            {
              foreach (var testResult in ReadTestResults(xmlFilePath, testCasesByFullName))
              {
                recordResult(testResult);
                testCasesNeedingReporting.Remove(testResult.TestCase);
              }
            }
          }
          finally
          {
            try 
            { 
              Directory.Delete(tempDirPath, true);
            } 
            catch (Exception ex)
            {
              logger(TestMessageLevel.Error, $"Failed to delete temp directory {tempDirPath} after running tests for {assemblyFilePath} - {ex}");
            }
          }
        }
        finally
        {
          foreach (var testCase in testCasesNeedingReporting)
          {
            recordEnd(testCase, TestOutcome.Skipped);
          }
        }
      }
    }

    /// <summary>
    /// Records test results for the tiven XML file.
    /// </summary>
    private static IEnumerable<TestResult> ReadTestResults(string xmlFilePath, 
      Dictionary<string, TestCase> testCasesByFullName)
    {
      var xmlText = File.ReadAllText(xmlFilePath);
      var doc = XElement.Parse(xmlText);
      foreach (var testResult in doc.Elements("testcase"))
      {
        var groupName = testResult.Attribute("classname").Value;
        var testName = testResult.Attribute("name").Value;
        var testId = $"{groupName}.{testName}";
        var testCase = testCasesByFullName[testId];

        var failureElement = testResult.Element("failure");
        if (failureElement == null)
        {
          yield return new TestResult(testCase)
          {
            Outcome = TestOutcome.Passed,
          };
        }
        else
        {
          var failureMessage = failureElement.Attribute("message")?.Value ?? "Test failed.";

          var file = testResult.Attribute("file")?.Value ?? "";
          var lineNumber = testResult.Attribute("line")?.Value ?? "";
          if (file.Length > 0)
          {
            string prefix = lineNumber.Length > 0
              ? $"At line {lineNumber} of {file}"
              : $"In {file}";

            failureMessage = $"{prefix}{Environment.NewLine}{Environment.NewLine}{failureMessage}";
          }

          yield return new TestResult(testCase)
          {
            ErrorMessage = failureMessage,
            Outcome = TestOutcome.Failed,
          };
        }
      }
    }

    /// <summary>
    /// Runs a process command and waits for it to exit.
    /// </summary>
    /// <param name="launchProcessWithDebuggerAttached">If not null, then this delegate should be
    /// used to launch the process.</param>
    private void RunProcess(
      LaunchProcessWithDebuggerAttachedDelegate launchProcessWithDebuggerAttached, 
      string filePath, 
      string workingDirectory, 
      string arguments)
    {
      Process proc = null;
      try
      {
        if (launchProcessWithDebuggerAttached != null)
        {
          int pid = launchProcessWithDebuggerAttached(
            filePath: filePath,
            workingDirectory: workingDirectory,
            arguments,
            environmentVariables: null);
          
          proc = Process.GetProcessById(pid);
          _ = proc.Handle; // force it to acquire the handle now
        }
        else
        {
          var startInfo = new ProcessStartInfo(filePath, arguments)
          {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
          };
          proc = Process.Start(startInfo);
        }

        while (!proc.HasExited && !_cancelSource.Task.IsCompleted)
        {
          Thread.Sleep(100);
        }
      }
      finally
      {
        if (proc != null)
        {
          try
          {
            if (!proc.HasExited)
            {
              proc.Kill();
            }
          }
          catch
          {
            // can't do much if the process won't die...
          }
          proc.Dispose();
        }
      }
    }
  }
}
