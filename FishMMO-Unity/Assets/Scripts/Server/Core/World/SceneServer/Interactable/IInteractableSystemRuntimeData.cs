using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Interactable;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for interactable ingress guards and debounce tracker cleanup.
	/// </summary>
	public interface IInteractableSystemRuntimeData : IRuntimeDataContainer
	{
		Dictionary<Type, FishMMO.Server.Implementation.World.SceneServer.Interactable.IInteractableHandler> InteractableHandlers { get; }

		ConcurrentDictionary<long, DateTime> InteractableNextAllowedUtcByCharacter { get; }
		ConcurrentDictionary<long, byte> InteractableInFlightByCharacter { get; }
		DateTime NextDebounceSweepUtc { get; set; }
	}
}
