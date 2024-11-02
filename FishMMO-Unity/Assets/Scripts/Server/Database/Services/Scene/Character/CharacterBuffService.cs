using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterBuffService
	{
		/// <summary>
		/// Save a characters buffs to the database.
		/// </summary>
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
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
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
		/// Load characters buffs from the database.
		/// </summary>
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