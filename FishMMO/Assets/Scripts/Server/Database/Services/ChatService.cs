using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class ChatService
	{
		/// <summary>
		/// Save a character Achievements to the database.
		/// </summary>
		public static void Save(ServerDbContext dbContext, long characterID, int worldServerID, int sceneServerID, byte channel, string message)
		{
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(ServerDbContext dbContext, long characterID, bool keepData = true)
		{
		}

		/// <summary>
		/// Load character Achievements from the database.
		/// </summary>
		public static void Load(ServerDbContext dbContext, Character character)
		{
		}
	}
}