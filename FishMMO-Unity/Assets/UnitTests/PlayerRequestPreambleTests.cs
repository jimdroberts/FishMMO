using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the player-request preamble is written in one place, and that a new server
	/// handler cannot quietly hand-roll its own copy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every player-initiated broadcast handler opens the same way: is there a connection, has it
	/// spawned an object, is that object a player character, and may that character act. The
	/// sequence had been pasted into roughly thirty handlers, and the copies had drifted — some
	/// checked <c>CanAct</c>, some did not, some resolved a controller in the same condition, and
	/// six carried a literal copy-paste scar with the newline lost in transit. Drift in a state
	/// gate is the failure mode that matters here: a handler that silently omits <c>CanAct</c> lets
	/// a dead, stunned or mid-teleport character act, and nothing about the omission looks any
	/// different from the deliberate ones.
	/// </para>
	/// <para>
	/// So this fixture does not check that the preamble is CORRECT — <c>CharacterStateValidation</c>
	/// owns that. It checks that there is only one of it. The budget below lists the places that
	/// still write it by hand, each for a reason; anything NOT on the list must go through
	/// <c>ServerBehaviour.TryBeginPlayerRequest</c>. A file may drop below its budget freely (that
	/// is a later migration), but it may never exceed it, and a new file may not appear at all.
	/// </para>
	/// <para>
	/// Source-scanning, like its neighbours, with the limits that implies: it sees what was
	/// WRITTEN, not what runs. Its value is that it fails when a copy is added, rather than when a
	/// player reports being able to trade while dead.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlayerRequestPreambleTests
	{
		private const string EntryPointPath =
			"Assets/Scripts/Server/Implementation/ServerBehaviour.PlayerRequests.cs";

		private const string EntryPointKey =
			"Implementation/ServerBehaviour.PlayerRequests.cs";

		/// <summary>The resolution the shared entry point replaces.</summary>
		private const string Resolution = "conn.FirstObject.GetComponent<IPlayerCharacter>()";

		private static string ServerRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Server");

		/// <summary>
		/// Files still allowed to resolve the acting player by hand, and how many times each may.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Three kinds of entry. The first is the earlier consolidations — <c>TryBeginGuildRequest</c>,
		/// <c>TryResolveOpenSession</c>, <c>TryGetActingPlayer</c>, <c>TryResolveCorpseRequest</c>, plus
		/// the arena and dungeon-entrance resolvers. Each has callers and its own out-parameters, so
		/// folding them into the shared entry point has a blast radius and is not this change.
		/// </para>
		/// <para>
		/// The second is handlers whose preamble is NOT the common one: they answer each refusal
		/// differently — a purchase result, a sell result, a craft failure, a container result, a
		/// travel refusal — and a shared boolean cannot say which check refused. Rewriting them to
		/// fit would either drop those answers or invent new ones, and a handler that stops
		/// answering is exactly the defect <c>MerchantPurchaseResultTests</c> exists to prevent.
		/// </para>
		/// <para>
		/// The third is not a preamble at all: post-commit achievement increments on the async path,
		/// which re-resolve the character because the handler's own local is long out of scope by
		/// then, and the naming system, which requires <c>IsLoaded</c> rather than <c>CanAct</c> in
		/// order to close a name-enumeration oracle.
		/// </para>
		/// </remarks>
		private static Dictionary<string, int> HandWrittenBudget()
		{
			return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
			{
				// The shared entry point itself. This is the one copy that is supposed to exist.
				{ EntryPointKey, 1 },

				// Earlier consolidations, each with live callers. Candidates for a later pass.
				{ "Implementation/World/SceneServer/Guild/GuildSystem.Ranks.cs", 1 },
				{ "Implementation/World/SceneServer/Housing/HousingSystem.Network.cs", 1 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Corpse.cs", 2 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.DungeonFinder.cs", 2 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Arena.cs", 1 },
				{ "Implementation/World/SceneServer/Trade/TradeSystem.Handlers.cs", 4 },

				// Handlers that answer each refusal with its own reason.
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.AbilityCraft.cs", 1 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Container.cs", 1 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Mailbox.cs", 3 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Merchant.cs", 2 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.Waypoint.cs", 1 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.cs", 1 },

				// Resolved after the guard, inside a try/finally that reports the failure reason.
				{ "Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs", 5 },

				// Not the state gate at all: these require IsLoaded, to close a name-enumeration oracle.
				{ "Implementation/World/SceneServer/Naming/NamingSystem.cs", 2 },

				// Post-commit achievement increments on the async path, not handler preambles.
				{ "Implementation/World/SceneServer/Friend/FriendSystem.cs", 1 },
				/* Five, not three: guild create and guild join each re-resolve twice on their async
				 * continuation — once to build the self roster entry for the GuildAddBroadcast and once
				 * for the achievement increment. These run AFTER the database call returns, where the
				 * handler-entry helper has nothing to offer: the request was admitted long ago and what
				 * is wanted is the character as it stands now. Raised deliberately when the join path
				 * gained the same pair the create path already had. */
				{ "Implementation/World/SceneServer/Guild/GuildSystem.cs", 5 },
				{ "Implementation/World/SceneServer/Party/PartySystem.cs", 3 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.ArenaMatch.cs", 1 },
				{ "Implementation/World/SceneServer/Interactable/InteractableSystem.GroupFinder.cs", 1 },
			};
		}

		/// <summary>Source text with line endings normalised, so offsets do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Every server script, keyed by its path relative to the server root.</summary>
		private static Dictionary<string, string> ServerSources()
		{
			var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (string file in Directory.GetFiles(ServerRoot, "*.cs", SearchOption.AllDirectories))
			{
				string key = file.Substring(ServerRoot.Length)
					.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					.Replace('\\', '/');
				sources[key] = File.ReadAllText(file).Replace("\r\n", "\n");
			}

			LogAssert.IsTrue(sources.Count > 0, "there must be server sources to scan");
			return sources;
		}

		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			int at = 0;
			while (true)
			{
				at = text.IndexOf(needle, at, StringComparison.Ordinal);
				if (at < 0)
				{
					return count;
				}
				++count;
				at += needle.Length;
			}
		}

		[Test]
		public void TheSharedEntryPointExists()
		{
			string source = ReadSource(EntryPointPath);

			LogAssert.IsTrue(source.Contains("protected bool TryBeginPlayerRequest("),
				"the shared entry point must exist on ServerBehaviour, or there is nothing to migrate to");
			LogAssert.IsTrue(source.Contains("public readonly struct PlayerRequestContext"),
				"the preamble's result must be a named type, so what it produces can grow without touching every handler");
			LogAssert.IsTrue(source.Contains("public enum PlayerRequestGate"),
				"the CanAct opt-out must be a named value rather than a bare bool at the call site");
		}

		[Test]
		public void TheSharedEntryPointRunsEveryCheckThePreamblesRan()
		{
			/* The refactor is safe only if this method is the union of what it replaced and nothing
			 * less. Each of these four checks stood in every inline copy that was migrated. */
			string source = ReadSource(EntryPointPath);
			int start = source.IndexOf("protected bool TryBeginPlayerRequest(", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "the entry point must still be declared");

			string body = source.Substring(start);

			LogAssert.IsTrue(body.Contains("if (conn == null || conn.FirstObject == null)"),
				"a request with no connection, or none spawned yet, must still be refused");
			LogAssert.IsTrue(body.Contains(Resolution),
				"the acting player must still be resolved from the connection's first object");
			LogAssert.IsTrue(body.Contains("if (character == null)"),
				"a first object that is not a player character must still be refused");
			LogAssert.IsTrue(body.Contains("CharacterStateValidation.CanAct(character)"),
				"the character-state gate must still run — this is the check whose omission is a security bug");
		}

		[Test]
		public void TheCanActGateIsOnByDefaultAndOptedOutOfByName()
		{
			/* Which way the default points is the whole safety property. A handler written without
			 * thinking about the gate gets the gate; skipping it has to be typed out, and what gets
			 * typed out is greppable. */
			string source = ReadSource(EntryPointPath);

			LogAssert.IsTrue(source.Contains("PlayerRequestGate gate = PlayerRequestGate.RequireCanAct"),
				"the default must REQUIRE CanAct, so forgetting to think about it is the safe outcome");
			LogAssert.IsTrue(source.Contains("RequireCanAct = 0"),
				"RequireCanAct must be the zero value, so a default-initialised gate is the strict one");
			LogAssert.IsTrue(source.Contains("SkipCanAct"),
				"the opt-out must exist: at least one request is deliberately not gated on character state");
		}

		[Test]
		public void EveryOptOutSaysWhichLaterGateCoversIt()
		{
			/* An ungated request is indistinguishable from a forgotten gate unless it says so.
			 * TryResolveArenaBoard was exactly that: no CanAct, no explanation, alongside a
			 * group-finder handler whose identical omission is documented in full. */
			foreach (KeyValuePair<string, string> entry in ServerSources())
			{
				if (entry.Key == EntryPointKey ||
					!entry.Value.Contains("PlayerRequestGate.SkipCanAct"))
				{
					continue;
				}

				LogAssert.IsTrue(entry.Value.Contains("CanActOrMove"),
					$"{entry.Key} skips the character-state gate, so it must name the later gate that covers it");
			}
		}

		[Test]
		public void EveryCallSiteRefusesTheRequestWhenTheEntryPointRefuses()
		{
			/* A caller that ignores the return reads its character off a `default` context and acts
			 * for somebody who was never resolved. Every call site is written as a refusal. */
			int callSites = 0;

			foreach (KeyValuePair<string, string> entry in ServerSources())
			{
				if (entry.Key == EntryPointKey)
				{
					// Its own declaration and doc comment name it without calling it.
					continue;
				}

				int guarded = Occurrences(entry.Value, "if (!TryBeginPlayerRequest(");
				int total = Occurrences(entry.Value, "TryBeginPlayerRequest(");

				LogAssert.AreEqual(total, guarded,
					$"every TryBeginPlayerRequest in {entry.Key} must be used as a refusal, not called and ignored");
				callSites += guarded;
			}

			LogAssert.IsTrue(callSites >= 38,
				"the migrated handlers must still go through the shared entry point");
		}

		[Test]
		public void NoHandlerOutsideTheBudgetResolvesTheActingPlayerByHand()
		{
			Dictionary<string, int> budget = HandWrittenBudget();
			var offenders = new List<string>();

			foreach (KeyValuePair<string, string> entry in ServerSources())
			{
				int found = Occurrences(entry.Value, Resolution);
				if (found == 0)
				{
					continue;
				}

				int allowed;
				if (!budget.TryGetValue(entry.Key, out allowed))
				{
					offenders.Add($"{entry.Key}: {found} hand-written resolution(s), none budgeted — " +
						"use ServerBehaviour.TryBeginPlayerRequest");
					continue;
				}

				/* Deliberately one-sided. Fewer is a migration, and welcome; more is a new copy. */
				if (found > allowed)
				{
					offenders.Add($"{entry.Key}: {found} hand-written resolution(s), {allowed} budgeted");
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"the player-request preamble must be written once: " + string.Join("; ", offenders.ToArray()));
		}

		[Test]
		public void TheCopyPasteScarIsGone()
		{
			/* Six handlers carried the resolution and the null check jammed onto one line — the same
			 * paste, propagated six times, missing newline and all. Harmless in itself, and the
			 * clearest possible evidence of how this preamble spread. If it comes back, something
			 * was pasted rather than called. */
			foreach (KeyValuePair<string, string> entry in ServerSources())
			{
				LogAssert.IsTrue(!entry.Value.Contains("GetComponent<IPlayerCharacter>();if "),
					$"{entry.Key} carries the pasted preamble scar; call TryBeginPlayerRequest instead");
			}
		}
	}
}
