using FishNet.Connection;
using FishNet.Transporting;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Server.Entities;
using Server.Services;

namespace Server
{
	/// <summary>
	/// Server Character Creation system.
	/// </summary>
	public class CharacterCreateSystem : ServerBehaviour
	{
		public const int characterNameMinLength = 3;
		public const int characterNameMaxLength = 32;

		public virtual bool IsAllowedCharacterName(string characterName)
		{
			return !string.IsNullOrWhiteSpace(characterName) &&
				   characterName.Length >= characterNameMinLength &&
				   characterName.Length <= characterNameMaxLength &&
				   Regex.IsMatch(characterName, @"^[a-zA-Z_]+$");
		}

		public WorldSceneDetailsCache worldSceneDetailsCache;

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived, true);
				
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived);
			}
		}

		private void OnServerCharacterCreateBroadcastReceived(NetworkConnection conn, CharacterCreateBroadcast msg)
		{
			if (conn.IsActive)
			{
				// validate character creation data
				if (!IsAllowedCharacterName(msg.characterName))
				{
					// invalid character name
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.InvalidCharacterName,
					});
					return;
				}

				using var dbContext = Server.DbContextFactory.CreateDbContext();
				var character = CharacterService.GetCharacterByName(dbContext, msg.characterName);
				if (character == null)
				{
					// character name already taken
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.CharacterNameTaken,
					});
					return;
				}

				if (worldSceneDetailsCache == null ||
					worldSceneDetailsCache.scenes == null ||
					worldSceneDetailsCache.scenes.Count < 1)
				{
					// failed to find spawn positions to validate with
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.InvalidSpawn,
					});
					return;
				}
				if (worldSceneDetailsCache.scenes.TryGetValue(msg.initialSpawnPosition.sceneName, out WorldSceneDetails details))
				{
					if (details.initialSpawnPositions.ContainsKey(msg.initialSpawnPosition.spawnerName))
					{
						if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
						{
							// add the new character to the database
							var newCharacter = new CharacterEntity()
							{
								Account = accountName,
								Name = msg.characterName,
								NameLowercase = msg.characterName?.ToLower(),
								RaceName = msg.raceName,
								SceneName = msg.initialSpawnPosition.sceneName,
								X = msg.initialSpawnPosition.position.x,
								Y = msg.initialSpawnPosition.position.y,
								Z = msg.initialSpawnPosition.position.z,
								RotX = msg.initialSpawnPosition.rotation.x,
								RotY = msg.initialSpawnPosition.rotation.y,
								RotZ = msg.initialSpawnPosition.rotation.z,
								RotW = msg.initialSpawnPosition.rotation.w,
							};
							dbContext.Characters.Add(newCharacter);
							dbContext.SaveChanges();
							
							// send success to the client
							conn.Broadcast(new CharacterCreateResultBroadcast()
							{
								result = CharacterCreateResult.Success,
							});

							// send the create broadcast back to the client
							conn.Broadcast(msg);
						}
					}
				}
			}
		}
	}
}