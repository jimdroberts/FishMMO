using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A new character takes its account's access level (2026-11-17, Jim): a staff account's characters are staff
	/// characters. Before, every character was created at Player whatever its account was.
	/// </summary>
	[TestFixture]
	public class CharacterCreateAccessLevelTests
	{
		private const string CreatePath = "Assets/FishMMO/Server/Implementation/LoginServer/CharacterCreate/CharacterCreateSystem.cs";

		private static string Code()
		{
			string source = ProjectFiles.ReadAllText(CreatePath).Replace("\r\n", "\n");
			var kept = new System.Text.StringBuilder();
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (!(t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)))
				{
					kept.Append(line).Append('\n');
				}
			}
			return kept.ToString();
		}

		[Test]
		public void TheCharacterIsCreatedAtItsAccountsLevel_ReadFromTheAccountRow()
		{
			string code = Code();
			LogAssert.IsFalse(code.Contains("accessLevel: (byte)AccessLevel.Player"), "no character is created at a fixed Player level");
			LogAssert.IsTrue(code.Contains("accessLevel: accountAccessLevel"), "the character row carries the account's level");

			int read = code.IndexOf("await accountService.FetchForLoginAsync(accountName)", StringComparison.Ordinal);
			int build = code.IndexOf("new CharacterData(", StringComparison.Ordinal);
			LogAssert.IsTrue(read >= 0 && build > read,
				"the level is read from the account row before the character is built, not from the login session's copy, so a demotion since login holds");
		}

		[Test]
		public void AnUnreadableAccount_RefusesTheCreate_AndABannedOrUnknownLevelIsNeverHandedOn()
		{
			string code = Code();
			int read = code.IndexOf("await accountService.FetchForLoginAsync(accountName)", StringComparison.Ordinal);
			int refusal = code.IndexOf("if (!accountResult.IsSuccess)", read, StringComparison.Ordinal);
			int error = code.IndexOf("Result = CharacterCreateResult.Error", refusal, StringComparison.Ordinal);
			int nextReturn = code.IndexOf("return;", refusal, StringComparison.Ordinal);
			LogAssert.IsTrue(refusal > read && error > refusal && nextReturn > error,
				"a failed read answers Error and stops, rather than creating at a guessed level");
			LogAssert.IsTrue(code.Contains("(AccessLevel)accountAccessLevel == AccessLevel.Banned") &&
				code.Contains("Enum.IsDefined(typeof(AccessLevel), accountAccessLevel)"),
				"a banned or out-of-range level becomes Player");
		}
	}
}
