using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The Mercenaries module's seams: the interaction, the menu entry, the right-click, the window's layout.</summary>
	[TestFixture]
	public class MercenariesModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Mercenaries";

		private static string Group(string name)
		{
			return ProjectFiles.ReadAllText($"Assets/AddressableAssetsData/AssetGroups/{name}.asset").Replace("\r\n", "\n");
		}

		[Test]
		public void SpeakingToAMercenary_SendsTheOffer()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(MercenaryBuilder.TriggerPath));
			LogAssert.IsTrue(Regex.IsMatch(Group("Shared_Static_Permanent"), $@"- m_GUID: {guid}\n    m_Address: Mercenary Interact\n"),
				"the trigger is addressable with the other interactions");
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(ProjectFiles.Path(MercenaryBuilder.TriggerPath));
			LogAssert.IsNotNull(trigger, "it loads as a trigger");
			LogAssert.IsTrue(trigger.OnConditionsMetActions.Exists(a => a is SendMercenaryOfferAction), "it asks whether to hire");
		}

		[Test]
		public void TheMenuOffersTheWindow_AndARightClickGivesGear()
		{
			MercenaryClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			bool listed = false;
			foreach (GameMenuEntries.Entry entry in GameMenuEntries.All)
			{
				listed |= entry.Id == MercenaryClient.MenuEntryId;
			}
			LogAssert.IsTrue(listed, "the game menu has a Mercenary entry");
			LogAssert.IsTrue(InventorySlotClaims.Unregister(MercenaryClient.ClaimId), "the module claims right-clicks on gear");
			MercenaryClient.Install();
		}

		[Test]
		public void TheGearContainer_AndTheSeat_AreRegistered()
		{
			MercenariesModule.InstallSeams(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			bool registered = false;
			for (int i = 0; i < ModuleItemContainers.Count; ++i)
			{
				registered |= ModuleItemContainers.TypeAt(i) == InventoryType.MercenaryEquipment;
			}
			LogAssert.IsTrue(registered, "the gear is saved and loaded with the character's items");
			LogAssert.IsNotNull(PartySeatCompanions.Resolve, "the party finds a seated mercenary");
			LogAssert.IsNull(PartySeatCompanions.ResolveFor(123456789), "and none for a member who has none");
		}

		[Test]
		public void TheWindowIsWired()
		{
			string uxml = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UIMercenary.uxml");
			string code = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UITKMercenary.cs");
			int lookups = 0;
			foreach (Match m in Regex.Matches(code, "Q(?:<\\w+>)?\\(\"([a-z-]+)\"\\)"))
			{
				++lookups;
				LogAssert.IsTrue(uxml.Contains($"name=\"{m.Groups[1].Value}\""), $"the window looks up #{m.Groups[1].Value}, which the layout has");
			}
			LogAssert.IsTrue(lookups > 10, "the lookups were found");
		}

		[Test]
		public void TheWindow_HasTheSheetsSockets_ButNoSaddle()
		{
			string uxml = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UIMercenary.uxml");
			// The character sheet's socket names, in ItemSlot order: the window finds its sockets by them.
			string[] sheet = CharacterSheetView.SlotElementNames;
			LogAssert.AreEqual(System.Enum.GetNames(typeof(ItemSlot)).Length, sheet.Length, "one socket name per ItemSlot");
			for (int i = 0; i < sheet.Length; ++i)
			{
				bool worn = MercenaryRules.Wears((ItemSlot)i);
				LogAssert.AreEqual(worn, uxml.Contains($"name=\"{sheet[i]}\""), worn
					? $"the window has the {sheet[i]} socket, where the sheet has it"
					: $"the window has no {sheet[i]} socket: a mercenary does not wear it");
			}
			LogAssert.IsTrue(uxml.Contains("UICharacterSheet.uss"), "laid out with the character sheet's stylesheet");
		}
	}
}
