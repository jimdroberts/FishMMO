using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The world clock and the weather, hosted by the scene server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A partial rather than its own behaviour so it needs no scene wiring, like the server control
	/// partial. Other systems reach the weather through <see cref="WeatherService"/>.
	/// </para>
	/// <para>
	/// The reports in this file are read-only and neutral: both <c>/gm</c> (which may only look) and
	/// <c>/admin</c> (which may change the weather) use them. The changing commands live in
	/// <c>SceneServerSystem.AdminCommands.Weather</c>.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		private WeatherHost weatherHost;
		private WorldClockHost worldClockHost;
		private ICharacterSystem<NetworkConnection, Scene> weatherCharacterSystem;

		/// <summary>The scene weather, for server systems (spawners, AI, abilities). Null before start.</summary>
		public IWeatherService WeatherService => weatherHost;

		/// <summary>The scene weather host, for the staff tools' weather report and controls. Null before start.</summary>
		public WeatherHost WeatherHost => weatherHost;

		/// <summary>The world clock, for the staff tools' clock report and controls. Null before start.</summary>
		public WorldClockHost WorldClock => worldClockHost;

		private void InitializeWeather(ICharacterSystem<NetworkConnection, Scene> characterSystem, ICharacterMappingData<NetworkConnection> characterMapping)
		{
			var networkManager = Server.NetworkWrapper.NetworkManager;
			SolarSystemProfile system = SolarSystemProfile.Active;
			long epoch = system != null ? system.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
			if (system == null)
			{
				_ = Log.Warning("SceneServerSystem", "No SolarSystemProfile is loaded; the world clock uses the default epoch and scenes use the home world's defaults.");
			}

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

			weatherHost = new WeatherHost(Server.NetworkWrapper, characterMapping);
			weatherHost.Start();
			// Shared content — ECA actions on triggers, which both peers load — reaches the
			// authoritative edits through here, because it cannot reference this assembly.
			WeatherQuery.Commands = weatherHost;

			weatherCharacterSystem = characterSystem;
			if (weatherCharacterSystem != null)
			{
				weatherCharacterSystem.OnSpawnCharacter += CharacterSystem_OnWeatherCharacterSpawned;
			}
		}

		private void DeinitializeWeather()
		{
			if (weatherCharacterSystem != null)
			{
				weatherCharacterSystem.OnSpawnCharacter -= CharacterSystem_OnWeatherCharacterSpawned;
				weatherCharacterSystem = null;
			}
			if (ReferenceEquals(WeatherQuery.Commands, weatherHost))
			{
				WeatherQuery.Commands = null;
			}
			weatherHost?.Stop();
			weatherHost = null;
			worldClockHost?.Stop();
			worldClockHost = null;
		}

		private void UpdateWeather(float deltaTime)
		{
			worldClockHost?.Tick(deltaTime);
			weatherHost?.Tick(deltaTime);
		}

		/// <summary>An arriving character learns the clock first, then its scene's weather.</summary>
		private void CharacterSystem_OnWeatherCharacterSpawned(NetworkConnection connection, IPlayerCharacter character, Scene scene)
		{
			worldClockHost?.SendTo(connection);
			weatherHost?.OnCharacterSpawned(connection, scene);
		}

	}
}
