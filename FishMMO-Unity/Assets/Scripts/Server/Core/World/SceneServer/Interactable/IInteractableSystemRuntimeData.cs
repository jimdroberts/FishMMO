using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for interactable ingress guards and debounce tracker cleanup.
	/// </summary>
	public interface IInteractableSystemRuntimeData : IRuntimeDataContainer
	{
		Dictionary<Type, FishMMO.Server.Implementation.World.SceneServer.Interactable.IInteractableHandler> InteractableHandlers { get; }

		FishMMO.Server.Core.IngressGuard IngressGuard { get; }
	}
}