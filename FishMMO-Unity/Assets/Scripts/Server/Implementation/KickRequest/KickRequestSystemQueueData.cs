using System;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Runtime data container for kick request processing state.
	/// Manages kick request database polling state separately from KickRequestSystem logic.
	/// </summary>
	public class KickRequestSystemQueueData : RuntimeDataContainer, IKickRequestSystemQueueData
	{
		/// <summary>
		/// Indicates whether the system is currently processing kick requests to prevent overlapping fetches.
		/// </summary>
		public bool IsProcessing { get; set; } = false;

		/// <inheritdoc/>
		/// <remarks>
		/// It replaced a <c>(time, id)</c> cursor seeded from this host's <c>DateTime.UtcNow</c> and
		/// compared with stamps the database wrote. A host running ahead of the database skipped every
		/// kick stamped inside the lead; and a strict cursor skipped a kick whose transaction committed
		/// after a later-stamped one's. See <see cref="KickRequestReadWindow"/>.
		/// </remarks>
		public KickRequestReadWindow ReadWindow { get; } =
			new KickRequestReadWindow(TimeSpan.FromSeconds(KickRequestService.PollCommitWindowSeconds));

		/// <inheritdoc/>
		public double WatchStartedAt { get; private set; }

		/// <summary>
		/// Initializes the kick request queue data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Clear();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the kick request queue state: the next poll is a first read again, reaching back
		/// to now.
		/// </summary>
		public override void Clear()
		{
			IsProcessing = false;
			ReadWindow.Reset();
			WatchStartedAt = MonotonicClock.NowSeconds;
		}

		/// <summary>
		/// Deinitializes the kick request queue data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}
