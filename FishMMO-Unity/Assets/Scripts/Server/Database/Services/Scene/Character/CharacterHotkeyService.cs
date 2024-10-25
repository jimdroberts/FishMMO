using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterHotkeyService
	{
		/// <summary>
		/// Checks if the characters hotkey list is full.
		/// </summary>
		public static bool Full(NpgsqlDbContext dbContext, long characterID, int max)
		{
			if (characterID == 0)
			{
				return false;
			}
			var dbHotkeys = dbContext.CharacterHotkeys.Where(a => a.CharacterID == characterID);
			if (dbHotkeys != null && dbHotkeys.Count() <= max)
			{
				return true;
			}
			return false;
		}

		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				return;
			}
			if (playerCharacter.Hotkeys != null)
			{
				foreach (HotkeyData hotkeyData in playerCharacter.Hotkeys)
				{
					SaveOrUpdate(dbContext, playerCharacter.ID, hotkeyData.Type, hotkeyData.Slot, hotkeyData.ReferenceID);
				}
			}
		}

		/// <summary>
		/// Save a characters hotkeys to the database.
		/// </summary>
		public static void SaveOrUpdate(NpgsqlDbContext dbContext, long characterID, byte type, int slot, long referenceID)
		{
			if (characterID == 0)
			{
				return;
			}

			if (type == 0)
			{
				return;
			}

			var dbHotkey = dbContext.CharacterHotkeys.FirstOrDefault(c => c.CharacterID == characterID && c.Slot == slot);
			// Update or add to hotkeys
			if (dbHotkey != null)
			{
				dbHotkey.Type = type;
				dbHotkey.Slot = slot;
				dbHotkey.ReferenceID = referenceID;
				dbContext.SaveChanges();
			}
			else
			{
				dbHotkey = new CharacterHotkeyEntity()
				{
					CharacterID = characterID,
					Type = type,
					Slot = slot,
					ReferenceID = referenceID,
				};
				dbContext.CharacterHotkeys.Add(dbHotkey);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Removes all entries from a hotkey list.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var dbHotkeys = dbContext.CharacterHotkeys.Where(a => a.CharacterID == characterID);
				if (dbHotkeys != null)
				{
					dbContext.CharacterHotkeys.RemoveRange(dbHotkeys);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Load characters hotkeys from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			var dbHotkeys = dbContext.CharacterHotkeys.Where(c => c.CharacterID == character.ID);
			foreach (CharacterHotkeyEntity dbHotkey in dbHotkeys)
			{
				character.Hotkeys.Add(new HotkeyData()
				{
					Type = dbHotkey.Type,
					Slot = dbHotkey.Slot,
					ReferenceID = dbHotkey.ReferenceID,
				});
			};
		}
	}
}