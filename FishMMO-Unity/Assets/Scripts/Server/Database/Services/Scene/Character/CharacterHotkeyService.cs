using System.Linq;
using System.Collections.Generic;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's hotkeys, including saving, updating, deleting, and loading hotkey data from the database.
		/// </summary>
		public class CharacterHotkeyService
	{
		/// <summary>
		/// Checks if the character's hotkey list is full.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="max">The maximum allowed hotkeys.</param>
		/// <returns>True if the hotkey list is not full; otherwise, false.</returns>
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

		/// <summary>
		/// Saves all hotkeys for a player character to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="playerCharacter">The player character whose hotkeys will be saved.</param>
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
		/// Saves or updates a specific hotkey for a character in the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="type">The hotkey type.</param>
		/// <param name="slot">The hotkey slot.</param>
		/// <param name="referenceID">The reference ID for the hotkey.</param>
		public static void SaveOrUpdate(NpgsqlDbContext dbContext, long characterID, byte type, int slot, long referenceID)
		{
			if (characterID == 0)
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
		/// Removes all hotkey entries for a character from the database. If keepData is false, the entries are removed.
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
				var dbHotkeys = dbContext.CharacterHotkeys.Where(a => a.CharacterID == characterID);
				if (dbHotkeys != null)
				{
					dbContext.CharacterHotkeys.RemoveRange(dbHotkeys);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's hotkeys from the database and assigns them to the character's hotkey data.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load hotkeys for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			var dbHotkeys = dbContext.CharacterHotkeys.Where(c => c.CharacterID == character.ID);

			foreach (CharacterHotkeyEntity dbHotkey in dbHotkeys)
			{
				HotkeyData data = character.Hotkeys[dbHotkey.Slot];
				if (data != null)
				{
					data.Type = dbHotkey.Type;
					data.Slot = dbHotkey.Slot;
					data.ReferenceID = dbHotkey.ReferenceID;
				}
			};
		}
	}
}