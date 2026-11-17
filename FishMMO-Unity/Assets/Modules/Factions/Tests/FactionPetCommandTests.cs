using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A pet sent at a neutral NPC by an owner with aggression on. The pet answers as its owner.
	/// </summary>
	[TestFixture]
	public class FactionPetCommandTests
	{
		private readonly List<GameObject> temporaries = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject temporary in temporaries)
			{
				if (temporary != null) Object.DestroyImmediate(temporary);
			}
			temporaries.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

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

		private static FactionController FactionsOf(ICharacter character)
		{
			FactionController factions = ((Component)character).GetComponent<FactionController>();
			if (factions == null)
			{
				factions = ((Component)character).gameObject.AddComponent<FactionController>();
			}
			factions.InitializeOnce(character);
			return factions;
		}

		private static void SetAggression(FactionController factions, bool on)
		{
			FieldInfo field = typeof(FactionController).GetField("aggressionEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field);
			field.SetValue(factions, on);
		}

		[Test]
		public void APetMayHarmANeutral_WhenItsOwnersAggressionIsOn()
		{
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("PetCommandOwner", 9501);
			Pet pet = NewCharacter<Pet>("PetCommandPet", -9502);
			pet.PetOwner = owner;
			NPC neutral = NewCharacter<NPC>("PetCommandNeutral", -9503);

			FactionController ownerFactions = FactionsOf(owner);
			FactionsOf(pet);
			FactionsOf(neutral);

			Assert.IsFalse(HarmRule.MayHarm(pet, neutral, out _), "a neutral is off limits while the toggle is off");

			SetAggression(ownerFactions, true);
			Assert.IsTrue(HarmRule.MayHarm(owner, neutral, out _), "the owner may harm a neutral with aggression on");
			Assert.IsTrue(HarmRule.MayHarm(pet, neutral, out _), "and so may the pet it sends, answering as its owner");
		}

		/// <summary>
		/// Sending a pet at a neutral turns the owner's aggression on but only where the toggle is all that stands
		/// in the way: never at a companion, which no toggle makes a target.
		/// </summary>
		[Test]
		public void OnlyARefusalTheToggleLifts_QualifiesForTurningAggressionOn()
		{
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("PetAggressOwner", 9511);
			Pet pet = NewCharacter<Pet>("PetAggressPet", -9512);
			pet.PetOwner = owner;
			Pet secondPet = NewCharacter<Pet>("PetAggressSecondPet", -9513);
			secondPet.PetOwner = owner;
			NPC neutral = NewCharacter<NPC>("PetAggressNeutral", -9514);

			FactionController ownerFactions = FactionsOf(owner);
			FactionsOf(pet);
			FactionsOf(secondPet);
			FactionsOf(neutral);

			Assert.IsTrue(HarmRule.RefusedForWantOfAggression(pet, neutral), "a neutral is refused only for want of aggression");
			Assert.IsFalse(HarmRule.RefusedForWantOfAggression(pet, secondPet), "the owner's other pet is a companion: no toggle makes it a target");

			SetAggression(ownerFactions, true);
			Assert.IsTrue(HarmRule.MayHarm(pet, neutral, out _), "once on, the pet may attack the neutral");
			Assert.IsFalse(HarmRule.RefusedForWantOfAggression(pet, neutral), "and nothing is refused any more");
			Assert.IsFalse(HarmRule.MayHarm(pet, secondPet, out _), "the companion stays off limits with the toggle on");
		}
	}
}
