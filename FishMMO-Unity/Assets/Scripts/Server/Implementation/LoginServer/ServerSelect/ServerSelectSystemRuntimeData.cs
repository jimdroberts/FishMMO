using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime data container for ServerSelectSystem mutable state.
	/// </summary>
	public class ServerSelectSystemRuntimeData : RuntimeDataContainer
	{
		/// <summary>
		/// Per-connection in-flight gate for server-list requests.
		/// </summary>
		public ConcurrentDictionary<int, byte> InFlightRequests { get; private set; }

		/// <summary>
		/// Per-connection time-based cooldown to prevent sequential server-list spam after in-flight release.
		/// </summary>
		public ConcurrentDictionary<int, DateTime> NextAllowedRequestUtcByClientId { get; private set; }

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InFlightRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedRequestUtcByClientId = new ConcurrentDictionary<int, DateTime>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			InFlightRequests?.Clear();
			NextAllowedRequestUtcByClientId?.Clear();
		}

		/// <inheritdoc/>
		public override void Deinitialize()
		{
			Clear();
			InFlightRequests = null;
			NextAllowedRequestUtcByClientId = null;
		}
	}
}