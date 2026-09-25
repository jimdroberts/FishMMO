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
		/// Where polling starts for a world that has not completed a poll yet.
		/// </summary>
		private DateTime lastPlotSyncUtc = DateTime.UtcNow;

		/// <summary>
		/// The moment each world's last complete poll covered up to. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>Rewound by one interval on every poll rather than read as exactly then. Server
		/// clocks differ by a little, and a write stamped a moment behind this server's clock would
		/// fall in the gap between two polls and be missed for good. Overlapping the windows re-reads
		/// a few rows instead, which costs a comparison and is idempotent.</para>
		///
		/// <para>Moved on only once a poll has read and applied everything it found. It used to move
		/// the moment a poll was sent, so a poll that then failed in the database took its window
		/// with it: the overlap absorbed one failure, and two in a row lost every change in between
		/// — a sale, a finished house, a revoked key — for as long as this server kept the scene
		/// loaded. Per world, because each world's poll succeeds or fails on its own.</para>
		/// </remarks>
		private readonly Dictionary<long, DateTime> plotSyncWatermarkUtc = new Dictionary<long, DateTime>();

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

			DateTime pollStartUtc = DateTime.UtcNow;
			TimeSpan overlap = TimeSpan.FromSeconds(Mathf.Max(1f, plotSyncIntervalSeconds));

			foreach (KeyValuePair<long, HashSet<int>> pair in scenesByWorld)
			{
				long worldServerID = pair.Key;
				List<int> handles = new List<int>(pair.Value);
				DateTime since = (plotSyncWatermarkUtc.TryGetValue(worldServerID, out DateTime watermark) ? watermark : lastPlotSyncUtc) - overlap;

				/* What to watch and which scenes to read are gathered here, on the main thread. The
				 * registry and the scene mapping are main-thread state, and the poll runs on the
				 * worker. */
				List<long> watched = CollectWatchedPlotIDs(handles);
				List<string> sceneNames = CollectSceneNames(handles);

				if (!TryEnqueueAsyncWork(() => SyncPlotsAsync(worldServerID, handles, watched, sceneNames, since, pollStartUtc)))
				{
					Log.Warning("HousingSystem", $"Could not enqueue the plot sync for world {worldServerID}.");
				}
			}
		}

		/// <summary>
		/// The distinct scene names behind a set of loaded scene handles. Main thread only.
		/// </summary>
		private List<string> CollectSceneNames(List<int> sceneHandles)
		{
			List<string> names = new List<string>();

			foreach (int sceneHandle in sceneHandles)
			{
				if (TryResolveWorld(sceneHandle, out _, out string sceneName) && !names.Contains(sceneName))
				{
					names.Add(sceneName);
				}
			}

			return names;
		}

		/// <summary>
		/// Records that a world's poll read and applied everything it found. Callable from the worker.
		/// </summary>
		/// <remarks>
		/// Only ever moved forward, so a slow poll finishing after a later one cannot rewind it.
		/// Failing to record it costs the next poll a wider window, not a missed change.
		/// </remarks>
		private void CompletePlotSync(long worldServerID, DateTime pollStartUtc)
		{
			TryEnqueueHousingMainThread(() =>
			{
				if (!plotSyncWatermarkUtc.TryGetValue(worldServerID, out DateTime watermark) || watermark < pollStartUtc)
				{
					plotSyncWatermarkUtc[worldServerID] = pollStartUtc;
				}
			});
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
		/// <param name="worldServerID">The world being polled.</param>
		/// <param name="sceneHandles">This server's loaded copies of that world's scenes.</param>
		/// <param name="watched">The plots those copies show, gathered on the main thread.</param>
		/// <param name="sceneNames">The scenes behind those copies, gathered on the main thread.</param>
		/// <param name="since">Where this poll's window starts.</param>
		/// <param name="pollStartUtc">Where the next window starts, if this poll completes.</param>
		private async Task SyncPlotsAsync(long worldServerID, List<int> sceneHandles, List<long> watched, List<string> sceneNames, DateTime since, DateTime pollStartUtc)
		{
			if (!TryGetDbService(out IPlotUpdateService plotUpdateService) ||
				!TryGetDbService(out IPlotService plotService))
			{
				Log.Error("HousingSystem", $"Plot sync for world {worldServerID} skipped: a plot service is unavailable.");
				return;
			}

			if (watched.Count < 1)
			{
				CompletePlotSync(worldServerID, pollStartUtc);
				return;
			}

			DatabaseResult<List<PlotUpdateData>> updates = await plotUpdateService.FetchAsync(watched, since);
			if (!updates.IsSuccess || updates.Data == null)
			{
				Log.Error("HousingSystem", $"Plot sync for world {worldServerID} failed; the next poll re-reads this window: [{updates.ErrorCode}] {updates.ErrorMessage}");
				return;
			}
			if (updates.Data.Count < 1)
			{
				CompletePlotSync(worldServerID, pollStartUtc);
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

			/* Whether everything this poll found was read and applied. Anything short of that leaves
			 * the window where it was, so the next poll reads it again rather than never. */
			bool complete = true;

			foreach (string sceneName in sceneNames)
			{
				DatabaseResult<List<PlotData>> plots = await plotService.FetchBySceneAsync(worldServerID, sceneName);
				if (!plots.IsSuccess || plots.Data == null)
				{
					Log.Warning("HousingSystem", $"Plot sync could not read '{sceneName}' in world {worldServerID}; the next poll re-reads it: [{plots.ErrorCode}] {plots.ErrorMessage}");
					complete = false;
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
				if (complete)
				{
					CompletePlotSync(worldServerID, pollStartUtc);
				}
				return;
			}

			/* Access is re-read for the changed plots too. A grant or a revocation on another channel
			 * marks its plot changed exactly as a sale does, and a copy that refreshed ownership but
			 * kept a stale guest list would keep admitting somebody the owner locked out — the one
			 * failure in this file that a player can be standing inside while it happens.
			 *
			 * A read that fails comes back null, and the copies keep the lists they have (see
			 * ApplyChangedPlots) until the next poll reads them again. */
			List<PlotData> changedPlots = new List<PlotData>(refreshed.Values);
			Dictionary<long, Dictionary<long, PlotPermission>> grantsByPlot = await FetchAccessGrantsAsync(changedPlots);
			if (grantsByPlot == null)
			{
				complete = false;
			}

			if (!TryEnqueueHousingMainThread(() => ApplyChangedPlots(sceneHandles, refreshed, grantsByPlot)))
			{
				Log.Warning("HousingSystem", $"Could not apply {refreshed.Count} changed plot(s) for world {worldServerID}; the next poll re-reads them.");
				complete = false;
			}

			if (complete)
			{
				CompletePlotSync(worldServerID, pollStartUtc);
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
		private void ApplyChangedPlots(
			List<int> sceneHandles,
			Dictionary<long, PlotData> refreshed,
			Dictionary<long, Dictionary<long, PlotPermission>> grantsByPlot)
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

					PlotState state = PlotStateExtensions.FromStored(plot.State);

					/* State is applied alongside ownership, and both are compared before deciding
					 * there is nothing to do.
					 *
					 * Testing the owner alone would skip every change that does not move the plot
					 * between hands — and finishing a house is exactly that. A house completed on one
					 * channel would stay a building site on every other one, which means locked to
					 * the friends its owner had just opened it to, until something else happened to
					 * that plot. */
					bool ownerChanged = foundation.Owner != owner;
					bool stateChanged = foundation.State != state;

					if (ownerChanged)
					{
						foundation.ApplyOwner(owner);
					}
					if (stateChanged)
					{
						foundation.ApplyState(state);
					}

					/* The guest list is replaced whenever the read produced one, change or no change:
					 * it is a set, and comparing it to decide would cost more than assigning it. A
					 * plot whose grants could not be read keeps the ones it has rather than being
					 * emptied — the safe direction is a stale key, not locking an owner's friends out
					 * of a house because one query failed. */
					if (grantsByPlot != null &&
						grantsByPlot.TryGetValue(plot.ID, out Dictionary<long, PlotPermission> grants))
					{
						foundation.ApplyAccessGrants(grants);
					}
					else if (grantsByPlot != null && foundation.HasResolvedAccess)
					{
						/* No row came back for this plot, which means nobody is on its list. Cleared
						 * rather than left, or a revocation processed elsewhere would never arrive:
						 * the absence of a grant is what a revocation looks like from here. */
						foundation.ApplyAccessGrants(new Dictionary<long, PlotPermission>());
					}

					if (ownerChanged || stateChanged)
					{
						++applied;
					}

					/* Anybody the change has just barred goes out now. A plot that became a building
					 * site, or was reclaimed, or whose owner revoked a key on another channel, may
					 * have people standing in it here. */
					EvictTrespassers(foundation);
				}
			}

			if (applied > 0)
			{
				Log.Debug("HousingSystem", $"Applied {applied} plot change(s) from other channels.");
			}
		}
	}
}
