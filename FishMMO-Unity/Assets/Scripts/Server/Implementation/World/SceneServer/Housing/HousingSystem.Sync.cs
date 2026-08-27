using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Keeping plots consistent across channels and across the server cluster.
	/// </summary>
	/// <remarks>
	/// Channels are several live copies of one scene, and a plot is deliberately shared between all
	/// of them — one row per world, scene and key. Resolving a scene stamps ownership onto its
	/// foundations once, which is correct at that moment and wrong the instant anybody claims,
	/// releases or loses a plot from anywhere else.
	///
	/// <para>Closing that gap is what <c>plot_updates</c> has existed for since the data model
	/// landed: every write marks its plot changed, and this polls for the marks. The same shape
	/// guilds use, for the same reason — the server that made the change is not the one that has to
	/// show it.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>
		/// Seconds between polls for plots changed elsewhere.
		/// </summary>
		/// <remarks>
		/// Issue #121 asks for state that syncs slowly across the cluster, and this is that dial.
		/// Land changes hands rarely, and a plot showing a stale owner for a few seconds costs
		/// nothing — polling hard would spend a query per scene server per second to find nothing.
		/// </remarks>
		[Header("Cross-channel sync")]
		[Tooltip("Seconds between polls for plots changed by another scene server or channel.")]
		[SerializeField]
		private float plotSyncIntervalSeconds = 10f;

		/// <summary>
		/// Seconds until the next poll.
		/// </summary>
		private float plotSyncCountdown;

		/// <summary>
		/// The moment the last poll covered.
		/// </summary>
		/// <remarks>
		/// Rewound by one interval on every poll rather than set to exactly now. Server clocks
		/// differ by a little, and a write stamped a moment behind this server's clock would fall in
		/// the gap between two polls and be missed for good. Overlapping the windows re-reads a few
		/// rows instead, which costs a comparison and is idempotent.
		/// </remarks>
		private DateTime lastPlotSyncUtc = DateTime.UtcNow;

		/// <summary>
		/// Polls for plots changed elsewhere, on its interval.
		/// </summary>
		private void TickPlotSync(float deltaTime)
		{
			if (!IsHousingEnabled)
			{
				return;
			}

			plotSyncCountdown -= deltaTime;
			if (plotSyncCountdown > 0f)
			{
				return;
			}
			plotSyncCountdown = Mathf.Max(1f, plotSyncIntervalSeconds);

			Dictionary<long, HashSet<int>> scenesByWorld = CollectResolvedScenesByWorld();
			if (scenesByWorld.Count < 1)
			{
				return;
			}

			DateTime since = lastPlotSyncUtc - TimeSpan.FromSeconds(Mathf.Max(1f, plotSyncIntervalSeconds));
			lastPlotSyncUtc = DateTime.UtcNow;

			foreach (KeyValuePair<long, HashSet<int>> pair in scenesByWorld)
			{
				long worldServerID = pair.Key;
				List<int> handles = new List<int>(pair.Value);

				if (!TryEnqueueAsyncWork(() => SyncPlotsAsync(worldServerID, handles, since)))
				{
					Log.Warning("HousingSystem", $"Could not enqueue the plot sync for world {worldServerID}.");
				}
			}
		}

		/// <summary>
		/// Groups this server's resolved scenes by the world they belong to.
		/// </summary>
		private Dictionary<long, HashSet<int>> CollectResolvedScenesByWorld()
		{
			Dictionary<long, HashSet<int>> scenesByWorld = new Dictionary<long, HashSet<int>>();

			foreach (int sceneHandle in resolvedScenes)
			{
				if (!TryResolveWorld(sceneHandle, out long worldServerID, out _))
				{
					continue;
				}

				if (!scenesByWorld.TryGetValue(worldServerID, out HashSet<int> handles))
				{
					handles = new HashSet<int>();
					scenesByWorld.Add(worldServerID, handles);
				}
				handles.Add(sceneHandle);
			}

			return scenesByWorld;
		}

		/// <summary>
		/// Re-reads the plots this server is showing that have changed since the last poll.
		/// </summary>
		/// <remarks>
		/// Asks the update table which plots moved before reading any of them. The alternative —
		/// re-fetching every plot on every poll — would be a full scan of a scene's land every few
		/// seconds to discover that nothing had happened, which is what the update table exists to
		/// avoid.
		/// </remarks>
		private async Task SyncPlotsAsync(long worldServerID, List<int> sceneHandles, DateTime since)
		{
			if (!TryGetDbService(out IPlotUpdateService plotUpdateService) ||
				!TryGetDbService(out IPlotService plotService))
			{
				return;
			}

			List<long> watched = CollectWatchedPlotIDs(sceneHandles);
			if (watched.Count < 1)
			{
				return;
			}

			DatabaseResult<List<PlotUpdateData>> updates = await plotUpdateService.FetchAsync(watched, since);
			if (!updates.IsSuccess || updates.Data == null)
			{
				Log.Error("HousingSystem", $"Plot sync for world {worldServerID} failed: {updates.ErrorMessage}");
				return;
			}
			if (updates.Data.Count < 1)
			{
				return;
			}

			HashSet<long> changed = new HashSet<long>();
			foreach (PlotUpdateData update in updates.Data)
			{
				changed.Add(update.PlotID);
			}

			/* Read back by scene rather than by plot. The scenes this server shows are already known,
			 * a scene's plots come in one query, and the alternative is a query per changed plot —
			 * which is worst exactly when a lot has changed at once. */
			Dictionary<long, PlotData> refreshed = new Dictionary<long, PlotData>();
			HashSet<string> seenScenes = new HashSet<string>();

			foreach (int sceneHandle in sceneHandles)
			{
				if (!TryResolveWorld(sceneHandle, out _, out string sceneName) ||
					!seenScenes.Add(sceneName))
				{
					continue;
				}

				DatabaseResult<List<PlotData>> plots = await plotService.FetchBySceneAsync(worldServerID, sceneName);
				if (!plots.IsSuccess || plots.Data == null)
				{
					continue;
				}

				foreach (PlotData plot in plots.Data)
				{
					if (changed.Contains(plot.ID))
					{
						refreshed[plot.ID] = plot;
					}
				}
			}

			if (refreshed.Count < 1)
			{
				return;
			}

			if (!TryEnqueueHousingMainThread(() => ApplyChangedPlots(sceneHandles, refreshed)))
			{
				Log.Warning("HousingSystem", $"Could not apply {refreshed.Count} changed plot(s) for world {worldServerID}.");
			}
		}

		/// <summary>
		/// The plots this server currently shows across the given scenes.
		/// </summary>
		private static List<long> CollectWatchedPlotIDs(List<int> sceneHandles)
		{
			HashSet<long> ids = new HashSet<long>();

			foreach (int sceneHandle in sceneHandles)
			{
				foreach (PlotFoundation foundation in PlotFoundation.Registry.ForScene(sceneHandle))
				{
					if (foundation != null && foundation.PlotID > 0)
					{
						ids.Add(foundation.PlotID);
					}
				}
			}

			return new List<long>(ids);
		}

		/// <summary>
		/// Pushes changed ownership onto every copy of the affected plots.
		/// </summary>
		/// <remarks>
		/// Applied to every loaded scene rather than to one, because that is the whole point: a plot
		/// bought in one channel has to look bought in all of them. Two channels of the same scene
		/// hold two foundations for one plot row, and both are updated here.
		///
		/// <para>A plot being built on is left alone. Its owner is mid-session, the build state is
		/// held in memory on whichever server is running it, and overwriting ownership underneath an
		/// active session would drop the plot open with somebody still inside editing it.</para>
		/// </remarks>
		private void ApplyChangedPlots(List<int> sceneHandles, Dictionary<long, PlotData> refreshed)
		{
			int applied = 0;

			foreach (int sceneHandle in sceneHandles)
			{
				foreach (PlotFoundation foundation in PlotFoundation.Registry.ForScene(sceneHandle))
				{
					if (foundation == null ||
						foundation.PlotID <= 0 ||
						!refreshed.TryGetValue(foundation.PlotID, out PlotData plot))
					{
						continue;
					}

					if (foundation.IsBeingBuilt)
					{
						continue;
					}

					if (!PlotOwner.TryFromColumns(plot.OwnerCharacterID, plot.OwnerGuildID, out PlotOwner owner))
					{
						Log.Error("HousingSystem",
							$"Plot {plot.ID} names both a character and a guild owner; leaving this copy as it was.");
						continue;
					}

					if (foundation.Owner == owner)
					{
						continue;
					}

					foundation.ApplyOwner(owner);
					++applied;
				}
			}

			if (applied > 0)
			{
				Log.Debug("HousingSystem", $"Applied {applied} plot ownership change(s) from other channels.");
			}
		}
	}
}
