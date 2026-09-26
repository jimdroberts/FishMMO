using System;
using System.Collections.Concurrent;
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
	/// landed: every write that changes what a copy shows — owner, state, structures, guest list —
	/// marks its plot changed, and this polls for the marks. The same shape guilds use, for the
	/// same reason — the server that made the change is not the one that has to show it. Tax
	/// writes do not mark: nothing applied here reads tax state.</para>
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
		/// <remarks>
		/// Starts at a random point in the interval (<see cref="RandomiseSweepPhases"/>), so the
		/// cluster's scene servers do not all poll in the same second.
		/// </remarks>
		private float plotSyncCountdown;

		/// <summary>
		/// How far before the last poll's database time the next one starts reading, in seconds.
		/// </summary>
		/// <remarks>
		/// For marks stamped before a poll and committed after it (see <see cref="PlotSyncWindow"/>).
		/// Clock skew no longer needs covering — every time in the window is the database's — so this
		/// is sized for a commit that lags its statement, not for servers that disagree about the
		/// time. Re-reading the few plots inside it costs a lookup by ID each.
		/// </remarks>
		private const float PlotSyncCommitMarginSeconds = 10f;

		/// <summary>
		/// The database time each world's last complete poll was taken at. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>Moved on only once a poll has read and applied everything it found. It used to move
		/// the moment a poll was sent, so a poll that then failed in the database took its window
		/// with it. Per world, because each world's poll succeeds or fails on its own.</para>
		///
		/// <para>Absent for a world that has not completed a poll since its window last restarted,
		/// which makes the next poll read every mark (<see cref="PlotSyncWindow.Since"/>).</para>
		/// </remarks>
		private readonly Dictionary<long, DateTime> plotSyncWatermarkUtc = new Dictionary<long, DateTime>();

		/// <summary>
		/// How many times each world's window has restarted. Main thread only.
		/// </summary>
		/// <remarks>
		/// Bumped when one of the world's scenes is resolved (<see cref="RestartPlotSyncWindow"/>).
		/// A poll carries the epoch it was sent under and may only move the watermark if it is still
		/// current — see <see cref="PlotSyncWindow.ShouldAdvance"/>.
		/// </remarks>
		private readonly Dictionary<long, int> plotSyncEpochs = new Dictionary<long, int>();

		/// <summary>
		/// Worlds with a poll on the worker right now.
		/// </summary>
		/// <remarks>
		/// Added on the main thread when a poll is sent and removed by the poll itself however it
		/// ends, so it is concurrent. A poll slower than the interval used to be joined by the next,
		/// both reading the same window.
		/// </remarks>
		private readonly ConcurrentDictionary<long, byte> plotSyncInFlight = new ConcurrentDictionary<long, byte>();

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

			TimeSpan margin = TimeSpan.FromSeconds(PlotSyncCommitMarginSeconds);

			foreach (KeyValuePair<long, HashSet<int>> pair in scenesByWorld)
			{
				long worldServerID = pair.Key;
				if (!plotSyncInFlight.TryAdd(worldServerID, 0))
				{
					// The last poll of this world is still out; it moves the window when it lands.
					continue;
				}

				List<int> handles = new List<int>(pair.Value);
				bool hasWatermark = plotSyncWatermarkUtc.TryGetValue(worldServerID, out DateTime watermark);
				DateTime since = PlotSyncWindow.Since(hasWatermark, watermark, margin);
				int epoch = plotSyncEpochs.TryGetValue(worldServerID, out int currentEpoch) ? currentEpoch : 0;

				/* What to watch is gathered here, on the main thread: the registry is main-thread
				 * state, and the poll runs on the worker. */
				List<long> watched = CollectWatchedPlotIDs(handles);

				if (!TryEnqueueAsyncWork(() => SyncPlotsAsync(worldServerID, handles, watched, since, epoch)))
				{
					plotSyncInFlight.TryRemove(worldServerID, out _);
					Log.Warning("HousingSystem", $"Could not enqueue the plot sync for world {worldServerID}.");
				}
			}
		}

		/// <summary>
		/// Starts a world's sync window over, so the next poll reads every mark. Main thread only.
		/// </summary>
		/// <remarks>
		/// Called when one of the world's scenes has just been resolved. The resolve read those plots
		/// at a moment the running window knows nothing about, and a change landing between that read
		/// and the window's start would otherwise never be seen by this copy of the scene.
		/// </remarks>
		private void RestartPlotSyncWindow(long worldServerID)
		{
			plotSyncWatermarkUtc.Remove(worldServerID);
			plotSyncEpochs[worldServerID] = (plotSyncEpochs.TryGetValue(worldServerID, out int epoch) ? epoch : 0) + 1;
		}

		/// <summary>
		/// Records that a world's poll read and applied everything it found. Callable from the worker.
		/// </summary>
		/// <remarks>
		/// Failing to record it costs the next poll a wider window, not a missed change.
		/// </remarks>
		private void CompletePlotSync(long worldServerID, DateTime pollAsOfUtc, int pollEpoch)
		{
			TryEnqueueHousingMainThread(() =>
			{
				int currentEpoch = plotSyncEpochs.TryGetValue(worldServerID, out int epoch) ? epoch : 0;
				bool hasWatermark = plotSyncWatermarkUtc.TryGetValue(worldServerID, out DateTime watermark);
				if (PlotSyncWindow.ShouldAdvance(pollEpoch, currentEpoch, hasWatermark, watermark, pollAsOfUtc))
				{
					plotSyncWatermarkUtc[worldServerID] = pollAsOfUtc;
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
		/// Asks the update table which plots moved, then reads exactly those. It used to read back
		/// every plot of every scene the world had loaded to find the changed few — a full re-read
		/// of a district for one sale, twice, since the overlapping windows see each change twice.
		/// </remarks>
		/// <param name="worldServerID">The world being polled.</param>
		/// <param name="sceneHandles">This server's loaded copies of that world's scenes.</param>
		/// <param name="watched">The plots those copies show, gathered on the main thread.</param>
		/// <param name="since">Where this poll's window starts, on the database clock.</param>
		/// <param name="epoch">The world's window epoch when this poll was sent.</param>
		private async Task SyncPlotsAsync(long worldServerID, List<int> sceneHandles, List<long> watched, DateTime since, int epoch)
		{
			try
			{
				if (!TryGetDbService(out IPlotUpdateService plotUpdateService) ||
					!TryGetDbService(out IPlotService plotService))
				{
					Log.Error("HousingSystem", $"Plot sync for world {worldServerID} skipped: a plot service is unavailable.");
					return;
				}

				DatabaseResult<PlotUpdatePollData> poll = await plotUpdateService.FetchAsync(watched, since);
				if (!poll.IsSuccess)
				{
					Log.Error("HousingSystem", $"Plot sync for world {worldServerID} failed; the next poll re-reads this window: [{poll.ErrorCode}] {poll.ErrorMessage}");
					return;
				}

				DateTime asOfUtc = poll.Data.AsOfUtc;
				List<PlotUpdateData> updates = poll.Data.Updates;
				if (updates == null || updates.Count < 1)
				{
					CompletePlotSync(worldServerID, asOfUtc, epoch);
					return;
				}

				HashSet<long> changed = new HashSet<long>();
				foreach (PlotUpdateData update in updates)
				{
					changed.Add(update.PlotID);
				}

				DatabaseResult<List<PlotData>> plots = await plotService.FetchByIdsAsync(changed);
				if (!plots.IsSuccess || plots.Data == null)
				{
					Log.Warning("HousingSystem", $"Plot sync could not read {changed.Count} changed plot(s) in world {worldServerID}; the next poll re-reads them: [{plots.ErrorCode}] {plots.ErrorMessage}");
					return;
				}

				Dictionary<long, PlotData> refreshed = new Dictionary<long, PlotData>(plots.Data.Count);
				foreach (PlotData plot in plots.Data)
				{
					refreshed[plot.ID] = plot;
				}

				if (refreshed.Count < 1)
				{
					CompletePlotSync(worldServerID, asOfUtc, epoch);
					return;
				}

				/* Whether everything this poll found was read and applied. Anything short of that
				 * leaves the window where it was, so the next poll reads it again rather than never. */
				bool complete = true;

				/* Access is re-read for the changed plots too. A grant or a revocation on another
				 * channel marks its plot changed exactly as a sale does, and a copy that refreshed
				 * ownership but kept a stale guest list would keep admitting somebody the owner locked
				 * out — the one failure in this file that a player can be standing inside while it
				 * happens.
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
					CompletePlotSync(worldServerID, asOfUtc, epoch);
				}
			}
			catch (Exception ex)
			{
				Log.Error("HousingSystem", $"Plot sync for world {worldServerID} threw; the next poll re-reads this window: {ex}");
			}
			finally
			{
				plotSyncInFlight.TryRemove(worldServerID, out _);
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
			List<PlotFoundation> toClear = null;

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
					 * have people standing in it here. Gathered and put out together below, so each
					 * scene's occupants are read once however many of its plots changed. An empty
					 * lot bars nobody, so it is not worth reading anybody for. */
					if (foundation.State != PlotState.Empty)
					{
						(toClear ??= new List<PlotFoundation>()).Add(foundation);
					}
				}
			}

			if (toClear != null)
			{
				EvictTrespassersFrom(toClear);
			}

			if (applied > 0)
			{
				Log.Debug("HousingSystem", $"Applied {applied} plot change(s) from other channels.");
			}
		}
	}
}
