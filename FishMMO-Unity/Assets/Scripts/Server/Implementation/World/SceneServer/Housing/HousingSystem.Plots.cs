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
		/// Scenes whose foundations have already been registered and resolved.
		/// </summary>
		/// <remarks>
		/// Registration is idempotent in the database, but doing it once per scene rather than once
		/// per foundation keeps a scene holding fifty plots to a single round trip instead of fifty.
		/// </remarks>
		private readonly HashSet<string> resolvedScenes = new HashSet<string>();

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
		/// Runs queued main-thread work.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainHousingMainThreadQueue(drainAll: false);
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
			foreach (string sceneName in new List<string>(PlotFoundation.Registry.Scenes))
			{
				ResolveScene(sceneName);
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

			// Anything still queued is a purchase half-finished; run it rather than drop it.
			DrainHousingMainThreadQueue(drainAll: true);
		}

		/// <summary>
		/// Registers a newly-populated scene's foundations and reads their ownership back.
		/// </summary>
		private void Registry_OnSceneGainedFoundations(string sceneName)
		{
			ResolveScene(sceneName);
		}

		/// <summary>
		/// Ensures every foundation in a scene has a row, then applies the stored ownership to it.
		/// </summary>
		private void ResolveScene(string sceneName)
		{
			if (string.IsNullOrWhiteSpace(sceneName) || !resolvedScenes.Add(sceneName))
			{
				return;
			}

			IReadOnlyList<PlotFoundation> foundations = PlotFoundation.Registry.ForScene(sceneName);
			if (foundations.Count < 1)
			{
				resolvedScenes.Remove(sceneName);
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
				resolvedScenes.Remove(sceneName);
				return;
			}

			if (!TryEnqueueAsyncWork(() => ResolveSceneAsync(sceneName, keys)))
			{
				/* Nothing registered, so nothing may be claimed here. Dropping the marker lets a
				 * later foundation joining this scene try again rather than leaving the land
				 * permanently unclaimable. */
				resolvedScenes.Remove(sceneName);
				Log.Warning("HousingSystem", $"Could not enqueue plot registration for scene '{sceneName}'.");
			}
		}

		/// <summary>
		/// Registers a scene's plots and pushes the resulting rows back onto its foundations.
		/// </summary>
		private async Task ResolveSceneAsync(string sceneName, List<string> keys)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				Log.Error("HousingSystem", $"Plot registration for '{sceneName}' skipped: IPlotService unavailable.");
				return;
			}

			DatabaseResult<int> registered = await plotService.RegisterAsync(sceneName, keys);
			if (!registered.IsSuccess)
			{
				Log.Error("HousingSystem", $"Plot registration for '{sceneName}' failed: {registered.ErrorMessage}");
				return;
			}
			if (registered.Data > 0)
			{
				Log.Debug("HousingSystem", $"Registered {registered.Data} new plot(s) in '{sceneName}'.");
			}

			DatabaseResult<List<PlotData>> plots = await plotService.FetchBySceneAsync(sceneName);
			if (!plots.IsSuccess || plots.Data == null)
			{
				Log.Error("HousingSystem", $"Could not read plots for '{sceneName}': {plots.ErrorMessage}");
				return;
			}

			// Unity objects may only be touched on the main thread.
			if (!TryEnqueueHousingMainThread(() => ApplyResolvedPlots(sceneName, plots.Data)))
			{
				Log.Warning("HousingSystem", $"Could not apply resolved plots for '{sceneName}'.");
			}
		}

		/// <summary>
		/// Matches database rows to the foundations authored in a scene.
		/// </summary>
		/// <remarks>
		/// A foundation with no matching row keeps a plot ID of zero and stays unclaimable. That is
		/// the honest outcome: registration is what creates rows, so a missing one means this
		/// foundation's key never reached the database, and letting it look claimable would only
		/// move the failure to the moment somebody tries to pay.
		/// </remarks>
		private void ApplyResolvedPlots(string sceneName, List<PlotData> plots)
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
			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForScene(sceneName))
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

				foundation.ApplyResolvedState(plot.ID, owner);
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

			DatabaseResult<int> claim = await plotService.TryClaimAsync(plotID, characterID, 0);
			if (!claim.IsSuccess)
			{
				Log.Error("HousingSystem", $"Claim of plot {plotID} for CharID={characterID} errored: {claim.ErrorMessage}");
				return;
			}
			if (claim.Data != 1)
			{
				// Somebody else owns it. Nothing was taken, so there is nothing to undo.
				Log.Debug("HousingSystem", $"CharID={characterID} lost the race for plot {plotID}.");
				return;
			}

			MarkPlotChanged(plotID);

			if (price <= 0)
			{
				ApplyOwnerOnMainThread(foundation, PlotOwner.ForCharacter(characterID));
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
				ApplyOwner(foundation, PlotOwner.ForCharacter(characterID));

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
			DatabaseResult<int> release = await plotService.ReleaseAsync(plotID, characterID, 0);
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
				if (!TryGetDbService(out ICurrencyEscrowService escrowService))
				{
					return;
				}

				DatabaseResult<long> hold = await escrowService.HoldAsync(characterID, amount, (int)CurrencyEscrowReason.LandPurchase);
				if (!hold.IsSuccess || hold.Data <= 0)
				{
					Log.Warning("HousingSystem", $"Currency ledger: could not record {amount} for CharID={characterID}.");
					return;
				}

				DatabaseResult<int> settle = await escrowService.AbsorbAsync(hold.Data);
				if (!settle.IsSuccess || settle.Data != 1)
				{
					Log.Warning("HousingSystem", $"Currency ledger: escrow {hold.Data} left unsettled for CharID={characterID}.");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Currency ledger: async worker rejected the record for CharID={characterID}.");
			}
		}

		/// <summary>
		/// Applies new ownership to a foundation from the main thread.
		/// </summary>
		private void ApplyOwnerOnMainThread(IPlotFoundation foundation, PlotOwner owner)
		{
			if (!TryEnqueueHousingMainThread(() => ApplyOwner(foundation, owner)))
			{
				Log.Warning("HousingSystem", "Could not apply plot ownership; it will correct on the next resolve.");
			}
		}

		/// <summary>
		/// Applies new ownership to a foundation.
		/// </summary>
		private static void ApplyOwner(IPlotFoundation foundation, PlotOwner owner)
		{
			if (foundation is PlotFoundation plotFoundation)
			{
				plotFoundation.ApplyOwner(owner);
			}
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
