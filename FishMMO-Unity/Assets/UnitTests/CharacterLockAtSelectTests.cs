using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A staff character lock is honoured at character select (issue #252).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Selection is the one door every world entry passes through, so the lock is read there:
	/// after the character is known to be the account's own, before <c>SetSelectedAsync</c> marks it
	/// selected. The read fails closed — a lock a database hiccup waves through is not a lock — and a
	/// locked character is answered with <see cref="CharacterSelectResult.CharacterLocked"/>, so the
	/// player is told why rather than handed a generic failure.
	/// </para>
	/// <para>
	/// Nothing sweeps a lapsed lock, so whether a lock is in force is a question asked with a clock:
	/// <see cref="CharacterLockState.IsLocked"/>, tested here behaviourally.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CharacterLockAtSelectTests
	{
		private const string CharacterSelectPath =
			"Assets/Scripts/Server/Implementation/LoginServer/CharacterSelect/CharacterSelectSystem.cs";

		private const string SelectSignature = "private async Task ProcessCharacterSelectAsync(";

		private static readonly DateTime Now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

		// ── CharacterLockState ────────────────────────────────────────────────

		[Test]
		public void NoLockIsNotLocked()
		{
			LogAssert.IsFalse(new CharacterLockState(null, null, null).IsLocked(Now));
			LogAssert.IsFalse(default(CharacterLockState).IsLocked(Now), "a row with no lock columns set is not locked");
		}

		[Test]
		public void ALapsedLockIsNotLocked()
		{
			LogAssert.IsFalse(new CharacterLockState(Now.AddSeconds(-1), "gm", "investigating").IsLocked(Now));
			LogAssert.IsFalse(new CharacterLockState(Now.AddDays(-30), "gm", "investigating").IsLocked(Now));
		}

		[Test]
		public void AFutureLockIsLocked()
		{
			LogAssert.IsTrue(new CharacterLockState(Now.AddSeconds(1), "gm", "investigating").IsLocked(Now));
			LogAssert.IsTrue(new CharacterLockState(DateTime.MaxValue, "gm", "investigating").IsLocked(Now));
		}

		[Test]
		public void ALockEndsAtItsEndTimeWithoutBeingCleared()
		{
			var state = new CharacterLockState(Now, "gm", "investigating");
			LogAssert.IsTrue(state.IsLocked(Now.AddTicks(-1)), "in force until its end");
			LogAssert.IsFalse(state.IsLocked(Now), "not in force at its end");
			LogAssert.IsFalse(state.IsLocked(Now.AddHours(1)), "the same stored row reads unlocked once the clock passes it");
		}

		// ── Character select ──────────────────────────────────────────────────

		[Test]
		public void TheLockIsReadAfterOwnershipAndBeforeSelection()
		{
			SourceScanPins.HoldsAndFires("ProcessCharacterSelectAsync", SourceScanPins.ReadCode(CharacterSelectPath),
				c => SourceScanPins.InOrder(SourceScanPins.Body(c, SelectSignature),
					"characterService.FetchAsync(characterName)",
					"string.Equals(character.Account, accountName",
					"characterService.FetchLockAsync(character.ID)",
					"lockResult.Data.IsLocked(DateTime.UtcNow)",
					"characterService.SetSelectedAsync("),
				SourceScanPins.InsertBefore("DatabaseResult<CharacterLockState> lockResult",
					"await characterService.SetSelectedAsync(accountName, character.ID);\n"),
				"the character is selected before the lock is read");
		}

		[Test]
		public void AFailedLockReadRefusesTheSelection()
		{
			SourceScanPins.HoldsAndFires("ProcessCharacterSelectAsync", SourceScanPins.ReadCode(CharacterSelectPath),
				c =>
				{
					string body = SourceScanPins.Body(c, SelectSignature);
					if (body == null)
					{
						return "ProcessCharacterSelectAsync is gone";
					}
					Match open = Regex.Match(body, @"if \(!lockResult\.IsSuccess\)\s*\{");
					if (!open.Success)
					{
						return "a failed lock read must be tested";
					}
					string branch = SourceScanPins.Braced(body, open.Index + open.Length - 1);
					return branch != null && Regex.IsMatch(branch, @"SendSelectFailure\(conn\);\s*return;\s*\}$")
						? null
						: "a failed lock read must send the select failure and return";
				},
				SourceScanPins.RegexReplaceFirst(@"(if \(!lockResult\.IsSuccess\)\s*\{[\s\S]*?SendSelectFailure\(conn\);\s*)return;", "$1"),
				"a failed read falls through to selection");
		}

		[Test]
		public void ALockedCharacterIsAnsweredAsLockedAndNotSelected()
		{
			LogAssert.IsTrue(Enum.IsDefined(typeof(CharacterSelectResult), CharacterSelectResult.CharacterLocked));

			Func<string, string> check = c =>
			{
				string body = SourceScanPins.Body(c, SelectSignature);
				if (body == null)
				{
					return "ProcessCharacterSelectAsync is gone";
				}
				Match open = Regex.Match(body, @"if \(lockResult\.Data\.IsLocked\(DateTime\.UtcNow\)\)\s*\{");
				if (!open.Success)
				{
					return "the lock must be tested with the current UTC clock";
				}
				string branch = SourceScanPins.Braced(body, open.Index + open.Length - 1);
				if (branch == null)
				{
					return "the locked branch does not balance";
				}
				if (!branch.Contains("Result = CharacterSelectResult.CharacterLocked,") || !branch.Contains("LockedUntilUtcTicks = "))
				{
					return "a locked character must be answered with CharacterLocked and when the lock ends";
				}
				return Regex.IsMatch(branch, @"return;\s*\}$") ? null : "the locked branch must return before selection";
			};

			string code = SourceScanPins.ReadCode(CharacterSelectPath);
			SourceScanPins.HoldsAndFires("ProcessCharacterSelectAsync", code, check,
				SourceScanPins.Replace("Result = CharacterSelectResult.CharacterLocked,", "Result = CharacterSelectResult.OtherCharacterInWorld,"),
				"the lock is answered with some other result");
			SourceScanPins.HoldsAndFires("ProcessCharacterSelectAsync", code, check,
				SourceScanPins.RegexReplaceFirst(@"(CharacterSelectResult\.CharacterLocked,[\s\S]*?\}\);\s*)return;", "$1"),
				"the locked branch falls through to selection");
		}
	}
}
