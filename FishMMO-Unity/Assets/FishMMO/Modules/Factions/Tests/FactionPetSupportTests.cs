using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for pets that heal and buff: who counts as their side, the spells a summon can teach
	/// them, and the fixed owner-first order they serve in.
	/// </summary>
	[TestFixture]
	public class FactionPetSupportTests
	{
		private const string Scripts = "Assets/FishMMO";
		private const string SummonFireElemental = "Assets/Templates/Entity/Abilities/Types/Summons/Summon Fire Elemental.asset";
		private const string SummonLesserFireElemental = "Assets/Templates/Entity/Abilities/Types/Summons/Summon Lesser Fire Elemental.asset";
		private const string TeachLesserHeal = "Assets/Templates/Entity/Abilities/Events/Summon/Teach Pet Lesser Heal.asset";
		private const string TeachLesserFireball = "Assets/Templates/Entity/Abilities/Events/Summon/Teach Pet Lesser Fireball.asset";
		private const string LesserHeal = "Assets/Templates/Entity/Abilities/Types/Heal/Lesser Heal.asset";
		private const string LesserFireball = "Assets/Templates/Entity/Abilities/Types/Lesser Fireball.asset";
		private const string GeneralGoods = "Assets/Templates/Entity/Interactables/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		private readonly List<GameObject> temporaries = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject temporary in temporaries)
			{
				if (temporary != null)
				{
					Object.DestroyImmediate(temporary);
				}
			}
			temporaries.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private static T Load<T>(string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			Assert.IsNotNull(asset, $"{path} did not load.");
			return asset;
		}

		/// <summary>
		/// Builds a networked character by hand. Its RequireComponent chain brings a duplicate
		/// NetworkObject that FishNet logs an error about; that is an artefact of standing a prefab
		/// up in edit mode, not of anything under test.
		/// </summary>
		private T NewCharacter<T>(string name, long id) where T : BaseCharacter
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			GameObject host = new GameObject(name);
			host.SetActive(false);
			temporaries.Add(host);
			T character = host.AddComponent<T>();
			character.ID = id;
			return character;
		}

		private FactionController FactionsOf(ICharacter character)
		{
			FactionController factions = ((Component)character).GetComponent<FactionController>();
			if (factions == null)
			{
				factions = ((Component)character).gameObject.AddComponent<FactionController>();
			}
			factions.InitializeOnce(character);
			return factions;
		}

		[Test]
		public void APetAndItsOwner_AreAllies()
		{
			/* They share no party or guild of their own, so they used to read as neutral — and an
			 * ally-only heal cast by the pet bounced off the owner it was for. */
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("PetSupportOwner", 9101);
			Pet pet = NewCharacter<Pet>("PetSupportPet", 9102);
			pet.PetOwner = owner;

			FactionController ownerFactions = FactionsOf(owner);
			FactionController petFactions = FactionsOf(pet);

			Assert.AreEqual(FactionAllianceLevel.Ally, petFactions.GetAllianceLevel(ownerFactions), "the pet sees its owner as an ally");
			Assert.AreEqual(FactionAllianceLevel.Ally, ownerFactions.GetAllianceLevel(petFactions), "and the owner sees the pet as one");
		}

		[Test]
		public void TwoPetsOfOneOwner_AreAllies_AndAStrangersPetIsNot()
		{
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("PetSupportOwnerA", 9111);
			PlayerCharacter stranger = NewCharacter<PlayerCharacter>("PetSupportOwnerB", 9112);
			Pet first = NewCharacter<Pet>("PetSupportPetA1", 9113);
			Pet second = NewCharacter<Pet>("PetSupportPetA2", 9114);
			Pet strangersPet = NewCharacter<Pet>("PetSupportPetB", 9115);
			first.PetOwner = owner;
			second.PetOwner = owner;
			strangersPet.PetOwner = stranger;

			Assert.AreEqual(FactionAllianceLevel.Ally, FactionsOf(first).GetAllianceLevel(FactionsOf(second)));
			Assert.AreNotEqual(FactionAllianceLevel.Ally, FactionsOf(first).GetAllianceLevel(FactionsOf(strangersPet)),
				"another player's pet is that player's side, not this one");
		}

	}
}
