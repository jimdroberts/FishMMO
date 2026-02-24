using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime data container for CharacterCreateSystem mutable state.
	/// </summary>
	public class CharacterCreateSystemRuntimeData : RuntimeDataContainer
	{
		/// <summary>
		/// Per-connection in-flight gate for character create requests.
		/// </summary>
		public ConcurrentDictionary<int, byte> InFlightRequests { get; private set; }

		/// <summary>
		/// Per-connection time-based cooldown to prevent sequential create spam after in-flight release.
		/// </summary>
		public ConcurrentDictionary<int, DateTime> NextAllowedCreateUtcByClientId { get; private set; }

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InFlightRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedCreateUtcByClientId = new ConcurrentDictionary<int, DateTime>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			InFlightRequests?.Clear();
			NextAllowedCreateUtcByClientId?.Clear();
		}

		/// <inheritdoc/>
		protected override void OnDeinitialize()
		{
			Clear();
			InFlightRequests = null;
			NextAllowedCreateUtcByClientId = null;
		}
	}
}