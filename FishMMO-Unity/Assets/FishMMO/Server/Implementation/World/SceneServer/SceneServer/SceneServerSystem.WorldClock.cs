using FishNet.Connection;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.Clock;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The world clock, hosted by the scene server: core, so every world feature (the sky, the weather, the sea, a mill's
	/// sails) and every module reads one shared time whether or not any of them is installed.
	/// </summary>
	/// <remarks>
	/// Started before the weather and told to an arriving character first, so its scene's weather arrives on a clock the
	/// client already has.
	/// </remarks>
	public partial class SceneServerSystem
	{
		private WorldClockHost worldClockHost;
		private ICharacterSystem<NetworkConnection, Scene> worldClockCharacterSystem;

		/// <summary>The world clock, for the staff tools' clock report and controls. Null before start.</summary>
		public WorldClockHost WorldClock => worldClockHost;

		private void InitializeWorldClock(ICharacterSystem<NetworkConnection, Scene> characterSystem)
		{
			var networkManager = Server.NetworkWrapper.NetworkManager;
			long epoch = WorldEpoch.UnixSeconds;
			_ = Log.Info("SceneServerSystem", $"The world clock counts from Unix second {epoch}{(epoch == WorldEpoch.DefaultUnixSeconds ? " (the default epoch)" : string.Empty)}.");

			// The world clock control row (/admin time, the Control Panel): written by the first scene
			// server to run, at this calendar's epoch; every server then reads it.
			worldClockHost = new WorldClockHost(networkManager, Server.Database, epoch, async () =>
			{
				if (!TryGetDbService(out IWorldClockControlService clockControl))
				{
					return false;
				}
				DatabaseResult<WorldClockReading> seeded = await clockControl.EnsureSeededAsync(epoch);
				if (!seeded.IsSuccess || seeded.Data == null || !seeded.Data.Exists)
				{
					return false;
				}
				if (seeded.Data.EpochUnixSeconds != epoch)
				{
					_ = Log.Warning("SceneServerSystem", $"The world clock control row was written with epoch {seeded.Data.EpochUnixSeconds}, this server's calendar uses {epoch}: world times will read differently in the Control Panel.");
				}
				return true;
			});
			worldClockHost.Start();

			worldClockCharacterSystem = characterSystem;
			if (worldClockCharacterSystem != null)
			{
				worldClockCharacterSystem.OnSpawnCharacter += CharacterSystem_OnWorldClockCharacterSpawned;
			}
		}

		private void DeinitializeWorldClock()
		{
			if (worldClockCharacterSystem != null)
			{
				worldClockCharacterSystem.OnSpawnCharacter -= CharacterSystem_OnWorldClockCharacterSpawned;
				worldClockCharacterSystem = null;
			}
			worldClockHost?.Stop();
			worldClockHost = null;
		}

		private void UpdateWorldClock(float deltaTime)
		{
			worldClockHost?.Tick(deltaTime);
		}

		/// <summary>An arriving character learns the clock before anything that runs on it.</summary>
		private void CharacterSystem_OnWorldClockCharacterSpawned(NetworkConnection connection, IPlayerCharacter character, Scene scene)
		{
			worldClockHost?.SendTo(connection);
		}
	}
}
