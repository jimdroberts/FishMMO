using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Registration and purchase of authored plots of land.
	/// </summary>
	public partial class HousingSystem
	{
		/// <summary>
		/// The currency attribute plots are bought with.
		/// </summary>
		[Header("Purchase")]
		[Tooltip("The attribute template land is paid for in. Must match the one the interactable systems charge against.")]
		[SerializeField]
		private CharacterAttributeTemplate currencyTemplate;

		/// <summary>
		/// Loaded scenes whose foundations have already been registered and resolved.
		/// </summary>
		/// <remarks>
		/// Registration is idempotent in the database, but doing it once per loaded scene rather
		/// than once per foundation keeps a scene holding fifty plots to a single round trip instead
		/// of fifty.
		/// </remarks>
		private readonly HashSet<int> resolvedScenes = new HashSet<int>();

		/// <summary>
		/// Loaded scenes holding foundations that could not yet be matched to a world server.
		/// </summary>
		/// <remarks>
		/// A foundation registers itself from <c>Awake</c>, during scene load, and the scene server
		/// records the instance separately — so which happens first is not something either side
		/// controls. Rather than depend on that ordering, a scene that cannot be resolved yet waits
		/// here and is retried until its instance appears.
		/// </remarks>
		private readonly HashSet<int> pendingScenes = new HashSet<int>();

		/// <summary>
		/// Maximum queued main-thread actions processed per frame.
		/// </summary>
		/// <remarks>
		/// Bounded so a burst of resolved scenes cannot stall a frame. Anything left over is
		/// processed next frame; the queue is drained in full at shutdown.
		/// </remarks>
		private const int MaxMainThreadActionsPerFrame = 16;

		/// <summary>
		/// Enqueues an action to run on the main thread via the housing queue.
		/// </summary>
		private bool TryEnqueueHousingMainThread(System.Action action)
		{
			return TryEnqueueMainThread<IHousingSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains queued main-thread actions.
		/// </summary>
		private void DrainHousingMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IHousingSystemMainThreadQueueData>(MaxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Runs queued main-thread work and retries any scene still waiting on its world server.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainHousingMainThreadQueue(drainAll: false);
			PruneUnloadedScenes();
			RetryPendingScenes();
			SweepBuildSessions(deltaTime);
			TickTax(deltaTime);
			TickPlotSync(deltaTime);
			TickAccessEnforcement(deltaTime);
		}

		/// <summary>
		/// Forgets scenes that have unloaded, so their handles can be resolved again.
		/// </summary>
		/// <remarks>
		/// Not merely tidiness. A Unity scene handle identifies one loaded copy of a scene inside
		/// this process, and the runtime reuses handles once the scene behind one is gone. A handle
		/// left in <see cref="resolvedScenes"/> after its scene unloaded is therefore a trap: the
		/// next scene to be given that number is treated as already resolved, its foundations are
		/// never registered, and every plot in it stays permanently unclaimable — with nothing in
		/// the log to say why, because the resolve simply never runs.
		///
		/// <para>Emptiness of the registry is the test, rather than the scene mapping, because that
		/// is the thing being indexed: a scene with no foundations left has nothing this system can
		/// act on whether it has technically unloaded or not.</para>
		/// </remarks>
		private void PruneUnloadedScenes()
		{
			if (resolvedScenes.Count < 1)
			{
				return;
			}

			List<int> gone = null;
			foreach (int sceneHandle in resolvedScenes)
			{
				if (PlotFoundation.Registry.ForScene(sceneHandle).Count < 1)
				{
					(gone ??= new List<int>()).Add(sceneHandle);
				}
			}

			if (gone == null)
			{
				return;
			}

			foreach (int sceneHandle in gone)
			{
				resolvedScenes.Remove(sceneHandle);
			}

			PruneStructureCache();
		}

		/// <summary>
		/// Drops cached contents for plots this server no longer shows.
		/// </summary>
		/// <remarks>
		/// The cache is keyed by plot while scenes are pruned by handle, so it cannot be cleaned in
		/// the same pass. Rebuilt against the plots still loaded rather than tracked incrementally:
		/// a scene unloads by destroying its foundations, which takes the plot IDs with it, so by
		/// the time this notices there is nothing left to look them up by.
		/// </remarks>
		private void PruneStructureCache()
		{
			if (structuresByPlot.Count < 1)
			{
				return;
			}

			HashSet<long> live = new HashSet<long>();
			foreach (int sceneHandle in resolvedScenes)
			{
				foreach (PlotFoundation foundation in PlotFoundation.Registry.ForScene(sceneHandle))
				{
					if (foundation != null && foundation.PlotID > 0)
					{
						live.Add(foundation.PlotID);
					}
				}
			}

			List<long> stale = null;
			foreach (long plotID in structuresByPlot.Keys)
			{
				if (!live.Contains(plotID))
				{
					(stale ??= new List<long>()).Add(plotID);
				}
			}

			if (stale == null)
			{
				return;
			}

			foreach (long plotID in stale)
			{
				structuresByPlot.Remove(plotID);
			}
		}

		/// <summary>
		/// Re-attempts resolution for scenes whose instance details were not available yet.
		/// </summary>
		private void RetryPendingScenes()
		{
			if (pendingScenes.Count < 1)
			{
				return;
			}

			// Copied, because a successful resolve mutates the set being walked.
			int[] handles = new int[pendingScenes.Count];
			pendingScenes.CopyTo(handles);

			foreach (int handle in handles)
			{
				/* A scene that has since unloaded takes its foundations with it, so it stops being
				 * pending rather than being retried forever. */
				if (PlotFoundation.Registry.ForScene(handle).Count < 1)
				{
					pendingScenes.Remove(handle);
					continue;
				}

				ResolveScene(handle);
			}
		}

		/// <summary>
		/// Finds the world server and scene name behind a loaded scene handle.
		/// </summary>
		/// <remarks>
		/// The world server is part of a plot's identity, and a scene server can host scenes for
		/// several worlds at once, so it has to be read per loaded scene rather than assumed.
		/// </remarks>
		private bool TryResolveWorld(int sceneHandle, out long worldServerID, out string sceneName)
		{
			worldServerID = 0;
			sceneName = null;

			if (Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out ISceneInstanceMappingData mappingData) ||
				mappingData.SceneInstanceByHandle == null ||
				!mappingData.SceneInstanceByHandle.TryGetValue(sceneHandle, out ISceneInstanceDetails details) ||
				details == null)
			{
				return false;
			}

			worldServerID = details.WorldServerID;
			sceneName = details.Name;

			return worldServerID > 0 && !string.IsNullOrWhiteSpace(sceneName);
		}

		/// <summary>
		/// Subscribes to foundation registration and claim requests.
		/// </summary>
		private void SubscribeToPlots()
		{
			PlotFoundation.Registry.OnSceneGainedFoundations += Registry_OnSceneGainedFoundations;
			PlotFoundation.Registry.OnClaimRequested += Registry_OnClaimRequested;

			/* Scenes already loaded before this system initialised still need resolving. The event
			 * only fires on the transition from no foundations to some, so a scene that finished
			 * loading first would otherwise never be picked up. */
			foreach (int sceneHandle in new List<int>(PlotFoundation.Registry.Scenes))
			{
				ResolveScene(sceneHandle);
			}
		}

		/// <summary>
		/// Releases the foundation subscriptions.
		/// </summary>
		private void UnsubscribeFromPlots()
		{
			PlotFoundation.Registry.OnSceneGainedFoundations -= Registry_OnSceneGainedFoundations;
			PlotFoundation.Registry.OnClaimRequested -= Registry_OnClaimRequested;
			resolvedScenes.Clear();
			pendingScenes.Clear();

			// Anything still queued is a purchase half-finished; run it rather than drop it.
			DrainHousingMainThreadQueue(drainAll: true);
		}

		/// <summary>
		/// Registers a newly-populated scene's foundations and reads their ownership back.
		/// </summary>
		private void Registry_OnSceneGainedFoundations(int sceneHandle)
		{
			ResolveScene(sceneHandle);
		}

		/// <summary>
		/// Ensures every foundation in a loaded scene has a row, then applies its stored ownership.
		/// </summary>
		private void ResolveScene(int sceneHandle)
		{
			if (resolvedScenes.Contains(sceneHandle))
			{
				return;
			}

			IReadOnlyList<PlotFoundation> foundations = PlotFoundation.Registry.ForScene(sceneHandle);
			if (foundations.Count < 1)
			{
				return;
			}

			if (!TryResolveWorld(sceneHandle, out long worldServerID, out string sceneName))
			{
				/* The scene server has not recorded this instance yet. Wait rather than guess: a
				 * plot registered against the wrong world is land that belongs to the wrong
				 * players. */
				pendingScenes.Add(sceneHandle);
				return;
			}

			List<string> keys = new List<string>(foundations.Count);
			foreach (PlotFoundation foundation in foundations)
			{
				if (!string.IsNullOrEmpty(foundation.PlotKey))
				{
					keys.Add(foundation.PlotKey);
				}
			}

			if (keys.Count < 1)
			{
				pendingScenes.Remove(sceneHandle);
				return;
			}

			resolvedScenes.Add(sceneHandle);
			pendingScenes.Remove(sceneHandle);

			if (!TryEnqueueAsyncWork(() => ResolveSceneAsync(sceneHandle, worldServerID, sceneName, keys)))
			{
				/* Nothing registered, so nothing may be claimed here. Dropping the marker lets a
				 * later attempt try again rather than leaving the land permanently unclaimable. */
				resolvedScenes.Remove(sceneHandle);
				pendingScenes.Add(sceneHandle);
				Log.Warning("HousingSystem", $"Could not enqueue plot registration for scene '{sceneName}'.");
			}
		}

		/// <summary>
		/// Registers a scene's plots and pushes the resulting rows back onto its foundations.
		/// </summary>
		private async Task ResolveSceneAsync(int sceneHandle, long worldServerID, string sceneName, List<string> keys)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				Log.Error("HousingSystem", $"Plot registration for '{sceneName}' skipped: IPlotService unavailable.");
				return;
			}

			DatabaseResult<int> registered = await plotService.RegisterAsync(worldServerID, sceneName, keys);
			if (!registered.IsSuccess)
			{
				Log.Error("HousingSystem", $"Plot registration for '{sceneName}' failed: {registered.ErrorMessage}");
				return;
			}
			if (registered.Data > 0)
			{
				Log.Debug("HousingSystem", $"Registered {registered.Data} new plot(s) in '{sceneName}' for world {worldServerID}.");
			}

			/* Before reading anything, give a due date to land claimed while tax was switched off.
			 * The sweep only looks at plots that have one, so without this a server that enabled tax
			 * later would never charge a single plot claimed before it did — and the difference
			 * between a founding player and a later one would be permanent. Idempotent, so running
			 * it on every resolve costs one no-op UPDATE. */
			DateTime? backfillDue = NextTaxDueUtc();
			if (backfillDue.HasValue)
			{
				DatabaseResult<int> backfilled = await plotService.BackfillTaxDueAsync(worldServerID, backfillDue.Value);
				if (!backfilled.IsSuccess)
				{
					Log.Warning("HousingSystem", $"Could not backfill tax dates for world {worldServerID}: {backfilled.ErrorMessage}");
				}
				else if (backfilled.Data > 0)
				{
					Log.Debug("HousingSystem", $"Gave {backfilled.Data} untaxed plot(s) in world {worldServerID} a first due date.");
				}
			}

			DatabaseResult<List<PlotData>> plots = await plotService.FetchBySceneAsync(worldServerID, sceneName);
			if (!plots.IsSuccess || plots.Data == null)
			{
				Log.Error("HousingSystem", $"Could not read plots for '{sceneName}': {plots.ErrorMessage}");
				return;
			}

			/* Access is read in the same pass as ownership, not lazily when somebody first walks up
			 * to a door. A plot whose grants have not arrived admits nobody but its owner, so
			 * deferring the read would lock every friend out of every house for as long as it took
			 * — and a player who reached a door during that window would be told no for a reason
			 * that had nothing to do with them. */
			Dictionary<long, Dictionary<long, PlotPermission>> grantsByPlot = await FetchAccessGrantsAsync(plots.Data);

			/* What is standing on each plot, read in the same pass and for the same reason. Placement
			 * tests a new piece against everything already there, so the alternative is a round trip
			 * in front of every piece a player drags into position. */
			List<long> plotIDs = new List<long>(plots.Data.Count);
			foreach (PlotData plot in plots.Data)
			{
				if (plot.ID > 0)
				{
					plotIDs.Add(plot.ID);
				}
			}
			Dictionary<long, List<PlotStructureData>> structures = await FetchStructuresAsync(plotIDs);

			// Unity objects may only be touched on the main thread.
			if (!TryEnqueueHousingMainThread(() => ApplyResolvedPlots(sceneHandle, sceneName, plots.Data, grantsByPlot, structures)))
			{
				Log.Warning("HousingSystem", $"Could not apply resolved plots for '{sceneName}'.");
			}
		}

		/// <summary>
		/// Matches database rows to the foundations in a loaded scene.
		/// </summary>
		/// <remarks>
		/// A foundation with no matching row keeps a plot ID of zero and stays unclaimable. That is
		/// the honest outcome: registration is what creates rows, so a missing one means this
		/// foundation's key never reached the database, and letting it look claimable would only
		/// move the failure to the moment somebody tries to pay.
		/// </remarks>
		private void ApplyResolvedPlots(
			int sceneHandle,
			string sceneName,
			List<PlotData> plots,
			Dictionary<long, Dictionary<long, PlotPermission>> grantsByPlot,
			Dictionary<long, List<PlotStructureData>> structuresByPlotID)
		{
			Dictionary<string, PlotData> byKey = new Dictionary<string, PlotData>(plots.Count);
			foreach (PlotData plot in plots)
			{
				if (!string.IsNullOrEmpty(plot.PlotKey))
				{
					byKey[plot.PlotKey] = plot;
				}
			}

			int resolved = 0;
			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForScene(sceneHandle))
			{
				if (foundation == null || string.IsNullOrEmpty(foundation.PlotKey))
				{
					continue;
				}

				if (!byKey.TryGetValue(foundation.PlotKey, out PlotData plot))
				{
					Log.Warning("HousingSystem",
						$"Foundation '{foundation.PlotKey}' in '{sceneName}' has no database row and cannot be claimed.");
					continue;
				}

				if (!PlotOwner.TryFromColumns(plot.OwnerCharacterID, plot.OwnerGuildID, out PlotOwner owner))
				{
					Log.Error("HousingSystem",
						$"Plot {plot.ID} ('{plot.PlotKey}' in '{sceneName}') names both a character and a guild owner; leaving it unresolved.");
					continue;
				}

				/* An unrecognised state is treated as Empty rather than cast blindly. The column is
				 * an integer and a row written by a newer build could hold a value this one has no
				 * name for; casting it would produce a PlotState that matches none of the branches
				 * that decide access, and the safe reading of "I do not know what this is" is the
				 * state that grants the least. */
				PlotState state = PlotStateExtensions.FromStored(plot.State);

				grantsByPlot.TryGetValue(plot.ID, out Dictionary<long, PlotPermission> grants);
				foundation.ApplyResolvedState(plot.ID, owner, state, grants ?? new Dictionary<long, PlotPermission>());

				structuresByPlot[plot.ID] = structuresByPlotID.TryGetValue(plot.ID, out List<PlotStructureData> built)
					? built
					: new List<PlotStructureData>();

				++resolved;
			}

			Log.Debug("HousingSystem", $"Resolved {resolved} plot(s) in '{sceneName}'.");
		}

		/// <summary>
		/// Handles a player's request to claim a plot.
		/// </summary>
		/// <remarks>
		/// Everything cheap and local is checked here, on the main thread, before any database work
		/// is started — an unaffordable claim should cost a round trip to nobody.
		/// </remarks>
		private void Registry_OnClaimRequested(IPlayerCharacter player, IPlotFoundation foundation)
		{
			if (player == null || foundation == null)
			{
				return;
			}

			if (!IsHousingEnabled)
			{
				return;
			}

			/* Only player ownership is offered here. Guild-owned land needs a guild treasury to buy
			 * it from, and guilds have no balance, so a guild claim would have to charge some
			 * member personally for land they do not own — see the plan in #121. */
			if (!AllowsPlayerOwnership)
			{
				return;
			}

			if (foundation.PlotID <= 0)
			{
				Log.Warning("HousingSystem", $"CharID={player.ID} tried to claim an unresolved plot ('{foundation.PlotKey}').");
				return;
			}

			/* Checked here as well as in the UPDATE's WHERE clause. This copy can be stale — the plot
			 * may have been claimed on another channel a moment ago — so it is a courtesy that saves
			 * a round trip, not the decision. The database's answer is the one that counts. */
			if (!foundation.State.IsClaimable())
			{
				return;
			}

			/* The plot was resolved for the world server hosting this scene, and the player is
			 * standing in it, so these agree in every ordinary case. Checked anyway because the
			 * failure is silent and expensive if they ever do not: a character would buy land on a
			 * world they are not playing on, and never see the house they paid for. */
			if (!TryResolveWorld(foundation.GameObject.scene.handle, out long worldServerID, out _) ||
				worldServerID != player.WorldServerID)
			{
				Log.Warning("HousingSystem",
					$"CharID={player.ID} (world {player.WorldServerID}) tried to claim plot {foundation.PlotID}, which belongs to world {worldServerID}.");
				return;
			}

			if (currencyTemplate == null)
			{
				Log.Error("HousingSystem", "Claim refused: currencyTemplate is not assigned, so land has no price.");
				return;
			}

			PlotOwner owner = PlotOwner.ForCharacter(player.ID);
			if (!owner.IsAllowedBy(OwnershipMode))
			{
				return;
			}

			long price = foundation.Price;
			if (price > 0 && !CharacterCurrency.CanAfford(player, currencyTemplate, price))
			{
				return;
			}

			long plotID = foundation.PlotID;
			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(() => ClaimPlotAsync(player, foundation, plotID, characterID, price), characterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue plot claim for CharID={characterID}.");
			}
		}

		/// <summary>
		/// Takes the plot first, then the money.
		/// </summary>
		/// <remarks>
		/// The order is deliberate, and it is the opposite of what a purchase usually looks like.
		///
		/// <para>The plot is the contended thing: two players on two scene servers can want the same
		/// foundation in the same second, and only one may have it. The claim is the atomic step
		/// that settles that, so it goes first — which means the common failure, losing the race,
		/// costs the loser nothing and needs no refund at all. Charging first would run a refund
		/// every time two people wanted the same land, and a compensation path that busy is a
		/// compensation path with a bug in it.</para>
		///
		/// <para>The balance, by contrast, is contended by nobody but its owner. It was checked a
		/// moment ago on the main thread, so the only way the charge now fails is if the player
		/// spent the money elsewhere during this round trip. That is rare, and it is recoverable:
		/// the plot is released back, pinned to this owner so a release cannot evict whoever claimed
		/// it next.</para>
		/// </remarks>
		private async Task ClaimPlotAsync(IPlayerCharacter player, IPlotFoundation foundation, long plotID, long characterID, long price)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				Log.Error("HousingSystem", "Claim failed: IPlotService unavailable.");
				return;
			}

			DatabaseResult<int> claim = await plotService.TryClaimAsync(
				plotID,
				characterID,
				0,
				NextTaxDueUtc(),
				(int)PlotStateExtensions.OnClaimed());

			if (!claim.IsSuccess)
			{
				/* A unique violation here is the one-house-per-player index firing, not a fault. Two
				 * claims on two scene servers can both pass the NOT EXISTS check inside the
				 * statement; the index is what stops the second becoming a second house. Reported at
				 * debug, like any other lost race, because that is what it is. */
				if (claim.ErrorCode == DatabaseErrorCodes.UniqueViolation)
				{
					Log.Debug("HousingSystem", $"CharID={characterID} already owns a plot and cannot claim {plotID}.");
					return;
				}

				Log.Error("HousingSystem", $"Claim of plot {plotID} for CharID={characterID} errored: {claim.ErrorMessage}");
				return;
			}
			if (claim.Data != 1)
			{
				/* Somebody else owns it, or this character already owns one. Nothing was taken, so
				 * there is nothing to undo either way. */
				Log.Debug("HousingSystem", $"CharID={characterID} lost the race for plot {plotID}, or already owns land.");
				return;
			}

			/* The land arrives clean, both ways.
			 *
			 * An abandoned plot carries its previous owner's grants until something removes them,
			 * and reclamation is a write that can be interrupted — so the claim clears them rather
			 * than trusting that it was. The same goes for anything left standing: guild land
			 * released when a guild disbanded was never vaulted, and an interrupted reclamation can
			 * leave a house on land the row says is free. Neither should become the new owner's
			 * problem, and a claim is rare enough that the two extra writes cost nothing. */
			ClearAccessGrants(plotID);
			ClearStructures(plotID);
			UncachePlot(plotID);

			MarkPlotChanged(plotID);

			if (price <= 0)
			{
				ApplyClaimedStateOnMainThread(foundation, PlotOwner.ForCharacter(characterID));
				return;
			}

			// The charge touches in-memory attributes, so it has to go back to the main thread.
			if (!TryEnqueueHousingMainThread(() => CompletePlotPurchase(player, foundation, plotID, characterID, price)))
			{
				Log.Error("HousingSystem",
					$"Plot {plotID} was claimed for CharID={characterID} but the charge could not be scheduled; releasing it.");
				await ReleaseClaimAsync(plotService, plotID, characterID);
			}
		}

		/// <summary>
		/// Charges for a plot that has already been claimed, releasing it if the charge fails.
		/// </summary>
		private void CompletePlotPurchase(IPlayerCharacter player, IPlotFoundation foundation, long plotID, long characterID, long price)
		{
			if (CharacterCurrency.TrySpend(player, currencyTemplate, price, () => TryPersistCurrency(player)))
			{
				ApplyClaimedState(foundation, PlotOwner.ForCharacter(characterID));

				/* Recorded only now, after the deduction is durable. A ledger entry that precedes
				 * its deduction would be returned by escrow reconciliation and hand back money that
				 * was never taken — see the invariant in #148. */
				RecordLandPurchase(characterID, price);
				return;
			}

			Log.Warning("HousingSystem", $"CharID={characterID} could not pay {price} for plot {plotID}; releasing it.");

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (TryGetDbService(out IPlotService plotService))
				{
					await ReleaseClaimAsync(plotService, plotID, characterID);
				}
			}, characterID))
			{
				Log.Error("HousingSystem",
					$"Plot {plotID} is claimed by CharID={characterID} who did not pay for it, and the release could not be scheduled.");
			}
		}

		/// <summary>
		/// Gives a plot back, pinned to the owner that claimed it.
		/// </summary>
		private async Task ReleaseClaimAsync(IPlotService plotService, long plotID, long characterID)
		{
			/* Back to Empty, not Abandoned. Nobody lived here — the claim was undone seconds after it
			 * was made, because the buyer could not pay — so the lot should look untouched rather
			 * than like a house somebody lost. */
			DatabaseResult<int> release = await plotService.ReleaseAsync(plotID, characterID, 0, (int)PlotStateExtensions.OnReleased());
			if (!release.IsSuccess || release.Data != 1)
			{
				Log.Error("HousingSystem",
					$"Plot {plotID} could not be released from CharID={characterID}; it is owned by someone who did not pay.");
				return;
			}

			MarkPlotChanged(plotID);
		}

		/// <summary>
		/// Tells the other scene servers that a plot changed hands.
		/// </summary>
		/// <remarks>
		/// Best effort and never awaited by the transaction. A missed notification delays the other
		/// channels noticing until their next poll; blocking the purchase on it would make a
		/// bookkeeping write able to fail a sale.
		/// </remarks>
		private void MarkPlotChanged(long plotID)
		{
			if (!TryEnqueueAsyncWork(async () =>
			{
				if (TryGetDbService(out IPlotUpdateService plotUpdateService))
				{
					await plotUpdateService.PersistAsync(plotID);
				}
			}))
			{
				Log.Warning("HousingSystem", $"Could not record the update for plot {plotID}; other channels will see it late.");
			}
		}

		/// <summary>
		/// Records a completed land purchase in the currency ledger.
		/// </summary>
		private void RecordLandPurchase(long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(
					characterID,
					amount,
					(int)CurrencyMovementReason.LandPurchase,
					(int)CurrencyMovementState.Absorbed);

				if (!record.IsSuccess)
				{
					Log.Warning("HousingSystem",
						$"Currency ledger: could not record {amount} (land purchase) for CharID={characterID}. {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Currency ledger: async worker rejected the record for CharID={characterID}.");
			}
		}

		/// <summary>
		/// Applies a completed claim to a foundation from the main thread.
		/// </summary>
		private void ApplyClaimedStateOnMainThread(IPlotFoundation foundation, PlotOwner owner)
		{
			if (!TryEnqueueHousingMainThread(() => ApplyClaimedState(foundation, owner)))
			{
				Log.Warning("HousingSystem", "Could not apply the plot claim; it will correct on the next resolve.");
			}
		}

		/// <summary>
		/// Applies a completed claim — new owner, new state — to a foundation.
		/// </summary>
		/// <remarks>
		/// Both together, because they were written together. Applying the owner alone would leave a
		/// plot that somebody holds and that every client still draws as an empty lot, and the state
		/// would not correct until the next cross-channel poll came round.
		/// </remarks>
		private void ApplyClaimedState(IPlotFoundation foundation, PlotOwner owner)
		{
			if (foundation is not PlotFoundation plotFoundation)
			{
				return;
			}

			plotFoundation.ApplyOwner(owner);
			plotFoundation.ApplyState(PlotStateExtensions.OnClaimed());

			/* Anybody standing on the lot is put out now rather than on the next sweep. The plot has
			 * just gone from public ground to a building site, and the people on it were doing
			 * nothing wrong a second ago — half a second of standing inside somebody else's
			 * construction is exactly the window the design says to close. */
			EvictTrespassers(plotFoundation);
		}

		/// <summary>
		/// Persists a character's attributes so a currency change survives a restart.
		/// </summary>
		private bool TryPersistCurrency(IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController) ||
				!TryGetDbService(out ICharacterAttributeService attributeService))
			{
				return false;
			}

			long characterID = character.ID;
			List<CharacterAttributeData> dtos = new List<CharacterAttributeData>();

			foreach (KeyValuePair<int, CharacterAttribute> kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(0, kvp.Value.Version, characterID, kvp.Key, kvp.Value.Value, 0.0f));
			}
			foreach (KeyValuePair<int, CharacterResourceAttribute> kvp in attributeController.ResourceAttributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(0, kvp.Value.Version, characterID, kvp.Key, kvp.Value.Value, kvp.Value.CurrentValue));
			}

			return TryEnqueueAsyncWork(async () => await attributeService.PersistAsync(dtos), characterID);
		}
	}
}
