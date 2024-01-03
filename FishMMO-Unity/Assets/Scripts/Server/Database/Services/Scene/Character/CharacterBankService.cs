using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterBankService
	{
		/// <summary>
		/// Updates a CharacterBankItem slot to new values or adds a new CharacterBankItem and initializes the Item with the new ID.
		/// </summary>
		public static void SetSlot(NpgsqlDbContext dbContext, long characterID, Item item)
		{
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
				item.Initialize(dbItem.ID, dbItem.Seed);
			}
		}

		/// <summary>
		/// Save a characters inventory to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character)
		{
			if (character == null)
			{
				return;
			}

			var dbBankItems = dbContext.CharacterBankItems.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.Slot);

			foreach (Item item in character.BankController.Items)
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
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the item. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = false)
		{
			if (!keepData)
			{
				var dbBankItems = dbContext.CharacterBankItems.Where(c => c.CharacterID == characterID);
				if (dbBankItems != null)
				{
					dbContext.CharacterBankItems.RemoveRange(dbBankItems);
				}
			}
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the item. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long slot, bool keepData = false)
		{
			if (!keepData)
			{
				var dbItem = dbContext.CharacterBankItems.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == slot);
				if (dbItem != null)
				{
					dbContext.CharacterBankItems.Remove(dbItem);
				}
			}
		}

		/// <summary>
		/// Load character inventory from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
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
				character.BankController.SetItemSlot(item, dbItem.Slot);
			};
		}
	}
}