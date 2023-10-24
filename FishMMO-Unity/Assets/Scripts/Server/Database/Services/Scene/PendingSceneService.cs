using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Entities;

namespace FishMMO.Server.Services
{
	public class PendingSceneService
	{
		public static bool Exists(ServerDbContext dbContext, long worldServerID, string sceneName)
		{
			return dbContext.PendingScenes.FirstOrDefault(c => c.WorldServerID == worldServerID && c.SceneName == sceneName) != null;
		}

		public static void Enqueue(ServerDbContext dbContext, long worldServerID, string sceneName)
		{
			var entity = dbContext.PendingScenes.FirstOrDefault(c => c.WorldServerID == worldServerID && c.SceneName == sceneName);
			if (entity == null)
			{
				entity = new PendingSceneEntity()
				{
					WorldServerID = worldServerID,
					SceneName = sceneName,
				};
				dbContext.PendingScenes.Add(entity);
			}
		}

		public static void Delete(ServerDbContext dbContext, long worldServerID)
		{
			var pending = dbContext.PendingScenes.Where(c => c.WorldServerID == worldServerID);
			dbContext.PendingScenes.RemoveRange(pending);
		}

		public static PendingSceneEntity Dequeue(ServerDbContext dbContext)
		{
			PendingSceneEntity entity = dbContext.PendingScenes.FirstOrDefault();
			if (entity != null)
			{
				dbContext.PendingScenes.Remove(entity);
			}
			return entity;
		}
	}
}