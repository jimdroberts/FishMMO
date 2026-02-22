using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime data container for CharacterSelectSystem mutable state.
	/// </summary>
	public class CharacterSelectSystemRuntimeData : RuntimeDataContainer
	{
		/// <summary>
		/// Per-connection in-flight gate for character select/delete requests.
		/// </summary>
		public ConcurrentDictionary<int, byte> InFlightRequests { get; private set; }

		/// <summary>
		/// Per-connection cooldown tracker: maps clientId to the earliest UTC time the next request is allowed.
		/// </summary>
		public ConcurrentDictionary<int, DateTime> NextAllowedRequestUtc { get; private set; }

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InFlightRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedRequestUtc = new ConcurrentDictionary<int, DateTime>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			InFlightRequests?.Clear();
			NextAllowedRequestUtc?.Clear();
		}

		/// <inheritdoc/>
		public override void Deinitialize()
		{
			Clear();
			InFlightRequests = null;
			NextAllowedRequestUtc = null;
		}
	}
}