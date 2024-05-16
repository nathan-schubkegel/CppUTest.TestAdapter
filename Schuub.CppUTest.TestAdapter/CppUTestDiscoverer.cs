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
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Schuub.CppUTest.TestAdapter
{
  [FileExtension(".exe")]
  [DefaultExecutorUri(CppUTestExecutor.ExecutorUriString)]
  public class CppUTestDiscoverer : ITestDiscoverer
  {
    /// <summary>
    /// Learns what CppUTest test cases exist in the given assemblies.
    /// </summary>
    public void DiscoverTests(
      IEnumerable<string> sources, 
      IDiscoveryContext discoveryContext, 
      IMessageLogger logger, 
      ITestCaseDiscoverySink discoverySink)
    {
      foreach (string sourceAssembly in sources)
      {
        var assemblyFileName = Path.GetFileName(sourceAssembly);
        try
        {
          if (!IsCppUTestExe(sourceAssembly))
          {
            continue;
          }

          using (var cancelSource = new CancellationTokenSource(5000))
          {
            LearnTestCases(sourceAssembly,
              (level, message) => logger.SendMessage(level, message),
              (testCase) => discoverySink.SendTestCase(testCase),
              cancelToken: cancelSource.Token);
          }
        }
        catch (Exception ex)
        {
          logger.SendMessage(TestMessageLevel.Error, $"Error discovering tests in {assemblyFileName} - {ex}");
        }
      }
    }

    /// <summary>
    /// Learns what CppUTest test cases exist in the given assemblies.
    /// </summary>
    public static void LearnTestCases(string sourceAssembly,
      Action<TestMessageLevel, string> logger,
      Action<TestCase> discoverySink,
      CancellationToken cancelToken)
    {
      var assemblyFileName = Path.GetFileName(sourceAssembly);

      // Support for the -ln argument was added to CppUTest in commit d603ba115a6e91fea8800447d16d1730d4d0963a
      // and released in v3.7
      string procOutput = RunProcessAndCollectOutupt(sourceAssembly, "-ln", cancelToken);

      var testCases = new List<(string FullName, string GroupName, string TestName)>();
      if (string.IsNullOrEmpty(procOutput?.Trim()))
      {
        logger(TestMessageLevel.Informational, $"No tests found in {assemblyFileName}");
        return;
      }

      foreach (var name in procOutput.Split(' '))
      {
        var parts = name.Split('.');
        if (parts.Length != 2)
        {
          throw new InvalidDataException($"Unsupported format of test name \"{name}\" reported by {assemblyFileName}");
        }
        testCases.Add((name, parts[0], parts[1]));
      }

      if (testCases.Count == 0)
      {
        logger(TestMessageLevel.Informational, $"No tests found in {assemblyFileName}");
        return;
      }

      foreach (var testCase in testCases)
      {
        discoverySink(new TestCase(testCase.FullName, CppUTestExecutor.ExecutorUri, sourceAssembly));
      }

      logger(TestMessageLevel.Informational, $"Discovered {testCases.Count} tests in {assemblyFileName}");
    }

    /// <summary>
    /// Runs the given process and collects its STDOUT.
    /// Gives up early and kills the process if the cancel token becomes signaled.
    /// </summary>
    /// <returns>The process's standard output.</returns>
    private static string RunProcessAndCollectOutupt(string exeFilePath, string arguments, CancellationToken cancelToken)
    {
      var startInfo = new ProcessStartInfo(exeFilePath, arguments)
      {
        RedirectStandardOutput = true,
        RedirectStandardInput = true,
      };
      var proc = Process.Start(startInfo);
      try
      {
        proc.StandardInput.Close();

        // use a thread to read standard output (because it's a blocking operation)
        string procOutput = "";
        var t = new Thread(() =>
        {
          try
          {
            procOutput = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
          }
          catch
          {
            // gotta eat exceptions or it'll bring the application down
          }
        });
        t.IsBackground = true;
        t.Start();
        
        while (t.IsAlive && !cancelToken.IsCancellationRequested)
        {
          Thread.Sleep(1);
        }

        cancelToken.ThrowIfCancellationRequested();

        return procOutput;
      }
      finally
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
          // well... can't do much if it won't die
        }
        proc.Dispose();
      }
    }

    /// <summary>
    /// This string was added to CppUTest in commit bdec98d5e6170e4888c593e71276e46add633f0a
    /// and was released in v4.0
    /// </summary>
    private static readonly byte[] _magicBytes = Encoding.ASCII.GetBytes("Thanks for using CppUTest.");

    /// <summary>
    /// Determines whether the given file is most likely a CppUTest test exe.
    /// </summary>
    public static bool IsCppUTestExe(string filePath)
    {
      var buffer = new byte[4096];
      using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
      {
        while (true)
        {
          // move the last N bytes of 'buffer' to the front
          Array.Copy(buffer, buffer.Length - _magicBytes.Length, buffer, 0, _magicBytes.Length);

          // read more bytes after that
          int count = fileStream.Read(buffer, _magicBytes.Length, buffer.Length - _magicBytes.Length);
          if (count == 0)
          {
            break; // stop when file is exhausted
          }
          
          // assess all of them
          if (BufferContainsMagicBytes(buffer, _magicBytes.Length + count))
          {
            return true;
          }
        }
      }
      return false;
    }

    /// <summary>
    /// Determines whether the given buffer contains the magic bytes that only a CppUTest executable would contain.
    /// </summary>
    private static bool BufferContainsMagicBytes(byte[] buffer, int count)
    {
      for (int i = 0; i < count; i++)
      {
        if (i + _magicBytes.Length > count)
        {
          break;
        }

        int matchingByteCount = 0;
        for (int j = 0; j < _magicBytes.Length; j++)
        {
          if (buffer[i + j] == _magicBytes[j])
          {
            matchingByteCount++;
          }
          else
          {
            break;
          }
        }

        if (matchingByteCount == _magicBytes.Length)
        {
          return true;
        }
      }

      return false;
    }
  }
}
