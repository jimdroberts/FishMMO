using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.UnitTests.Harness;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Adds entries under the <c>FishMMO/Unit Tests</c> menu for opening the
	/// Test Runner window and running the FishMMO.UnitTests EditMode assembly
	/// directly. Results from a direct run are logged to the Unity console.
	/// </summary>
	internal static class UnitTestMenu
	{
		private const string AssemblyName = "FishMMO.UnitTests";

		[MenuItem("FishMMO/Unit Tests/Open Test Runner", priority = 100)]
		public static void OpenTestRunner()
		{
			// Same path Unity uses for Window > General > Test Runner.
			EditorApplication.ExecuteMenuItem("Window/General/Test Runner");
		}

		[MenuItem("FishMMO/Unit Tests/Run All EditMode Tests", priority = 101)]
		public static void RunAllEditModeTests()
		{
			AuthTestTrace.Verbose = false;
			Execute();
		}

		[MenuItem("FishMMO/Unit Tests/Run All EditMode Tests (Verbose)", priority = 102)]
		public static void RunAllEditModeTestsVerbose()
		{
			AuthTestTrace.Verbose = true;
			AuthTestTrace.Reset();
			Execute();
		}

		[MenuItem("FishMMO/Unit Tests/Print Auth Assembly Identities", priority = 110)]
		public static void PrintAuthAssemblies()
		{
			FishMMO.Logging.Log.WritePartsToConsole(
				LogLevel.Info,
				"UnitTestMenu",
				0,
				("white", BuildAssemblyReport())
			).Wait();
		}

		private static void Execute()
		{
			FishMMO.Logging.Log.WritePartsToConsole(
				LogLevel.Info,
				"UnitTestMenu",
				0,
				("white", BuildAssemblyReport())
			).Wait();
			TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
			api.RegisterCallbacks(new ConsoleLogCallbacks());
			api.Execute(new ExecutionSettings(new Filter
			{
				testMode = TestMode.EditMode,
				assemblyNames = new[] { AssemblyName },
			}));
		}

		/// <summary>
		/// Build a multi-line report of every auth-related assembly's identity + on-disk location,
		/// so it's obvious whether the test is loading the compiled DLLs from
		/// <c>Assets/Dependencies</c> or has accidentally fallen back to local sources.
		/// </summary>
		private static string BuildAssemblyReport()
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("[FishMMO.UnitTests] Auth assembly identities (proves DLL provenance):");
			AppendAsm(sb, typeof(ClientAuthenticatorCore));          // FishMMO-ClientAuth
			AppendAsm(sb, typeof(SrpAuthenticatorCore<>));           // FishMMO-ServerAuth
			AppendAsm(sb, typeof(ClientAuthenticationResult));       // FishMMO-AuthShared
			AppendAsm(sb, typeof(CryptoHelper));                     // FishMMO-AuthShared (helper)
			AppendAsm(sb, typeof(InMemoryAccountStore));             // FishMMO.UnitTests
			return sb.ToString();
		}

		private static void AppendAsm(StringBuilder sb, Type t)
		{
			Assembly asm = t.Assembly;
			AssemblyName name = asm.GetName();
			string location;
			try { location = string.IsNullOrEmpty(asm.Location) ? "<dynamic>" : asm.Location; }
			catch (Exception ex) { location = $"<unavailable: {ex.GetType().Name}>"; }
			sb.AppendLine($"  • {name.Name} v{name.Version}  [{t.FullName}]");
			sb.AppendLine($"      location: {location}");
		}

		private sealed class ConsoleLogCallbacks : ICallbacks
		{
			private int passed;
			private int failed;
			private int skipped;
			private int other;

			public void RunStarted(ITestAdaptor testsToRun)
			{
				passed = failed = skipped = other = 0;
				FishMMO.Logging.Log.WritePartsToConsole(
					LogLevel.Info,
					"UnitTestMenu",
					0,
					("cyan", $"[FishMMO.UnitTests] Run started: {testsToRun.TestCaseCount} test case(s). Verbose={AuthTestTrace.Verbose}")
				).Wait();
			}

			public void TestStarted(ITestAdaptor test)
			{
				if (test.IsSuite) return;
				FishMMO.Logging.Log.WritePartsToConsole(
					LogLevel.Info,
					"UnitTestMenu",
					0,
					("white", $"[FishMMO.UnitTests] ▶ {test.FullName}")
				).Wait();
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.Test.IsSuite) return;

				switch (result.TestStatus)
				{
					case TestStatus.Passed:
						passed++;
						FishMMO.Logging.Log.WritePartsToConsole(
							LogLevel.Info,
							"UnitTestMenu",
							0,
							("green", $"[FishMMO.UnitTests] ✓ {result.Test.FullName} ({result.Duration:F3}s)")
						).Wait();
						break;
					case TestStatus.Failed:
						failed++;
						FishMMO.Logging.Log.WritePartsToConsole(
							LogLevel.Error,
							"UnitTestMenu",
							0,
							("red", $"[FishMMO.UnitTests] ✗ FAILED: {result.Test.FullName} ({result.Duration:F3}s)\n{result.Message}\n{result.StackTrace}")
						).Wait();
						break;
					case TestStatus.Skipped:
					case TestStatus.Inconclusive:
						skipped++;
						FishMMO.Logging.Log.WritePartsToConsole(
							LogLevel.Warning,
							"UnitTestMenu",
							0,
							("yellow", $"[FishMMO.UnitTests] ⌀ {result.TestStatus}: {result.Test.FullName}\n{result.Message}")
						).Wait();
						break;
					default:
						other++;
						FishMMO.Logging.Log.WritePartsToConsole(
							LogLevel.Warning,
							"UnitTestMenu",
							0,
							("yellow", $"[FishMMO.UnitTests] ? {result.TestStatus}: {result.Test.FullName}\n{result.Message}")
						).Wait();
						break;
				}
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				string summary = $"[FishMMO.UnitTests] Run finished: {passed} passed, {failed} failed, {skipped} skipped, {other} other ({result.Duration:F2}s).";
				if (failed > 0)
					FishMMO.Logging.Log.WritePartsToConsole(
						LogLevel.Error,
						"UnitTestMenu",
						0,
						("red", summary)
					).Wait();
				else
					FishMMO.Logging.Log.WritePartsToConsole(
						LogLevel.Info,
						"UnitTestMenu",
						0,
						("green", summary)
					).Wait();
			}
		}
	}
}