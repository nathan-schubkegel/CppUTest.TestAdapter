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

using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Schuub.CppUTest.TestAdapter.Tests
{
  public class CppUTestExecutorTests
  {
    [Test]
    public void RunTests_ForWholeAssembly_ProducesExpectedResults()
    {
      // learn all the test cases
      var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NativeTestExe.exe");
      var testCases = new List<TestCase>();
      var logMessages = new List<string>();
      CppUTestDiscoverer.LearnTestCases(assemblyPath,
        logger: (level, message) => logMessages.Add($"{level}: {message}"),
        testCases.Add,
        cancelToken: CancellationToken.None);

      Assert.That(logMessages, Is.EqualTo(new[]
      {
        "Informational: Discovered 4 tests in NativeTestExe.exe",
      }));

      // Then try to run them
      logMessages.Clear();
      var recordMessages = new List<string>();
      new CppUTestExecutor().RunTests(testCases,
        logger: (level, message) => logMessages.Add($"{level}: {message}"),
        isBeingDebugged: false,
        launchProcessWithDebuggerAttached: null,
        recordStart: (testCase) => recordMessages.Add($"RecordStart: {Path.GetFileName(testCase.Source)} - {testCase.FullyQualifiedName}"),
        recordEnd: (testCase, outcome) => recordMessages.Add($"RecordEnd: {Path.GetFileName(testCase.Source)} - {testCase.FullyQualifiedName} - {outcome}"),
        recordResult: (testResult) => recordMessages.Add($"RecordResult: {Path.GetFileName(testResult.TestCase.Source)} - {testResult.TestCase.FullyQualifiedName} - {testResult.Outcome} - {testResult.ErrorMessage}")
      );

      Assert.That(logMessages, Is.Empty);

      Assert.That(recordMessages.Take(7).ToArray(), Is.EqualTo(new[]
      {
        "RecordStart: NativeTestExe.exe - AquaManAndBarnacleBoy_testGroup.test_PassingTest2",
        "RecordStart: NativeTestExe.exe - AquaManAndBarnacleBoy_testGroup.test_PassingTest1",
        "RecordStart: NativeTestExe.exe - MyFunnyValentine_testGroup.test_PassingTest2",
        "RecordStart: NativeTestExe.exe - MyFunnyValentine_testGroup.test_FailingTest1",
        "RecordResult: NativeTestExe.exe - AquaManAndBarnacleBoy_testGroup.test_PassingTest2 - Passed - ",
        "RecordResult: NativeTestExe.exe - AquaManAndBarnacleBoy_testGroup.test_PassingTest1 - Passed - ",
        "RecordResult: NativeTestExe.exe - MyFunnyValentine_testGroup.test_PassingTest2 - Passed - ",
      }));

      var failureMessage = recordMessages[7];
      Assert.That(failureMessage, Does.Match("^" + Regex.Escape(@"RecordResult: NativeTestExe.exe - MyFunnyValentine_testGroup.test_FailingTest1 - Failed - At line 23 of ") + @".*ExampleTests.cpp"));
      Assert.That(failureMessage, Does.Contain(@"ExampleTests.cpp:26: expected <steve>{newline} but was  <harvey>{newline} difference starts at position 0 at: <          harvey    >{newline}"));
      Assert.That(recordMessages.Skip(8).ToArray(), Is.Empty);
    }
  }
}
