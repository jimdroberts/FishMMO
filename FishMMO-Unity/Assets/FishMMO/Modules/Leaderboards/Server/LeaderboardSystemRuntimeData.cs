using FishMMO.Database.Data;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for the leaderboard system's request guard and read caches.
	/// </summary>
	public class LeaderboardSystemRuntimeData : RuntimeDataContainer, ILeaderboardSystemRuntimeData
	{
		/// <inheritdoc/>
		public IngressGuard IngressGuard { get; private set; }

		/// <inheritdoc/>
		public SingleFlightCache<LeaderboardPageKey, LeaderboardPageData> Pages { get; private set; }

		/// <inheritdoc/>
		public SingleFlightCache<LeaderboardStandingKey, LeaderboardStandingData> Standings { get; private set; }

		/// <summary>
		/// Creates the guard and the caches.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			Pages = new SingleFlightCache<LeaderboardPageKey, LeaderboardPageData>();
			Standings = new SingleFlightCache<LeaderboardStandingKey, LeaderboardStandingData>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the guard and drops every cached read.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
			Pages?.Clear();
			Standings?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}
