using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class LoadedSceneService
	{
		/// <summary>
		/// Adds a new server to the server list. The Login server will fetch this list for new clients.
		/// </summary>
		public static LoadedSceneEntity Add(
			ServerDbContext dbContext,
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

		public static void Pulse(ServerDbContext dbContext, int handle, int characterCount)
		{
			var loadedScenes = dbContext.LoadedScenes.FirstOrDefault(c => c.SceneHandle == handle);
			if (loadedScenes == null) throw new Exception($"Couldn't find Scene Server with Scene Handle: {handle}");

			loadedScenes.CharacterCount = characterCount;
		}

		public static void Delete(ServerDbContext dbContext, long sceneServerID)
		{
			var loadedScenes = dbContext.LoadedScenes.Where(c => c.ID == sceneServerID);
			if (loadedScenes != null)
			{
				dbContext.LoadedScenes.RemoveRange(loadedScenes);
			}
		}

		public static List<LoadedSceneEntity> GetServerList(ServerDbContext dbContext, long worldServerID, string sceneName, int maxClients)
		{
			return dbContext.LoadedScenes.Where(c => c.WorldServerID == worldServerID &&
													 c.SceneName == sceneName &&
													 c.CharacterCount < maxClients)
										 .ToList();
		}

		public static List<LoadedSceneEntity> GetServerList(ServerDbContext dbContext, long worldServerID)
		{
			return dbContext.LoadedScenes.Where(c => c.WorldServerID == worldServerID)
										 .ToList();
		}
	}
}