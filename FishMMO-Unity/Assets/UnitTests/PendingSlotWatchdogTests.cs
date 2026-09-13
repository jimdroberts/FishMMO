using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// There is one pending-slot watchdog in the client, and it runs on the unscaled clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every panel that shows a shared pile — the corpse loot window, the world container window —
	/// locks a row while its take is in flight, because the row cannot disappear until the server
	/// says the item is gone and a player whose click appears to do nothing clicks again. A lock
	/// needs a watchdog: the reply can be lost to a dropped connection, a handler that returned
	/// without broadcasting, or a scene hand-off, and a lock with nothing to release it is worse
	/// than no lock at all — the row is dead for the life of the window.
	/// </para>
	/// <para>
	/// Both panels had written that watchdog themselves, and they disagreed about the clock. The
	/// loot panel measured against <c>Time.time</c>, which Unity scales by <c>Time.timeScale</c>,
	/// so any pause or slow-motion froze its watchdog while the network kept running: a reply lost
	/// during a pause left the row locked, and at <c>timeScale</c> 0 no amount of waiting could
	/// ever release it. The container panel, doing the same job twenty lines of code away, used
	/// <c>Time.unscaledTime</c> and was right. That is the shape of bug duplication produces —
	/// not two copies of one behaviour, but two behaviours wearing the same name.
	/// </para>
	/// <para>
	/// Both now use <see cref="ItemSlotPendingSet"/>, which keys one
	/// <see cref="PendingReplyGuard"/> per slot and expires against unscaled time. These tests are
	/// deliberately part source-scan and part reflection rather than purely behavioural: the thing
	/// worth preventing is not a wrong answer from a function, it is a third copy of the watchdog
	/// appearing in the next panel somebody writes. A behavioural test cannot see that, because a
	/// fresh hand-rolled dictionary passes every behavioural test on the day it is written — it is
	/// the day the game first pauses that it fails. Sources are read with line endings normalised,
	/// because the tree is CRLF on Windows and CI has checked it out both ways.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PendingSlotWatchdogTests
	{
		private const string LootPath = "Assets/Scripts/Client/GUI/World/Loot/UITKLoot.cs";
		private const string ContainerPath = "Assets/Scripts/Client/GUI/World/Container/UITKContainer.cs";
		private const string SharedSetPath = "Assets/Scripts/Client/GUI/World/ItemContainers/ItemSlotPendingSet.cs";
		private const string GuardPath = "Assets/Scripts/Client/GUI/Core/PendingReplyGuard.cs";
		private const string GuiRoot = "Assets/Scripts/Client/GUI";

		/// <summary>The scaled clock, and only it — <c>Time.timeScale</c> must not match.</summary>
		private static readonly Regex ScaledClock = new Regex(@"\bTime\.time\b", RegexOptions.Compiled);

		/// <summary>A slot-to-timestamp map, however it is spaced.</summary>
		private static readonly Regex SlotTimestampMap =
			new Regex(@"Dictionary\s*<\s*int\s*,\s*(float|double)\s*>", RegexOptions.Compiled);

		/// <summary>A dictionary field or local whose name says it tracks pending state.</summary>
		private static readonly Regex PendingMapDeclaration =
			new Regex(@"Dictionary\s*<[^>]*>\s+\w*[Pp]ending\w*", RegexOptions.Compiled);

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>
		/// The source with comment lines dropped, so a scan sees code and not prose.
		/// </summary>
		/// <remarks>
		/// Both panels explain the timeScale bug in a comment, and those comments name
		/// <c>Time.time</c> — the explanation is the point of them. Scanning raw text would make the
		/// fix fail its own test and, worse, would teach the next person to delete the explanation
		/// to get green. Line-level stripping is enough here: this is a grep with manners, not a
		/// parser, and a trailing comment after real code still leaves the code visible.
		/// </remarks>
		private static string CodeOnly(string source)
		{
			string[] lines = source.Split('\n');
			System.Text.StringBuilder code = new System.Text.StringBuilder(source.Length);

			for (int i = 0; i < lines.Length; ++i)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				code.Append(lines[i]).Append('\n');
			}

			return code.ToString();
		}

		/// <summary>Every C# file under the client GUI tree.</summary>
		private static List<string> GuiSources()
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), GuiRoot);
			LogAssert.IsTrue(Directory.Exists(root), $"the client GUI tree must exist at {root}.");

			List<string> files = new List<string>(Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories));
			LogAssert.IsTrue(files.Count > 0, "the GUI tree must contain sources to scan");
			return files;
		}

		[Test]
		public void NeitherWorldItemPanelKeepsItsOwnPendingSlotMap()
		{
			/* The duplication itself. Both panels must hold the shared set rather than a private
			 * slot-to-timestamp dictionary, because a private one is where the clock can drift out
			 * of agreement again without anybody noticing. */
			string loot = CodeOnly(ReadSource(LootPath));
			string container = CodeOnly(ReadSource(ContainerPath));

			LogAssert.IsTrue(loot.Contains("new ItemSlotPendingSet()"),
				"the loot panel must track pending slots with the shared set");
			LogAssert.IsTrue(container.Contains("new ItemSlotPendingSet()"),
				"the container panel must track pending slots with the shared set");

			LogAssert.IsFalse(SlotTimestampMap.IsMatch(loot),
				"the loot panel must not keep its own slot-to-send-time map");
			LogAssert.IsFalse(SlotTimestampMap.IsMatch(container),
				"the container panel must not keep its own slot-to-send-time map");
		}

		[Test]
		public void ThePanelsHoldTheSharedSetRatherThanAnythingShapedLikeItsReplacement()
		{
			/* Reflection rather than text, so renaming the field or wrapping the dictionary in a
			 * helper struct does not slip past. A panel is allowed to hold no pending set at all —
			 * the inventory-side panels share the static ones in ItemOperationTracker — but it is
			 * not allowed to hold a map from slot to a timestamp. */
			AssertNoHandRolledPendingMap(typeof(UITKLoot));
			AssertNoHandRolledPendingMap(typeof(UITKContainer));

			LogAssert.IsTrue(HasFieldOfType(typeof(UITKLoot), typeof(ItemSlotPendingSet)),
				"UITKLoot must hold an ItemSlotPendingSet");
			LogAssert.IsTrue(HasFieldOfType(typeof(UITKContainer), typeof(ItemSlotPendingSet)),
				"UITKContainer must hold an ItemSlotPendingSet");

			/* The bulk watchdog — take-all and currency — is one wait rather than a set of them, so
			 * it is a bare guard. What matters is that it is the SAME guard type, and therefore the
			 * same clock, and not a float sentinel compared against a timestamp again. */
			LogAssert.IsTrue(HasFieldOfType(typeof(UITKLoot), typeof(PendingReplyGuard)),
				"UITKLoot's take-all and currency wait must use PendingReplyGuard, not a float sentinel");
		}

		[Test]
		public void EveryPendingWatchdogInTheGuiRunsOnTheUnscaledClock()
		{
			/* The bug, stated as a rule. A deadline on something the SERVER is doing cannot be
			 * measured on a clock the client can stop: the server does not slow down when a player
			 * opens the settings menu or dies behind a slow-motion effect.
			 *
			 * Scoped to files that mention pending state rather than banning Time.time from the
			 * whole GUI tree, because a purely cosmetic animation is entitled to scale with the
			 * game. A file that tracks a request in flight is not. */
			List<string> offenders = new List<string>();
			List<string> files = GuiSources();

			for (int i = 0; i < files.Count; ++i)
			{
				string raw = File.ReadAllText(files[i]).Replace("\r\n", "\n");
				if (raw.IndexOf("pending", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				string code = CodeOnly(raw);
				if (ScaledClock.IsMatch(code))
				{
					offenders.Add($"{Path.GetFileName(files[i])} measures a pending wait against Time.time");
				}

				if (PendingMapDeclaration.IsMatch(code))
				{
					offenders.Add($"{Path.GetFileName(files[i])} declares its own pending dictionary");
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"pending waits belong to ItemSlotPendingSet/PendingReplyGuard on unscaled time: " +
				string.Join("; ", offenders.ToArray()));
		}

		[Test]
		public void TheSharedWatchdogItselfNeverReadsTheScaledClock()
		{
			/* Where the fix actually lives. Both panels are now only as correct as these two types,
			 * so the clock is pinned here as well as at the call sites. */
			string set = CodeOnly(ReadSource(SharedSetPath));
			string guard = CodeOnly(ReadSource(GuardPath));

			LogAssert.IsFalse(ScaledClock.IsMatch(set), "ItemSlotPendingSet must not read Time.time");
			LogAssert.IsFalse(ScaledClock.IsMatch(guard), "PendingReplyGuard must not read Time.time");
			LogAssert.IsTrue(guard.Contains("Time.unscaledTime"),
				"PendingReplyGuard expires against unscaled time");
		}

		[Test]
		public void TheFiveSecondTimeoutSurvivedTheMoveToTheSharedSet()
		{
			/* Adopting a shared type is where a timeout silently changes. ItemSlotPendingSet
			 * defaults to 8s and PendingReplyGuard to 30s; both of these panels lock a row in a pile
			 * somebody else is emptying and have always used 5s. Every claim must therefore pass the
			 * constant explicitly — an argument-less TryBegin here is a behaviour change disguised as
			 * a cleanup. */
			AssertTimeoutIsPassedExplicitly(LootPath);
			AssertTimeoutIsPassedExplicitly(ContainerPath);
		}

		/// <summary>
		/// Asserts one panel still declares a 5s timeout and hands it to every claim it makes.
		/// </summary>
		private static void AssertTimeoutIsPassedExplicitly(string relativePath)
		{
			string name = Path.GetFileName(relativePath);
			string code = CodeOnly(ReadSource(relativePath));

			LogAssert.IsTrue(Regex.IsMatch(code, @"PENDING_TIMEOUT_SECONDS\s*=\s*5f"),
				$"{name} must still time a pending row out after 5 seconds");

			/* Matched as a bare call rather than a whole statement: both panels claim inside an
			 * `if (!...TryBegin(...))`, so there is no semicolon to anchor on. Arguments here never
			 * contain a nested call or a line break, which is what makes this safe without a
			 * parser. */
			MatchCollection claims = Regex.Matches(code, @"(?:TryBegin|\.Begin)\s*\(([^()\n]*)\)");
			LogAssert.IsTrue(claims.Count > 0, $"{name} must claim a pending wait somewhere");

			for (int i = 0; i < claims.Count; ++i)
			{
				LogAssert.IsTrue(claims[i].Groups[1].Value.Contains("PENDING_TIMEOUT_SECONDS"),
					$"{name} must pass its own timeout rather than inherit a longer default: {claims[i].Value.Trim()}");
			}
		}

		[Test]
		public void ASecondClaimOnAWaitingSlotIsRefusedRatherThanRearmed()
		{
			/* The behaviour the panels depend on, and the reason TryBegin returns a bool instead of
			 * just recording the claim: re-arming on every click would push a stuck row's deadline
			 * out forever, so the one player most likely to be stuck — the one clicking repeatedly —
			 * would be the one the watchdog never helps. */
			ItemSlotPendingSet set = new ItemSlotPendingSet();

			LogAssert.IsTrue(set.TryBegin(4, 5f), "a free slot is claimed");
			LogAssert.IsFalse(set.TryBegin(4, 5f), "and a second claim on it is refused");
			LogAssert.IsTrue(set.IsPending(4), "the slot is still waiting after the refusal");
			LogAssert.AreEqual(1, set.PendingCount, "and the refusal did not count as a second wait");

			LogAssert.IsTrue(set.Release(4), "the reply releases it");
			LogAssert.IsFalse(set.IsPending(4), "after which the row is clickable again");
			LogAssert.IsTrue(set.TryBegin(4, 5f), "and the slot can be claimed afresh");
		}

		[Test]
		public void PendingSlotsCanBeEnumeratedSoALostRaceIsPrunedImmediately()
		{
			/* What the loot panel's prune needs. A corpse is re-sent whole whenever any looter takes
			 * something, so a slot that has vanished from the snapshot is a race this client lost;
			 * its row is already gone, and leaving the claim to time out would hold a lock on a slot
			 * nobody can see. The panel is looking for slots it no longer HAS, so it cannot ask
			 * IsPending about them — the set has to be enumerable. */
			ItemSlotPendingSet set = new ItemSlotPendingSet();
			set.TryBegin(1, 5f);
			set.TryBegin(7, 5f);
			set.Release(1);

			List<int> pending = new List<int>();
			set.CollectPending(pending);

			LogAssert.AreEqual(1, pending.Count, "only the slot still waiting is reported");
			LogAssert.AreEqual(7, pending[0], "and it is the one that was never released");

			/* Released guards are kept for reuse rather than removed, so a set that has been busy
			 * must still report nothing once everything is answered. */
			set.Release(7);
			pending.Clear();
			set.CollectPending(pending);
			LogAssert.AreEqual(0, pending.Count, "a fully answered set reports no pending slots");
			LogAssert.IsFalse(set.HasAnyPending, "and knows it has nothing outstanding");
		}

		/// <summary>Whether a type declares a field of exactly <paramref name="fieldType"/>.</summary>
		private static bool HasFieldOfType(Type owner, Type fieldType)
		{
			FieldInfo[] fields = owner.GetFields(BindingFlags.Instance | BindingFlags.Static |
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

			for (int i = 0; i < fields.Length; ++i)
			{
				if (fields[i].FieldType == fieldType)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Asserts a panel holds no field that maps a slot index to a timestamp.
		/// </summary>
		private static void AssertNoHandRolledPendingMap(Type owner)
		{
			FieldInfo[] fields = owner.GetFields(BindingFlags.Instance | BindingFlags.Static |
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

			for (int i = 0; i < fields.Length; ++i)
			{
				Type type = fields[i].FieldType;
				if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>))
				{
					continue;
				}

				Type[] args = type.GetGenericArguments();
				bool slotKeyed = args[0] == typeof(int);
				bool timestampValued = args[1] == typeof(float) || args[1] == typeof(double);

				LogAssert.IsFalse(slotKeyed && timestampValued,
					$"{owner.Name}.{fields[i].Name} is a hand-rolled pending-slot watchdog; use ItemSlotPendingSet");
			}
		}
	}
}
