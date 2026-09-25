using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
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
		private async Task<bool> TryReleaseIntoVaultAsync(IPlotService plotService, PlotData plot)
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
				(int)PlotState.Abandoned);

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
		/// Buys one stored stack back out of the vault — refused, for now.
		/// </summary>
		/// <remarks>
		/// <para>Refused before anything is read or charged, because there is nowhere yet to put what
		/// would be bought. A vault row is a structure template and a count; structures are not
		/// items, so there is no inventory to hand them to, and a plot's pieces are placed with a
		/// position the row does not keep. This used to charge the fee and delete the row all the
		/// same, and hand over nothing: the player paid to have their own furniture destroyed.</para>
		///
		/// <para>When retrieval opens, the order it needs is the one that was here: charge, then
		/// remove the row (the removal's row count is what settles a double click, and a fee taken
		/// for a row already gone is refunded), then hand over — and only record the fee in the
		/// ledger after the deduction. The hand-over is the step that has to exist first.</para>
		/// </remarks>
		public void RetrieveFromVault(NetworkConnection conn, IPlayerCharacter player, long vaultID)
		{
			if (player == null || vaultID <= 0 || !IsHousingEnabled)
			{
				return;
			}

			Log.Debug("HousingSystem", $"CharID={player.ID} asked to retrieve vault entry {vaultID}; retrieval is not open yet.");
			SendHousingResult(conn, 0, HousingResult.Failed);
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
