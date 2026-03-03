using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for interactable ingress guards and debounce tracker cleanup.
	/// </summary>
	public interface IInteractableSystemRuntimeData : IRuntimeDataContainer
	{
		Dictionary<Type, IInteractableHandler> InteractableHandlers { get; }

		IngressGuard IngressGuard { get; }
	}
}