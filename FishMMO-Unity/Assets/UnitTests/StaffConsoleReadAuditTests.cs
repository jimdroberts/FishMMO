using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Auth.Core;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The staff console's reads are audited (issue #252), except automatic refreshes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every staff action must be recorded, and a read of a player's roster row or ticket is an
	/// action. <c>TryAuthorizeStaffRequest</c> records an allowed read through
	/// <see cref="ChatHelper.ReportElevatedRequest"/> — after the throttle, so a request dropped
	/// unanswered is not recorded as answered, and after the refusal, which is recorded as a refusal.
	/// </para>
	/// <para>
	/// <b>Automatic refreshes are not recorded (2026-09-14).</b> The owner's decision: the first load
	/// and every read somebody asked for are recorded; the console's refresh timer marks its roster
	/// request with <see cref="StaffRosterRequestBroadcast.AutoRefresh"/>, and an allowed request so
	/// marked is answered without a row. The mark never reaches the refusal, which is recorded
	/// unconditionally, nor the throttle. The client side is pinned too: only the timer marks a
	/// request, and only once a roster has arrived, so the first load is never marked.
	/// </para>
	/// <para>
	/// <b>Every handler goes through that one gate.</b> The gate is only an audit if nothing reaches
	/// the console's data around it, so this fixture finds the handlers rather than listing them:
	/// every <c>OnStaff*Request</c> declared, every broadcast registered, and every such method
	/// reflection can see on <see cref="SceneServerSystem"/>, which catches one declared in another
	/// partial file.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class StaffConsoleReadAuditTests
	{
		private const string StaffConsolePath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.StaffConsole.cs";

		private const string ConsoleClientPath =
			"Assets/Scripts/Client/GUI/World/StaffConsole/UITKStaffConsole.cs";

		private const string ElevatedCall =
			"ChatHelper.ReportElevatedRequest(character, \"staffconsole.\" + request, detail, AccessLevel.GameMaster);";

		private const string RefusedCall =
			"ChatHelper.ReportRefused(character, \"staffconsole.\" + request, AccessLevel.GameMaster);";

		private const string HandlerDeclaration = @"\b(?:private|protected|public|internal)\s+(?:async\s+)?(?:void|Task)\s+(OnStaff\w*Request)\(";

		// ── The gate ──────────────────────────────────────────────────────────

		private static string GateFailure(string code)
		{
			string body = SourceScanPins.Body(code, "private bool TryAuthorizeStaffRequest(");
			string order = SourceScanPins.InOrder(body,
				RefusedCall,
				"next > now",
				"staffRequestNextTicks[key] = now + StaffRequestIntervalTicks;",
				ElevatedCall,
				"return true;");
			if (order != null)
			{
				return order;
			}
			int inFile = Regex.Matches(code, @"ReportElevatedRequest\(").Count;
			if (inFile != 1)
			{
				return $"ReportElevatedRequest is called {inFile} times in the console; the gate is the one place";
			}

			// The refusal is unconditional: nothing before it may consult the client's flag.
			string beforeRefusal = body.Substring(0, body.IndexOf(RefusedCall, StringComparison.Ordinal));
			if (Regex.IsMatch(beforeRefusal, @"\bautoRefresh\b"))
			{
				return "the refusal consults autoRefresh; a refused request is recorded whatever the client marks";
			}

			// The allowed read is skipped for an automatic refresh, and for nothing else.
			if (!Regex.IsMatch(body, @"if \(!autoRefresh\)\s*\{\s*" + Regex.Escape(ElevatedCall) + @"\s*\}"))
			{
				return "the allowed read must be recorded under exactly `if (!autoRefresh) { ReportElevatedRequest(...) }`";
			}
			int flagUses = Regex.Matches(body, @"\bautoRefresh\b").Count;
			return flagUses == 1
				? null
				: $"autoRefresh is read {flagUses} times in the gate; only the recording may depend on it, never the refusal or the throttle";
		}

		[Test]
		public void AnAllowedReadIsRecordedAfterTheThrottleUnlessAutoRefresh()
		{
			string code = SourceScanPins.ReadCode(StaffConsolePath);

			SourceScanPins.HoldsAndFires("TryAuthorizeStaffRequest", code, GateFailure,
				s => SourceScanPins.InsertBefore("long now = DateTime.UtcNow.Ticks;", ElevatedCall + "\n")(s.Replace(ElevatedCall, string.Empty)),
				"the read is recorded before the throttle");
			SourceScanPins.HoldsAndFires("TryAuthorizeStaffRequest", code, GateFailure,
				SourceScanPins.Replace(ElevatedCall, string.Empty),
				"an allowed read is not recorded");
			SourceScanPins.HoldsAndFires("TryAuthorizeStaffRequest", code, GateFailure,
				SourceScanPins.RegexReplaceFirst(@"if \(!autoRefresh\)\s*\{\s*(ChatHelper\.ReportElevatedRequest\([^;]*;)\s*\}", "$1"),
				"an automatic refresh is recorded anyway");
			SourceScanPins.HoldsAndFires("TryAuthorizeStaffRequest", code, GateFailure,
				SourceScanPins.InsertBefore("long now = DateTime.UtcNow.Ticks;", "if (autoRefresh) { return true; }\n"),
				"an automatic refresh skips the throttle");
		}

		[Test]
		public void ARefusalIsRecordedWhateverTheClientMarks()
		{
			string code = SourceScanPins.ReadCode(StaffConsolePath);

			SourceScanPins.HoldsAndFires("TryAuthorizeStaffRequest", code, GateFailure,
				SourceScanPins.Replace(RefusedCall, "if (!autoRefresh) { " + RefusedCall + " }"),
				"a refused automatic refresh is not recorded");
		}

		[Test]
		public void AnElevatedRequestReachesTheAuditEvent()
		{
			IPlayerCharacter staff = DispatchProxy.Create<IPlayerCharacter, OperatorCommandBoundaryTests.GateCharacter>();
			((OperatorCommandBoundaryTests.GateCharacter)(object)staff).Level = AccessLevel.GameMaster;

			var seen = new List<(IPlayerCharacter sender, string request, string arguments, AccessLevel level)>();
			Action<IPlayerCharacter, string, string, AccessLevel> onElevated = (c, request, arguments, level) => seen.Add((c, request, arguments, level));

			ChatHelper.OnElevatedCommand += onElevated;
			try
			{
				ChatHelper.ReportElevatedRequest(staff, "staffconsole.ticket", "#42", AccessLevel.GameMaster);
				ChatHelper.ReportElevatedRequest(staff, "staffconsole.roster", null, AccessLevel.GameMaster);
			}
			finally
			{
				ChatHelper.OnElevatedCommand -= onElevated;
			}

			LogAssert.AreEqual(2, seen.Count);
			LogAssert.AreSame(staff, seen[0].sender);
			LogAssert.AreEqual("staffconsole.ticket", seen[0].request);
			LogAssert.AreEqual("#42", seen[0].arguments);
			LogAssert.AreEqual(AccessLevel.GameMaster, seen[0].level);
			LogAssert.AreEqual(string.Empty, seen[1].arguments, "a read with no detail is recorded with empty text, never null");
		}

		// ── The client marks only its timer ───────────────────────────────────

		private static string ClientFailure(string code)
		{
			string request = SourceScanPins.Body(code, "private void RequestRoster(bool autoRefresh = false)");
			if (request == null)
			{
				return "RequestRoster(bool autoRefresh = false) is gone";
			}
			if (!Regex.IsMatch(request, @"AutoRefresh\s*=\s*autoRefresh\b"))
			{
				return "RequestRoster must send the flag it was given";
			}
			if (Regex.IsMatch(code, @"AutoRefresh\s*=\s*true\b") || Regex.IsMatch(code, @"RequestRoster\((?:true|false|hasRoster)\)"))
			{
				return "a roster request is marked some other way than the timer's named argument";
			}

			var marked = Regex.Matches(code, @"RequestRoster\(autoRefresh: ([^)]*)\)").Cast<Match>().ToList();
			if (marked.Count != 1)
			{
				return $"{marked.Count} calls mark a roster request automatic; only the refresh timer in OnTick may";
			}
			string tick = SourceScanPins.Body(code, "protected override void OnTick(");
			if (tick == null || !tick.Contains(marked[0].Value))
			{
				return "the one automatic roster request must be the refresh timer's, in OnTick";
			}
			return marked[0].Groups[1].Value == "hasRoster"
				? null
				: $"the timer marks its request '{marked[0].Groups[1].Value}'; it must be hasRoster, so a first load is never marked";
		}

		[Test]
		public void OnlyTheConsoleRefreshTimerMarksARequestAutomatic()
		{
			string code = SourceScanPins.ReadCode(ConsoleClientPath);

			SourceScanPins.HoldsAndFires("staff console client", code, ClientFailure,
				SourceScanPins.RegexReplaceFirst(@"RequestRoster\(\);", "RequestRoster(autoRefresh: true);"),
				"a user-triggered roster request is marked automatic");
			SourceScanPins.HoldsAndFires("staff console client", code, ClientFailure,
				SourceScanPins.Replace("RequestRoster(autoRefresh: hasRoster)", "RequestRoster(autoRefresh: true)"),
				"the timer marks the first load automatic");
			SourceScanPins.HoldsAndFires("staff console client", code, ClientFailure,
				SourceScanPins.Replace("AutoRefresh = autoRefresh,", "AutoRefresh = true,"),
				"every roster request is marked automatic");
		}

		// ── Every handler goes through the gate ───────────────────────────────

		private static string HandlersFailure(string code)
		{
			var declared = Regex.Matches(code, HandlerDeclaration).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
			if (declared.Count < 3)
			{
				return $"found {declared.Count} OnStaff*Request handlers; the scan must see the roster, queue and detail handlers";
			}

			string register = SourceScanPins.Body(code, "private void RegisterStaffConsoleBroadcasts(");
			if (register == null)
			{
				return "RegisterStaffConsoleBroadcasts is gone";
			}
			var registrations = Regex.Matches(register, @"RegisterBroadcast<\w+>\(\s*([A-Za-z_]\w*)\s*,").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
			int allRegistrations = Regex.Matches(register, @"RegisterBroadcast<").Count;
			if (registrations.Count != allRegistrations)
			{
				return "every staff console broadcast must be registered to a named handler, never a lambda around the gate";
			}
			if (!new HashSet<string>(registrations).SetEquals(declared))
			{
				return $"registered [{string.Join(", ", registrations)}] but declared [{string.Join(", ", declared)}]";
			}

			var kinds = new HashSet<int>();
			var requests = new HashSet<string>(StringComparer.Ordinal);
			foreach (string handler in declared)
			{
				string body = SourceScanPins.Body(code, handler + "(NetworkConnection conn,");
				if (body == null)
				{
					return $"{handler} must take the connection its request arrived on";
				}
				Match gate = Regex.Match(body, @"TryAuthorizeStaffRequest\(conn, ""([a-z]+)"", (\d+),");
				if (!gate.Success)
				{
					return $"{handler} does not go through TryAuthorizeStaffRequest";
				}
				foreach (string work in new[] { "Broadcast(", "TryEnqueueAsyncWork(", "CharactersByID", "FetchAsync(", "SearchAsync(" })
				{
					int at = body.IndexOf(work, StringComparison.Ordinal);
					if (at >= 0 && at < gate.Index)
					{
						return $"{handler} reaches {work} before the gate";
					}
				}

				// The flag comes from the request itself, or is false. A handler that hard-codes true hides every read.
				if (!Regex.IsMatch(body.Substring(gate.Index), @"^TryAuthorizeStaffRequest\([^\n]*?,\s*(?:msg\.AutoRefresh|autoRefresh: false),\s*out IPlayerCharacter staff\)"))
				{
					return $"{handler} must pass its request's own AutoRefresh or autoRefresh: false to the gate";
				}

				// The throttle key is ID * 4 + kind: a kind outside 0-3 collides with another staff member's key.
				int kind = int.Parse(gate.Groups[2].Value);
				if (kind < 0 || kind > 3 || !kinds.Add(kind))
				{
					return $"{handler}'s throttle kind {kind} is out of range or shared with another request";
				}
				if (!requests.Add(gate.Groups[1].Value))
				{
					return $"{handler} records the same request name as another handler";
				}
			}
			return null;
		}

		[Test]
		public void EveryConsoleHandlerGoesThroughTheAuditedGate()
		{
			string code = SourceScanPins.ReadCode(StaffConsolePath);

			SourceScanPins.HoldsAndFires("staff console handlers", code, HandlersFailure,
				SourceScanPins.Replace("TryAuthorizeStaffRequest(conn, \"roster\", 0, string.Empty, msg.AutoRefresh, out IPlayerCharacter staff)",
					"TryGetStaff(conn, out IPlayerCharacter staff)"),
				"the roster handler skips the gate");
			SourceScanPins.HoldsAndFires("staff console handlers", code, HandlersFailure,
				SourceScanPins.InsertBefore("Server.NetworkWrapper.RegisterBroadcast<StaffRosterRequestBroadcast>",
					"Server.NetworkWrapper.RegisterBroadcast<StaffExtraRequestBroadcast>((c, m, ch) => { }, true);\n"),
				"a request is registered to a lambda");
			SourceScanPins.HoldsAndFires("staff console handlers", code, HandlersFailure,
				SourceScanPins.Replace("\"tickets\", 1,", "\"tickets\", 0,"),
				"two requests share a throttle kind");
			SourceScanPins.HoldsAndFires("staff console handlers", code, HandlersFailure,
				SourceScanPins.Replace("msg.AutoRefresh, out IPlayerCharacter staff", "true, out IPlayerCharacter staff"),
				"a handler hard-codes the automatic-refresh flag");
		}

		[Test]
		public void NoConsoleHandlerIsDeclaredOutsideTheScannedFile()
		{
			var scanned = new HashSet<string>(
				Regex.Matches(SourceScanPins.ReadCode(StaffConsolePath), HandlerDeclaration).Cast<Match>().Select(m => m.Groups[1].Value));

			var compiled = new HashSet<string>(typeof(SceneServerSystem)
				.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.Select(m => m.Name)
				.Where(n => Regex.IsMatch(n, @"^OnStaff\w*Request$")));

			LogAssert.IsTrue(compiled.Count >= 3, "reflection must see the handlers, or it checks nothing");
			LogAssert.IsTrue(compiled.SetEquals(scanned),
				$"compiled [{string.Join(", ", compiled)}] but the scanned file declares [{string.Join(", ", scanned)}]");
		}
	}
}
