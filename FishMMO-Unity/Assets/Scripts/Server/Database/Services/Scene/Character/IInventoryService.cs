using System;
using FishMMO.Database.Npgsql;

namespace FishMMO.Shared
{
		/// <summary>
		/// Defines methods for inventory services, including setting slots and saving inventory data.
		/// </summary>
		public interface IInventoryService
	{
		/// <summary>
		/// Sets an inventory slot for a character in the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The character ID.</param>
		/// <param name="item">The item to set in the slot.</param>
		static void SetSlot(NpgsqlDbContext dbContext, long id, Item item) => throw new NotImplementedException();
		/// <summary>
		/// Saves a character's inventory to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose inventory will be saved.</param>
		static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character) => throw new NotImplementedException();
	}
}