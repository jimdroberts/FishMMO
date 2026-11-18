using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// A concrete implementation of <see cref="IServerEvents"/>.
	/// Provides Action delegates for server lifecycle events. The <c>Action</c> properties
	/// use <c>get; set;</c> rather than the <c>event</c> keyword to allow invocation from
	/// external types. Callers MUST use <c>+=</c>/<c>-=</c> — direct assignment (<c>=</c>)
	/// replaces all existing subscribers and is reserved for initialization only.
	/// </summary>
	public class ServerEvents : IServerEvents
	{
		/// <summary>
		/// Triggered when the login server is initialized.
		/// </summary>
		public Action OnLoginServerInitialized { get; set; }

		/// <summary>
		/// Triggered when the world server is initialized.
		/// </summary>
		public Action OnWorldServerInitialized { get; set; }

		/// <summary>
		/// Triggered when the scene server is initialized.
		/// </summary>
		public Action OnSceneServerInitialized { get; set; }
	}
}
