using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_inventory
		{
			public string character { get; set; }
			public long instanceID { get; set; }
			public int templateID { get; set; }
			public int seed { get; set; }
			public int slot { get; set; }
			public string name { get; set; }
			public int amount { get; set; }
		}

		class character_equipment : character_inventory // same layout
		{
			// PRIMARY KEY (character, slot) is created manually.
		}

		/*void SaveInventory(CharacterInventory inventory)
		{
			// inventory: remove old entries first, then add all new ones
			// (we could use UPDATE where slot=... but deleting everything makes
			//  sure that there are never any ghosts)
			connection.Execute("DELETE FROM character_inventory WHERE character=?", inventory.name);
			for (int i = 0; i < inventory.slots.Count; ++i)
			{
				ItemSlot slot = inventory.slots[i];
				if (slot.amount > 0) // only relevant items to save queries/storage/time
				{
					// note: .Insert causes a 'Constraint' exception. use Replace.
					connection.InsertOrReplace(new character_inventory
					{
						character = inventory.name,
						slot = i,
						name = slot.item.name,
						amount = slot.amount,
						durability = slot.item.durability,
						summonedHealth = slot.item.summonedHealth,
						summonedLevel = slot.item.summonedLevel,
						summonedExperience = slot.item.summonedExperience
					});
				}
			}
		}

		void LoadInventory(CharacterInventory inventory)
		{
			// fill all slots first
			for (int i = 0; i < inventory.size; ++i)
				inventory.slots.Add(new ItemSlot());

			// then load valid items and put into their slots
			// (one big query is A LOT faster than querying each slot separately)
			foreach (character_inventory row in connection.Query<character_inventory>("SELECT * FROM character_inventory WHERE character=?", inventory.name))
			{
				if (row.slot < inventory.size)
				{
					if (ScriptableItem.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableItem itemData))
					{
						Item item = new Item(itemData);
						item.durability = Mathf.Min(row.durability, item.maxDurability);
						item.summonedHealth = row.summonedHealth;
						item.summonedLevel = row.summonedLevel;
						item.summonedExperience = row.summonedExperience;
						inventory.slots[row.slot] = new ItemSlot(item, row.amount);
					}
					else Debug.LogWarning("LoadInventory: skipped item " + row.name + " for " + inventory.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
				}
				else Debug.LogWarning("LoadInventory: skipped slot " + row.slot + " for " + inventory.name + " because it's bigger than size " + inventory.size);
			}
		}

		void SaveEquipment(CharacterEquipment equipment)
		{
			// equipment: remove old entries first, then add all new ones
			// (we could use UPDATE where slot=... but deleting everything makes
			//  sure that there are never any ghosts)
			connection.Execute("DELETE FROM character_equipment WHERE character=?", equipment.name);
			for (int i = 0; i < equipment.slots.Count; ++i)
			{
				ItemSlot slot = equipment.slots[i];
				if (slot.amount > 0) // only relevant equip to save queries/storage/time
				{
					connection.InsertOrReplace(new character_equipment
					{
						character = equipment.name,
						slot = i,
						name = slot.item.name,
						amount = slot.amount,
						durability = slot.item.durability,
						summonedHealth = slot.item.summonedHealth,
						summonedLevel = slot.item.summonedLevel,
						summonedExperience = slot.item.summonedExperience
					});
				}
			}
		}

		void LoadEquipment(CharacterEquipment equipment)
		{
			// fill all slots first
			for (int i = 0; i < equipment.slotInfo.Length; ++i)
				equipment.slots.Add(new ItemSlot());

			// then load valid equipment and put into their slots
			// (one big query is A LOT faster than querying each slot separately)
			foreach (character_equipment row in connection.Query<character_equipment>("SELECT * FROM character_equipment WHERE character=?", equipment.name))
			{
				if (row.slot < equipment.slotInfo.Length)
				{
					if (ScriptableItem.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableItem itemData))
					{
						Item item = new Item(itemData);
						item.durability = Mathf.Min(row.durability, item.maxDurability);
						item.summonedHealth = row.summonedHealth;
						item.summonedLevel = row.summonedLevel;
						item.summonedExperience = row.summonedExperience;
						equipment.slots[row.slot] = new ItemSlot(item, row.amount);
					}
					else Debug.LogWarning("LoadEquipment: skipped item " + row.name + " for " + equipment.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
				}
				else Debug.LogWarning("LoadEquipment: skipped slot " + row.slot + " for " + equipment.name + " because it's bigger than size " + equipment.slotInfo.Length);
			}
		}*/
	}
}