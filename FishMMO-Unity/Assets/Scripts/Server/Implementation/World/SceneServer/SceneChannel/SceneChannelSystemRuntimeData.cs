using System;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for the SceneChannelSystem.
	/// Holds all mutable state: ingress guard, per-connection cooldowns, and cleanup timers.
	/// </summary>
	public class SceneChannelSystemRuntimeData : RuntimeDataContainer, ISceneChannelSystemRuntimeData
	{
		/// <inheritdoc/>
		public IngressGuard IngressGuard { get; private set; }

		/// <inheritdoc/>
		public Dictionary<int, DateTime> ChannelSwitchCooldownByClientId { get; private set; }

		/// <inheritdoc/>
		public float NextCooldownCleanup { get; set; }

		/// <summary>
		/// Cache of <c>FetchAvailableAsync</c> results, keyed by world server ID and scene name.
		/// Entries expire after a configurable TTL to reduce database polling.
		/// </summary>
		/// <remarks>
		/// The world server must be part of the key: a scene server hosts scenes for every world
		/// server, so a key of scene name alone collides between them. See
		/// <c>SceneChannelSystem.BuildAvailableSceneCacheKey</c>.
		/// </remarks>
		public TimedCache<string, IReadOnlyList<SceneData>> AvailableSceneCache { get; set; }

		/// <summary>
		/// Cache of scene server addresses keyed by scene server ID.
		/// Addresses change infrequently, so a longer TTL is appropriate.
		/// </summary>
		public TimedCache<long, ushort> SceneServerAddressCache { get; set; }

		/// <summary>
		/// Initializes the runtime data, creating the ingress guard and cooldown dictionary.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			ChannelSwitchCooldownByClientId = new Dictionary<int, DateTime>();
			NextCooldownCleanup = 0f;
			AvailableSceneCache = new TimedCache<string, IReadOnlyList<SceneData>>(StringComparer.OrdinalIgnoreCase);
			SceneServerAddressCache = new TimedCache<long, ushort>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all tracked state without releasing references.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
			ChannelSwitchCooldownByClientId?.Clear();
			NextCooldownCleanup = 0f;
			AvailableSceneCache?.Clear();
			SceneServerAddressCache?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data, releasing all references.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			IngressGuard = null;
			ChannelSwitchCooldownByClientId = null;
			AvailableSceneCache?.Clear();
			AvailableSceneCache = null;
			SceneServerAddressCache?.Clear();
			SceneServerAddressCache = null;
		}
	}
}