using System;
using System.IO;
using System.Text;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The item half of the character write gate: an item write captured under a claim must find that claim, and every item
	/// assertion in the inventory system uses the one rule. Moved out of <c>CharacterWriteGateTests</c> with the inventory
	/// system (the Items module, 2026-11-20).
	/// </summary>
	[TestFixture]
	public class ItemWriteGateTests
	{
		private const string InventoryPath = "Assets/FishMMO/Modules/Items/Server/CharacterInventorySystem.cs";


		[Test]
		public void AnItemWriteThatCarriesAClaim_MustFindThatClaim()
		{
			LogAssert.IsFalse(CharacterInventorySystem.AllowsUnclaimedItemWrite(new CharacterSessionLeaseData(18, 3, Guid.NewGuid())),
				"a released character's unclaimed row does not accept a write captured under its old claim");
			LogAssert.IsTrue(CharacterInventorySystem.AllowsUnclaimedItemWrite(default),
				"a write with no triple at all keeps the old fallback");
		}

		[Test]
		public void EveryItemAssertion_UsesTheRule()
		{
			string source = CodeOnly(ReadSource(InventoryPath));
			LogAssert.IsFalse(source.Contains("allowUnclaimed: true"), "no assertion accepts an unclaimed row unconditionally");
			LogAssert.AreEqual(3, Occurrences(source, "allowUnclaimed: AllowsUnclaimedItemWrite("),
				"the batch and both legs of an exchange");
		}


		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Comment lines stripped, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}
	}
}
