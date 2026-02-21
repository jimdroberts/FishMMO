using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Runtime data container for interactable ingress protection and debounce tracking.
	/// </summary>
	public class InteractableSystemRuntimeData : RuntimeDataContainer, IInteractableSystemRuntimeData
	{
		public Dictionary<Type, IInteractableHandler> InteractableHandlers { get; private set; }

		public ConcurrentDictionary<long, DateTime> InteractableNextAllowedUtcByCharacter { get; private set; }
		public ConcurrentDictionary<long, byte> InteractableInFlightByCharacter { get; private set; }
		public DateTime NextDebounceSweepUtc { get; set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InteractableHandlers = new Dictionary<Type, IInteractableHandler>();
			InteractableNextAllowedUtcByCharacter = new ConcurrentDictionary<long, DateTime>();
			InteractableInFlightByCharacter = new ConcurrentDictionary<long, byte>();
			NextDebounceSweepUtc = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			InteractableHandlers?.Clear();
			InteractableNextAllowedUtcByCharacter?.Clear();
			InteractableInFlightByCharacter?.Clear();
			NextDebounceSweepUtc = DateTime.UtcNow;
		}

		public override void Deinitialize()
		{
			Clear();
			InteractableHandlers = null;
			InteractableNextAllowedUtcByCharacter = null;
			InteractableInFlightByCharacter = null;
		}
	}
}