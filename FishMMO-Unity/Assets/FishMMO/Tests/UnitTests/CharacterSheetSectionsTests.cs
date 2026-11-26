using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What the character sheet lists under the attributes: the PvP attributes under their own Arena heading in plain words,
	/// and the sections modules keep themselves (<see cref="CharacterSheetSections"/>), such as Duels. The PvP attributes used
	/// to sit in Core as "PvP Rank", "PvP Wins" and so on, with nothing saying the rank was an Elo rating or that duels were
	/// counted elsewhere.
	/// </summary>
	[TestFixture]
	public class CharacterSheetSectionsTests
	{
		private const string PvPFolder = "Assets/FishMMO/Templates/Entity/CharacterAttributes/PvP/";

		private static readonly List<string> registered = new List<string>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < registered.Count; ++i)
			{
				CharacterSheetSections.Unregister(registered[i]);
			}
			registered.Clear();
		}

		private static void Register(string id, string title, int order)
		{
			registered.Add(id);
			CharacterSheetSections.Register(id, title, order, (subject, rows) => true);
		}

		private static int IndexOf(string id)
		{
			IReadOnlyList<CharacterSheetSections.Section> all = CharacterSheetSections.All;
			for (int i = 0; i < all.Count; ++i)
			{
				if (all[i].Id == id)
				{
					return i;
				}
			}
			return -1;
		}

		[Test]
		public void Sections_ListInOrder_AndRegisteringAnIdAgainReplacesIt()
		{
			Register("test.sheet.b", "B", 900001);
			Register("test.sheet.a", "A", 900002);
			LogAssert.IsTrue(IndexOf("test.sheet.b") < IndexOf("test.sheet.a"), "lowest order first, whatever the id");

			Register("test.sheet.b", "B again", 900003);
			int count = 0;
			foreach (CharacterSheetSections.Section section in CharacterSheetSections.All)
			{
				if (section.Id == "test.sheet.b")
				{
					count++;
					LogAssert.AreEqual("B again", section.Title, "the newer registration wins");
				}
			}
			LogAssert.AreEqual(1, count, "one section per id");
			LogAssert.IsTrue(IndexOf("test.sheet.a") < IndexOf("test.sheet.b"), "and it moves to its new place");

			LogAssert.IsTrue(CharacterSheetSections.Unregister("test.sheet.a"));
			LogAssert.AreEqual(-1, IndexOf("test.sheet.a"), "a removed module leaves no heading");
		}

		[TestCase("PvP Rank", "Rating")]
		[TestCase("PvP Wins", "Wins")]
		[TestCase("PvP Losses", "Losses")]
		[TestCase("PvP Matches", "Matches")]
		public void ThePvPAttributes_ListUnderArena_InPlainWords(string asset, string label)
		{
			CharacterAttributeTemplate template = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(ProjectFiles.Path(PvPFolder + asset + ".asset"));
			LogAssert.IsNotNull(template, $"{asset} exists");
			LogAssert.AreEqual(asset, template.Name, "the asset keeps its name: the ID is a hash of it, and every saved value hangs off the ID");
			LogAssert.AreEqual("Arena", template.SheetCategory, "its own heading, apart from Core and from Duels");
			LogAssert.AreEqual(label, template.SheetName);

			TooltipContent content = ((ITooltip)template).BuildContent();
			LogAssert.IsFalse(content.IsEmpty, "hovering the row explains it");
		}

		[Test]
		public void AnAttributeWithNoSheetLabel_ShowsItsName()
		{
			CharacterAttributeTemplate template = UnityEngine.ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			try
			{
				template.name = "Jump Speed";
				LogAssert.AreEqual("Jump Speed", template.SheetName);
				template.SheetLabel = "  ";
				LogAssert.AreEqual("Jump Speed", template.SheetName, "blank is empty");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(template);
			}
		}
	}
}
