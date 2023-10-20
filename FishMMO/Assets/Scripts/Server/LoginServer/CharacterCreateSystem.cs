using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Text.RegularExpressions;
using FishMMO_DB.Entities;
using FishMMO.Server.Services;
using FishNet.Object;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Character Creation system.
	/// </summary>
	public class CharacterCreateSystem : ServerBehaviour
	{
		public const int CharacterNameMinLength = 3;
		public const int CharacterNameMaxLength = 32;
		public int MaxCharacters = 8;

		public virtual bool IsAllowedCharacterName(string characterName)
		{
			return !string.IsNullOrWhiteSpace(characterName) &&
				   characterName.Length >= CharacterNameMinLength &&
				   characterName.Length <= CharacterNameMaxLength &&
				   Regex.IsMatch(characterName, @"^[A-Za-z]+(?: [A-Za-z]+){0,2}$");
		}

		public WorldSceneDetailsCache WorldSceneDetailsCache;

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
				if (!AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					// account not found??
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}
				int characterCount = CharacterService.GetCount(dbContext, accountName);
				if (characterCount >= MaxCharacters)
				{
					// character name already taken
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.TooMany,
					});
					return;
				}
				var character = CharacterService.GetByName(dbContext, msg.characterName);
				if (character != null)
				{
					// character name already taken
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.CharacterNameTaken,
					});
					return;
				}

				if (WorldSceneDetailsCache == null ||
					WorldSceneDetailsCache.Scenes == null ||
					WorldSceneDetailsCache.Scenes.Count < 1)
				{
					// failed to find spawn positions to validate with
					conn.Broadcast(new CharacterCreateResultBroadcast()
					{
						result = CharacterCreateResult.InvalidSpawn,
					});
					return;
				}
				// validate spawn details
				if (WorldSceneDetailsCache.Scenes.TryGetValue(msg.initialSpawnPosition.SceneName, out WorldSceneDetails details))
				{
					// validate spawner
					if (details.InitialSpawnPositions.TryGetValue(msg.initialSpawnPosition.SpawnerName, out CharacterInitialSpawnPosition initialSpawnPosition))
					{
						// invalid race name! default to first race for now....
						// FIXME add race selection to UICharacterCreate.cs
						int raceID = 0;
						for (int i = 0; i < Server.NetworkManager.SpawnablePrefabs.GetObjectCount(); ++i)
						{
							NetworkObject prefab = Server.NetworkManager.SpawnablePrefabs.GetObject(true, i);
							if (prefab != null &&
								(string.IsNullOrWhiteSpace(msg.raceName) || prefab.name == msg.raceName))
							{
								raceID = i;
								break;
							}
						}
						var newCharacter = new CharacterEntity()
						{
							Account = accountName,
							Name = msg.characterName,
							NameLowercase = msg.characterName?.ToLower(),
							RaceID = raceID,
							SceneName = initialSpawnPosition.SceneName,
							X = initialSpawnPosition.Position.x,
							Y = initialSpawnPosition.Position.y,
							Z = initialSpawnPosition.Position.z,
							RotX = initialSpawnPosition.Rotation.x,
							RotY = initialSpawnPosition.Rotation.y,
							RotZ = initialSpawnPosition.Rotation.z,
							RotW = initialSpawnPosition.Rotation.w,
							TimeCreated = DateTime.UtcNow,
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