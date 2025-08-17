using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's buffs, including saving, deleting, and loading buff data from the database.
		/// </summary>
		public class CharacterBuffService
	{
		/// <summary>
		/// Saves a character's buffs to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose buffs will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IBuffController buffController))
			{
				return;
			}

			var buffs = dbContext.CharacterBuffs.Where(c => c.CharacterID == character.ID)
												.ToDictionary(k => k.TemplateID);

			// remove dead buffs
			foreach (CharacterBuffEntity dbBuff in new List<CharacterBuffEntity>(buffs.Values))
			{
				if (!buffController.Buffs.ContainsKey(dbBuff.TemplateID))
				{
					buffs.Remove(dbBuff.TemplateID);
					dbContext.CharacterBuffs.Remove(dbBuff);
				}
			}

			foreach (Buff buff in buffController.Buffs.Values)
			{
				if (buffs.TryGetValue(buff.Template.ID, out CharacterBuffEntity dbBuff))
				{
					dbBuff.CharacterID = character.ID;
					dbBuff.TemplateID = buff.Template.ID;
					dbBuff.RemainingTime = buff.RemainingTime;
					dbBuff.Stacks = buff.Stacks;
				}
				else
				{
					CharacterBuffEntity newBuff = new CharacterBuffEntity()
					{
						CharacterID = character.ID,
						TemplateID = buff.Template.ID,
						RemainingTime = buff.RemainingTime,
						Stacks = buff.Stacks,
					};

					dbContext.CharacterBuffs.Add(newBuff);
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all buffs for a character from the database. If keepData is false, the entries are removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = true)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var buffs = dbContext.CharacterBuffs.Where(c => c.CharacterID == characterID);
				if (buffs != null)
				{
					dbContext.CharacterBuffs.RemoveRange(buffs);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's buffs from the database and assigns them to the character's buff controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load buffs for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IBuffController buffController))
			{
				return;
			}
			var buffs = dbContext.CharacterBuffs.Where(c => c.CharacterID == character.ID);
			foreach (CharacterBuffEntity buff in buffs)
			{
				Buff newBuff = new Buff(buff.TemplateID, buff.RemainingTime, buff.TickTime, buff.Stacks);
				buffController.Apply(newBuff);
			};
		}
	}
}