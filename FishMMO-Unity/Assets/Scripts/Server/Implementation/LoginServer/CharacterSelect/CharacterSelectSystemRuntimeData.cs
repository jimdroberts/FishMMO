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
		/// Per-connection cooldown tracker: maps clientId to the earliest time the next request is
		/// allowed, as <see cref="MonotonicClock"/> seconds. A cooldown is a local duration, which a
		/// wall-clock step back would have stretched to the length of the step.
		/// </summary>
		public ConcurrentDictionary<int, double> NextAllowedRequestSeconds { get; private set; }

		/// <summary>
		/// Per-connection in-flight gate for character LIST requests, tracked separately from
		/// select/delete.
		/// </summary>
		/// <remarks>
		/// These were one shared pair, so the list request's cooldown also throttled selection.
		/// The client requests the list on arrival and the player then clicks Play — well inside
		/// the two-second window the list had just armed — and the selection was refused as
		/// "cooldown or request already in flight", surfacing as "Character selection failed.
		/// Please try again." Waiting a moment and clicking again worked, which is what made it
		/// look intermittent rather than systematic.
		/// <para>
		/// They are separate operations: the list is worth rate-limiting because a player can
		/// hold down Refresh, whereas a selection is a single deliberate act that follows the
		/// list immediately by design. Sharing one gate made the second impossible to do
		/// promptly after the first.
		/// </para>
		/// </remarks>
		public ConcurrentDictionary<int, byte> InFlightListRequests { get; private set; }

		/// <summary>
		/// Per-connection cooldown tracker for character LIST requests, as <see cref="MonotonicClock"/>
		/// seconds. See <see cref="InFlightListRequests"/>.
		/// </summary>
		public ConcurrentDictionary<int, double> NextAllowedListRequestSeconds { get; private set; }

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InFlightRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedRequestSeconds = new ConcurrentDictionary<int, double>();
			InFlightListRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedListRequestSeconds = new ConcurrentDictionary<int, double>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			InFlightRequests?.Clear();
			NextAllowedRequestSeconds?.Clear();
			InFlightListRequests?.Clear();
			NextAllowedListRequestSeconds?.Clear();
		}

		/// <inheritdoc/>
		protected override void OnDeinitialize()
		{
			Clear();
			InFlightRequests = null;
			NextAllowedRequestSeconds = null;
			InFlightListRequests = null;
			NextAllowedListRequestSeconds = null;
		}
	}
}