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

		public IngressGuard IngressGuard { get; private set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InteractableHandlers = new Dictionary<Type, IInteractableHandler>();
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			InteractableHandlers?.Clear();
			IngressGuard?.Clear();
		}

		public override void Deinitialize()
		{
			Clear();
			InteractableHandlers = null;
			IngressGuard = null;
		}
	}
}