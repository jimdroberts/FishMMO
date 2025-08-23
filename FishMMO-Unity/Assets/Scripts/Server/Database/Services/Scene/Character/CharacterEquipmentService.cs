using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's equipment, including setting, deleting, and loading equipped items from the database.
		/// </summary>
		public class CharacterEquipmentService
	{
		/// <summary>
		/// Updates an equipment slot or adds a new equipped item for a character, initializing the item with the new ID if added.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="item">The item to set in the slot.</param>
		public static void SetSlot(NpgsqlDbContext dbContext, long characterID, Item item)
		{
			if (characterID == 0)
			{
				return;
			}

			if (item == null)
			{
				return;
			}

			var dbItem = dbContext.CharacterEquippedItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == item.Slot);
			// update slot or add
			if (dbItem != null)
			{
				dbItem.CharacterID = characterID;
				dbItem.TemplateID = item.Template.ID;
				dbItem.Slot = item.Slot;
				dbItem.Seed = item.Generator != null ? item.Generator.Seed : 0;
				dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
				dbContext.SaveChanges();
			}
			else
			{
				dbItem = new CharacterEquipmentEntity()
				{
					CharacterID = characterID,
					TemplateID = item.Template.ID,
					Slot = item.Slot,
					Seed = item.Generator != null ? item.Generator.Seed : 0,
					Amount = item.IsStackable ? item.Stackable.Amount : 0,
				};
				dbContext.CharacterEquippedItems.Add(dbItem);
				dbContext.SaveChanges();
				item.Initialize(dbItem.ID, dbItem.Amount, dbItem.Seed);
			}
		}

		/// <summary>
		/// Deletes all equipped items for a character from the database. If keepData is false, the entries are removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}

			if (!keepData)
			{
				var dbEquippedItems = dbContext.CharacterEquippedItems.Where(c => c.CharacterID == characterID);
				if (dbEquippedItems != null)
				{
					dbContext.CharacterEquippedItems.RemoveRange(dbEquippedItems);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Deletes a specific equipped item for a character from the database. If keepData is false, the entry is removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="slot">The equipment slot to delete.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long slot, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var dbItem = dbContext.CharacterEquippedItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == slot);
				if (dbItem != null)
				{
					dbContext.CharacterEquippedItems.Remove(dbItem);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's equipment from the database and assigns the items to the character's equipment controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load equipment for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}
			var dbEquippedItems = dbContext.CharacterEquippedItems.Where(c => c.CharacterID == character.ID);
			foreach (CharacterEquipmentEntity dbItem in dbEquippedItems)
			{
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(dbItem.TemplateID);
				if (template == null)
				{
					return;
				}
				Item item = new Item(dbItem.ID, dbItem.Seed, template, dbItem.Amount);
				if (item == null)
				{
					return;
				}
				equipmentController.SetItemSlot(item, dbItem.Slot);
				if (item.IsEquippable)
				{
					item.Equippable.Equip(character);
				}
			};
		}
	}
}