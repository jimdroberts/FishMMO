using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterEquipmentService
	{
		/// <summary>
		/// Updates a CharacterInventoryItem slot to new values or adds a new CharacterInventoryItem and initializes the Item with the new ID.
		/// </summary>
		public static void SetSlot(NpgsqlDbContext dbContext, long characterID, Item item)
		{
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
				dbItem.Amount = item.IsStackable ? item.Stackable.Amount : 0;
			}
			else
			{
				dbItem = new CharacterEquipmentEntity()
				{
					CharacterID = characterID,
					TemplateID = item.Template.ID,
					Slot = item.Slot,
					Amount = item.IsStackable ? item.Stackable.Amount : 0,
				};
				dbContext.CharacterEquippedItems.Add(dbItem);
				dbContext.SaveChanges();
				item.Initialize(dbItem.ID);
			}
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the item. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = false)
		{
			if (!keepData)
			{
				var dbEquippedItems = dbContext.CharacterEquippedItems.Where(c => c.CharacterID == characterID);
				if (dbEquippedItems != null)
				{
					dbContext.CharacterEquippedItems.RemoveRange(dbEquippedItems);
				}
			}
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the item. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long itemID, bool keepData = false)
		{
			if (!keepData)
			{
				var dbItem = dbContext.CharacterEquippedItems.FirstOrDefault(c => c.CharacterID == characterID && c.ID == itemID);
				if (dbItem != null)
				{
					dbContext.CharacterEquippedItems.Remove(dbItem);
				}
			}
		}

		/// <summary>
		/// Load character equipment from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			var dbEquippedItems = dbContext.CharacterEquippedItems.Where(c => c.CharacterID == character.ID);
			foreach (CharacterEquipmentEntity dbItem in dbEquippedItems)
			{
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(dbItem.TemplateID);
				if (template == null)
				{
					return;
				}
				Item item = new Item(dbItem.ID, template, dbItem.Amount);
				if (item == null)
				{
					return;
				}
				character.EquipmentController.SetItemSlot(item, dbItem.Slot);
			};
		}
	}
}