using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's friends list, including saving, updating, deleting, and loading friend data from the database.
		/// </summary>
		public class CharacterFriendService
	{
		/// <summary>
		/// Checks if the character's friends list is full.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="max">The maximum allowed friends.</param>
		/// <returns>True if the friends list is not full; otherwise, false.</returns>
		public static bool Full(NpgsqlDbContext dbContext, long characterID, int max)
		{
			if (characterID == 0)
			{
				return false;
			}
			var characterFriends = dbContext.CharacterFriends.Where(a => a.CharacterID == characterID);
			if (characterFriends != null && characterFriends.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a character's friends to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose friends will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IFriendController friendController))
			{
				return;
			}

			var friends = dbContext.CharacterFriends.Where(c => c.CharacterID == character.ID)
													.ToDictionary(k => k.FriendCharacterID);

			foreach (long friendID in friendController.Friends)
			{
				if (!friends.ContainsKey(friendID))
				{
					dbContext.CharacterFriends.Add(new CharacterFriendEntity()
					{
						CharacterID = character.ID,
						FriendCharacterID = friendID,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Saves a friend relationship between two characters to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="friendID">The friend character ID.</param>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long friendID)
		{
			if (friendID == 0)
			{
				return;
			}
			var characterFriendEntity = dbContext.CharacterFriends.FirstOrDefault(a => a.CharacterID == characterID && a.FriendCharacterID == friendID);
			if (characterFriendEntity == null)
			{
				characterFriendEntity = new CharacterFriendEntity()
				{
					CharacterID = characterID,
					FriendCharacterID = friendID,
				};
				dbContext.CharacterFriends.Add(characterFriendEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Removes a specific friend from a character's friends list.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="friendID">The friend character ID to remove.</param>
		/// <returns>True if the friend was removed; otherwise, false.</returns>
		public static bool Delete(NpgsqlDbContext dbContext, long characterID, long friendID)
		{
			if (friendID == 0)
			{
				return false;
			}
			var characterFriendEntity = dbContext.CharacterFriends.FirstOrDefault(a => a.CharacterID == characterID && a.FriendCharacterID == friendID);
			if (characterFriendEntity != null)
			{
				dbContext.CharacterFriends.Remove(characterFriendEntity);
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes all friends from a character's friends list. If keepData is false, the entries are removed.
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
				var characterFriends = dbContext.CharacterFriends.Where(a => a.CharacterID == characterID);
				if (characterFriends != null)
				{
					dbContext.CharacterFriends.RemoveRange(characterFriends);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's friends from the database and assigns them to the character's friend controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load friends for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IFriendController friendController))
			{
				return;
			}
			var friends = dbContext.CharacterFriends.Where(c => c.CharacterID == character.ID);
			foreach (CharacterFriendEntity friend in friends)
			{
				friendController.AddFriend(friend.FriendCharacterID);
			};
		}

		/// <summary>
		/// Loads all friend entities from the database for a specific character.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <returns>A list of friend entities, or null if the character ID is invalid.</returns>
		public static List<CharacterFriendEntity> Friends(NpgsqlDbContext dbContext, long characterID)
		{
			if (characterID == 0)
			{
				return null;
			}
			return dbContext.CharacterFriends.Where(a => a.CharacterID == characterID).ToList();
		}
	}
}