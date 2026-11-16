using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Where a house goes when its owner loses the land under it.
	/// </summary>
	/// <remarks>
	/// Reclaiming a plot destroys something a player built and paid for. Doing that with no way back
	/// would make one missed payment the most punishing event in the game — worse than dying, worse
	/// than being robbed — and would make going on holiday a risk. The vault is the answer: what
	/// stood on the plot is moved into it rather than deleted, and the owner may buy it back or let
	/// it go.
	///
	/// <para>The fee is what stops the vault being free storage. It grows with time held, so it is
	/// cheapest to collect your things promptly and increasingly expensive to treat the vault as a
	/// warehouse — and the money leaves the economy, which is the other half of what land tax is
	/// for.</para>
	///
	/// <para>Buying back is not open yet. A vault row is a structure template and a count, and there
	/// is nowhere yet to put one: structures are not items, and a plot's pieces are placed, not
	/// carried. Retrieval used to charge the fee and delete the row anyway, handing over nothing.
	/// It is refused until there is somewhere for the pieces to go; the fee is still quoted, and
	/// forfeiting still works.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>
		/// What retrieving one stored stack costs before any time has passed.
		/// </summary>
		[Header("House vault")]
		[Tooltip("Base retrieval fee per stored stack, charged the moment it is stored. Zero makes retrieval free.")]
		[SerializeField]
		private long vaultBaseFee = 100;

		/// <summary>
		/// How much of the base fee is added per day held, as a percentage.
		/// </summary>
		[Tooltip("Percent of the base fee added per day stored. 10 means a stack costs double after ten days.")]
		[SerializeField]
		private float vaultFeePercentPerDay = 10f;

		/// <summary>
		/// The fee rate stored on new vault rows, as a fraction rather than a percentage.
		/// </summary>
		/// <remarks>
		/// Converted once, here, and then frozen onto each row. Rows carry their own rate so a
		/// rebalance cannot change what a player owes on something already in their vault: they were
		/// quoted a figure when their house came down, and that is the figure they pay.
		/// </remarks>
		private float VaultFeeRatePerDay => Mathf.Max(0f, vaultFeePercentPerDay) * 0.01f;

		/// <summary>
		/// Takes an unpaid plot back from its owner, moving what stood on it into their vault and
		/// clearing their guest list — all in one transaction.
		/// </summary>
		/// <param name="plotService">The plot service the sweep is already using.</param>
		/// <param name="plot">The plot as the sweep read it; the release is pinned to its owner.</param>
		/// <param name="releasedState">
		/// What the land becomes: <see cref="PlotState.Abandoned"/> for a reclamation, or for an owner
		/// giving up a plot with something on it; <see cref="PlotState.Empty"/> for one given up bare.
		/// </param>
		/// <returns>True when this call took the plot back.</returns>
		/// <remarks>
		/// <para>One transaction, because the three writes must not be able to land apart. This used
		/// to release the land and then queue the vault move separately. When the move failed, or had
		/// simply not run yet, the house stood on land anybody could claim — and the claim clears
		/// whatever it finds standing, so the owner's house was demolished outright with nothing put
		/// in their vault. Now the land is free only once the house is in the vault and the keys are
		/// gone; if either write fails the release is undone with it and the plot stays delinquent,
		/// for the next sweep to try again.</para>
		///
		/// <para>Guild-owned land still has no vault to move anything into — there is no guild that
		/// owns a container — so its structures are demolished inside the same transaction, and that
		/// is said out loud rather than done quietly.</para>
		/// </remarks>
		private async Task<bool> TryReleaseIntoVaultAsync(IPlotService plotService, PlotData plot, PlotState releasedState = PlotState.Abandoned)
		{
			if (!TryGetDbService(out IUnitOfWorkService unitOfWorkService) ||
				!TryGetDbService(out IPlotVaultService vaultService) ||
				!TryGetDbService(out IPlotAccessService accessService) ||
				!TryGetDbService(out IPlotStructureService structureService))
			{
				/* Nothing is released. The owner keeps a plot they have stopped paying for a little
				 * longer, which is recoverable; land released without its house going anywhere is
				 * not. */
				Log.Error("HousingSystem", $"Plot {plot.ID} is due for reclamation but a housing database service is unavailable; it was left with its owner.");
				return false;
			}

			DatabaseResult<IUnitOfWork> begin = await unitOfWorkService.BeginAsync();
			if (!begin.IsSuccess || begin.Data == null)
			{
				Log.Warning("HousingSystem", $"Reclaiming plot {plot.ID}: could not begin a unit of work: [{begin.ErrorCode}] {begin.ErrorMessage}");
				return false;
			}

			await using IUnitOfWork unitOfWork = begin.Data;

			/* Abandoned, not Empty. The two are both unowned, and the difference is the whole reason
			 * the state column exists: a lot nobody has ever claimed is bare ground, while this is a
			 * house somebody lost. A passer-by should be told which they are looking at, and a
			 * channel loading the scene should draw them differently. */
			DatabaseResult<int> released = await plotService.ReleaseAsync(
				plot.ID,
				plot.OwnerCharacterID,
				plot.OwnerGuildID,
				(int)releasedState);

			if (!released.IsSuccess)
			{
				await unitOfWork.RollbackAsync();
				Log.Warning("HousingSystem", $"Could not reclaim plot {plot.ID}; the next sweep tries again: [{released.ErrorCode}] {released.ErrorMessage}");
				return false;
			}
			if (released.Data != 1)
			{
				// Sold or given up since the sweep read it; not ours to take.
				await unitOfWork.RollbackAsync();
				return false;
			}

			if (plot.OwnerCharacterID > 0)
			{
				/* Vaulted rather than demolished, so a missed payment costs the owner their land and
				 * a retrieval fee rather than everything they built. The rate is frozen onto each
				 * row here, so a later rebalance cannot change what they owe. */
				DatabaseResult<int> stored = await vaultService.StorePlotContentsAsync(plot.ID, plot.OwnerCharacterID, Math.Max(0L, vaultBaseFee), VaultFeeRatePerDay);
				if (!stored.IsSuccess)
				{
					await unitOfWork.RollbackAsync();
					Log.Error("HousingSystem",
						$"Could not vault the contents of plot {plot.ID} for CharID={plot.OwnerCharacterID}, so it was not reclaimed; the next sweep tries again: [{stored.ErrorCode}] {stored.ErrorMessage}");
					return false;
				}
				if (stored.Data > 0)
				{
					Log.Debug("HousingSystem", $"Vaulting {stored.Data} stack(s) from plot {plot.ID} for CharID={plot.OwnerCharacterID}.");
				}
			}
			else
			{
				DatabaseResult<int> demolished = await structureService.DemolishAllAsync(plot.ID);
				if (!demolished.IsSuccess)
				{
					await unitOfWork.RollbackAsync();
					Log.Error("HousingSystem",
						$"Could not clear guild plot {plot.ID}, so it was not reclaimed; the next sweep tries again: [{demolished.ErrorCode}] {demolished.ErrorMessage}");
					return false;
				}
				if (demolished.Data > 0)
				{
					/* No character means no vault. A guild has no balance to be charged a retrieval
					 * fee from and no inventory to put anything back into, so guild halls are
					 * demolished outright until one exists — logged, because it is a real loss
					 * rather than a no-op. */
					Log.Warning("HousingSystem",
						$"Plot {plot.ID} is being reclaimed from a guild; its {demolished.Data} structure(s) are demolished rather than vaulted, as guilds have no vault.");
				}
			}

			/* The previous owner's guest list goes with the house. Whoever buys this land next must
			 * not inherit a set of keys they did not cut, and the old owner's friends must not keep
			 * walking into somebody else's home. */
			DatabaseResult<int> revoked = await accessService.RevokeAllAsync(plot.ID);
			if (!revoked.IsSuccess)
			{
				await unitOfWork.RollbackAsync();
				Log.Error("HousingSystem",
					$"Could not clear the guest list of plot {plot.ID}, so it was not reclaimed; the next sweep tries again: [{revoked.ErrorCode}] {revoked.ErrorMessage}");
				return false;
			}

			DatabaseResult commit = await unitOfWork.CommitAsync();
			if (!commit.IsSuccess)
			{
				Log.Error("HousingSystem", $"Could not commit the reclamation of plot {plot.ID}; the next sweep tries again: [{commit.ErrorCode}] {commit.ErrorMessage}");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Reads back everything a character is owed, with today's fee against each entry.
		/// </summary>
		/// <remarks>
		/// The quote is computed here, from the row, with the same arithmetic the charge uses. A UI
		/// that worked the fee out for itself would drift from what is actually taken the moment
		/// either side was changed, and the player would see the game charge more than it said.
		/// </remarks>
		/// <param name="player">Whose vault to read.</param>
		/// <param name="onFetched">Given the entries and their fees, on the main thread.</param>
		/// <param name="onFailed">Called on the main thread when the vault could not be read.</param>
		public void FetchVault(IPlayerCharacter player, Action<List<PlotVaultData>, List<long>> onFetched, Action onFailed)
		{
			if (player == null || onFetched == null || !IsHousingEnabled)
			{
				return;
			}

			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					Log.Error("HousingSystem", $"Could not read the vault for CharID={characterID}: IPlotVaultService unavailable.");
					ReportVaultReadFailed(onFailed, characterID);
					return;
				}

				DatabaseResult<List<PlotVaultData>> entries = await vaultService.FetchByCharacterAsync(characterID);
				if (!entries.IsSuccess || entries.Data == null)
				{
					Log.Error("HousingSystem", $"Could not read the vault for CharID={characterID}: [{entries.ErrorCode}] {entries.ErrorMessage}");
					ReportVaultReadFailed(onFailed, characterID);
					return;
				}

				DateTime now = DateTime.UtcNow;
				List<long> fees = new List<long>(entries.Data.Count);
				foreach (PlotVaultData entry in entries.Data)
				{
					fees.Add(PlotVaultFee.Calculate(entry.BaseFee, entry.StoredAtUtc, now, entry.FeeRatePerDay));
				}

				if (!TryEnqueueHousingMainThread(() => onFetched(entries.Data, fees)))
				{
					Log.Warning("HousingSystem", $"Could not deliver the vault contents for CharID={characterID}.");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the vault read for CharID={characterID}.");
				onFailed?.Invoke();
			}
		}

		/// <summary>
		/// Tells the requester their vault could not be read, rather than leaving them with nothing.
		/// </summary>
		private void ReportVaultReadFailed(Action onFailed, long characterID)
		{
			if (onFailed != null && !TryEnqueueHousingMainThread(onFailed))
			{
				Log.Warning("HousingSystem", $"Could not report the failed vault read to CharID={characterID}.");
			}
		}

		/// <summary>
		/// Buys one stored stack back out of the vault, as structure kits delivered to the bank.
		/// </summary>
		/// <remarks>
		/// <para>A vault row is a structure template and a count, and a structure standing on a plot is
		/// not an item; what is handed over is the structure's <see cref="PlotStructureTemplate.Kit"/>,
		/// an item the building tool places. A structure with no kit cannot be retrieved, and is refused
		/// before anything is charged (it used to charge the fee, delete the row and hand over nothing).</para>
		///
		/// <para>The order is the README's: charge, then remove the row (its row count settles a double
		/// click; a fee taken for a row already gone is refunded), then hand over — and the fee reaches
		/// the ledger only after the hand-over. See <see cref="BeginRetrieve"/>.</para>
		/// </remarks>
		public void RetrieveFromVault(NetworkConnection conn, IPlayerCharacter player, long vaultID)
		{
			if (player == null || vaultID <= 0 || !IsHousingEnabled)
			{
				return;
			}
			if (currencyTemplate == null)
			{
				Log.Error("HousingSystem", "Vault retrieval refused: currencyTemplate is not assigned, so the fee cannot be charged.");
				SendHousingResult(conn, 0, HousingResult.Failed);
				return;
			}

			long characterID = player.ID;
			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					SendHousingResultOnMainThread(conn, 0, HousingResult.Failed);
					return;
				}
				DatabaseResult<PlotVaultData?> read = await vaultService.FetchEntryAsync(vaultID, characterID);
				if (!read.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not read vault entry {vaultID} for CharID={characterID}: [{read.ErrorCode}] {read.ErrorMessage}");
					SendHousingResultOnMainThread(conn, 0, HousingResult.Failed);
					return;
				}
				if (read.Data == null)
				{
					SendHousingResultOnMainThread(conn, 0, HousingResult.NothingStored);
					return;
				}
				PlotVaultData entry = read.Data.Value;
				long fee = PlotVaultFee.Calculate(entry.BaseFee, entry.StoredAtUtc, DateTime.UtcNow, entry.FeeRatePerDay);
				if (!TryEnqueueHousingMainThread(() => BeginRetrieve(conn, player, entry, fee)))
				{
					SendHousingResultOnMainThread(conn, 0, HousingResult.Failed);
				}
			}, characterID))
			{
				SendHousingResult(conn, 0, HousingResult.Failed);
			}
		}

		/// <summary>
		/// Charges the fee, removes the row, hands the kits over — in that order (README, "The orderings
		/// that matter"). Main thread.
		/// </summary>
		/// <remarks>
		/// <para>Room is checked before anything is charged: every stack must fit in the bank, or failing
		/// that in the bags, or nothing happens. The row is the contended step (a double click), so its
		/// removal decides who hands over; a fee taken for a row already gone is refunded.</para>
		///
		/// <para>Kits are only ever handed out. Nothing puts an item back into the vault: the vault is
		/// where a lost house waits, not storage.</para>
		/// </remarks>
		private void BeginRetrieve(NetworkConnection conn, IPlayerCharacter player, PlotVaultData entry, long fee)
		{
			if (player == null || player.ID != entry.CharacterID)
			{
				return;
			}
			PlotStructureTemplate structure = PlotStructureTemplate.Get<PlotStructureTemplate>(entry.TemplateID);
			StructureKitTemplate kit = structure != null ? structure.Kit : null;
			if (kit == null || entry.Amount <= 0)
			{
				SendHousingResult(conn, 0, HousingResult.NotRetrievable);
				return;
			}
			List<uint> stacks = Stacks(kit, entry.Amount);
			InventoryType destination;
			if (FreeSlots(player, InventoryType.Bank) >= stacks.Count)
			{
				destination = InventoryType.Bank;
			}
			else if (FreeSlots(player, InventoryType.Inventory) >= stacks.Count)
			{
				destination = InventoryType.Inventory;
			}
			else
			{
				SendHousingResult(conn, 0, HousingResult.NoRoom);
				return;
			}

			if (fee > 0 && !CharacterCurrency.TrySpend(player, currencyTemplate, fee, () => TryPersistCurrency(player)))
			{
				SendHousingResult(conn, 0, HousingResult.CannotAfford);
				return;
			}

			long characterID = player.ID;
			if (!TryEnqueueAsyncWork(async () =>
			{
				HousingResult refusal = HousingResult.Failed;
				bool removed = false;
				if (TryGetDbService(out IPlotVaultService vaultService))
				{
					DatabaseResult<int> result = await vaultService.TryRemoveEntryAsync(entry.ID, characterID);
					removed = result.IsSuccess && result.Data == 1;
					refusal = result.IsSuccess ? HousingResult.NothingStored : HousingResult.Failed;
				}
				if (!TryEnqueueHousingMainThread(() =>
				{
					if (removed)
					{
						DeliverKits(conn, player, entry, kit, stacks, destination, fee);
					}
					else
					{
						RefundVaultFee(player, fee);
						SendHousingResult(conn, 0, refusal);
					}
				}))
				{
					Log.Error("HousingSystem", $"Vault entry {entry.ID} retrieval for CharID={characterID} could not finish on the main thread (removed: {removed}); the fee of {fee} was not refunded.");
				}
			}, characterID))
			{
				RefundVaultFee(player, fee);
				SendHousingResult(conn, 0, HousingResult.Failed);
			}
		}

		/// <summary>
		/// Hands the kits over. Whatever cannot be placed (the bank and bags filled in the moment since
		/// the room was checked) goes back into the vault as it was; the fee is refunded only when
		/// nothing at all was handed over.
		/// </summary>
		private void DeliverKits(NetworkConnection conn, IPlayerCharacter player, PlotVaultData entry, StructureKitTemplate kit, List<uint> stacks, InventoryType destination, long fee)
		{
			int delivered = 0;
			if (player != null && Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				InventoryType other = destination == InventoryType.Bank ? InventoryType.Inventory : InventoryType.Bank;
				foreach (uint amount in stacks)
				{
					if (!inventorySystem.TryGrantItem(player, new Item(kit, amount), destination) &&
						!inventorySystem.TryGrantItem(player, new Item(kit, amount), other))
					{
						break;
					}
					delivered += (int)amount;
				}
			}

			int left = entry.Amount - delivered;
			if (left > 0)
			{
				EnqueuePersistence(async () =>
				{
					if (!TryGetDbService(out IPlotVaultService vaultService))
					{
						Log.Error("HousingSystem", $"{left} undelivered kit(s) of vault entry {entry.ID} for CharID={entry.CharacterID} could not be put back: IPlotVaultService unavailable.");
						return;
					}
					DatabaseResult<int> restored = await vaultService.RestoreEntryAsync(entry.CharacterID, entry.TemplateID, left, entry.OriginalPlotID, entry.StoredAtUtc, entry.BaseFee, entry.FeeRatePerDay);
					if (!restored.IsSuccess)
					{
						Log.Error("HousingSystem", $"{left} undelivered kit(s) of vault entry {entry.ID} for CharID={entry.CharacterID} could not be put back: [{restored.ErrorCode}] {restored.ErrorMessage}");
					}
				}, entry.CharacterID);
			}

			if (delivered == 0)
			{
				RefundVaultFee(player, fee);
				SendHousingResult(conn, 0, HousingResult.NoRoom);
				return;
			}

			RecordVaultFee(entry.CharacterID, fee);
			SendHousingResult(conn, 0, left > 0 ? HousingResult.NoRoom : HousingResult.Success);
			if (player != null)
			{
				FetchVault(player, (entries, fees) => SendVault(conn, entries, fees), null);
			}
		}

		/// <summary>The stacks an amount of kits comes in, each no bigger than the kit stacks.</summary>
		private static List<uint> Stacks(StructureKitTemplate kit, int amount)
		{
			uint size = Math.Max(1u, kit.MaxStackSize);
			var stacks = new List<uint>();
			for (uint left = (uint)Math.Max(0, amount); left > 0;)
			{
				uint take = Math.Min(size, left);
				stacks.Add(take);
				left -= take;
			}
			return stacks;
		}

		private static int FreeSlots(IPlayerCharacter player, InventoryType container)
		{
			switch (container)
			{
				case InventoryType.Bank:
					return player.TryGet(out IBankController bank) ? bank.FreeSlots() : 0;
				case InventoryType.Inventory:
					return player.TryGet(out IInventoryController inventory) ? inventory.FreeSlots() : 0;
				default:
					return 0;
			}
		}

		private void RefundVaultFee(IPlayerCharacter player, long fee)
		{
			if (player == null || fee <= 0)
			{
				return;
			}
			CharacterCurrency.TryAdd(player, currencyTemplate, fee);
			if (!TryPersistCurrency(player))
			{
				Log.Error("HousingSystem", $"The refunded vault fee of {fee} for CharID={player.ID} could not be persisted; the in-memory balance is right, the database holds the charge.");
			}
		}

		private void RecordVaultFee(long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					Log.Warning("HousingSystem", $"Currency ledger: could not record {amount} (vault fee) for CharID={characterID}: ICurrencyLedgerService unavailable.");
					return;
				}
				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)CurrencyMovementReason.HouseVaultFee, (int)CurrencyMovementState.Absorbed);
				if (!record.IsSuccess)
				{
					Log.Warning("HousingSystem", $"Currency ledger: could not record {amount} (vault fee) for CharID={characterID}: [{record.ErrorCode}] {record.ErrorMessage}");
				}
			}, characterID);
		}

		/// <summary>
		/// Gives up one stored stack permanently, for nothing.
		/// </summary>
		/// <remarks>
		/// Offered because the fee grows without limit, and a player who does not want a thing back
		/// should not be left with a row that gets more expensive forever. Nothing is charged and
		/// nothing is returned.
		/// </remarks>
		public void ForfeitFromVault(NetworkConnection conn, IPlayerCharacter player, long vaultID)
		{
			if (player == null || vaultID <= 0 || !IsHousingEnabled)
			{
				return;
			}

			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					Log.Error("HousingSystem", $"Could not forfeit vault entry {vaultID} for CharID={characterID}: IPlotVaultService unavailable.");
					SendHousingResultOnMainThread(conn, 0, HousingResult.Failed);
					return;
				}

				DatabaseResult<int> removed = await vaultService.TryRemoveEntryAsync(vaultID, characterID);
				if (!removed.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not forfeit vault entry {vaultID} for CharID={characterID}: [{removed.ErrorCode}] {removed.ErrorMessage}");
					SendHousingResultOnMainThread(conn, 0, HousingResult.Failed);
					return;
				}

				/* Pinned to the owner, so zero is somebody else's row or one already given up — both
				 * of which are "there is nothing here". */
				SendHousingResultOnMainThread(conn, 0, removed.Data == 1 ? HousingResult.Success : HousingResult.NothingStored);
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the vault forfeit for CharID={characterID}.");
				SendHousingResult(conn, 0, HousingResult.Failed);
			}
		}
	}
}
