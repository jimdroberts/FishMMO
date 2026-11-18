using System;
using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world server instance state.
	/// Manages world server ID and lock status separately from WorldServerSystem logic.
	/// </summary>
	public class WorldServerSystemRuntimeData : RuntimeDataContainer, IWorldServerSystemRuntimeData
	{
		/// <summary>
		/// Database ID for this world server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Indicates whether the world server is locked (not accepting new connections).
		/// </summary>
		public bool IsLocked { get; set; }

		/// <inheritdoc />
		public DateTime? ShutdownAtUtc { get; set; }

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
		/// Initializes the world server runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			ID = 0;
			IsLocked = false;
			ShutdownAtUtc = null;
			Interlocked.Exchange(ref pulseInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the world server state.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			IsLocked = false;
			ShutdownAtUtc = null;
			Interlocked.Exchange(ref pulseInFlight, 0);
		}

		/// <summary>
		/// Deinitializes the world server runtime data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}