using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Shared;

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
		/// Per-connection time-based cooldown to prevent sequential server-list spam after in-flight
		/// release, as <see cref="MonotonicClock"/> seconds: a local duration, which a wall-clock
		/// step back would have stretched to the length of the step.
		/// </summary>
		public ConcurrentDictionary<int, double> NextAllowedRequestSecondsByClientId { get; private set; }

		/// <summary>
		/// The active world-server list, shared by every request inside its short lifetime and by
		/// every request that arrives while it is being read. Keyed by a single constant: the
		/// list is the same for every client.
		/// </summary>
		/// <remarks>
		/// Timed on the monotonic clock, handed to the cache through its clock seam as a
		/// <see cref="DateTime"/> it only ever subtracts from itself. On the default wall clock a
		/// step back would have served one read of the list for the length of the step.
		/// </remarks>
		public SingleFlightCache<byte, WorldServerDetails[]> ServerList { get; private set; }

		/// <summary>
		/// <see cref="MonotonicClock.NowSeconds"/> as a <see cref="DateTime"/>, for clock seams that
		/// only compare readings with each other. Never compare it with a wall-clock value.
		/// </summary>
		private static DateTime MonotonicInstant() =>
			new DateTime((long)(MonotonicClock.NowSeconds * TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			InFlightRequests = new ConcurrentDictionary<int, byte>();
			NextAllowedRequestSecondsByClientId = new ConcurrentDictionary<int, double>();
			ServerList = new SingleFlightCache<byte, WorldServerDetails[]>(MonotonicInstant);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			InFlightRequests?.Clear();
			NextAllowedRequestSecondsByClientId?.Clear();
			ServerList?.Clear();
		}

		/// <inheritdoc/>
		protected override void OnDeinitialize()
		{
			Clear();
			InFlightRequests = null;
			NextAllowedRequestSecondsByClientId = null;
			ServerList = null;
		}
	}
}