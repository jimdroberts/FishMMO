using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class LoadedSceneService
	{
		/// <summary>
		/// Adds a new server to the server list. The Login server will fetch this list for new clients.
		/// </summary>
		public static LoadedSceneEntity Add(
			NpgsqlDbContext dbContext,
			long sceneServerID,
			long worldServerID,
			string sceneName,
			int sceneHandle)
		{
			var server = new LoadedSceneEntity()
			{
				SceneServerID = sceneServerID,
				WorldServerID = worldServerID,
				SceneName = sceneName,
				SceneHandle = sceneHandle,
				CharacterCount = 0,
			};
			dbContext.LoadedScenes.Add(server);
			return server;
		}

		public static void Pulse(NpgsqlDbContext dbContext, int handle, int characterCount)
		{
			var loadedScenes = dbContext.LoadedScenes.FirstOrDefault(c => c.SceneHandle == handle);
			if (loadedScenes == null) throw new Exception($"Couldn't find Scene Server with Scene Handle: {handle}");

			loadedScenes.CharacterCount = characterCount;
		}

		public static void Delete(NpgsqlDbContext dbContext, long sceneServerID)
		{
			var loadedScenes = dbContext.LoadedScenes.Where(c => c.SceneServerID == sceneServerID);
			if (loadedScenes != null)
			{
				dbContext.LoadedScenes.RemoveRange(loadedScenes);
			}
		}

		public static void Delete(NpgsqlDbContext dbContext, long sceneServerID, int sceneHandle)
		{
			var loadedScenes = dbContext.LoadedScenes.Where(c => c.SceneServerID == sceneServerID &&
																 c.SceneHandle == sceneHandle);
			if (loadedScenes != null)
			{
				dbContext.LoadedScenes.RemoveRange(loadedScenes);
			}
		}

		public static List<LoadedSceneEntity> GetServerList(NpgsqlDbContext dbContext, long worldServerID, string sceneName, int maxClients)
		{
			return dbContext.LoadedScenes.Where(c => c.WorldServerID == worldServerID &&
													 c.SceneName == sceneName &&
													 c.CharacterCount < maxClients)
										 .ToList();
		}

		public static List<LoadedSceneEntity> GetServerList(NpgsqlDbContext dbContext, long worldServerID)
		{
			return dbContext.LoadedScenes.Where(c => c.WorldServerID == worldServerID)
										 .ToList();
		}
	}
}