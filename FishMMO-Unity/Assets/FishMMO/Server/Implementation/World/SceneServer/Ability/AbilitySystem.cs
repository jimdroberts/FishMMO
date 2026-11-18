using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Owns the two halves of a crafted ability's life: granting one, and forgetting one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a system rather than a partial of whoever asked.</b> Three paths hand out abilities —
	/// the ability crafter, the merchant selling a premade, and (in principle) anything a content
	/// author adds next — and all three had their own copy of "construct, learn, persist". Only the
	/// item layer had worked out what the copies were missing, which is why
	/// <c>InteractableSystem.SendNewItemBroadcast</c> was emptied into
	/// <c>CharacterInventorySystem.TryGrantItem</c> and its doc says so. This is the same move for
	/// abilities, and it is the fix for the same class of bug.
	/// </para>
	/// <para>
	/// <b>The bug.</b> <c>Ability(template, events)</c> sets <c>ID = -1</c> by design — its own doc
	/// comment says the database assigns the real id — and nothing ever put that id back.
	/// <c>PersistAbilityAsync</c> read <c>IsSuccess</c> and threw away the <c>long</c> the upsert
	/// returned, so <c>AbilityController.LearnAbility</c> filed every crafted ability under
	/// <c>-1</c>: crafting a second ability from a different template silently replaced the first,
	/// and because <c>HotkeyData.UnsetReferenceID</c> is also <c>-1</c> a hotkey bound to either of
	/// them validated against the sentinel. The identity is applied here, before the learn, so the
	/// window in which a crafted ability has no key does not exist.
	/// </para>
	/// <para>
	/// <b>Why the learn is deferred rather than corrected afterwards.</b> The item layer can hand an
	/// item a provisional identity and correct it on the wire because an item lives in a slot: it can
	/// be locked while unidentified, and the slot re-sent
	/// (<c>CharacterInventorySystem.ApplyAssignedIdentities</c>). An ability has no slot — the
	/// instance id <i>is</i> its key on the server, in the client's known-ability table, in
	/// <c>AbilityActivatedBroadcast</c>, and in every persisted hotkey row. Deferring the grant
	/// costs one database round trip inside a request that already waits for its result broadcast,
	/// and removes the window entirely.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "AbilitySystem", menuName = "FishMMO/Server/SceneServer/Ability System", order = 2)]
	[RequiresDataContainer(typeof(AbilitySystemRuntimeData))]
	[RequiresDataContainer(typeof(AbilitySystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class AbilitySystem : ServerBehaviour, IAbilitySystem
	{
		/// <summary>
		/// Debounce window in milliseconds for ability ingress requests.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between ability requests per connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 75;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale guard entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Maximum stale guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Maximum number of grant completions applied per frame.
		/// </summary>
		[Header("Main Thread Queue")]
		[Tooltip("Maximum database-minted ability identities applied per frame")]
		[SerializeField] private int maxGrantCompletionsPerFrame = 64;

		/// <summary>
		/// Operation codes used by ability ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			Forget = 1,
		}

		/// <summary>
		/// Everything one admitted grant needs in order to be finished later.
		/// </summary>
		/// <remarks>
		/// A class rather than a closure per parameter, because the request crosses a thread
		/// boundary twice and a named carrier is what keeps the main-thread completion from
		/// closing over a worker's locals.
		/// </remarks>
		private sealed class GrantRequest
		{
			/// <summary>Character receiving the ability.</summary>
			public long CharacterID;
			/// <summary>The ability instance, already constructed but not yet learned.</summary>
			public Ability Ability;
			/// <summary>The version the row is written at, and the one the ability carries once it lands.</summary>
			public long Version;
			/// <summary>The row to write.</summary>
			public CharacterAbilityData AbilityData;
			/// <summary>
			/// The session claim the grant was requested under. The row is written only while it is
			/// still held, and a revoke of the row quotes it too — see
			/// <see cref="ServerBehaviour.TryCaptureSessionClaim"/>.
			/// </summary>
			public CharacterSessionLeaseData Claim;
			/// <summary>Crafted event template ids for the wire, or null when none were chosen.</summary>
			public int[] CraftedEvents;
			/// <summary>
			/// Settles the caller's side of the grant — the charge — on the main thread, once the row
			/// exists and the character is resident. Null when there is nothing to settle.
			/// </summary>
			/// <remarks>
			/// A false return means the caller has already answered the player and the row is to be
			/// revoked: nothing was learned, nothing was announced, nothing was paid.
			/// </remarks>
			public Func<IPlayerCharacter, bool> Settle;
			/// <summary>Releases the caller's in-flight guard. Invoked exactly once, on completion.</summary>
			public Action ReleaseGuard;
			/// <summary>Runs on the main thread once the ability is settled, learned and broadcast.</summary>
			public Action<IPlayerCharacter, Ability> OnGranted;
			/// <summary>Runs on the main thread when the ability could not be recorded.</summary>
			public Action<IPlayerCharacter> OnFailed;
		}

		/// <summary>
		/// Initializes the ability system, registering the forget request handler.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("AbilitySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IAbilitySystemRuntimeData>(out _))
			{
				Log.Error("AbilitySystem", "InitializeOnce: IAbilitySystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			Server.NetworkWrapper.RegisterBroadcast<AbilityForgetBroadcast>(OnServerAbilityForgetBroadcastReceived, true);

			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Log.Debug("AbilitySystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the ability system, unregistering broadcast handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("AbilitySystem", "OnDeinitialize: Server is null");
				return;
			}

			Server.NetworkWrapper.UnregisterBroadcast<AbilityForgetBroadcast>(OnServerAbilityForgetBroadcastReceived);

			if (Server.DataContainerRegistry.TryGet<IAbilitySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Drains stale ingress entries with bounded cleanup each frame, and applies database-minted
		/// ability identities that arrived during the frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<IAbilitySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}

			// The granting half of a crafted ability. The write happens on a worker and every
			// structure it touches — the Ability, the ability controller, the broadcasts — is
			// main-thread only.
			DrainMainThreadQueue<IAbilitySystemMainThreadQueueData>(maxGrantCompletionsPerFrame, drainAll: false);
		}

		#region Granting

		/// <inheritdoc />
		public bool TryGrantAbility(
			IPlayerCharacter character,
			AbilityTemplate template,
			IReadOnlyList<int> events,
			Func<IPlayerCharacter, bool> settle,
			Action releaseGuard,
			Action<IPlayerCharacter, Ability> onGranted,
			Action<IPlayerCharacter> onFailed)
		{
			if (character == null ||
				template == null ||
				character.ID <= 0 ||
				!character.TryGet(out IAbilityController _))
			{
				releaseGuard?.Invoke();
				return false;
			}

			/* A server with no ability service refuses the grant rather than granting without a
			 * write-back. The write-back IS the fix this system exists for: an ability granted into a
			 * process that cannot record it is keyed on the constructor's -1 for the rest of the
			 * session and replaces whichever crafted ability preceded it. The item layer refuses on
			 * exactly this reasoning — see the doc on InteractableSystem.SendNewItemBroadcast. */
			if (!TryGetDbService<ICharacterAbilityService>(out _))
			{
				Log.Error("AbilitySystem",
					$"TryGrantAbility: ICharacterAbilityService is not registered; refusing the grant of template {template.ID} to CharID={character.ID}.");
				releaseGuard?.Invoke();
				return false;
			}

			/* Null and empty mean the same thing on the wire — the serializer writes a length prefix
			 * either way and the reader returns an empty array for a length of zero — so a request
			 * that named no events is normalised here rather than carried as a null that the row
			 * builder would have to special-case. */
			List<int> craftedEvents = new List<int>(events != null ? events.Count : 0);
			if (events != null)
			{
				for (int i = 0; i < events.Count; ++i)
				{
					craftedEvents.Add(events[i]);
				}
			}

			/* The claim the row will be written under, captured now, with the request. A resident
			 * character always holds one; one that does not is leaving or has been evicted, and a
			 * grant it could never record is refused before anything is constructed or charged. */
			long characterID = character.ID;
			if (!TryCaptureSessionClaim(characterID, out CharacterSessionLeaseData claim))
			{
				Log.Warning("AbilitySystem",
					$"TryGrantAbility: this server holds no session claim for CharID={characterID}; refusing the grant of template {template.ID}.");
				releaseGuard?.Invoke();
				return false;
			}

			Ability ability = new Ability(template, craftedEvents);
			long version = ++ability.Version;

			GrantRequest request = new GrantRequest()
			{
				CharacterID = characterID,
				Ability = ability,
				Version = version,
				AbilityData = new CharacterAbilityData(
					id: ability.ID,
					version: version,
					characterID: characterID,
					templateID: template.ID,
					abilityEvents: craftedEvents,
					cooldown: 0f),
				Claim = claim,
				CraftedEvents = craftedEvents.Count > 0 ? craftedEvents.ToArray() : null,
				Settle = settle,
				ReleaseGuard = releaseGuard,
				OnGranted = onGranted,
				OnFailed = onFailed,
			};

			/* Keyed by character so this lands on the same ordered lane as the character's own saves
			 * and as a forget's delete. See AsyncWorkerData: work sharing an entityKey runs one at a
			 * time, in the order it was enqueued. */
			if (!TryEnqueueAsyncWork(() => PersistGrantedAbilityAsync(request), characterID))
			{
				Log.Warning("AbilitySystem",
					$"TryGrantAbility: Async worker rejected the persist of template {template.ID} for CharID={characterID}; the grant was refused.");
				releaseGuard?.Invoke();
				return false;
			}

			return true;
		}

		/// <summary>
		/// Writes the crafted ability's row and hands the database-minted identity back to the main
		/// thread.
		/// </summary>
		/// <param name="request">The admitted grant.</param>
		private async Task PersistGrantedAbilityAsync(GrantRequest request)
		{
			try
			{
				if (!TryGetDbService<ICharacterAbilityService>(out var abilityService))
				{
					await Log.Error("AbilitySystem",
						$"PersistGrantedAbilityAsync: ICharacterAbilityService is not registered; the grant of template {request.AbilityData.TemplateID} to CharID={request.CharacterID} was not recorded.");
					QueueGrantCompletion(request, persisted: false);
					return;
				}

				/* Ownership-gated: the row lands only while the claim the grant was requested under is
				 * still held. A refusal means another session owns the character now (or none does,
				 * after a release) — the grant is answered as any failed write is: nothing learned,
				 * nothing charged. The row save and the lease refresh evict such a character within
				 * one interval, so the player's next sight of it is the owning server's state. */
				DatabaseResult<long> result = await abilityService.PersistOwnedAsync(request.AbilityData, request.Claim);
				if (!result.IsSuccess)
				{
					await Log.Warning("AbilitySystem",
						IsClaimRefusal(result)
							? $"PersistGrantedAbilityAsync: the grant of template {request.AbilityData.TemplateID} to CharID={request.CharacterID} was refused because this server no longer holds the character's session claim."
							: $"PersistGrantedAbilityAsync DB error (CharID={request.CharacterID}, TemplateID={request.AbilityData.TemplateID}): {result.ErrorCode} - {result.ErrorMessage}");
					QueueGrantCompletion(request, persisted: false);
					return;
				}

				/* The identity the upsert's RETURNING clause minted — the whole point of this pass.
				 * Written onto the Ability here, on the worker, ONLY because the Ability is not yet
				 * reachable from anywhere: it is not in KnownAbilities, has never been broadcast, and
				 * is referenced by this request alone. Every read of it happens on the main thread
				 * after the completion below. */
				request.Ability.ID = result.Data;
				if (!QueueGrantCompletion(request, persisted: true))
				{
					/* Nothing on the main thread will settle this grant, so the row must not stand:
					 * the charge is taken only in CompleteGrant, and a row left behind here would be
					 * an ability the player owns from their next login without ever paying for it.
					 * Revoked inline — this worker is already on the character's lane, behind the
					 * write it undoes. */
					await RevokeUnsettledGrantAsync(request.CharacterID, request.Ability.ID, request.Claim, "the main-thread queue was saturated before settlement");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("AbilitySystem", $"Error persisting a granted ability: {ex}");
				QueueGrantCompletion(request, persisted: false);
			}
		}

		/// <summary>
		/// Marshals a grant's outcome onto the main thread, or releases its guard and reports that it
		/// could not be marshalled.
		/// </summary>
		/// <param name="request">The grant.</param>
		/// <param name="persisted">True when the row was written and only the learn remains.</param>
		/// <returns>
		/// False when the outcome could not be marshalled. For a persisted grant the caller then owns
		/// the row, which nothing has paid for.
		/// </returns>
		private bool QueueGrantCompletion(GrantRequest request, bool persisted)
		{
			if (TryEnqueueMainThread<IAbilitySystemMainThreadQueueData>(
					() =>
					{
						if (persisted)
						{
							CompleteGrant(request);
						}
						else
						{
							FailGrant(request);
						}
					}))
			{
				return true;
			}

			/* The queue is at capacity, which means the main thread has stalled long enough for async
			 * workers to saturate it. Nothing can be applied to the character and neither callback can
			 * run, so the guard is released here — the caller's request is over either way — and the
			 * outcome is written down instead of being lost. */
			request.ReleaseGuard?.Invoke();

			if (persisted)
			{
				/* The row exists but was never settled: the charge runs in CompleteGrant, which will
				 * not run. It used to be left standing on the reasoning that the character "owns it"
				 * — but nothing had been paid, so the character owned it free from the next login.
				 * The caller revokes it. Nothing was charged and nothing was learned, so the player
				 * ends up where they started, unanswered: the panel's own watchdog clears it. */
				Log.Error("AbilitySystem",
					$"QueueGrantCompletion: the row for template {request.AbilityData.TemplateID} (CharID={request.CharacterID}) was written but could not be settled because the main-thread queue is saturated; it is being revoked. Nothing was charged.");
			}
			else
			{
				Log.Error("AbilitySystem",
					$"QueueGrantCompletion: the failure of the grant of template {request.AbilityData.TemplateID} (CharID={request.CharacterID}) could not be reported to the caller, which was never told. Nothing was charged.");
			}
			return false;
		}

		/// <summary>
		/// Learns the granted ability with the identity the database assigned, and announces it.
		/// </summary>
		/// <param name="request">The grant.</param>
		private void CompleteGrant(GrantRequest request)
		{
			try
			{
				IPlayerCharacter character = ResolveResidentCharacter(request.CharacterID);
				if (character == null)
				{
					/* Logged out inside a database round trip. Nothing has been paid — settlement
					 * runs here, on a resident character, and never before the row exists — so the
					 * row is revoked rather than left standing as an ability nobody paid for. The
					 * player asked, left, and ends up exactly where they started. */
					Log.Warning("AbilitySystem",
						$"CompleteGrant: CharID={request.CharacterID} left before ability {request.Ability.ID} could be settled; the row is being revoked.");
					RevokeUnsettledGrant(request, "the character left before settlement");
					return;
				}

				/* The caller's charge, taken only now: the row exists and the payer is here to pay.
				 * Charging BEFORE the write, as this used to, left a real loss path — a write that
				 * failed after the player had transferred or logged out could not be refunded, and the
				 * database held the deduction with no row to show for it. A refusal here (the balance
				 * moved between the affordability check and now) has already answered the player
				 * inside the callback; what is left is the row it was written for, which is revoked so
				 * the database does not hold an ability that was never paid for. */
				if (request.Settle != null && !request.Settle(character))
				{
					RevokeUnsettledGrant(request, "settlement was refused");
					return;
				}

				/* The version the row now holds. Re-stated rather than assumed because
				 * MarkPersisted clears the dirty flag by comparing the ability's version with the one
				 * that was written, and an ability that is not marked here is rewritten by the next
				 * save for nothing. */
				request.Ability.Version = request.Version;
				request.Ability.MarkPersisted(request.Version);

				if (character.TryGet(out IAbilityController abilityController))
				{
					abilityController.LearnAbility(request.Ability);
				}

				NetworkConnection owner = character.Owner;
				if (owner != null)
				{
					Server.NetworkWrapper.Broadcast(owner, new AbilityAddBroadcast()
					{
						ID = request.Ability.ID,
						TemplateID = request.Ability.Template.ID,
						Events = request.CraftedEvents,
					}, true, Channel.Reliable);
				}

				/* Tell the character's observers too.
				 *
				 * An observer's copy of a peer's known abilities is written once, by the spawn
				 * payload, when it starts observing. Everyone already watching this character learns
				 * nothing from the message above — it is addressed to the owner — so every cast of
				 * this ability would resolve to nothing on their clients and draw nothing, for as long
				 * as they kept observing. The bytes go on the rare learn, not on every cast. */
				if (character.NetworkObject != null)
				{
					ObserverBroadcastScope.BroadcastToObserversExceptOwner(character.NetworkObject, new AbilityLearnedObserverBroadcast()
					{
						CasterObjectID = character.NetworkObject.ObjectId,
						AbilityID = request.Ability.ID,
						TemplateID = request.Ability.Template.ID,
						Events = request.CraftedEvents,
					}, Channel.Reliable);
				}

				request.OnGranted?.Invoke(character, request.Ability);
			}
			finally
			{
				request.ReleaseGuard?.Invoke();
			}
		}

		/// <summary>
		/// Reports a grant that was attempted and not recorded, so the caller can undo what it did in
		/// anticipation.
		/// </summary>
		/// <param name="request">The grant.</param>
		private void FailGrant(GrantRequest request)
		{
			try
			{
				IPlayerCharacter character = ResolveResidentCharacter(request.CharacterID);
				if (character == null)
				{
					/* Nothing to undo: the charge is settled only on completion, so a grant that never
					 * landed never cost anything. There is simply nobody left to answer. */
					Log.Warning("AbilitySystem",
						$"FailGrant: the grant of template {request.AbilityData.TemplateID} for CharID={request.CharacterID} failed and the character has since left; nothing was charged.");
					return;
				}

				request.OnFailed?.Invoke(character);
			}
			finally
			{
				request.ReleaseGuard?.Invoke();
			}
		}

		/// <summary>
		/// Removes a row that was written but never settled, so the character does not own an
		/// ability that was never paid for.
		/// </summary>
		/// <param name="request">The grant whose row is to go.</param>
		/// <param name="why">For the log.</param>
		/// <remarks>
		/// <para>
		/// Keyed by character, so it lands on the same ordered lane as the write it undoes and after
		/// it. The ability was never learned or announced, so no in-memory or observer state needs
		/// touching — only the row. Quotes the version ceiling for the reason a forget does: the row
		/// is resolved by identity and ownership, and the version is not the safety predicate.
		/// </para>
		/// <para>
		/// <b>Quotes the grant's claim, and is admitted after its release too.</b> The commonest
		/// revoke is a player who logged out inside the grant's round trip: their save-and-release
		/// was queued on this lane before the settlement found them gone, so the revoke always runs
		/// after the release. Refused there, the unpaid row would stand and the character would own
		/// the ability from their next login. So the delete is admitted while the claim is held OR
		/// while the character holds none at all (<c>admitReleased</c>), and refused only once
		/// another session has claimed — and loaded — the character.
		/// </para>
		/// </remarks>
		private void RevokeUnsettledGrant(GrantRequest request, string why)
		{
			long characterID = request.CharacterID;
			long abilityID = request.Ability.ID;
			CharacterSessionLeaseData claim = request.Claim;

			if (abilityID <= 0)
			{
				Log.Error("AbilitySystem",
					$"RevokeUnsettledGrant: ability for CharID={characterID} has no identity to revoke ({why}).");
				return;
			}

			if (!TryEnqueueAsyncWork(() => RevokeUnsettledGrantAsync(characterID, abilityID, claim, why), characterID))
			{
				Log.Error("AbilitySystem",
					$"RevokeUnsettledGrant: the async worker refused the revoke of ability {abilityID} for CharID={characterID} ({why}); the row stands and the character owns an ability that was never settled.");
			}
		}

		/// <summary>
		/// The worker half of <see cref="RevokeUnsettledGrant"/>.
		/// </summary>
		private async Task RevokeUnsettledGrantAsync(long characterID, long abilityID, CharacterSessionLeaseData claim, string why)
		{
			try
			{
				if (!TryGetDbService<ICharacterAbilityService>(out var abilityService))
				{
					await Log.Error("AbilitySystem",
						$"RevokeUnsettledGrantAsync: ICharacterAbilityService is not registered; ability {abilityID} for CharID={characterID} was not revoked ({why}).");
					return;
				}

				DatabaseResult result = await abilityService.DeleteAbilityOwnedAsync(characterID, abilityID, long.MaxValue, claim, admitReleased: true);
				if (!result.IsSuccess)
				{
					await Log.Error("AbilitySystem",
						IsClaimRefusal(result)
							? $"RevokeUnsettledGrantAsync: ability {abilityID} for CharID={characterID} was not revoked ({why}) because another session has claimed the character and loaded the row. The row stands unpaid."
							: $"RevokeUnsettledGrantAsync DB error (AbilityID={abilityID}, CharID={characterID}, {why}): {result.ErrorCode} - {result.ErrorMessage}. The row stands unpaid.");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("AbilitySystem", $"Error revoking unsettled ability {abilityID} for CharID={characterID} ({why}): {ex}");
			}
		}

		#endregion

		#region Forgetting

		/// <summary>
		/// Handles a request to forget one crafted ability.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Addressed by the ability's row identity, never by its template. The service's statement
		/// matches <c>WHERE id = ?</c>, and a template id would either match nothing — reporting
		/// success for a delete that removed no row — or, on a table with several rows per template,
		/// remove the wrong one.
		/// </para>
		/// <para>
		/// The guard is held until the delete lands, not until this handler returns. A second forget
		/// arriving in that window would otherwise read the same live ability and delete the same row
		/// twice, and a re-craft arriving in it would see a character that still knows the ability.
		/// </para>
		/// </remarks>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">Forget request naming the ability.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerAbilityForgetBroadcastReceived(NetworkConnection conn, AbilityForgetBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			IPlayerCharacter character = request.Character;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Forget, out long guardKey))
			{
				/* A refusal is still an answer. The panel clears its pending row on the result, and
				 * silence would leave it waiting for its own watchdog to expire. */
				SendForgetResult(conn, msg.AbilityID, AbilityForgetFailure.Busy);
				return;
			}

			bool guardTransferred = false;
			try
			{
				if (!character.TryGet(out IAbilityController abilityController) ||
					!abilityController.KnownAbilities.TryGetValue(msg.AbilityID, out Ability ability) ||
					ability == null)
				{
					/* Nothing to forget. Refused rather than answered with success: the client's
					 * panel removes the row on a success, and a success for something the server
					 * never held would hide a disagreement rather than resolve one. */
					SendForgetResult(conn, msg.AbilityID, AbilityForgetFailure.Unknown);
					return;
				}

				/* The delete admits a row whose version is at or below what it is given. A forget is
				 * the player's authoritative decision about a row this server resolved by identity, so
				 * it quotes the ceiling rather than the in-memory version plus one: the in-memory
				 * version can trail the row's (a previous server's late snapshot landing after this
				 * one loaded the character bumps the row and not the copy), and quoting from the
				 * stale copy refused the forget as PersistFailed until a relog re-read it. Identity
				 * and ownership are the safety predicate here, not the version. */
				long version = long.MaxValue;
				long characterID = character.ID;
				long abilityID = msg.AbilityID;

				/* The delete quotes the claim it was requested under, captured now. A resident
				 * character always holds one; without it the character is not ours to change, which
				 * the player hears as the write failing — which is what it would do. */
				if (!TryCaptureSessionClaim(characterID, out CharacterSessionLeaseData claim))
				{
					Log.Warning("AbilitySystem",
						$"OnServerAbilityForgetBroadcastReceived: this server holds no session claim for CharID={characterID}; the forget of ability {abilityID} was refused.");
					SendForgetResult(conn, abilityID, AbilityForgetFailure.PersistFailed);
					return;
				}

				guardTransferred = TryEnqueueAsyncWork(
					() => ForgetAbilityAsync(characterID, abilityID, version, claim, guardKey),
					characterID);

				if (!guardTransferred)
				{
					Log.Warning("AbilitySystem",
						$"OnServerAbilityForgetBroadcastReceived: Async worker rejected the delete of ability {abilityID} for CharID={characterID}.");
					SendForgetResult(conn, abilityID, AbilityForgetFailure.Busy);
				}
			}
			finally
			{
				if (!guardTransferred)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Deletes the ability's row and hands the outcome back to the main thread.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="abilityID">The row identity.</param>
		/// <param name="version">The version the delete is issued at.</param>
		/// <param name="claim">The session claim the forget was requested under.</param>
		/// <param name="guardKey">The ingress guard to release when this finishes.</param>
		private async Task ForgetAbilityAsync(long characterID, long abilityID, long version, CharacterSessionLeaseData claim, long guardKey)
		{
			try
			{
				if (!TryGetDbService<ICharacterAbilityService>(out var abilityService))
				{
					await Log.Error("AbilitySystem",
						$"ForgetAbilityAsync: ICharacterAbilityService is not registered; ability {abilityID} for CharID={characterID} was not forgotten.");
					QueueForgetCompletion(characterID, abilityID, AbilityForgetFailure.PersistFailed);
					return;
				}

				/* Ownership-gated. A refusal is answered as the write failing — the ability is still
				 * known, which is true of every copy the player can see — and the character, whose
				 * claim is gone, is evicted by the row save or the lease refresh within one interval. */
				DatabaseResult result = await abilityService.DeleteAbilityOwnedAsync(characterID, abilityID, version, claim);
				if (!result.IsSuccess)
				{
					await Log.Warning("AbilitySystem",
						IsClaimRefusal(result)
							? $"ForgetAbility: the forget of ability {abilityID} for CharID={characterID} was refused because this server no longer holds the character's session claim."
							: $"ForgetAbility DB error (AbilityID={abilityID}, CharID={characterID}): {result.ErrorCode} - {result.ErrorMessage}");
					QueueForgetCompletion(characterID, abilityID, AbilityForgetFailure.PersistFailed);
					return;
				}

				QueueForgetCompletion(characterID, abilityID, AbilityForgetFailure.None);
			}
			catch (Exception ex)
			{
				await Log.Error("AbilitySystem", $"Error forgetting ability {abilityID} for CharID={characterID}: {ex}");
				QueueForgetCompletion(characterID, abilityID, AbilityForgetFailure.PersistFailed);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Marshals a forget's outcome onto the main thread, or reports that it could not be.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="abilityID">The row identity.</param>
		/// <param name="failure">Why it was refused, or <see cref="AbilityForgetFailure.None"/>.</param>
		private void QueueForgetCompletion(long characterID, long abilityID, AbilityForgetFailure failure)
		{
			if (TryEnqueueMainThread<IAbilitySystemMainThreadQueueData>(
					() => CompleteForget(characterID, abilityID, failure)))
			{
				return;
			}

			Log.Error("AbilitySystem",
				$"QueueForgetCompletion: the outcome of forgetting ability {abilityID} for CharID={characterID} could not be applied because the main-thread queue is saturated. The row's state and the character's in-memory ability set may now disagree until the next login.");
		}

		/// <summary>
		/// Applies a landed forget: drops the ability, clears the hotkeys bound to it, and answers.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="abilityID">The row identity.</param>
		/// <param name="failure">Why it was refused, or <see cref="AbilityForgetFailure.None"/>.</param>
		private void CompleteForget(long characterID, long abilityID, AbilityForgetFailure failure)
		{
			IPlayerCharacter character = ResolveResidentCharacter(characterID);
			if (character == null)
			{
				/* Logged out while the delete was in flight. The row is gone, and the ability was
				 * never in a loaded character's set to begin with, so there is nothing to undo and
				 * nobody to answer. */
				if (failure != AbilityForgetFailure.None)
				{
					Log.Warning("AbilitySystem",
						$"CompleteForget: forgetting ability {abilityID} for CharID={characterID} failed and the character has since left, so nothing could be undone.");
				}
				return;
			}

			if (failure != AbilityForgetFailure.None)
			{
				SendForgetResult(character.Owner, abilityID, failure);
				return;
			}

			if (character.TryGet(out IAbilityController abilityController))
			{
				abilityController.RemoveAbility(abilityID);
			}

			/* Observers too, symmetric with the learn path and for the reason spelled out on
			 * AbilityForgottenObserverBroadcast: their copy is written once when they start
			 * observing and corrected by these two messages and nothing else. Nothing renders a
			 * stale entry — the server will not activate the ability, so the activation that would
			 * resolve it is never sent — but Inspect, CanActivate and faction evaluation all read
			 * the observed character's real state, and all of them would still list this. */
			if (character.NetworkObject != null)
			{
				ObserverBroadcastScope.BroadcastToObserversExceptOwner(character.NetworkObject, new AbilityForgottenObserverBroadcast()
				{
					CasterObjectID = character.NetworkObject.ObjectId,
					AbilityID = abilityID,
				}, Channel.Reliable);
			}

			/* A hotkey bound to a forgotten ability names an id nothing resolves any more. The
			 * server's bar and the database row would keep it until the next login, because bindings
			 * are validated when they are MADE and in the login prune and nowhere in between — so the
			 * client would show a dead slot for the rest of the session. Clearing here, and echoing
			 * the whole bar back, is what corrects both sides now. */
			if (Server.BehaviourRegistry.TryGet(out IHotkeySystem hotkeySystem) && hotkeySystem != null)
			{
				hotkeySystem.ForgetAbilityBindings(character, abilityID);
			}

			SendForgetResult(character.Owner, abilityID, AbilityForgetFailure.None);
		}

		/// <summary>
		/// Answers a forget request.
		/// </summary>
		/// <param name="conn">The requesting connection, or null when it has gone.</param>
		/// <param name="abilityID">The row identity the request named.</param>
		/// <param name="failure">Why it was refused, or <see cref="AbilityForgetFailure.None"/>.</param>
		private void SendForgetResult(NetworkConnection conn, long abilityID, AbilityForgetFailure failure)
		{
			if (conn == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new AbilityForgetResultBroadcast()
			{
				AbilityID = abilityID,
				Success = failure == AbilityForgetFailure.None,
				Failure = failure,
			}, true, Channel.Reliable);
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IAbilitySystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases a previously acquired ingress guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IAbilitySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Finds a connected character by ID. Main thread only.
		/// </summary>
		/// <remarks>
		/// A character that has since logged out is simply not found. Every completion treats that as
		/// "nothing left to apply" rather than as an error, because it is the ordinary outcome of a
		/// player quitting inside a database round trip.
		/// </remarks>
		private IPlayerCharacter ResolveResidentCharacter(long characterID)
		{
			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				return character;
			}
			return null;
		}

		#endregion
	}
}
