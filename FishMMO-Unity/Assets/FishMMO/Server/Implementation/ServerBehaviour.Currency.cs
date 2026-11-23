using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Currency bookkeeping every system that takes or pays currency shares: the immediate attribute
	/// save a spend is conditioned on, and the economy ledger row written once it has happened.
	/// </summary>
	/// <remarks>
	/// These were the merchant's (InteractableSystem.Merchant) and were borrowed by ability crafting
	/// and corpse looting; with merchants and loot in the Commerce module and crafting in the core,
	/// they live on the base so neither copies the other.
	/// </remarks>
	public abstract partial class ServerBehaviour
	{
		/// <summary>The log source the helpers below report under: the system's own type name.</summary>
		private string LogSource => GetType().Name;

		/// <summary>
		/// Appends a completed currency movement to the economy ledger.
		/// </summary>
		/// <remarks>
		/// Call this only once the movement has actually happened AND its balance change has been
		/// persisted. The row carries its outcome, so nothing revisits it and nothing infers a
		/// balance from it — which is what keeps a lost row a reporting gap rather than an
		/// economic one.
		///
		/// <para>This is NOT an escrow and must not be reasoned about as one. Writing the row
		/// before the deduction is durable, or writing it without the outcome, would recreate the
		/// window where a later pass has to guess whether currency was taken. There is no such
		/// pass, deliberately: the two guesses available to it are paying out money that was never
		/// taken and refunding a purchase that completed, and both create currency.</para>
		///
		/// <para>Recording is fire-and-forget and nothing waits on it. A player never pays latency
		/// for bookkeeping.</para>
		/// </remarks>
		/// <param name="characterID">The character whose balance moved.</param>
		/// <param name="amount">Amount moved. Non-positive amounts are not recorded.</param>
		/// <param name="reason">Which sink the movement belongs to.</param>
		/// <param name="absorbed">True when the currency left the economy, false when it was given back.</param>
		protected void RecordCurrencyMovement(long characterID, long amount, CurrencyMovementReason reason, bool absorbed)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			CurrencyMovementState state = absorbed
				? CurrencyMovementState.Absorbed
				: CurrencyMovementState.Returned;

			if (!EnqueuePersistence(async () =>
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICurrencyLedgerService>(out ICurrencyLedgerService ledgerService))
				{
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)reason, (int)state);
				if (!record.IsSuccess)
				{
					await Log.Warning(LogSource, $"Currency ledger: could not record {amount} ({reason}/{state}) for CharID={characterID}: [{record.ErrorCode}] {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning(LogSource, $"Currency ledger: the persistence queue is saturated; the record for CharID={characterID} is still written, but late (behind the backlog, or through the teardown fallback).");
			}
		}

		/// <summary>
		/// Enqueues character attribute data (including currency) persistence for a spend or a payout.
		/// Called BEFORE in-memory deduction to ensure the DB reflects the change even if the
		/// server crashes before the in-memory state is updated.
		/// </summary>
		/// <remarks>
		/// The rows quote the session claim held for the character now, captured here, and land only
		/// while it is still held (the service's <c>PersistOwnedAsync</c>). With no claim nothing is
		/// queued and false is returned — the answer every caller already has for a persist that
		/// could not be queued (<c>CharacterCurrency.TrySpend</c> refunds). A refused write clears no
		/// dirty mark: this path clears none at all, so the periodic save carries the values again.
		/// </remarks>
		/// <returns>True if the persist was successfully enqueued, false otherwise.</returns>
		protected bool TryPersistCharacterAttributes(IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}

			long charID = character.ID;
			if (!TryCaptureSessionClaim(charID, out CharacterSessionLeaseData claim))
			{
				Log.Warning(LogSource, $"TryPersistCharacterAttributes: this server holds no session claim for CharID={charID}; nothing was queued.");
				return false;
			}

			var dtos = new List<CharacterAttributeData>();
			/* Version++ AND MarkPersistPending, together — the pair is what makes the dirty flag
			 * work. A bump alone moves the attribute past the version a periodic save in flight
			 * recorded, so that save's confirmation clears a mark it no longer owns. */
			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				kvp.Value.MarkPersistPending(kvp.Value.Version);
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attributeController.ResourceAttributes)
			{
				kvp.Value.Version++;
				kvp.Value.MarkPersistPending(kvp.Value.Version);
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue
				));
			}

			return dtos.Count > 0 &&
				EnqueuePersistence(() => PersistCharacterAttributesToDbAsync(dtos, charID, claim), charID);
		}

		/// <summary>
		/// Asynchronously persists the attribute rows <see cref="TryPersistCharacterAttributes"/> captured.
		/// </summary>
		private async Task PersistCharacterAttributesToDbAsync(List<CharacterAttributeData> dtos, long charID, CharacterSessionLeaseData claim)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var service))
				{
					await Log.Error(LogSource, "PersistCharacterAttributesToDbAsync: Failed to resolve ICharacterAttributeService");
					return;
				}

				await BulkWriteReporting.ReportAsync(LogSource, "Currency attribute save",
					await service.PersistOwnedAsync(dtos, ClaimsOf(claim)), $"CharID={charID}");
			}
			catch (Exception ex)
			{
				await Log.Error(LogSource, $"PersistCharacterAttributesToDbAsync failed (CharID={charID}): {ex}");
			}
		}
	}
}
