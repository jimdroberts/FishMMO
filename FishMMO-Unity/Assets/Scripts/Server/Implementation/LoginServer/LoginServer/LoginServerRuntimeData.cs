using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime data container for login server state, storing the unique server ID.
	/// </summary>
	public class LoginServerRuntimeData : RuntimeDataContainer, ILoginServerRuntimeData
	{
		/// <summary>
		/// Gets the unique ID of this login server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>1 while a heartbeat pulse is in flight; 0 when idle.</summary>
		private int pulseInFlight;

		/// <inheritdoc/>
		public bool TryBeginPulse()
		{
			return Interlocked.CompareExchange(ref pulseInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndPulse()
		{
			Interlocked.Exchange(ref pulseInFlight, 0);
		}

		/// <summary>
		/// Initializes the runtime data once. Called when the data container is first set up.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			ID = 0;
			Interlocked.Exchange(ref pulseInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data. Called when resetting state between sessions.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			Interlocked.Exchange(ref pulseInFlight, 0);
		}

		/// <summary>
		/// Deinitializes the runtime data. Called when shutting down the server.
		/// </summary>
		protected override void OnDeinitialize()
		{
			ID = 0;
			Interlocked.Exchange(ref pulseInFlight, 0);
		}
	}
}