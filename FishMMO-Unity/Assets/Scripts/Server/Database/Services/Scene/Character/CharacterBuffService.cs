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
		public static void Save(NpgsqlDbContext dbContext, Character character)
		{
			if (character == null)
			{
				return;
			}

			var buffs = dbContext.CharacterBuffs.Where(c => c.CharacterID == character.ID)
												.ToDictionary(k => k.TemplateID);

			// remove dead buffs
			foreach (CharacterBuffEntity dbBuff in new List<CharacterBuffEntity>(buffs.Values))
			{
				if (!character.BuffController.Buffs.ContainsKey(dbBuff.TemplateID))
				{
					buffs.Remove(dbBuff.TemplateID);
					dbContext.CharacterBuffs.Remove(dbBuff);
				}
			}

			foreach (Buff buff in character.BuffController.Buffs.Values)
			{
				if (buffs.TryGetValue(buff.Template.ID, out CharacterBuffEntity dbBuff))
				{
					dbBuff.CharacterID = character.ID;
					dbBuff.TemplateID = buff.Template.ID;
					dbBuff.RemainingTime = buff.RemainingTime;
					dbBuff.Stacks.Clear();
					foreach (Buff stack in buff.Stacks)
					{
						CharacterBuffEntity dbStack = new CharacterBuffEntity();
						dbStack.CharacterID = character.ID;
						dbStack.TemplateID = stack.Template.ID;
						dbStack.RemainingTime = stack.RemainingTime;
						dbBuff.Stacks.Add(dbStack);
					}
				}
				else
				{
					CharacterBuffEntity newBuff = new CharacterBuffEntity()
					{
						CharacterID = character.ID,
						TemplateID = buff.Template.ID,
						RemainingTime = buff.RemainingTime,
					};
					foreach (Buff stack in buff.Stacks)
					{
						CharacterBuffEntity dbStack = new CharacterBuffEntity();
						dbStack.CharacterID = character.ID;
						dbStack.TemplateID = stack.Template.ID;
						dbStack.RemainingTime = stack.RemainingTime;
						newBuff.Stacks.Add(dbStack);
					}

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
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			var buffs = dbContext.CharacterBuffs.Where(c => c.CharacterID == character.ID);
			foreach (CharacterBuffEntity buff in buffs)
			{
				List<Buff> stacks = new List<Buff>();
				if (buff.Stacks == null || buff.Stacks.Count > 0)
				{
					foreach (CharacterBuffEntity stack in buff.Stacks)
					{
						Buff newStack = new Buff(stack.TemplateID, stack.RemainingTime);
						stacks.Add(newStack);
					}
				}
				Buff newBuff = new Buff(buff.TemplateID, buff.RemainingTime, stacks);
				character.BuffController.Apply(newBuff);
			};
		}
	}
}