using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
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
		/// Moves everything on a plot into its owner's vault, then leaves the land bare.
		/// </summary>
		/// <param name="plotID">The plot being taken back.</param>
		/// <param name="ownerCharacterID">The owner losing it, or zero for guild-owned land.</param>
		/// <remarks>
		/// Replaces the outright demolition reclamation used to do. Guild-owned land still has no
		/// vault to move anything into — there is no guild that owns a container — so it falls back
		/// to clearing, and says so rather than failing quietly.
		/// </remarks>
		public void StoreContentsInVault(long plotID, long ownerCharacterID)
		{
			if (plotID <= 0)
			{
				return;
			}

			if (ownerCharacterID <= 0)
			{
				/* No character means no vault. A guild has no balance to be charged a retrieval fee
				 * from and no inventory to put anything back into, so guild halls are demolished
				 * outright until one exists — logged, because it is a real loss rather than a
				 * no-op. */
				Log.Warning("HousingSystem",
					$"Plot {plotID} was reclaimed from a guild; its structures are demolished rather than vaulted, as guilds have no vault.");
				ClearStructures(plotID);
				return;
			}

			long baseFee = Math.Max(0L, vaultBaseFee);
			float rate = VaultFeeRatePerDay;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					/* Falling back to demolition here would destroy the owner's house precisely
					 * because a service was missing. Leaving the structures standing is the
					 * recoverable failure: the land is already released, so the next owner sees an
					 * unclaimed plot with somebody else's house on it, which is visible, reportable,
					 * and fixable — where deletion is none of those. */
					Log.Error("HousingSystem",
						$"Plot {plotID} was reclaimed but IPlotVaultService is unavailable; its structures were left standing rather than destroyed.");
					return;
				}

				DatabaseResult<int> stored = await vaultService.StorePlotContentsAsync(plotID, ownerCharacterID, baseFee, rate);
				if (!stored.IsSuccess)
				{
					Log.Error("HousingSystem",
						$"Could not vault the contents of plot {plotID} for CharID={ownerCharacterID}: {stored.ErrorMessage}");
					return;
				}

				if (stored.Data > 0)
				{
					Log.Debug("HousingSystem",
						$"Vaulted {stored.Data} stack(s) from plot {plotID} for CharID={ownerCharacterID}.");
				}
			}, ownerCharacterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the vault move for plot {plotID}.");
			}
		}

		/// <summary>
		/// Reads back everything a character is owed, with today's fee against each entry.
		/// </summary>
		/// <remarks>
		/// The quote is computed here, from the row, with the same arithmetic the charge uses. A UI
		/// that worked the fee out for itself would drift from what is actually taken the moment
		/// either side was changed, and the player would see the game charge more than it said.
		/// </remarks>
		public void FetchVault(IPlayerCharacter player, Action<List<PlotVaultData>, List<long>> onFetched)
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
					return;
				}

				DatabaseResult<List<PlotVaultData>> entries = await vaultService.FetchByCharacterAsync(characterID);
				if (!entries.IsSuccess || entries.Data == null)
				{
					Log.Error("HousingSystem", $"Could not read the vault for CharID={characterID}: {entries.ErrorMessage}");
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
			}
		}

		/// <summary>
		/// Buys one stored stack back out of the vault.
		/// </summary>
		/// <remarks>
		/// The money goes first here, which is the opposite of how a plot is claimed — and for the
		/// same underlying reason. A plot is contended, so the contended step goes first and losing
		/// costs nothing. A vault row is contended by nobody but its owner: the only race is the
		/// player clicking twice, and the removal is what settles that. So the order is charge,
		/// then remove, then hand over.
		///
		/// <para>The failure that order admits is being charged for a row that had already gone,
		/// which is why the removal's row count is checked and the fee refunded when it comes back
		/// zero. The opposite order admits losing the furniture for free, which cannot be undone at
		/// all.</para>
		/// </remarks>
		public void RetrieveFromVault(IPlayerCharacter player, long vaultID)
		{
			if (player == null || vaultID <= 0 || !IsHousingEnabled)
			{
				return;
			}

			if (currencyTemplate == null)
			{
				Log.Error("HousingSystem", "Vault retrieval refused: currencyTemplate is not assigned, so nothing has a price.");
				return;
			}

			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					return;
				}

				DatabaseResult<PlotVaultData?> found = await vaultService.FetchEntryAsync(vaultID, characterID);
				if (!found.IsSuccess || !found.Data.HasValue)
				{
					// Somebody else's row, or one already taken. Both are "there is nothing here".
					return;
				}

				PlotVaultData entry = found.Data.Value;
				long fee = PlotVaultFee.Calculate(entry.BaseFee, entry.StoredAtUtc, DateTime.UtcNow, entry.FeeRatePerDay);

				if (!TryEnqueueHousingMainThread(() => CompleteVaultRetrieval(player, entry, fee)))
				{
					Log.Warning("HousingSystem", $"Could not schedule the vault retrieval for CharID={characterID}.");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the vault retrieval for CharID={characterID}.");
			}
		}

		/// <summary>
		/// Takes the fee and then the row, giving the fee back if the row had already gone.
		/// </summary>
		private void CompleteVaultRetrieval(IPlayerCharacter player, PlotVaultData entry, long fee)
		{
			/* Unity's null is the point of the second check. A character that despawned during the
			 * round trip leaves a destroyed component behind, and the interface reference to it does
			 * not compare equal to null — but its transform does. Charging a corpse would take
			 * nothing and then remove the vault row anyway, which is the one outcome the whole
			 * charge-then-remove ordering exists to avoid. */
			if (player == null || player.Transform == null)
			{
				return;
			}

			if (fee > 0 && !CharacterCurrency.TrySpend(player, currencyTemplate, fee, () => TryPersistCurrency(player)))
			{
				return;
			}

			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotVaultService vaultService))
				{
					RefundVaultFee(player, fee);
					return;
				}

				DatabaseResult<int> removed = await vaultService.TryRemoveEntryAsync(entry.ID, characterID);
				if (!removed.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not remove vault entry {entry.ID} for CharID={characterID}: {removed.ErrorMessage}");
					RefundVaultFee(player, fee);
					return;
				}

				if (removed.Data != 1)
				{
					/* The row went between the quote and the take — a second click, or another
					 * session. Nothing was handed over, so the fee goes back. */
					RefundVaultFee(player, fee);
					return;
				}

				if (fee > 0)
				{
					/* Recorded only after the deduction is durable and the row is gone, exactly as
					 * the land purchase is. A ledger entry that precedes what it describes would be
					 * returned by escrow reconciliation and refund money that was correctly taken. */
					RecordVaultFee(characterID, fee);
				}

				Log.Debug("HousingSystem",
					$"CharID={characterID} retrieved {entry.Amount}x template {entry.TemplateID} from the vault for {fee}.");
			}, characterID))
			{
				Log.Error("HousingSystem", $"Vault retrieval for CharID={characterID} was charged but could not be completed; refunding.");
				RefundVaultFee(player, fee);
			}
		}

		/// <summary>
		/// Gives back a retrieval fee that bought nothing.
		/// </summary>
		/// <remarks>
		/// Marshalled back to the main thread because it touches in-memory attributes. Failing to
		/// schedule it is logged as an error rather than a warning: the player has paid for
		/// something they did not receive, and unlike most of what goes wrong here that is not
		/// self-correcting on the next sweep.
		/// </remarks>
		private void RefundVaultFee(IPlayerCharacter player, long fee)
		{
			if (player == null || player.Transform == null || fee <= 0 || currencyTemplate == null)
			{
				return;
			}

			if (!TryEnqueueHousingMainThread(() =>
			{
				/* Granted first, then persisted — the same order TrySpend uses, and for the same
				 * reason: persistence snapshots the in-memory attributes as they stand, so a write
				 * scheduled before the grant would store the balance without the refund in it. */
				if (CharacterCurrency.TryAdd(player, currencyTemplate, fee))
				{
					TryPersistCurrency(player);
				}
			}))
			{
				Log.Error("HousingSystem", $"Could not refund {fee} to CharID={player.ID} for a vault retrieval that did not complete.");
			}
		}

		/// <summary>
		/// Gives up one stored stack permanently, for nothing.
		/// </summary>
		/// <remarks>
		/// Offered because the fee grows without limit, and a player who does not want a thing back
		/// should not be left with a row that gets more expensive forever. Nothing is charged and
		/// nothing is returned.
		/// </remarks>
		public void ForfeitFromVault(IPlayerCharacter player, long vaultID)
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
					return;
				}

				DatabaseResult<int> removed = await vaultService.TryRemoveEntryAsync(vaultID, characterID);
				if (!removed.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not forfeit vault entry {vaultID} for CharID={characterID}: {removed.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the vault forfeit for CharID={characterID}.");
			}
		}

		/// <summary>
		/// Records a paid vault retrieval fee in the currency ledger.
		/// </summary>
		private void RecordVaultFee(long characterID, long amount)
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
					(int)CurrencyMovementReason.HouseVaultFee,
					(int)CurrencyMovementState.Absorbed);

				if (!record.IsSuccess)
				{
					Log.Warning("HousingSystem",
						$"Currency ledger: could not record {amount} (house vault fee) for CharID={characterID}. {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Currency ledger: async worker rejected the vault fee record for CharID={characterID}.");
			}
		}
	}
}
