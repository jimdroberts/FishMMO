using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The <c>/gm rescue</c>, <c>/gm points</c> and <c>/gm escalate</c> entries of the game master
	/// table (issue #252).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The table is built by the real <c>BuildGameMasterCommands</c>, as
	/// <see cref="OperatorCommandBoundaryTests"/> builds it, so these read what the dispatcher, help
	/// and the staff console read.
	/// </para>
	/// <para>
	/// <b>rescue's choice list and <see cref="RescuePoints.KindWords"/> are two copies of one list.</b>
	/// The handler parses the word with <see cref="RescuePoints.TryParseKind"/>, and the console
	/// offers the spec's choices as a dropdown; a kind added to one and not the other is a dropdown
	/// entry the handler refuses, or a kind no operator can pick.
	/// </para>
	/// <para>
	/// <b>An escalation reason is ticket text.</b> It lands on the ticket as a staff note, so the audit
	/// row must withhold it exactly as it withholds a reply.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class GameMasterRescueEscalateTableTests
	{
		private const string GameMasterCommandsPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.GameMasterCommands.cs";

		private sealed class Entry
		{
			public string Name;
			public string Category;
			public string Summary;
			public string Arguments;
			public bool RosterAction;
			public bool TicketAction;
			public bool HasRun;
		}

		private static List<Entry> GameMasterTable()
		{
			SceneServerSystem system = ScriptableObject.CreateInstance<SceneServerSystem>();
			try
			{
				MethodInfo build = typeof(SceneServerSystem).GetMethod("BuildGameMasterCommands", BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(build, "SceneServerSystem.BuildGameMasterCommands must exist");

				var entries = new List<Entry>();
				foreach (object command in (IEnumerable)build.Invoke(system, null))
				{
					Type type = command.GetType();
					object Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public).GetValue(command);
					entries.Add(new Entry()
					{
						Name = (string)Field("Name"),
						Category = (string)Field("Category"),
						Summary = (string)Field("Summary"),
						Arguments = (string)Field("Arguments"),
						RosterAction = (bool)Field("RosterAction"),
						TicketAction = (bool)Field("TicketAction"),
						HasRun = Field("Run") != null,
					});
				}
				return entries;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(system);
			}
		}

		private static Entry Find(string name)
		{
			List<Entry> matches = GameMasterTable().Where(e => e.Name == name).ToList();
			LogAssert.AreEqual(1, matches.Count, $"/gm {name} must be in the table exactly once");
			return matches[0];
		}

		private static List<OperatorCommandParsing.ArgumentSpec> Parse(Entry entry)
		{
			LogAssert.IsTrue(OperatorCommandParsing.TryParseSpec(entry.Arguments, out var arguments, out string error),
				$"/gm {entry.Name}'s spec does not parse: {error}");
			return arguments;
		}

		private static string Redact(string arguments)
		{
			MethodInfo redact = typeof(SceneServerSystem).GetMethod("RedactGameMasterAudit", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(redact, "SceneServerSystem.RedactGameMasterAudit must exist");
			return (string)redact.Invoke(null, new object[] { arguments });
		}

		/// <summary>
		/// Null when a spec's single choice argument offers exactly <see cref="RescuePoints.KindWords"/>,
		/// in order; otherwise why not.
		/// </summary>
		private static string RescueChoiceFailure(string spec)
		{
			if (!OperatorCommandParsing.TryParseSpec(spec, out var arguments, out string error))
			{
				return "the spec does not parse: " + error;
			}
			var choices = arguments.Where(a => a.Kind == StaffArgumentKind.Choice).ToList();
			if (choices.Count != 1)
			{
				return $"expected one choice argument, found {choices.Count}";
			}
			if (choices[0].Optional)
			{
				return "the point kind must be required";
			}
			if (!choices[0].Choices.SequenceEqual(RescuePoints.KindWords, StringComparer.Ordinal))
			{
				return $"the choices are [{string.Join(",", choices[0].Choices)}] but the kinds are [{string.Join(",", RescuePoints.KindWords)}]";
			}
			return null;
		}

		// ── The entries exist and are usable ──────────────────────────────────

		[Test]
		public void RescuePointsAndEscalateAreDescribedRunnableAndTheirSpecsParse()
		{
			foreach (string name in new[] { "rescue", "points", "escalate" })
			{
				Entry entry = Find(name);
				LogAssert.IsTrue(entry.HasRun, $"/gm {name} has no handler");
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(entry.Category), $"/gm {name} has no category");
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(entry.Summary), $"/gm {name} has no summary");
				LogAssert.IsTrue(entry.Summary.Length <= ChatBroadcast.MaxTextLength, $"/gm {name}'s summary would be cut in help");
				Parse(entry);

				string usage = "Usage: " + OperatorCommandParsing.FormatUsage("/gm", entry.Name, entry.Arguments);
				LogAssert.IsTrue(usage.Length <= ChatBroadcast.MaxTextLength, $"/gm {name}'s usage line is {usage.Length} long");
			}
		}

		// ── rescue ────────────────────────────────────────────────────────────

		[Test]
		public void RescueIsARosterActionOnACharacter()
		{
			Entry rescue = Find("rescue");
			LogAssert.IsTrue(rescue.RosterAction, "rescue must be offered on a roster row");
			var arguments = Parse(rescue);
			LogAssert.IsTrue(arguments.Count > 0 && arguments[0].Kind == StaffArgumentKind.Character,
				"a roster action's first argument must be the character");
			LogAssert.IsFalse(arguments[0].Optional, "rescue must name who is rescued");
		}

		[Test]
		public void RescueChoicesAreExactlyTheRescuePointKinds()
		{
			Entry rescue = Find("rescue");
			string failure = RescueChoiceFailure(rescue.Arguments);
			LogAssert.IsNull(failure, $"/gm rescue: {failure}");

			// Every offered word must parse, or the console offers a choice the handler refuses.
			foreach (string word in RescuePoints.KindWords)
			{
				LogAssert.IsTrue(RescuePoints.TryParseKind(word, out _), $"'{word}' is offered but does not parse as a rescue kind");
			}

			// Control: the same check on a spec that has lost a kind, and on one that has grown one.
			LogAssert.IsNotNull(RescueChoiceFailure(rescue.Arguments.Replace(",waypoint", string.Empty)),
				"the choice check must fire when a kind is dropped from the spec");
			LogAssert.IsNotNull(RescueChoiceFailure(rescue.Arguments.Replace("waypoint", "waypoint,bind")),
				"the choice check must fire when the spec offers a word that is not a kind");
		}

		// ── points ────────────────────────────────────────────────────────────

		[Test]
		public void PointsTakesAnOptionalCharacter()
		{
			var arguments = Parse(Find("points"));
			LogAssert.AreEqual(1, arguments.Count, "points takes only the character whose scene is listed");
			LogAssert.AreEqual(StaffArgumentKind.Character, arguments[0].Kind);
			LogAssert.IsTrue(arguments[0].Optional, "with no character, points lists the operator's own scene");
		}

		// ── escalate ──────────────────────────────────────────────────────────

		[Test]
		public void EscalateIsATicketActionWithARequiredReason()
		{
			Entry escalate = Find("escalate");
			LogAssert.IsTrue(escalate.TicketAction, "escalate must be offered on a ticket");
			var arguments = Parse(escalate);
			LogAssert.AreEqual(2, arguments.Count, "escalate takes a ticket and a reason");
			LogAssert.AreEqual(StaffArgumentKind.Ticket, arguments[0].Kind);
			LogAssert.AreEqual(StaffArgumentKind.Text, arguments[1].Kind);
			LogAssert.IsFalse(arguments[1].Optional, "the next tier picks the ticket up cold; the reason is required");
		}

		[Test]
		public void AnEscalationReasonIsWithheldFromTheAuditRow()
		{
			const string reason = "the player gave their address as 1 Example Road";
			foreach (string word in new[] { "escalate", "ESCALATE", "Escalate" })
			{
				string typed = $"{word} 42 {reason}";
				string audited = Redact(typed);

				LogAssert.IsTrue(typed.Contains("Example"), "control: the typed text carries the detail");
				LogAssert.IsFalse(audited.Contains("Example"), $"'{word}' left the reason in the audit row: {audited}");
				LogAssert.IsTrue(audited.Contains("42"), "the ticket number is what the auditor needs");
				LogAssert.IsTrue(audited.StartsWith(word, StringComparison.Ordinal), "the row still says which command ran");
				LogAssert.IsTrue(audited.Contains(reason.Length.ToString()), "the row records how much was written");
			}
		}

		[Test]
		public void RescueAndPointsAreRecordedAsTyped()
		{
			// Where a character was sent is what an auditor of a rescue needs; nothing in it is ticket text.
			LogAssert.AreEqual("rescue Bob waypoint Camp Waypoint", Redact("rescue Bob waypoint Camp Waypoint"));
			LogAssert.AreEqual("points Bob", Redact("points Bob"));
		}

		[Test]
		public void TheRedactorIsInstalledOnGm()
		{
			string code = SourceScanPins.ReadCode(GameMasterCommandsPath);
			const string install = "ChatHelper.SetAuditRedactor(\"/gm\", RedactGameMasterAudit);";

			SourceScanPins.HoldsAndFires("RegisterGameMasterCommands", code,
				c =>
				{
					string body = SourceScanPins.Body(c, "private void RegisterGameMasterCommands(");
					if (body == null)
					{
						return "RegisterGameMasterCommands is gone";
					}
					return SourceScanPins.InOrder(body, "ChatHelper.AddCommands(", install);
				},
				SourceScanPins.Replace(install, string.Empty),
				"the redactor is never installed");
		}
	}
}
