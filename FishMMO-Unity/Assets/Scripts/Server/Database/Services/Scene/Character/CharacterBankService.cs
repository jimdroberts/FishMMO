using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's bank inventory, including updating, saving, deleting, and loading bank items from the database.
		/// </summary>
		public class CharacterBankService
	{
		/// <summary>
		/// Updates an existing bank item by its ID for a specific character.
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

			var dbItem = dbContext.CharacterBankItems.FirstOrDefault(c => c.CharacterID == characterID && c.ID == item.ID);
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
		/// Updates a bank slot or adds a new bank item for a character, initializing the item with the new ID if added.
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

			var dbItem = dbContext.CharacterBankItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == item.Slot);
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
				dbItem = new CharacterBankEntity()
				{
					CharacterID = characterID,
					TemplateID = item.Template.ID,
					Slot = item.Slot,
					Seed = item.IsGenerated ? item.Generator.Seed : 0,
					Amount = item.IsStackable ? item.Stackable.Amount : 0,
				};
				dbContext.CharacterBankItems.Add(dbItem);
				dbContext.SaveChanges();
				item.Initialize(dbItem.ID, dbItem.Amount, dbItem.Seed);
			}
		}

		/// <summary>
		/// Saves a character's bank inventory to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose bank inventory will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IBankController bankController))
			{
				return;
			}

			var dbBankItems = dbContext.CharacterBankItems.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.Slot);

			foreach (Item item in bankController.Items)
			{
				if (dbBankItems.TryGetValue(item.Slot, out CharacterBankEntity dbItem))
				{
					dbItem.CharacterID = character.ID;
					dbItem.TemplateID = item.Template.ID;
					dbItem.Slot = item.Slot;
					dbItem.Seed = item.IsGenerated ? item.Generator.Seed : 0;
					dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
				}
				else
				{
					dbContext.CharacterBankItems.Add(new CharacterBankEntity()
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
		/// Deletes all bank items for a character from the database. If keepData is false, the entries are removed.
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
				var dbBankItems = dbContext.CharacterBankItems.Where(c => c.CharacterID == characterID);
				if (dbBankItems != null)
				{
					dbContext.CharacterBankItems.RemoveRange(dbBankItems);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Deletes a specific bank item for a character from the database. If keepData is false, the entry is removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="slot">The bank slot to delete.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long slot, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}

			if (!keepData)
			{
				var dbItem = dbContext.CharacterBankItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == slot);
				if (dbItem != null)
				{
					dbContext.CharacterBankItems.Remove(dbItem);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's bank inventory from the database and assigns the items to the character's bank controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load bank inventory for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IBankController bankController))
			{
				return;
			}

			var dbBankItems = dbContext.CharacterBankItems.Where(c => c.CharacterID == character.ID);
			foreach (CharacterBankEntity dbItem in dbBankItems)
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
				bankController.SetItemSlot(item, dbItem.Slot);
			};
		}
	}
}