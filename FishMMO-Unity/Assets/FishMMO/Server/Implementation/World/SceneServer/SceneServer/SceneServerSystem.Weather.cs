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
	/// The weather, hosted by the scene server. The world clock it runs on is core (<c>SceneServerSystem.WorldClock</c>).
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
		private ICharacterSystem<NetworkConnection, Scene> weatherCharacterSystem;

		/// <summary>The scene weather, for server systems (spawners, AI, abilities). Null before start.</summary>
		public IWeatherService WeatherService => weatherHost;

		/// <summary>The scene weather host, for the staff tools' weather report and controls. Null before start.</summary>
		public WeatherHost WeatherHost => weatherHost;

		private void InitializeWeather(ICharacterSystem<NetworkConnection, Scene> characterSystem, ICharacterMappingData<NetworkConnection> characterMapping)
		{
			weatherHost = new WeatherHost(Server.NetworkWrapper, characterMapping);
			weatherHost.Start();
			// Shared content — ECA actions on triggers, which both peers load — reaches the
			// authoritative edits through here, because it cannot reference this assembly.
			WeatherTimelines.Commands = weatherHost;

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
			if (ReferenceEquals(WeatherTimelines.Commands, weatherHost))
			{
				WeatherTimelines.Commands = null;
			}
			weatherHost?.Stop();
			weatherHost = null;
		}

		private void UpdateWeather(float deltaTime)
		{
			weatherHost?.Tick(deltaTime);
		}

		/// <summary>An arriving character learns its scene's weather (the clock went first: SceneServerSystem.WorldClock).</summary>
		private void CharacterSystem_OnWeatherCharacterSpawned(NetworkConnection connection, IPlayerCharacter character, Scene scene)
		{
			weatherHost?.OnCharacterSpawned(connection, scene);
		}

	}
}
