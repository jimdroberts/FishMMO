using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's inventory, including updating, saving, deleting, and loading inventory items from the database.
		/// </summary>
		public class CharacterInventoryService
	{
		/// <summary>
		/// Updates an existing inventory item by its ID for a specific character.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="item">The item to update.</param>
		public static void Update(NpgsqlDbContext dbContext, long characterID, Item item)
		{
			if (characterID == 0)
			{
				return;
			}

			if (item == null)
			{
				return;
			}

			var dbItem = dbContext.CharacterInventoryItems.FirstOrDefault(c => c.CharacterID == characterID && c.ID == item.ID);
			// update slot
			if (dbItem != null)
			{
				dbItem.CharacterID = characterID;
				dbItem.TemplateID = item.Template.ID;
				dbItem.Slot = item.Slot;
				dbItem.Seed = item.IsGenerated ? item.Generator.Seed : 0;
				dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Updates an inventory slot or adds a new inventory item for a character, initializing the item with the new ID if added.
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

			var dbItem = dbContext.CharacterInventoryItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == item.Slot);
			// update slot or add
			if (dbItem != null)
			{
				dbItem.CharacterID = characterID;
				dbItem.TemplateID = item.Template.ID;
				dbItem.Slot = item.Slot;
				dbItem.Seed = item.IsGenerated ? item.Generator.Seed : 0;
				dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
				dbContext.SaveChanges();
			}
			else
			{
				dbItem = new CharacterInventoryEntity()
				{
					CharacterID = characterID,
					TemplateID = item.Template.ID,
					Slot = item.Slot,
					Seed = item.IsGenerated ? item.Generator.Seed : 0,
					Amount = item.IsStackable ? item.Stackable.Amount : 0,
				};
				dbContext.CharacterInventoryItems.Add(dbItem);
				dbContext.SaveChanges();
				item.Initialize(dbItem.ID, dbItem.Amount, dbItem.Seed);
			}
		}

		/// <summary>
		/// Saves a character's inventory to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose inventory will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			var dbInventoryItems = dbContext.CharacterInventoryItems.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.Slot);

			foreach (Item item in inventoryController.Items)
			{
				if (dbInventoryItems.TryGetValue(item.Slot, out CharacterInventoryEntity dbItem))
				{
					dbItem.CharacterID = character.ID;
					dbItem.TemplateID = item.Template.ID;
					dbItem.Slot = item.Slot;
					dbItem.Seed = item.IsGenerated ? item.Generator.Seed : 0;
					dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
				}
				else
				{
					dbContext.CharacterInventoryItems.Add(new CharacterInventoryEntity()
					{
						CharacterID = character.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						Amount = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all inventory items for a character from the database. If keepData is false, the entries are removed.
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
				var dbInventoryItems = dbContext.CharacterInventoryItems.Where(c => c.CharacterID == characterID);
				if (dbInventoryItems != null)
				{
					dbContext.CharacterInventoryItems.RemoveRange(dbInventoryItems);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Deletes a specific inventory item for a character from the database. If keepData is false, the entry is removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="slot">The inventory slot to delete.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long slot, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}

			if (!keepData)
			{
				var dbItem = dbContext.CharacterInventoryItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == slot);
				if (dbItem != null)
				{
					dbContext.CharacterInventoryItems.Remove(dbItem);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's inventory from the database and assigns the items to the character's inventory controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load inventory for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}
			var dbInventoryItems = dbContext.CharacterInventoryItems.Where(c => c.CharacterID == character.ID);
			foreach (CharacterInventoryEntity dbItem in dbInventoryItems)
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
				inventoryController.SetItemSlot(item, dbItem.Slot);
			};
		}
	}
}