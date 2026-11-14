using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The scene server's network send schedule (<see cref="NetworkSendSchedule"/>): how often it flushes, how often its
	/// clients must, and the replicate queue depth that keeps two-tick input delivery from ever leaving the queue empty.
	/// </summary>
	/// <remarks>
	/// Configuration: <c>NetworkSendInterval</c> (this server's flush interval) and <c>ClientNetworkSendInterval</c> (its
	/// clients'), each 1 or 2, default 1 (FishNet's own every-tick behaviour). The queue depth follows the CLIENT interval and
	/// is applied before any client connects, so a client that has not yet been told (it is told on arrival, before anything
	/// else) only ever sends more often than the depth assumes, never less.
	/// <para>
	/// <c>IdleNetworkSendInterval</c> (0 = off, up to <see cref="NetworkSendSchedule.MaximumIdleInterval"/>): a player who is
	/// nowhere near a fight has its unreliable data flushed this often instead (fewer packets, one tick later), while
	/// reliable data still leaves on the normal interval so replies are never slower. See <see cref="NetworkSendSchedule.IsEngaged"/>.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		private byte networkSendInterval = 1;
		private byte clientNetworkSendInterval = 1;
		private byte idleNetworkSendInterval;
		private ICharacterSystem<NetworkConnection, Scene> scheduleCharacterSystem;
		private TimeManager scheduleTimeManager;
		/// <summary>Players on this server and the last tick each was engaged, for the idle send interval.</summary>
		private readonly Dictionary<NetworkConnection, (IPlayerCharacter Character, uint EngagedTick)> scheduledPlayers =
			new Dictionary<NetworkConnection, (IPlayerCharacter, uint)>();
		private readonly List<NetworkConnection> scheduledPlayerScratch = new List<NetworkConnection>();
		/// <summary>Player-ticks scheduled, and those spent on the idle interval (for the simulate report).</summary>
		private long scheduledPlayerTicks, idleIntervalPlayerTicks;

		private void ApplyNetworkScheduleConfiguration()
		{
			if (Server?.Configuration != null)
			{
				if (Server.Configuration.TryGetInt("NetworkSendInterval", out int serverInterval))
				{
					networkSendInterval = NetworkSendSchedule.Clamp(serverInterval);
				}
				if (Server.Configuration.TryGetInt("ClientNetworkSendInterval", out int clientInterval))
				{
					clientNetworkSendInterval = NetworkSendSchedule.Clamp(clientInterval);
				}
				if (Server.Configuration.TryGetInt("IdleNetworkSendInterval", out int idleInterval))
				{
					idleNetworkSendInterval = NetworkSendSchedule.ClampIdle(idleInterval, networkSendInterval);
				}
			}
			NetworkSendSchedule.Apply(Server?.NetworkWrapper?.NetworkManager, networkSendInterval, clientNetworkSendInterval, asServer: true);
			_ = Log.Info("SceneServerSystem",
				$"Network schedule: this server flushes every {networkSendInterval} tick(s), clients every {clientNetworkSendInterval}; " +
				$"replicate queue depth {NetworkSendSchedule.CurrentStateInterpolation(Server?.NetworkWrapper?.NetworkManager)}; " +
				$"players away from any fight {(idleNetworkSendInterval > 0 ? $"every {idleNetworkSendInterval}" : "off")}.");
		}

		private void InitializeNetworkSchedule(ICharacterSystem<NetworkConnection, Scene> characterSystem)
		{
			scheduleCharacterSystem = characterSystem;
			if (scheduleCharacterSystem != null)
			{
				scheduleCharacterSystem.OnSpawnCharacter += CharacterSystem_OnScheduleCharacterSpawned;
				scheduleCharacterSystem.OnDespawnCharacter += CharacterSystem_OnScheduleCharacterDespawned;
			}
			scheduleTimeManager = Server?.NetworkWrapper?.NetworkManager != null ? Server.NetworkWrapper.NetworkManager.TimeManager : null;
			if (scheduleTimeManager != null && idleNetworkSendInterval > 0)
			{
				scheduleTimeManager.OnPreTick += TimeManager_OnSchedulePreTick;
			}
		}

		private void DeinitializeNetworkSchedule()
		{
			if (scheduleCharacterSystem != null)
			{
				scheduleCharacterSystem.OnSpawnCharacter -= CharacterSystem_OnScheduleCharacterSpawned;
				scheduleCharacterSystem.OnDespawnCharacter -= CharacterSystem_OnScheduleCharacterDespawned;
				scheduleCharacterSystem = null;
			}
			if (scheduleTimeManager != null)
			{
				scheduleTimeManager.OnPreTick -= TimeManager_OnSchedulePreTick;
				scheduleTimeManager = null;
			}
			scheduledPlayers.Clear();
		}

		/// <summary>A leaving player's connection goes back to the server's interval (the connection may stay, e.g. a hop).</summary>
		private void CharacterSystem_OnScheduleCharacterDespawned(NetworkConnection connection, IPlayerCharacter character)
		{
			if (connection != null && scheduledPlayers.Remove(connection))
			{
				connection.SetSendInterval(0);
			}
		}

		/// <summary>
		/// Before each tick's data is written: each player near a fight (or within the hold after one) flushes on the
		/// server's interval, every other player on the idle interval.
		/// </summary>
		private void TimeManager_OnSchedulePreTick()
		{
			if (scheduledPlayers.Count == 0 || scheduleTimeManager == null)
			{
				return;
			}
			uint tick = scheduleTimeManager.LocalTick;
			scheduledPlayerScratch.Clear();
			scheduledPlayerScratch.AddRange(scheduledPlayers.Keys);
			for (int i = 0; i < scheduledPlayerScratch.Count; ++i)
			{
				NetworkConnection connection = scheduledPlayerScratch[i];
				(IPlayerCharacter character, uint engagedTick) = scheduledPlayers[connection];
				if (connection == null || !connection.IsActive || character == null)
				{
					scheduledPlayers.Remove(connection);
					continue;
				}
				if (NetworkSendSchedule.IsEngaged(character.IsFlagged(CharacterFlags.IsInCombat),
						ObserverStreamingRegistry.IsViewerNearCombat(connection.ClientId)))
				{
					engagedTick = tick;
					scheduledPlayers[connection] = (character, engagedTick);
				}
				byte interval = NetworkSendSchedule.ResolveConnectionInterval(idleNetworkSendInterval, tick, engagedTick);
				if (connection.SendInterval != interval)
				{
					connection.SetSendInterval(interval);
				}
				scheduledPlayerTicks++;
				idleIntervalPlayerTicks += interval != 0 ? 1 : 0;
			}
		}

		/// <summary>An arriving character's client learns the schedule first, so it flushes at the interval this server buffers for.</summary>
		private void CharacterSystem_OnScheduleCharacterSpawned(NetworkConnection connection, IPlayerCharacter character, Scene scene)
		{
			if (connection == null || !connection.IsActive)
			{
				return;
			}
			Server.NetworkWrapper.Broadcast(connection, new NetworkScheduleBroadcast
			{
				ServerSendInterval = networkSendInterval,
				ClientSendInterval = clientNetworkSendInterval,
			}, true, Channel.Reliable);
			if (idleNetworkSendInterval > 0 && character != null && scheduleTimeManager != null)
			{
				// Starts engaged: the arrival burst (spawns, sheets) goes out at the full rate.
				scheduledPlayers[connection] = (character, scheduleTimeManager.LocalTick);
			}
		}
	}
}
