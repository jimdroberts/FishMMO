using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Ticket tiers are enforced in the ticket service, not only by the callers that list tickets
	/// (issue #252).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A ticket number can be typed. A tier that only filters the queue is a tier a game master can
	/// act through by typing <c>/gm reply 42 ...</c> for a ticket they cannot see, so every staff
	/// write in <c>SupportTicketService</c> refuses a ticket above the actor's level before it
	/// changes anything. The reads are gated by their callers, because a read has no actor level in
	/// its signature: both in-game read paths compare the ticket's tier to the staff level, and the
	/// staff queue query carries the level into the SQL.
	/// </para>
	/// <para>
	/// Source scans, with each pin's control run built in (see <see cref="SourceScanPins"/>): the
	/// service has no in-memory seam, and a throwaway database is not an EditMode test.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SupportTicketTierEnforcementTests
	{
		private const string ServicePath =
			"../FishMMO-Database/FishMMO-DB/Npgsql/Services/Support/SupportTicketService.cs";

		private const string CommandDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer";
		private const string SupportCommandsPath = CommandDirectory + "/SceneServerSystem.GameMasterCommands.Support.cs";
		private const string StaffConsolePath = CommandDirectory + "/SceneServerSystem.StaffConsole.cs";

		private const string EnsureCall = "EnsureTier(ticket, actorAccessLevel);";

		/// <summary>The staff writes issue #252 names. Any other method taking an actor level is held to the same rule.</summary>
		private static readonly string[] StaffWrites = { "AppendMessageAsync", "AssignAsync", "SetStatusAsync", "SetPriorityAsync", "SetTierAsync" };

		private static string MethodSignature(string method) => "> " + method + "(";

		/// <summary>
		/// Null when <paramref name="method"/> calls EnsureTier before it writes a field, adds a row or
		/// saves; otherwise why not.
		/// </summary>
		private static string StaffWriteFailure(string code, string method)
		{
			string body = SourceScanPins.Body(code, MethodSignature(method));
			if (body == null)
			{
				return $"{method} is not declared";
			}

			int ensure = body.IndexOf(EnsureCall, StringComparison.Ordinal);
			if (ensure < 0)
			{
				return $"{method} never calls EnsureTier";
			}

			Match write = Regex.Match(body, @"\bticket\.\w+\s*=(?!=)");
			if (write.Success && write.Index < ensure)
			{
				return $"{method} writes '{write.Value}' before EnsureTier";
			}
			foreach (string effect in new[] { "SupportTicketMessages.AddAsync(", "SaveChangesAsync(" })
			{
				int at = body.IndexOf(effect, StringComparison.Ordinal);
				if (at >= 0 && at < ensure)
				{
					return $"{method} reaches {effect} before EnsureTier";
				}
			}

			// A player's own reply is not tier-gated; a staff one always is.
			if (method == "AppendMessageAsync" &&
				!Regex.IsMatch(body, @"if \(authorIsStaff\)\s*\{\s*EnsureTier\(ticket, actorAccessLevel\);\s*\}"))
			{
				return "AppendMessageAsync must gate every staff message, and only on authorIsStaff";
			}
			return null;
		}

		/// <summary>A mutation confined to one method's body.</summary>
		private static Func<string, string> InMethod(string method, Func<string, string> mutate)
		{
			return s =>
			{
				string body = SourceScanPins.Body(s, MethodSignature(method));
				return body == null ? s : s.Replace(body, mutate(body));
			};
		}

		// ── The service ───────────────────────────────────────────────────────

		[Test]
		public void EveryStaffWriteChecksTheTierBeforeItChangesAnything()
		{
			string code = SourceScanPins.ReadCode(ServicePath);
			foreach (string method in StaffWrites)
			{
				SourceScanPins.HoldsAndFires(method, code,
					c => StaffWriteFailure(c, method),
					InMethod(method, SourceScanPins.Replace(EnsureCall, string.Empty)),
					"the EnsureTier call is removed");

				SourceScanPins.HoldsAndFires(method, code,
					c => StaffWriteFailure(c, method),
					InMethod(method, SourceScanPins.InsertBefore(EnsureCall, "ticket.Priority = 0;\n")),
					"a field is written before the tier check");
			}
		}

		[Test]
		public void AnyServiceMethodTakingAnActorLevelIsAStaffWrite()
		{
			/* The named five are the ones that exist today. A sixth method that takes the actor's
			 * level is a staff write by construction, so it is found by its signature rather than by
			 * editing a list here. */
			Func<string, string> check = c =>
			{
				var actorMethods = Regex.Matches(c, @"public async Task<[^\n]*?>\s+(\w+)\(([^)]*)\)")
					.Cast<Match>()
					.Where(m => m.Groups[2].Value.Contains("byte actorAccessLevel"))
					.Select(m => m.Groups[1].Value)
					.ToList();

				foreach (string named in StaffWrites)
				{
					if (!actorMethods.Contains(named))
					{
						return $"{named} no longer takes the actor's level";
					}
				}
				foreach (string method in actorMethods)
				{
					string failure = StaffWriteFailure(c, method);
					if (failure != null)
					{
						return failure;
					}
				}
				return null;
			};

			SourceScanPins.HoldsAndFires("SupportTicketService", SourceScanPins.ReadCode(ServicePath), check,
				SourceScanPins.InsertBefore("private const byte MinTierAccessLevel",
					"public async Task<DatabaseResult> ReopenAsync(long ticketId, byte actorAccessLevel)\n{\nticket.Status = 0;\n}\n"),
				"a new staff write that skips the tier");
		}

		[Test]
		public void EnsureTierRefusesAnActorBelowTheTicketsTier()
		{
			SourceScanPins.HoldsAndFires("EnsureTier", SourceScanPins.ReadCode(ServicePath),
				c =>
				{
					string body = SourceScanPins.Body(c, "private static void EnsureTier(");
					if (body == null)
					{
						return "EnsureTier is gone";
					}
					if (!body.Contains("if (actorAccessLevel < ticket.RequiredAccessLevel)"))
					{
						return "EnsureTier must refuse an actor below the ticket's tier";
					}
					return SourceScanPins.InOrder(body, "throw new DatabaseException(", "DatabaseErrorCodes.Forbidden");
				},
				SourceScanPins.Replace("actorAccessLevel < ticket.RequiredAccessLevel", "actorAccessLevel > ticket.RequiredAccessLevel"),
				"the comparison is inverted");
		}

		[Test]
		public void SearchAppliesTheMaximumTierInTheQuery()
		{
			SourceScanPins.HoldsAndFires("SearchAsync", SourceScanPins.ReadCode(ServicePath),
				c =>
				{
					string body = SourceScanPins.Body(c, MethodSignature("SearchAsync"));
					if (body == null)
					{
						return "SearchAsync is gone";
					}
					string order = SourceScanPins.InOrder(body,
						"if (query.MaxRequiredAccessLevel.HasValue)",
						"byte maxLevel = query.MaxRequiredAccessLevel.Value;",
						"q = q.Where(t => t.RequiredAccessLevel <= maxLevel);",
						"await q.CountAsync(");
					return order;
				},
				SourceScanPins.Replace("q = q.Where(t => t.RequiredAccessLevel <= maxLevel);", string.Empty),
				"the tier filter is dropped");
		}

		[Test]
		public void MovingATicketWritesAnInternalNoteAndLeavesTheActivityClock()
		{
			Func<string, string> check = c =>
			{
				string body = SourceScanPins.Body(c, MethodSignature("SetTierAsync"));
				if (body == null)
				{
					return "SetTierAsync is gone";
				}
				if (!body.Contains("ExecuteTransactionAsync("))
				{
					return "the tier move and its note must commit together";
				}
				if (!Regex.IsMatch(body,
					@"SupportTicketMessages\.AddAsync\(new SupportTicketMessageEntity\s*\{[^{}]*\bAuthorIsStaff = true,[^{}]*\bInternal = true,"))
				{
					return "SetTierAsync must write the reason as an internal staff note";
				}
				if (Regex.IsMatch(body, @"LastActivityUtc\s*=(?!=)"))
				{
					return "SetTierAsync must not move the activity clock";
				}
				return null;
			};

			string code = SourceScanPins.ReadCode(ServicePath);
			SourceScanPins.HoldsAndFires("SetTierAsync", code, check,
				InMethod("SetTierAsync", SourceScanPins.Replace("Internal = true,", "Internal = false,")),
				"the reason is written as a player-visible message");
			SourceScanPins.HoldsAndFires("SetTierAsync", code, check,
				InMethod("SetTierAsync", SourceScanPins.InsertBefore("ticket.AssignedTo = null;", "ticket.LastActivityUtc = DateTime.UtcNow;\n")),
				"the activity clock is moved");
		}

		// ── The in-game callers ───────────────────────────────────────────────

		[Test]
		public void ChatTicketReadRefusesATicketAboveTheStaffLevel()
		{
			SourceScanPins.HoldsAndFires("ReportTicket", SourceScanPins.ReadCode(SupportCommandsPath),
				c =>
				{
					string body = SourceScanPins.Body(c, "private void ReportTicket(");
					string order = SourceScanPins.InOrder(body,
						"byte level = (byte)character.AccessLevel;",
						"tickets.FetchAsync(ticketID, includeInternal: true)",
						"if (result.Data.RequiredAccessLevel > level)",
						"SupportTicketData ticket = result.Data;");
					if (order != null)
					{
						return order;
					}
					return Regex.IsMatch(body, @"if \(result\.Data\.RequiredAccessLevel > level\)\s*\{\s*return ")
						? null
						: "the tier refusal must return before the ticket is described";
				},
				SourceScanPins.Replace("result.Data.RequiredAccessLevel > level", "false"),
				"the tier comparison is removed");
		}

		[Test]
		public void ConsoleTicketDetailRefusesATicketAboveTheStaffLevel()
		{
			SourceScanPins.HoldsAndFires("OnStaffTicketDetailRequest", SourceScanPins.ReadCode(StaffConsolePath),
				c =>
				{
					string body = SourceScanPins.Body(c, "private void OnStaffTicketDetailRequest(");
					string order = SourceScanPins.InOrder(body,
						"byte level = (byte)staff.AccessLevel;",
						"tickets.FetchAsync(ticketID, includeInternal: true)",
						"result.Data.RequiredAccessLevel > level",
						"response.Found = true;");
					if (order != null)
					{
						return order;
					}
					return Regex.IsMatch(body, @"result\.Data\.RequiredAccessLevel > level\)\s*\{\s*failure = [^;]*;\s*\}\s*else if")
						? null
						: "the tier refusal must answer a failure and skip building the detail";
				},
				SourceScanPins.Replace("result.Data.RequiredAccessLevel > level", "false"),
				"the tier comparison is removed");
		}

		[Test]
		public void EveryInGameQueueSearchCarriesTheStaffLevel()
		{
			const string separator = "\n//// file boundary ////\n";
			string code = SourceScanPins.ReadCode(SupportCommandsPath) + separator + SourceScanPins.ReadCode(StaffConsolePath);

			SourceScanPins.HoldsAndFires("BuildStaffTicketQuery", code,
				c =>
				{
					string builder = SourceScanPins.Body(c, "private static SupportTicketQuery BuildStaffTicketQuery(");
					if (builder == null || !builder.Contains("MaxRequiredAccessLevel = accessLevel,"))
					{
						return "BuildStaffTicketQuery must set MaxRequiredAccessLevel from the staff level";
					}

					int searches = Regex.Matches(c, @"\.SearchAsync\(").Count;
					int built = Regex.Matches(c, @"\.SearchAsync\(\s*BuildStaffTicketQuery\(filter, account, level,").Count;
					if (searches < 2 || searches != built)
					{
						return $"{searches} ticket searches, {built} built from the staff level; every in-game search must go through the builder";
					}

					foreach (var (signature, level) in new[]
					{
						("private void ReportTickets(", "byte level = (byte)character.AccessLevel;"),
						("private void OnStaffTicketQueueRequest(", "byte level = (byte)staff.AccessLevel;"),
					})
					{
						string body = SourceScanPins.Body(c, signature);
						if (body == null || !body.Contains(level))
						{
							return $"{signature} must take the level from the staff member";
						}
					}
					return null;
				},
				SourceScanPins.Replace("MaxRequiredAccessLevel = accessLevel,", string.Empty),
				"the builder stops filtering by tier");
		}
	}
}
