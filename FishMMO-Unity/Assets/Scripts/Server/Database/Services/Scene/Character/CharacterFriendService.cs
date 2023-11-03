using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterFriendService
	{
		/// <summary>
		/// Checks if the characters friends list is full.
		/// </summary>
		public static bool Full(NpgsqlDbContext dbContext, long characterID, int max)
		{
			var characterFriends = dbContext.CharacterFriends.Where(a => a.CharacterID == characterID);
			if (characterFriends != null && characterFriends.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Save a characters friends to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character)
		{
			if (character == null)
			{
				return;
			}

			var friends = dbContext.CharacterFriends.Where(c => c.CharacterID == character.ID)
													.ToDictionary(k => k.FriendCharacterID);

			foreach (long friendID in character.FriendController.Friends)
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
		}

		/// <summary>
		/// Saves a CharacterFriendEntity to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long friendID)
		{
			var characterFriendEntity = dbContext.CharacterFriends.FirstOrDefault(a => a.CharacterID == characterID && a.FriendCharacterID == friendID);
			if (characterFriendEntity == null)
			{
				characterFriendEntity = new CharacterFriendEntity()
				{
					CharacterID = characterID,
					FriendCharacterID = friendID,
				};
				dbContext.CharacterFriends.Add(characterFriendEntity);
			}
		}

		/// <summary>
		/// Removes a character from a friend list.
		/// </summary>
		public static bool Delete(NpgsqlDbContext dbContext, long characterID, long friendID)
		{
			var characterFriendEntity = dbContext.CharacterFriends.FirstOrDefault(a => a.CharacterID == characterID && a.FriendCharacterID == friendID);
			if (characterFriendEntity != null)
			{
				dbContext.CharacterFriends.Remove(characterFriendEntity);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes all characters from a friend list.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID)
		{
			var characterFriends = dbContext.CharacterFriends.Where(a => a.CharacterID == characterID);
			if (characterFriends != null)
			{
				dbContext.CharacterFriends.RemoveRange(characterFriends);
			}
		}

		/// <summary>
		/// Load characters friends from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			var friends = dbContext.CharacterFriends.Where(c => c.CharacterID == character.ID);
			foreach (CharacterFriendEntity friend in friends)
			{
				character.FriendController.AddFriend(friend.FriendCharacterID);
			};
		}

		/// <summary>
		/// Load all CharacterFriendEntity from the database for a specific character.
		/// </summary>
		public static List<CharacterFriendEntity> Friends(NpgsqlDbContext dbContext, long characterID)
		{
			return dbContext.CharacterFriends.Where(a => a.CharacterID == characterID).ToList();
		}
	}
}