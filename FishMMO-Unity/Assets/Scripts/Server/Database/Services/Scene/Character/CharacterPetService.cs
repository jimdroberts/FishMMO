using System.Linq;
using FishNet.Object;
using FishNet.Utility.Performance;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Server.DatabaseServices
{
	/// <summary>
	/// Handles all Database<->Server Character Pet interactions.
	/// </summary>
	public class CharacterPetService
	{
		public static CharacterPetEntity GetByCharacterID(NpgsqlDbContext dbContext, long characterID, bool checkSpawned = false)
		{
			if (characterID == 0)
			{
				return null;
			}
			var pet = dbContext.CharacterPets.FirstOrDefault(c => c.CharacterID == characterID);
			if (pet == null ||
				checkSpawned && !pet.Spawned)
			{
				//throw new Exception($"Couldn't find pet with id {id}");
				return null;
			}
			return pet;
		}

		/// <summary>
		/// Save a character to the database. Only Scene Servers should be saving characters. A character can only be in one scene at a time.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, ICharacter character, bool spawned)
		{
			if (character == null)
			{
				return;
			}

			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			int petID = spawned ? petController.Pet.PetAbilityTemplate.ID : 0;
			List<int> petAbilities = spawned ? petController.Pet.Abilities : new List<int>();

			var dbPet = dbContext.CharacterPets.FirstOrDefault((c) => c.CharacterID == character.ID);
			if (dbPet != null)
			{
				dbPet.CharacterID = character.ID;
				dbPet.TemplateID = petID;
				dbPet.Abilities = petAbilities;
				dbPet.Spawned = spawned;
				dbContext.SaveChanges();
			}
			else if (spawned)
			{
				dbPet = new CharacterPetEntity()
				{
					CharacterID = character.ID,
					TemplateID = petID,
					Abilities = petAbilities,
					Spawned = spawned,
				};
				dbContext.CharacterPets.Add(dbPet);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Attempts to load a pet from the database. The pet is loaded to the last known position/rotation and set inactive.
		/// </summary>
		public static bool TryLoad(NpgsqlDbContext dbContext, IPlayerCharacter character, out Pet pet)
		{
			pet = null;

			if (character == null ||
				!character.TryGet(out IPetController petController))
			{
				return false;
			}

			using var dbTransaction = dbContext.Database.BeginTransaction();

			var dbPet = dbContext.CharacterPets.FirstOrDefault((c) => c.CharacterID == character.ID && c.Spawned);
			if (dbPet != null &&
				dbTransaction != null)
			{
				// validate pet template
				PetAbilityTemplate petAbilityTemplate = PetAbilityTemplate.Get<PetAbilityTemplate>(dbPet.TemplateID);
				if (petAbilityTemplate == null ||
					petAbilityTemplate.PetPrefab == null)
				{
					return false;
				}

				// validate spawnable prefab
				if (character.NetworkObject.NetworkManager.SpawnablePrefabs.GetObject(true, petAbilityTemplate.PetPrefab.PrefabId) == null)
				{
					return false;
				}

				// instantiate the pet object
				NetworkObject nob = character.NetworkObject.NetworkManager.GetPooledInstantiated(petAbilityTemplate.PetPrefab.PrefabId, petAbilityTemplate.PetPrefab.SpawnableCollectionId, ObjectPoolRetrieveOption.Unset, null, character.Transform.position, character.Transform.rotation, null, true);
				pet = nob.GetComponent<Pet>();
				if (pet == null)
				{
					//throw exception
					throw new UnityEngine.UnityException("Network object is missing the Pet component!");
				}

				pet.PetAbilityTemplate = petAbilityTemplate;

				// pet becomes immortal when loading.. just in case..
				if (pet.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = true;
				}

				pet.LearnAbilities(dbPet.Abilities);

				//CharacterPetAttributeService.Load(dbContext, pet);
				//CharacterPetBuffService.Load(dbContext, pet);

				/*Log.Debug(dbCharacter.Name + " has been loaded at Pos:" +
					  nob.transform.position.ToString() +
					  " Rot:" + nob.transform.rotation.ToString());*/

				dbTransaction.Commit();

				return true;
			}
			return false;
		}
	}
}