using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A character can have several pets out: the Pet Slots character attribute (one unless an archetype, item or
	/// buff raises it), each summon spell with its
	/// own maximum, every command to all pets, every pet saved under its slot, and a row per pet in the panel that pins it.
	/// </summary>
	[TestFixture]
	public class MultiplePetsTests
	{
		private const int Wolf = 11;
		private const int Elemental = -22;

		private static string Read(string relative)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relative);
			if (!ProjectFiles.Exists(path))
			{
				Assert.Ignore($"{relative} is not checked out at {path}.");
			}
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private const string Scripts = "Assets/FishMMO/";
		private const string Services = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/";

		[Test]
		public void OneSlot_ASummonReplacesThePetOut()
		{
			LogAssert.AreEqual(0, PetSlotRules.IndexToReplace(new List<int> { Elemental }, Wolf, spellMax: 3, slots: 1),
				"with one slot, any summon replaces the pet that is out, whatever spell raised it");
			LogAssert.AreEqual(-1, PetSlotRules.IndexToReplace(new List<int>(), Wolf, spellMax: 3, slots: 1), "an empty slot fits");
			LogAssert.AreEqual(1, PetSlotRules.SpellCap(3, 1), "the slots cap a spell that allows more");
		}

		[Test]
		public void TheSpellsOwnMaximumReplacesTheOldestOfThatSpell()
		{
			// Three slots; the wolf spell allows two. Out: wolf, elemental, wolf.
			List<int> out3 = new List<int> { Wolf, Elemental, Wolf };
			LogAssert.AreEqual(0, PetSlotRules.IndexToReplace(out3, Wolf, spellMax: 2, slots: 3),
				"a third wolf replaces the OLDEST wolf, not the elemental");

			List<int> wolfAndElemental = new List<int> { Elemental, Wolf };
			LogAssert.AreEqual(-1, PetSlotRules.IndexToReplace(wolfAndElemental, Wolf, spellMax: 2, slots: 3),
				"a second wolf fits while a slot is free");
			LogAssert.AreEqual(1, PetSlotRules.IndexToReplace(wolfAndElemental, Wolf, spellMax: 1, slots: 3),
				"a spell allowing one replaces its own pet even with slots free");
		}

		[Test]
		public void FullSlotsReplaceTheOldestPet()
		{
			LogAssert.AreEqual(0, PetSlotRules.IndexToReplace(new List<int> { Elemental, Elemental }, Wolf, spellMax: 3, slots: 2),
				"with every slot taken by other pets, the oldest goes");
		}

		[Test]
		public void APetKeepsItsSlot_TheLowestFreeOneIsReused()
		{
			LogAssert.AreEqual(0, PetSlotRules.LowestFreeSlot(new List<int> { 1 }, 3), "slot 0 freed by the first pet's death is reused");
			LogAssert.AreEqual(2, PetSlotRules.LowestFreeSlot(new List<int> { 0, 1 }, 3));
			LogAssert.AreEqual(-1, PetSlotRules.LowestFreeSlot(new List<int> { 0 }, 1), "no slot free");
			LogAssert.AreEqual(PetSlotRules.MaxSlots, PetSlotRules.ClampSlots(99), "slots are bounded");
			LogAssert.AreEqual(1, PetSlotRules.ClampSlots(0), "a character always has one slot");
		}

		[Test]
		public void TheSlotsAreTheFinalValueOfThePetSlotsAttribute()
		{
			LogAssert.AreEqual(1, PetSlotRules.SlotsFrom(false, 0), "a character without the attribute has one slot");
			LogAssert.AreEqual(3, PetSlotRules.SlotsFrom(true, 3), "an attribute raised to three (an archetype, an item) gives three");
			LogAssert.AreEqual(1, PetSlotRules.SlotsFrom(true, -2), "a modifier cannot take the last slot away");
			LogAssert.AreEqual(PetSlotRules.MaxSlots, PetSlotRules.SlotsFrom(true, 50), "nor raise it past the bound");

			LogAssert.IsTrue(PetSlotRules.AnyPastSlots(new List<int> { 0, 2 }, 2), "slots fell from three to two: the pet in slot 2 must go");
			LogAssert.IsFalse(PetSlotRules.AnyPastSlots(new List<int> { 0, 1 }, 2), "every pet still inside its slots");

			string asset = Read("Assets/FishMMO/Templates/Entity/CharacterAttributes/Misc/Pet Slots.asset");
			LogAssert.IsTrue(asset.Contains("m_Name: " + PetSlotRules.AttributeName + "\n") && asset.Contains("InitialValue: 1\n") &&
				asset.Contains("MinValue: 1\n") && asset.Contains("ClampFinalValue: 1\n"),
				"the Pet Slots attribute starts at one and its final value never falls under one");

			string meta = Read("Assets/FishMMO/Templates/Entity/CharacterAttributes/Misc/Pet Slots.asset.meta");
			int at = meta.IndexOf("guid: ", StringComparison.Ordinal);
			string guid = meta.Substring(at + 6, 32);
			LogAssert.IsTrue(Read("Assets/FishMMO/Templates/Entity/CharacterAttributes/StandardAttributeDatabase.asset").Contains("guid: " + guid),
				"every character built on the standard attribute database has the Pet Slots attribute");

			string controller = Read(Scripts + "Shared/Implementation/Entity/NPC/Pet/PetController.cs");
			LogAssert.IsTrue(controller.Contains("attributes.TryGetAttribute(PetSlotsAttributeID, out attribute);") &&
				controller.Contains("PetSlotRules.SlotsFrom(has, has ? attribute.FinalValue : 0)"),
				"the slots are read from the attribute's FINAL value, so modifiers count");
			LogAssert.IsTrue(Read(Scripts + "Shared/Core/Entity/NPC/Pet/IPetController.cs").Contains("int PetSlots { get; }"),
				"nothing sets the slots behind the attribute's back");
		}

		[Test]
		public void FallingSlotsDismissThePetsOutsideThem()
		{
			string controller = Read(Scripts + "Shared/Implementation/Entity/NPC/Pet/PetController.cs");
			LogAssert.IsTrue(controller.Contains("subscribedSlots.OnAttributeUpdated += OnPetSlotsAttributeUpdated;") &&
				controller.Contains("subscribedSlots.OnAttributeUpdated -= OnPetSlotsAttributeUpdated;"),
				"the server listens to the Pet Slots attribute while there is a pet, and lets go");

			string system = Read(Scripts + "Server/Implementation/World/SceneServer/Pet/PetSystem.cs");
			LogAssert.IsTrue(system.Contains("petController.OnPetSlotsReduced += PetController_OnPetSlotsReduced;") &&
				system.Contains("petController.OnPetSlotsReduced -= PetController_OnPetSlotsReduced;"),
				"the pet system answers a fall in slots");
			int handler = system.IndexOf("private void PetController_OnPetSlotsReduced(", StringComparison.Ordinal);
			LogAssert.IsTrue(handler >= 0 && system.IndexOf("pet.Slot >= slots", handler, StringComparison.Ordinal) > handler,
				"only the pets outside the new slot count are dismissed");
		}

		[Test]
		public void ARestoredPetTakesOnlyItsOwnSlotsRows()
		{
			List<CharacterPetAttributeData> rows = new List<CharacterPetAttributeData>
			{
				new CharacterPetAttributeData(1, version: 7, characterID: 5, slot: 0, templateID: 100, value: 50, currentValue: 20f),
				new CharacterPetAttributeData(2, version: 7, characterID: 5, slot: 1, templateID: 100, value: 80, currentValue: 75f),
				new CharacterPetAttributeData(3, version: 6, characterID: 5, slot: 1, templateID: 200, value: 9, currentValue: 9f),
			};

			List<PetPersistedAttribute> second = PetSystem.PersistedAttributesFor(rows, slot: 1, petVersion: 7);
			LogAssert.IsNotNull(second, "slot 1 has rows at its version");
			LogAssert.AreEqual(1, second.Count, "only slot 1's row at the pet row's version: not slot 0's, not an older pet's");
			LogAssert.AreEqual(75f, second[0].CurrentValue, "the second pet comes back with its own health");

			LogAssert.IsNull(PetSystem.PersistedAttributesFor(rows, slot: 2, petVersion: 7), "an empty slot restores nothing");

			List<CharacterPetBuffData> buffs = new List<CharacterPetBuffData>
			{
				new CharacterPetBuffData(1, version: 7, characterID: 5, slot: 0, templateID: 300, remainingTime: 4, tickTime: 1, stacks: 1, tickCount: 0),
			};
			LogAssert.IsNull(PetSystem.PersistedBuffsFor(buffs, slot: 1, petVersion: 7), "slot 0's buff stays on slot 0's pet");
			LogAssert.AreEqual(1, PetSystem.PersistedBuffsFor(buffs, slot: 0, petVersion: 7).Count);
		}

		[Test]
		public void TheDatabaseKeysEveryPetTableBySlot()
		{
			string pets = Read(Services + "CharacterPetService.cs");
			LogAssert.IsTrue(pets.Contains("ON CONFLICT (character_id, slot)") && !pets.Contains("ON CONFLICT (character_id)\n"),
				"a pet row is one per character AND slot");
			LogAssert.IsTrue(pets.Contains("AND p.slot = d.slot"),
				"a pet's attribute and buff rows are pruned against the pet row of their own slot, not another pet's");

			LogAssert.IsTrue(Read(Services + "CharacterPetAttributeService.cs").Contains("ON CONFLICT (character_id, slot, template_id)"),
				"pet attributes are keyed by slot");
			LogAssert.IsTrue(Read(Services + "CharacterPetBuffService.cs").Contains("ON CONFLICT (character_id, slot, template_id)"),
				"pet buffs are keyed by slot");
		}

		[Test]
		public void EveryPetOutIsSaved_UnderItsSlot()
		{
			string saving = Read(Scripts + "Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs");
			int capture = saving.IndexOf("private void AppendPetData(IPlayerCharacter character, List<PetSnapshot> pets)", StringComparison.Ordinal);
			// Through the core IPetRoster: every pet the roster holds, by index.
			int loop = saving.IndexOf("for (int i = 0; i < petController.PetSlotCount; ++i)", capture, StringComparison.Ordinal);
			LogAssert.IsTrue(capture >= 0 && loop > capture, "the save captures every pet out");
			LogAssert.IsTrue(saving.Contains("slot: pet.Slot,"), "a pet row is written under the pet's slot");
		}

		[Test]
		public void CommandsGoToEveryPet_AndADeathTakesOnlyThatPet()
		{
			string system = Read(Scripts + "Server/Implementation/World/SceneServer/Pet/PetSystem.cs");
			LogAssert.IsTrue(system.Contains("DismissAllPets(player, petController, conn);"), "Release releases every pet");
			LogAssert.IsTrue(system.Contains("WarpPetToOwner(petController.Pets[i], ownerPosition);"), "Summon calls every pet");
			LogAssert.IsTrue(system.Contains("DefendOwner(petController, petController.Pets[i], attacker);"), "every pet defends its owner");

			int killed = system.IndexOf("private void CharacterSystem_OnPetKilled(", StringComparison.Ordinal);
			int dismiss = system.IndexOf("DismissPet(character, petController, pet, conn);", killed, StringComparison.Ordinal);
			LogAssert.IsTrue(killed >= 0 && dismiss > killed, "a pet's death dismisses that pet, not its owner's others");

			string broadcasts = Read(Scripts + "Shared/Implementation/Network/Character/PetBroadcasts.cs");
			int remove = broadcasts.IndexOf("public struct PetRemoveBroadcast", StringComparison.Ordinal);
			LogAssert.IsTrue(broadcasts.IndexOf("public long ID;", remove, StringComparison.Ordinal) > remove,
				"the owner is told WHICH pet left");
		}

		[Test]
		public void EachBarHasItsPetsName_CutToFit_AndIsNeverNarrowerThan32px()
		{
			string uss = Read(Scripts + "Client/GUI/World/Pet/UIPetControl.uss");
			string Block(string selector)
			{
				int at = uss.IndexOf("\n" + selector + " {", StringComparison.Ordinal);
				LogAssert.IsTrue(at >= 0, selector + " is styled");
				return uss.Substring(at, uss.IndexOf('}', at) - at);
			}

			LogAssert.IsTrue(Block(".pet-row").Contains("flex-direction: row;"), "the name sits beside its bar, not above it");
			string name = Block(".pet-row__name");
			LogAssert.IsTrue(name.Contains("text-overflow: ellipsis;") && name.Contains("overflow: hidden;") &&
				name.Contains("white-space: nowrap;") && name.Contains("flex-shrink: 1;") && name.Contains("min-width: 0;"),
				"a long name is cut with an ellipsis rather than pushing the bar out");
			string bar = Block(".pet-health");
			LogAssert.IsTrue(bar.Contains("min-width: 32px;") && bar.Contains("flex-shrink: 0;"), "the bar is at least 32px wide");

			string panel = Read(Scripts + "Client/GUI/World/Pet/UITKPetControl.cs");
			int build = panel.IndexOf("private void BuildRows()", StringComparison.Ordinal);
			int nameAt = panel.IndexOf("row.Add(name);", build, StringComparison.Ordinal);
			int barAt = panel.IndexOf("row.Add(bar);", build, StringComparison.Ordinal);
			LogAssert.IsTrue(build >= 0 && nameAt > build && barAt > nameAt, "each row is the pet's name, then its bar");
		}

		[Test]
		public void APetsRowPinsThatPet()
		{
			string uxml = Read(Scripts + "Client/GUI/World/Pet/UIPetControl.uxml");
			LogAssert.IsTrue(uxml.Contains("name=\"pet-list\""), "the panel has a list for one row per pet");

			string panel = Read(Scripts + "Client/GUI/World/Pet/UITKPetControl.cs");
			LogAssert.IsTrue(panel.Contains("row.RegisterCallback<ClickEvent>(_ => OnPetRowClicked(pet));") &&
				panel.Contains("targetController.TryPinTarget(pet.Transform);"),
				"clicking a pet's row pins that pet");
			LogAssert.IsTrue(panel.Contains("health.OnAttributeUpdated += bound.OnHealth;") &&
				panel.Contains("bound.Health.OnAttributeUpdated -= bound.OnHealth;"),
				"each row follows its own pet's health and releases that subscription");
		}
	}
}
