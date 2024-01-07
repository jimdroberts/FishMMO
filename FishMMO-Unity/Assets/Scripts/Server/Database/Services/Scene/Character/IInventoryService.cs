using System;
using FishMMO.Database.Npgsql;

namespace FishMMO.Shared
{
	public interface IInventoryService
	{
		static void SetSlot(NpgsqlDbContext dbContext, long id, Item item) => throw new NotImplementedException();
		static void Save(NpgsqlDbContext dbContext, Character character) => throw new NotImplementedException();
	}
}