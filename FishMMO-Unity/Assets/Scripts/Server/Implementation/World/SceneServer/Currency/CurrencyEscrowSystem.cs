using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Returns currency holds that were never settled.
	/// </summary>
	/// <remarks>
	/// A hold is taken before a balance is deducted and settled once the transaction completes.
	/// If the process dies in between, the row is left <see cref="CurrencyEscrowState.Held"/> with
	/// nothing running that will ever settle it. This sweeps those at startup and gives the money
	/// back.
	///
	/// <para>Startup is the right moment because the owners are offline: the credit is applied
	/// straight to the persisted attribute, which is only safe while nothing else is mutating that
	/// character's balance.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "CurrencyEscrowSystem", menuName = "FishMMO/Server/SceneServer/Currency Escrow System", order = 1)]
	public class CurrencyEscrowSystem : ServerBehaviour, ICurrencyEscrowSystem
	{
		/// <summary>
		/// The currency attribute holds are denominated in.
		/// </summary>
		[Header("Currency")]
		[Tooltip("The attribute template currency is stored in. Must match the one the interactable systems charge against.")]
		[SerializeField]
		private CharacterAttributeTemplate currencyTemplate;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CurrencyEscrowSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			if (currencyTemplate == null)
			{
				Log.Error("CurrencyEscrowSystem", "InitializeOnce: currencyTemplate is not assigned; held currency cannot be returned.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!TryEnqueueAsyncWork(ReconcileHeldCurrencyAsync))
			{
				Log.Warning("CurrencyEscrowSystem", "InitializeOnce: could not enqueue escrow reconciliation; held currency stays held until the next start.");
			}

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
		}

		/// <summary>
		/// Returns every hold still outstanding.
		/// </summary>
		/// <remarks>
		/// Each hold is <em>claimed</em> before it is credited, not after.
		///
		/// <para>The two steps cannot be made atomic — the settlement lives in one table and the
		/// balance in another — so one of them has to go first, and the choice is between the two
		/// ways it can fail. Crediting first means a crash before the row is marked returns the
		/// same hold again on the next start, paying it out twice. Claiming first means a crash
		/// after the mark loses that hold. Duplication is the worse outcome: it creates currency
		/// from nothing and compounds, where a loss is bounded, visible in the row, and
		/// recoverable by hand.</para>
		///
		/// <para>The claim is what makes it safe. <c>ReturnAsync</c> only transitions a row that is
		/// still Held, so exactly one caller can ever claim a given hold; anything that reports no
		/// affected rows has lost the race and must not pay out.</para>
		/// </remarks>
		private async Task ReconcileHeldCurrencyAsync()
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ICurrencyEscrowService>(out ICurrencyEscrowService escrowService) ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out ICharacterAttributeService attributeService))
			{
				Log.Error("CurrencyEscrowSystem", "Reconciliation skipped: escrow or attribute service unavailable.");
				return;
			}

			DatabaseResult<List<CurrencyEscrowData>> heldResult = await escrowService.FetchHeldAsync();
			if (!heldResult.IsSuccess || heldResult.Data == null)
			{
				Log.Error("CurrencyEscrowSystem", $"Reconciliation skipped: could not read held currency. {heldResult.ErrorMessage}");
				return;
			}

			if (heldResult.Data.Count < 1)
			{
				Log.Debug("CurrencyEscrowSystem", "No held currency to reconcile.");
				return;
			}

			Log.Warning("CurrencyEscrowSystem",
				$"Reconciling {heldResult.Data.Count} unsettled currency hold(s) — each is an interrupted transaction.");

			int returned = 0;
			foreach (CurrencyEscrowData held in heldResult.Data)
			{
				// Claim first. Anything other than exactly one affected row means someone else has it.
				DatabaseResult<int> claim = await escrowService.ReturnAsync(held.ID);
				if (!claim.IsSuccess || claim.Data != 1)
				{
					continue;
				}

				if (await TryCreditPersistedCurrencyAsync(attributeService, held.CharacterID, held.Amount))
				{
					++returned;
				}
				else
				{
					/* The row is already marked Returned, so this hold will not be swept again.
					 * Logged at Error because it is the failure the ordering above accepts: the
					 * money is owed and the row records exactly how much, to whom, and why. */
					Log.Error("CurrencyEscrowSystem",
						$"Escrow {held.ID}: claimed but could not credit {held.Amount} to CharID={held.CharacterID}; owed and recorded.");
				}
			}

			Log.Debug("CurrencyEscrowSystem", $"Escrow reconciliation complete: {returned} hold(s) returned.");
		}

		/// <summary>
		/// Adds an amount to a character's persisted currency attribute.
		/// </summary>
		/// <remarks>
		/// Writes the stored attribute directly rather than going through
		/// <see cref="CharacterCurrency"/>, because the owner is offline and has no in-memory
		/// controller to read.
		/// </remarks>
		private async Task<bool> TryCreditPersistedCurrencyAsync(ICharacterAttributeService attributeService, long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return false;
			}

			DatabaseResult<IReadOnlyList<CharacterAttributeData>> attributesResult = await attributeService.FetchAsync(characterID);
			if (!attributesResult.IsSuccess || attributesResult.Data == null)
			{
				return false;
			}

			int templateID = currencyTemplate.ID;
			foreach (CharacterAttributeData attribute in attributesResult.Data)
			{
				if (attribute.TemplateID != templateID)
				{
					continue;
				}

				/* Clamped to what the column holds. The attribute stores an int, and wrapping it
				 * would turn a refund into currency destruction or duplication. */
				long credited = (long)attribute.Value + amount;
				int newValue = credited > int.MaxValue ? int.MaxValue : (int)credited;

				CharacterAttributeData updated = new CharacterAttributeData(
					attribute.ID,
					attribute.Version,
					attribute.CharacterID,
					attribute.TemplateID,
					newValue,
					attribute.CurrentValue);

				DatabaseResult<BulkWriteResult> persistResult = await attributeService.PersistAsync(new List<CharacterAttributeData> { updated });
				return persistResult.IsSuccess;
			}

			Log.Error("CurrencyEscrowSystem",
				$"CharID={characterID} has no currency attribute row; {amount} cannot be returned.");
			return false;
		}
	}
}
