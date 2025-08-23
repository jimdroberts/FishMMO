using System;

namespace FishMMO.Server.Core
{
	// Interface for a service that raises server lifecycle events.
	// It provides event delegates for subscription.
	public interface IServerEvents
	{
		Action OnLoginServerInitialized { get; set; }
		Action OnWorldServerInitialized { get; set; }
		Action OnSceneServerInitialized { get; set; }
	}
}