using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Logging;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Assembly-level NUnit setup fixture. Initializes <see cref="FishMMO.Logging.Log"/> exactly once
	/// before any test in the <c>FishMMO.UnitTests</c> namespace runs, so all test classes share a
	/// single, consistently configured logger without per-class duplication.
	/// </summary>
	[SetUpFixture]
	public class TestAssemblySetup
	{
		/// <summary>Initializes the FishMMO logger for the entire unit-test assembly.</summary>
		[OneTimeSetUp]
		public void InitializeLogger()
		{
			try
			{
				FishMMO.Logging.Log.RegisterLoggerFactory(
					nameof(UnityConsoleLoggerConfig),
					(cfg, logCallback) => new UnityConsoleLogger((UnityConsoleLoggerConfig)cfg, logCallback));

				var defaultConfig = new UnityConsoleLoggerConfig();
				var formatter = new UnityConsoleFormatter(defaultConfig.LogLevelColors, true);

				var loggers = new List<FishMMO.Logging.ILogger>
				{
					new UnityConsoleLogger(
						new UnityConsoleLoggerConfig
						{
							Enabled = true,
							AllowedLevels = new HashSet<LogLevel>
							{
								LogLevel.Info, LogLevel.Debug, LogLevel.Warning,
								LogLevel.Error, LogLevel.Critical, LogLevel.Verbose,
							},
						},
						(message) => Debug.Log($"{message}")),
				};

				FishMMO.Logging.Log.Initialize(
					null,
					formatter,
					loggers,
					FishMMO.Logging.Log.OnInternalLogMessage,
					new List<Type> { typeof(UnityConsoleLoggerConfig) });

				Debug.Log("[UnitTest] Logger initialized by TestAssemblySetup.");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[UnitTest] Failed to initialize logger: {ex.Message}");
			}
		}
	}
}