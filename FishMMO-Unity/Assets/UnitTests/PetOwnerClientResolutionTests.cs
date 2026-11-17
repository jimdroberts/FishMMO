using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A pet's owner on a client: only the server ever assigned <see cref="Pet.PetOwner"/>, so on every client a pet had no
	/// owner, <see cref="HarmRule.Principal"/> answered the pet itself, and with aggression on the owner's client predicted
	/// hits on its own pet that the server refused. The owner's ID rides the pet's spawn payload.
	/// </summary>
	[TestFixture]
	public class PetOwnerClientResolutionTests
	{
		private readonly List<GameObject> temporaries = new List<GameObject>();
		private readonly List<long> registered = new List<long>();

		[TearDown]
		public void TearDown()
		{
			foreach (long id in registered)
			{
				BaseCharacter.ClientCharacters.Remove(id);
			}
			registered.Clear();
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

		private static void SetPayloadOwner(Pet pet, long ownerID)
		{
			FieldInfo field = typeof(Pet).GetField("payloadOwnerID", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, "Pet keeps the owner ID read from its spawn payload.");
			field.SetValue(pet, ownerID);
		}

		[Test]
		public void APetResolvesItsOwner_FromThePayloadID_OnceTheOwnerIsKnown()
		{
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("ClientPetOwner", 9301);
			Pet pet = NewCharacter<Pet>("ClientPet", -9302);
			SetPayloadOwner(pet, owner.ID);

			Assert.IsNull(pet.PetOwner, "the owner has not spawned on this client yet");

			BaseCharacter.ClientCharacters[owner.ID] = owner;
			registered.Add(owner.ID);

			Assert.AreSame(owner, pet.PetOwner, "resolved lazily once the owner is known");
			Assert.AreSame(owner, HarmRule.Principal(pet), "so the harm rule sees the owner and its pet as one side");
		}

		[Test]
		public void APooledOwnerSlot_ReusedAsSomebodyElse_IsNotKept()
		{
			PlayerCharacter owner = NewCharacter<PlayerCharacter>("ClientPetOwnerA", 9311);
			PlayerCharacter stranger = NewCharacter<PlayerCharacter>("ClientPetOwnerB", 9312);
			Pet pet = NewCharacter<Pet>("ClientPetPooled", -9313);
			SetPayloadOwner(pet, owner.ID);
			BaseCharacter.ClientCharacters[owner.ID] = owner;
			registered.Add(owner.ID);
			Assert.AreSame(owner, pet.PetOwner);

			// The owner's object goes back to the pool and comes out as somebody else.
			BaseCharacter.ClientCharacters.Remove(owner.ID);
			owner.ID = stranger.ID + 1000;

			Assert.IsNull(pet.PetOwner, "an object whose ID no longer matches is not the owner");
		}
	}
}
