using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The Pets module's client wiring (2026-11-20): a pet's nameplate names its owner ("&lt;Name's pet&gt;"). Moved out of the
	/// core Client.
	/// </summary>
	public static class PetsClient
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Install()
		{
			Pet.OnReadID -= OnPetReadId;
			Pet.OnReadID += OnPetReadId;
		}

		/// <summary>
		/// Called when a pet ID is read. Sets the pet's label with the owner's name.
		/// </summary>
		/// <param name="ownerId">The owner's character ID.</param>
		/// <param name="pet">The pet instance.</param>
		private static void OnPetReadId(long ownerId, Pet pet)
		{
			if (pet == null || ownerId == 0) return;

			/* The name lookup can go to the server and come back arbitrarily later, by which
			 * time this pet may have been despawned and handed out of the object pool as some
			 * other player's pet. Capturing the ID and re-checking it on the way back is what
			 * stops the callback labelling a stranger's pet with this owner's name. */
			long petId = pet.ID;
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, ownerId, name =>
			{
				if (pet == null || pet.ID != petId) return;
				if (pet.CharacterNameplate != null) pet.CharacterNameplate.SetLine(NameplateSlot.GuildName, $"<{name}'s pet>");
			});
		}
	}
}
