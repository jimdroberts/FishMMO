using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using FishMMO.Logging;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Level gating in <see cref="Log"/>: a refused level must cost nothing, and must never be
	/// refused on behalf of a sink that would have taken it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The defect.</b> <c>Log.Write</c> guarded only on <c>loggers == null</c> and then went
	/// straight to <c>new LogEntry(...)</c>. No level test stood between the call and the allocation,
	/// so a <c>Log.Debug</c> call at Error level still paid for a LogEntry, an async state machine,
	/// a Task, the Where/Select closures and enumerators, the <c>List&lt;Task&gt;</c> from
	/// <c>.ToList()</c> and a <c>Task.WhenAll</c> over it — all of it discarded unread.
	/// </para>
	/// <para>
	/// <b>Why it mattered enough to pin.</b> The Debug call sites sit on the expected-false branches
	/// of the per-target ECA conditions (IsCharacterAliveCondition, HasRequiredAttributeCondition,
	/// IsImmortalCondition, IsCharacterNPCCondition, HasGuildCondition, HasPartyCondition). Those run
	/// once per target per condition per cast, so a 10-body AoE behind a 3-condition gate ran ~30 of
	/// them for one button press. This is not a level that shows up in a profiler as one hot frame;
	/// it shows up as steady allocation pressure on a populated scene server.
	/// </para>
	/// <para>
	/// <b>The risk the early-out introduces, which is the larger half of these tests.</b> An
	/// early-out that disagrees with the dispatch rule is worse than no early-out: it drops messages
	/// a configured sink was asked to keep, and it does so silently. So the agreement is checked
	/// end to end and exhaustively — for every <see cref="LogLevel"/>, under several sink
	/// configurations, <c>IsEnabled</c> must equal whether a write actually reached anything. A test
	/// that only checked "Debug is skipped at Error level" would pass on an early-out that dropped
	/// everything.
	/// </para>
	/// <para>
	/// <b>Why reflection.</b> <c>FishMMO-Logger</c> is a vendored precompiled DLL. Calling
	/// <c>Log.IsEnabled</c> directly would make the whole test assembly fail to COMPILE against a
	/// stale drop of that DLL, which reads as hundreds of unrelated errors; through reflection a
	/// stale DLL is one legible failure naming the missing member. The private static fields are
	/// reached the same way so a configuration can be installed and, more importantly, restored —
	/// <see cref="Log.Initialize"/> cannot put back the state <c>TestAssemblySetup</c> established
	/// for the rest of the assembly.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LogLevelGatingTests
	{
		private const string LogPath = "../FishMMO-Logger/FishMMO-Logger/Log.cs";

		private static readonly LogLevel[] AllLevels = (LogLevel[])Enum.GetValues(typeof(LogLevel));

		private static FieldInfo LoggersField => Field("loggers");
		private static FieldInfo ConsoleLevelsField => Field("consoleAllowedLevels");
		private static FieldInfo FormatterField => Field("_consoleFormatter");

		private object savedLoggers;
		private object savedConsoleLevels;
		private object savedFormatter;

		[SetUp]
		public void SaveLogState()
		{
			savedLoggers = LoggersField.GetValue(null);
			savedConsoleLevels = ConsoleLevelsField.GetValue(null);
			savedFormatter = FormatterField.GetValue(null);
		}

		[TearDown]
		public void RestoreLogState()
		{
			/* Log is static and TestAssemblySetup configures it once for every fixture in the
			 * assembly. Leaving a two-sink test configuration behind would not fail this fixture —
			 * it would quietly change where every later fixture's LogAssert output goes. Restore the
			 * exact references that were there, not an approximation of them. */
			LoggersField.SetValue(null, savedLoggers);
			ConsoleLevelsField.SetValue(null, savedConsoleLevels);
			FormatterField.SetValue(null, savedFormatter);
		}

		private static FieldInfo Field(string name)
		{
			FieldInfo field = typeof(Log).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsNotNull(field,
				$"Log.{name} is gone. These tests install and restore the logger's own state through it; " +
				"if it was renamed, re-anchor them rather than deleting them.");
			return field;
		}

		/// <summary>
		/// <c>Log.IsEnabled(LogLevel)</c>, reached reflectively so a stale vendored DLL reports the
		/// missing member instead of breaking the assembly's compile.
		/// </summary>
		private static bool IsEnabled(LogLevel level)
		{
			MethodInfo method = typeof(Log).GetMethod("IsEnabled", BindingFlags.Public | BindingFlags.Static,
				null, new[] { typeof(LogLevel) }, null);
			LogAssert.IsNotNull(method,
				"Log.IsEnabled(LogLevel) was not found on the loaded FishMMO-Logger assembly. " +
				"Either it has not been added yet, or the vendored FishMMO-Logger.dll is stale and needs redeploying.");
			return (bool)method.Invoke(null, new object[] { level });
		}

		/// <summary>Installs a sink configuration, replacing whatever is there.</summary>
		private static RecordingFormatter Configure(IEnumerable<LogLevel> consoleLevels, params RecordingLogger[] sinks)
		{
			var formatter = new RecordingFormatter();
			FormatterField.SetValue(null, formatter);
			ConsoleLevelsField.SetValue(null, consoleLevels == null ? null : new HashSet<LogLevel>(consoleLevels));

			var dictionary = new Dictionary<string, ILogger>();
			for (int i = 0; i < sinks.Length; ++i)
			{
				dictionary[$"sink{i}"] = sinks[i];
			}
			LoggersField.SetValue(null, dictionary);
			return formatter;
		}

		/// <summary>Writes at <paramref name="level"/> and reports whether anything received it.</summary>
		private static bool Delivered(LogLevel level, RecordingFormatter formatter, params RecordingLogger[] sinks)
		{
			formatter.Received.Clear();
			foreach (RecordingLogger sink in sinks)
			{
				sink.Received.Clear();
			}

			Log.Write(level, "LogLevelGatingTests", "probe").GetAwaiter().GetResult();

			return formatter.Received.Count > 0 || sinks.Any(s => s.Received.Count > 0);
		}

		[Test]
		public void ARefusedDebugReachesNothingAndAnAcceptedErrorDoes()
		{
			var sink = new RecordingLogger(LogLevel.Error);
			RecordingFormatter formatter = Configure(new[] { LogLevel.Error }, sink);

			LogAssert.IsFalse(IsEnabled(LogLevel.Debug),
				"nothing accepts Debug in this configuration, so IsEnabled must say so — this is the test a hot call site makes before interpolating its message");
			LogAssert.IsFalse(Delivered(LogLevel.Debug, formatter, sink), "a refused Debug reached a sink");

			LogAssert.IsTrue(IsEnabled(LogLevel.Error), "Error is accepted by both sinks here");
			LogAssert.IsTrue(Delivered(LogLevel.Error, formatter, sink), "an accepted Error reached nothing");
		}

		[Test]
		public void ALevelOnlyOneSinkWantsIsStillDelivered()
		{
			/* The failure mode that matters. An early-out that tested only the console's levels, or
			 * only the loggers', would drop these — a message the configuration explicitly asked to
			 * keep, lost with no error anywhere. */

			var loggerOnly = new RecordingLogger(LogLevel.Debug);
			RecordingFormatter formatter = Configure(Array.Empty<LogLevel>(), loggerOnly);
			LogAssert.IsTrue(IsEnabled(LogLevel.Debug), "the console refuses Debug but a logger allows it");
			LogAssert.IsTrue(Delivered(LogLevel.Debug, formatter, loggerOnly), "the logger's Debug was dropped");
			LogAssert.AreEqual(1, loggerOnly.Received.Count, "the logger should have received exactly the one entry");

			RecordingFormatter consoleOnly = Configure(new[] { LogLevel.Debug });
			LogAssert.IsTrue(IsEnabled(LogLevel.Debug), "there are no loggers, but the console allows Debug");
			LogAssert.IsTrue(Delivered(LogLevel.Debug, consoleOnly), "the console's Debug was dropped");
		}

		[Test]
		public void ADisabledLoggerDoesNotHoldALevelOpen()
		{
			/* ILogger.IsEnabled is part of the dispatch predicate, so it has to be part of the gate
			 * too. A gate that read AllowedLevels alone would answer true for a level no enabled sink
			 * would have taken, which costs the allocation the early-out exists to avoid. */
			var disabled = new RecordingLogger(LogLevel.Debug) { Enabled = false };
			RecordingFormatter formatter = Configure(new[] { LogLevel.Error }, disabled);

			LogAssert.IsFalse(IsEnabled(LogLevel.Debug), "the only sink allowing Debug is disabled");
			LogAssert.IsFalse(Delivered(LogLevel.Debug, formatter, disabled), "a disabled logger received an entry");
		}

		[Test]
		public void IsEnabledAgreesWithActualDeliveryForEveryLevelAndConfiguration()
		{
			/* The exhaustive form, because the three tests above are examples and examples are what
			 * an over-eager early-out passes. Every level, against configurations chosen to separate
			 * the clauses of the predicate: console-only, logger-only, split across two loggers,
			 * disabled sinks, and a null console level set (the state Initialize leaves behind when
			 * it cannot read a config).
			 *
			 * It compares IsEnabled against what a real write actually reaches, not against a
			 * re-implementation of the rule — a re-implementation would be a second copy of the
			 * thing under test and would agree with a wrong answer. */
			foreach (LogLevel[] consoleLevels in new[]
			{
				Array.Empty<LogLevel>(),
				new[] { LogLevel.Error },
				new[] { LogLevel.Debug, LogLevel.Verbose },
				AllLevels,
			})
			{
				foreach (RecordingLogger[] sinks in new[]
				{
					Array.Empty<RecordingLogger>(),
					new[] { new RecordingLogger(LogLevel.Error) },
					new[] { new RecordingLogger(LogLevel.Debug) { HandlesConsole = true } },
					new[] { new RecordingLogger(LogLevel.Debug) { Enabled = false }, new RecordingLogger(LogLevel.Warning) },
					new[] { new RecordingLogger(LogLevel.Info), new RecordingLogger(LogLevel.Verbose, LogLevel.Debug) },
				})
				{
					RecordingFormatter formatter = Configure(consoleLevels, sinks);
					foreach (LogLevel level in AllLevels)
					{
						string where = $"console=[{string.Join(",", consoleLevels)}] sinks=[{string.Join(" ", sinks.Select(s => s.Describe()))}] level={level}";
						LogAssert.AreEqual(Delivered(level, formatter, sinks), IsEnabled(level),
							$"IsEnabled disagreed with what the write actually reached ({where}). " +
							"A false where the write delivers means the early-out drops configured output; a true where it does not means the guard buys nothing.");
					}
				}
			}

			// The null case on its own: Initialize's failure paths can leave consoleAllowedLevels null.
			var sink = new RecordingLogger(LogLevel.Warning);
			RecordingFormatter nullLevels = Configure(null, sink);
			foreach (LogLevel level in AllLevels)
			{
				LogAssert.AreEqual(Delivered(level, nullLevels, sink), IsEnabled(level),
					$"IsEnabled disagreed with delivery when consoleAllowedLevels is null, at {level}");
			}
		}

		[Test]
		public void AnUninitializedManagerIsNotEnabled()
		{
			/* loggers == null is Write's pre-existing "not initialized or shut down" case, which
			 * still emits its [INTERNAL] CRITICAL notice through OnInternalLogMessage. IsEnabled
			 * reports false there deliberately: it exists to keep per-target gameplay logging off
			 * the hot path, and a hot path must not pay for a not-initialized notice thirty times a
			 * cast. TearDown puts the real state back. */
			LoggersField.SetValue(null, null);
			foreach (LogLevel level in AllLevels)
			{
				LogAssert.IsFalse(IsEnabled(level), $"IsEnabled({level}) must be false with no logger dictionary at all");
			}
		}

		[Test]
		public void TheEntryPointsAreNotAsyncSoTheRefusedPathAllocatesNothing()
		{
			/* This is the allocation claim, checked against the compiled artifact rather than the
			 * source, and without measuring bytes (a GC probe here would be flaky and would not say
			 * WHY it regressed).
			 *
			 * An `async Task` method runs through AsyncTaskMethodBuilder even when it returns before
			 * its first await, and every level helper that awaited Write added a second builder and
			 * a second Task on top of Write's. Write and the six helpers are therefore plain
			 * Task-returning methods that hand back Task.CompletedTask or the inner task directly.
			 * The compiler marks async methods with [AsyncStateMachine], so its absence is exactly
			 * the property being pinned — and if someone reverts one of them to `async Task`, this
			 * names which one. */
			foreach (string name in new[] { "Write", "Critical", "Error", "Warning", "Info", "Debug", "Verbose" })
			{
				MethodInfo method = typeof(Log).GetMethods(BindingFlags.Public | BindingFlags.Static)
					.FirstOrDefault(m => m.Name == name);
				LogAssert.IsNotNull(method, $"Log.{name} is gone.");
				LogAssert.IsNull(method.GetCustomAttribute<AsyncStateMachineAttribute>(),
					$"Log.{name} is compiled as an async method again. On the refused path that reintroduces the state machine and Task the level gate exists to avoid; return the inner task instead of awaiting it.");
			}
		}

		[Test]
		public void TheDebugRefusalReturnsTheSingletonCompletedTask()
		{
			/* A directly observable consequence of Write not being async: the refused path returns
			 * Task.CompletedTask itself, so a fire-and-forget Log.Debug at a disabled level creates
			 * no object of any kind. Paired with the [AsyncStateMachine] pin above rather than
			 * standing alone, since a synchronously-completing async method can return that same
			 * singleton. */
			var sink = new RecordingLogger(LogLevel.Error);
			Configure(new[] { LogLevel.Error }, sink);

			LogAssert.AreSame(Task.CompletedTask, Log.Debug("LogLevelGatingTests", "probe"),
				"a refused Log.Debug should hand back the shared completed task, not a new one");
		}

		[Test]
		public void WriteRefusesBeforeItBuildsTheEntry()
		{
			SourceScanPins.HoldsAndFires("Log.Write", SourceScanPins.ReadCode(LogPath),
				c =>
				{
					string body = SourceScanPins.Body(c, "public static Task Write(LogLevel level");
					if (body == null)
					{
						return "Log.Write(LogLevel, ...) is gone, or is declared `async Task` again — an async early-out still runs the state machine builder";
					}
					if (body.Contains("new LogEntry("))
					{
						return "Write builds a LogEntry itself; the allocation must happen only past the level gate, in WriteCore";
					}
					return SourceScanPins.InOrder(body,
						"if (!WouldAnySinkAccept(level, sinks))",
						"return WriteCore(");
				},
				SourceScanPins.RegexReplaceFirst(@"if \(!WouldAnySinkAccept\(level, sinks\)\)\s*\{[^{}]*\}", ""),
				"the level gate is deleted from Write");
		}

		[Test]
		public void OneSharedPredicateDecidesForBothIsEnabledAndWrite()
		{
			/* Two copies of the acceptance rule is the way this regresses: IsEnabled and the
			 * early-out drift apart, a call site trusts IsEnabled, and the message is gone. The
			 * behavioural sweep above would catch the drift, but only this says where to put the
			 * fix. */
			SourceScanPins.HoldsAndFires("Log level gate", SourceScanPins.ReadCode(LogPath),
				c =>
				{
					string isEnabled = SourceScanPins.Body(c, "public static bool IsEnabled(LogLevel level)");
					if (isEnabled == null || !isEnabled.Contains("WouldAnySinkAccept(level, sinks)"))
					{
						return "IsEnabled must answer through WouldAnySinkAccept, not with its own copy of the rule";
					}
					string predicate = SourceScanPins.Body(c, "private static bool WouldAnySinkAccept(");
					if (predicate == null)
					{
						return "WouldAnySinkAccept is gone";
					}
					foreach (string clause in new[] { "_consoleFormatter", "consoleLevels.Contains(level)", "logger.IsEnabled", "allowed.Contains(level)" })
					{
						if (!predicate.Contains(clause))
						{
							return $"the shared predicate no longer consults '{clause}', so it can disagree with WriteCore's dispatch";
						}
					}
					return null;
				},
				SourceScanPins.Replace("return sinks != null && WouldAnySinkAccept(level, sinks);", "return sinks != null;"),
				"IsEnabled stops consulting the shared predicate");
		}

		/// <summary>An <see cref="ILogger"/> that keeps what it was handed.</summary>
		private sealed class RecordingLogger : ILogger
		{
			private HashSet<LogLevel> allowed;

			public RecordingLogger(params LogLevel[] levels)
			{
				allowed = new HashSet<LogLevel>(levels);
			}

			public List<LogEntry> Received { get; } = new List<LogEntry>();

			public bool Enabled { get; set; } = true;
			public bool HandlesConsole { get; set; }

			public bool IsEnabled => Enabled;
			public IReadOnlyCollection<LogLevel> AllowedLevels => allowed;
			public bool HandlesConsoleParts => HandlesConsole;

			public Task Log(LogEntry entry)
			{
				Received.Add(entry);
				return Task.CompletedTask;
			}

			public void SetEnabled(bool enabled) => Enabled = enabled;
			public void SetAllowedLevels(HashSet<LogLevel> levels) => allowed = levels ?? new HashSet<LogLevel>();
			public void Dispose() { }

			public string Describe() => $"({(Enabled ? "on" : "off")}{(HandlesConsole ? ",parts" : "")}:{string.Join("|", allowed)})";
		}

		/// <summary>An <see cref="IConsoleFormatter"/> that keeps what it was handed and writes nothing.</summary>
		private sealed class RecordingFormatter : IConsoleFormatter
		{
			public List<LogEntry> Received { get; } = new List<LogEntry>();

			public void WriteStructuredLog(LogEntry entry) => Received.Add(entry);

			public void WriteColoredParts(LogLevel level, string source, int columnWidth, params (string color, string text)[] parts)
			{
			}
		}
	}

	/// <summary>
	/// <c>CanUseItemCondition</c>: what it checks, and that it says out loud what it does not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The condition carried <c>// FIXME: Add a check for item usage conditions, such as cooldowns or
	/// requirements.</c> and returned <c>ContainsItem(RequiredItem)</c>. A condition named
	/// "CanUseItem" that answers on possession alone passes while the potion is still on cooldown,
	/// and an author wiring a Trigger in the inspector has no way to know that.
	/// </para>
	/// <para>
	/// <b>It was left as a presence check on purpose, and these pins hold it to that.</b> The
	/// cooldown belongs to <c>HasCooldownCondition</c>, which already resolves the replicate-versus-
	/// authoritative tick domain correctly; a second copy of that resolution living here would drift
	/// from it and answer wrongly during reconcile replay. Charges belong to
	/// <c>ConsumableTemplate.CanConsume</c>, which needs the Item instance and an IPlayerCharacter
	/// that this condition does not have. "Requirements" belongs to nothing — BaseItemTemplate has no
	/// requirement fields of any kind, so the FIXME promised a check against a system that has never
	/// existed.
	/// </para>
	/// <para>
	/// So the pins guard three things: the FIXME is not silently back, the remark still names each
	/// gap, and the authoring warning stays a warning — it must not start deciding the answer, which
	/// is how "documented gap" would quietly become "half-check that looks complete".
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CanUseItemConditionScopeTests
	{
		private const string ConditionPath =
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Conditions/Character/Inventory/CanUseItemCondition.cs";

		private const string PresenceReturn = "return inventoryController.ContainsItem(RequiredItem);";
		private const string LimitationPhrase = "checks inventory presence and nothing else";

		[Test]
		public void NoFixmeStandsInPlaceOfAStatedLimitation()
		{
			/* Read the full source, comments included: the whole point is what the comments say. The
			 * prose deliberately mentions the word FIXME when explaining what was removed, so the
			 * pin looks for the comment MARKER (`// FIXME`) rather than the bare word. */
			SourceScanPins.HoldsAndFires("CanUseItemCondition", SourceScanPins.ReadSource(ConditionPath),
				s =>
				{
					Match fixme = Regex.Match(s, @"//+\s*FIXME\b");
					if (fixme.Success)
					{
						return "a FIXME stands where a stated limitation belongs; a note to a future reader is not a warning to a content author";
					}
					return s.Contains(LimitationPhrase)
						? null
						: $"the remark no longer states plainly that the condition '{LimitationPhrase}'";
				},
				SourceScanPins.InsertBefore(PresenceReturn,
					"// FIXME: Add a check for item usage conditions, such as cooldowns or requirements.\n\t\t\t"),
				"the FIXME comes back");
		}

		[Test]
		public void TheRemarkNamesEveryGapAndDisclaimsTheGate()
		{
			SourceScanPins.HoldsAndFires("CanUseItemCondition", SourceScanPins.ReadSource(ConditionPath),
				s =>
				{
					foreach (string required in new[]
					{
						LimitationPhrase,
						"HasCooldownCondition",        // where the cooldown check actually lives
						"ConsumableTemplate.CanConsume", // where charges are actually checked
						"no level, class, attribute or faction requirement fields", // "requirements" does not exist
						"not the authoritative use gate", // and this must never be mistaken for one
					})
					{
						if (!s.Contains(required))
						{
							return $"the remark no longer says '{required}', so a content author can still read the class name as a promise";
						}
					}
					return null;
				},
				SourceScanPins.Replace("not the authoritative use gate", "the authoritative use gate"),
				"the remark stops disclaiming the gate");
		}

		[Test]
		public void EvaluateStillAnswersOnPresenceAlone()
		{
			/* The behaviour was deliberately unchanged, so it is pinned as deliberate. If a cooldown
			 * check is ever added here it should be added with the tick-domain resolution
			 * HasCooldownCondition uses — and this test should fail loudly and be rewritten, not
			 * quietly satisfied by an approximation. */
			SourceScanPins.HoldsAndFires("CanUseItemCondition.Evaluate", SourceScanPins.ReadCode(ConditionPath),
				c =>
				{
					string body = SourceScanPins.Body(c, "public override bool Evaluate(");
					if (body == null)
					{
						return "Evaluate is gone";
					}
					if (!Regex.IsMatch(body, @"\breturn inventoryController\.ContainsItem\(RequiredItem\);\s*\}$"))
					{
						return "Evaluate must end by answering on inventory presence alone";
					}
					if (!body.Contains("WarnIfGatingACooldownItem();"))
					{
						return "the authoring warning is no longer raised, so the gap is silent again";
					}
					return Regex.IsMatch(body, @"return[^;]*WarnIfGatingACooldownItem")
						? "the warning is feeding the returned answer; it is a diagnostic, and a diagnostic that gates is a half-check"
						: null;
				},
				SourceScanPins.Replace(PresenceReturn,
					"return inventoryController.ContainsItem(RequiredItem) && !warnedAboutUncheckedCooldown;"),
				"the answer stops being presence alone");
		}

		[Test]
		public void TheAuthoringWarningLatchesBeforeItLogs()
		{
			/* Same hot path as the Log fix: Evaluate runs once per target per cast. A warning that
			 * latched after logging, or not at all, would emit one line per body per AoE — which is
			 * how a useful diagnostic gets muted by whoever is drowning in it. */
			SourceScanPins.HoldsAndFires("WarnIfGatingACooldownItem", SourceScanPins.ReadCode(ConditionPath),
				c => SourceScanPins.InOrder(SourceScanPins.Body(c, "private void WarnIfGatingACooldownItem()"),
					"if (warnedAboutUncheckedCooldown",
					"warnedAboutUncheckedCooldown = true;",
					"Log.Warning("),
				SourceScanPins.Replace("warnedAboutUncheckedCooldown = true;", ""),
				"the latch is dropped and the warning fires once per target");
		}
	}
}
