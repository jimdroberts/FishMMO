using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Interface for a service that raises server lifecycle events.
	/// Provides event delegates for subscription.
	/// </summary>
	public interface IServerEvents
	{
		/// <summary>
		/// Invoked when the login server has been initialized.
		/// </summary>
		Action OnLoginServerInitialized { get; set; }

		/// <summary>
		/// Invoked when the world server has been initialized.
		/// </summary>
		Action OnWorldServerInitialized { get; set; }

		/// <summary>
		/// Invoked when the scene server has been initialized.
		/// </summary>
		Action OnSceneServerInitialized { get; set; }
	}
}