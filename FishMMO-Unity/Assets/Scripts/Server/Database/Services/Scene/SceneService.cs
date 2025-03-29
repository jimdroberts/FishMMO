using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class SceneService
	{
		/// <summary>
		/// Enqueues a new scene load request to the database.
		/// </summary>
		public static bool Enqueue(NpgsqlDbContext dbContext, long worldServerID, string sceneName, SceneType sceneType, long characterID = 0)
		{
			if (worldServerID == 0)
			{
				return false;
			}
			int type = (int)sceneType;

			var entity = dbContext.Scenes.FirstOrDefault(c => c.WorldServerID == worldServerID &&
															  c.SceneName == sceneName &&
															  c.CharacterID == characterID &&
															  c.SceneType == type &&
															  (c.SceneStatus == (int)SceneStatus.Pending || c.SceneStatus == (int)SceneStatus.Loading));

			// If there is no pending scene load add a new one to the database.
			if (entity == null)
			{
				entity = new SceneEntity()
				{
					WorldServerID = worldServerID,
					SceneName = sceneName,
					SceneType = type,
					SceneStatus = (int)SceneStatus.Pending,
					CharacterID = characterID,
					TimeCreated = DateTime.UtcNow,
				};
				dbContext.Scenes.Add(entity);
				dbContext.SaveChanges();

				return true;
			}
			return false;
		}

		/// <summary>
		/// Dequeues a pending scene load request from the database by setting the scene status to Loading.
		/// </summary>
		public static SceneEntity Dequeue(NpgsqlDbContext dbContext)
		{
			SceneEntity entity = dbContext.Scenes.FirstOrDefault(c => c.SceneStatus == (int)SceneStatus.Pending);
			if (entity != null)
			{
				entity.SceneStatus = (int)SceneStatus.Loading;
				dbContext.SaveChanges();
			}
			return entity;
		}

		/// <summary>
		/// Updates the scene server id, scene handle, and the scene status for a loading scene to Ready.
		/// </summary>
		public static void Update(NpgsqlDbContext dbContext, long sceneServerID, long worldServerID, string sceneName, int sceneHandle)
		{
			if (sceneServerID == 0 || worldServerID == 0)
			{
				return;
			}

			SceneEntity entity = dbContext.Scenes.FirstOrDefault(c => c.WorldServerID == worldServerID &&
																			 c.SceneName == sceneName &&
																			 c.SceneStatus == (int)SceneStatus.Loading);
			if (entity != null)
			{
				entity.SceneServerID = sceneServerID;
				entity.SceneHandle = sceneHandle;
				entity.CharacterCount = 0;
				entity.SceneStatus = (int)SceneStatus.Ready;

				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Updates the character count for the scene.
		/// </summary>
		public static void Pulse(NpgsqlDbContext dbContext, int handle, int characterCount)
		{
			var loadedScenes = dbContext.Scenes.FirstOrDefault(c => c.SceneHandle == handle);
			if (loadedScenes == null) throw new Exception($"Couldn't find Scene Server with Scene Handle: {handle}");

			loadedScenes.CharacterCount = characterCount;
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all scene entities from the database using the scene server id.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long sceneServerID)
		{
			if (sceneServerID == 0)
			{
				return;
			}
			var sceneEntities = dbContext.Scenes.Where(c => c.SceneServerID == sceneServerID);
			if (sceneEntities != null)
			{
				dbContext.Scenes.RemoveRange(sceneEntities);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Deletes all scene entities from the database using the world server id.
		/// </summary>
		public static void WorldDelete(NpgsqlDbContext dbContext, long worldServerID)
		{
			if (worldServerID == 0)
			{
				return;
			}
			var sceneEntities = dbContext.Scenes.Where(c => c.WorldServerID == worldServerID);
			if (sceneEntities != null)
			{
				dbContext.Scenes.RemoveRange(sceneEntities);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Deletes a specific scene using the scene server id and scene handle.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long sceneServerID, int sceneHandle)
		{
			if (sceneServerID == 0)
			{
				return;
			}
			var loadedScenes = dbContext.Scenes.Where(c => c.SceneServerID == sceneServerID &&
																 c.SceneHandle == sceneHandle);
			if (loadedScenes != null)
			{
				dbContext.Scenes.RemoveRange(loadedScenes);
				dbContext.SaveChanges();
			}
		}

		public static SceneEntity GetCharacterInstance(NpgsqlDbContext dbContext, long characterID, long worldServerID)
		{
			if (worldServerID == 0)
			{
				return null;
			}
			return dbContext.Scenes.FirstOrDefault(c => c.CharacterID == characterID);
		}

		public static List<SceneEntity> GetServerList(NpgsqlDbContext dbContext, long worldServerID, string sceneName, int maxClients)
		{
			if (worldServerID == 0)
			{
				return null;
			}

			var result = dbContext.Scenes
				.Where(c => c.WorldServerID == worldServerID &&
							c.SceneName == sceneName &&
							c.CharacterCount < maxClients &&
							c.SceneStatus == (int)SceneStatus.Ready)
				.ToList();

			return result;
		}

		public static List<SceneEntity> GetServerList(NpgsqlDbContext dbContext, long worldServerID)
		{
			if (worldServerID == 0)
			{
				return null;
			}
			return dbContext.Scenes.Where(c => c.WorldServerID == worldServerID && c.SceneStatus == (int)SceneStatus.Ready).ToList();
		}
	}
}