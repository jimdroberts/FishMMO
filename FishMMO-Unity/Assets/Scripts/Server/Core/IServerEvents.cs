using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Interface for a service that raises server lifecycle events.
	/// Provides event delegates for subscription. The <c>Action</c> properties use
	/// <c>get; set;</c> rather than the <c>event</c> keyword to allow invocation
	/// from external types (e.g., <c>CoreServer</c>). Callers MUST use <c>+=</c>/<c>-=</c>
	/// for subscription management — direct assignment (<c>=</c>) will replace all
	/// existing subscribers and should never be used outside of the implementing
	/// <c>ServerEvents</c> class initialization.
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
