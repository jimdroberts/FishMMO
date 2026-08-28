using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls and manages ability cooldowns using immutable <see cref="CooldownInstance"/>
	/// based on StartTick + DurationTicks. No per-tick mutation — cooldowns expire
	/// automatically via integer comparison: <c>(currentTick - StartTick) &gt;= DurationTicks</c>.
	/// </summary>
	public class CooldownController : CharacterBehaviour, ICooldownController, IPredictableController
	{
		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs before <see cref="AbilityController"/> so newly elapsed cooldowns
		/// are removed before ability start checks in the same tick.
		/// </summary>
		public int Order => 90;

		/// <summary>
		/// Dictionary of active cooldowns, keyed by ability ID.
		/// Entries are immutable <see cref="CooldownInstance"/> values — they are never
		/// mutated after insertion, only removed when expired.
		/// </summary>
		private SortedDictionary<long, CooldownInstance> cooldowns = new SortedDictionary<long, CooldownInstance>();

		/// <summary>
		/// Reusable buffer for collecting expired cooldown keys during <see cref="ExpireElapsed"/>.
		/// </summary>
		private List<long> keysToRemove = new List<long>();

		/// <summary>
		/// Reusable ability-ID sets for <see cref="RestoreFromReconcile"/>.
		/// </summary>
		/// <remarks>
		/// Preallocated because reconcile runs on every differing tick — every corrected tick of
		/// combat — and a fresh <see cref="HashSet{T}"/> per call is garbage generated at tick rate
		/// on the client's hottest path. Cleared on entry rather than on exit so neither can hold a
		/// reference to an ID after the call.
		/// </remarks>
		private readonly HashSet<long> reconcileIDs = new HashSet<long>();

		/// <summary>
		/// Ability IDs present before a reconcile replaced them, used to tell an updated cooldown
		/// from a newly added one. Reused for the same reason as <see cref="reconcileIDs"/>.
		/// </summary>
		private readonly HashSet<long> preReconcileIDs = new HashSet<long>();

		/// <summary>
		/// Cached reconcile snapshot, reused across ticks when cooldowns haven't changed.
		/// Invalidated by <see cref="AddCooldown"/>, <see cref="RemoveCooldown"/>,
		/// <see cref="Clear"/>, and <see cref="RestoreFromReconcile"/>.
		/// </summary>
		private CooldownReconcileEntry[] cachedSnapshot;

		/// <summary>
		/// The replicate input tick captured at the start of each <see cref="OnReplicate"/> call.
		/// Used by server-authoritative cooldown queries to compare against cooldown start ticks
		/// in the same domain as <see cref="ExpireElapsed"/>.
		/// </summary>
		private uint lastReplicateTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// When true, <see cref="cachedSnapshot"/> is stale and must be rebuilt.
		/// </summary>
		private bool snapshotDirty = true;

		/// <summary>
		/// Whether this controller has observed its first non-UNSET replicate tick.
		/// Used to suppress noisy pre-replicate warnings after the first occurrence.
		/// </summary>
		private bool hasSeenFirstReplicate = false;

		/// <summary>
		/// Prevents repeatedly logging the same ResolveAuthoritativeTick pre-replicate warning.
		/// </summary>
		private bool resolveAuthoritativeWarningLogged = false;

		/// <summary>
		/// Payload reference tick for cooldowns read before this controller has a usable
		/// local or replicate reference tick. The first valid replicate pass consumes it
		/// so late-join payload ticks can still be translated from the writer's domain.
		/// </summary>
		private uint preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// True while <see cref="OnReplicate"/> is executing a replayed (reconcile replay) tick.
		/// Mutation helpers (<see cref="RemoveCooldown"/>) check this flag to suppress UI / ECA
		/// events that would otherwise fire on every replayed tick.
		/// External callers (non-prediction paths) leave this <c>false</c> so events fire normally.
		/// </summary>
		private bool isReplayingTick;

		/// <summary>
		/// Cached tick delta for converting between seconds and ticks.
		/// Eagerly initialized from <see cref="TimeManager.TickDelta"/> in
		/// <see cref="OnStartNetwork"/>.
		/// </summary>
		private float cachedTickDelta;

		/// <summary>
		/// Cached <see cref="CharacterPredictionController"/> for resolving the replicate-domain
		/// tick snapshot when computing authoritative cooldown ticks.
		/// </summary>
		private CharacterPredictionController predictionController;

		/// <summary>
		/// Returns the fixed time step per tick. Caches on first access.
		/// Throws if accessed before <see cref="OnStartNetwork"/> has run — using a wall-clock
		/// fallback (e.g. Time.fixedDeltaTime) would silently desync cooldown expiry between
		/// clients running at different tick rates (per §3.2).
		/// </summary>
		internal float TickDelta
		{
			get
			{
				if (cachedTickDelta <= 0f && base.TimeManager != null)
				{
					cachedTickDelta = (float)base.TimeManager.TickDelta;
				}
				if (cachedTickDelta <= 0f)
				{
					throw new System.InvalidOperationException(
						"CooldownController.TickDelta: TimeManager not available. " +
						"Cooldown calculations require a deterministic tick delta — " +
						"ensure OnStartNetwork has run before invoking cooldown logic.");
				}
				return cachedTickDelta;
			}
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				cachedTickDelta = (float)base.TimeManager.TickDelta;
			}
			predictionController = GetComponent<CharacterPredictionController>();
		}

		/// <summary>
		/// Returns the internal cooldown dictionary. Used by reconcile serialization.
		/// </summary>
		internal SortedDictionary<long, CooldownInstance> Cooldowns => cooldowns;

		/// <summary>
		/// Cooldowns do not contribute owner input into <see cref="CharacterReplicateData"/>.
		/// </summary>
		/// <param name="input">Unified replicate data for this tick.</param>
		public void PopulateInput(ref CharacterReplicateData input)
		{
		}

		/// <summary>
		/// Expires elapsed cooldowns deterministically for the current prediction tick.
		/// </summary>
		/// <param name="input">Unified replicate input containing the network tick.</param>
		/// <param name="state">Current replicate execution state.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			uint inputTick = input.GetTick();
			if (lastReplicateTick == TimeManager.UNSET_TICK &&
				inputTick != TimeManager.UNSET_TICK &&
				base.TimeManager != null)
			{
				uint sourceReferenceTick = preReplicatePayloadReferenceTick != TimeManager.UNSET_TICK
					? preReplicatePayloadReferenceTick
					: base.TimeManager.LocalTick;
				TranslatePreReplicateCooldownTicks(GetSignedTickOffset(sourceReferenceTick, inputTick,
					nameof(TranslatePreReplicateCooldownTicks)));
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
			}
			lastReplicateTick = inputTick;

			if (inputTick != TimeManager.UNSET_TICK)
			{
				hasSeenFirstReplicate = true;
			}

			// Gate event emission for replayed ticks. The deterministic state mutation
			// (removing expired entries) still runs every replay tick so the dictionary remains
			// in lock-step with the authoritative server; only the OnRemoveCooldown ECA / UI
			// notification is suppressed during replay.
			bool wasReplaying = isReplayingTick;
			isReplayingTick = state.ContainsReplayed();
			try { ExpireElapsed(inputTick); }
			finally { isReplayingTick = wasReplaying; }
		}

		/// <summary>
		/// Converts cooldowns created before the first replicate tick from raw LocalTick space into
		/// the replicate-tick domain used by <see cref="ExpireElapsed"/>.
		/// </summary>
		/// <param name="tickOffset">Signed offset from raw LocalTick space to replicate-tick space.</param>
		private void TranslatePreReplicateCooldownTicks(int tickOffset)
		{
			if (tickOffset == 0 || cooldowns.Count == 0)
			{
				return;
			}

			keysToRemove.Clear();
			foreach (KeyValuePair<long, CooldownInstance> pair in cooldowns)
			{
				keysToRemove.Add(pair.Key);
			}

			float tickDelta = TickDelta;
			for (int i = 0; i < keysToRemove.Count; i++)
			{
				long key = keysToRemove[i];
				CooldownInstance cooldown = cooldowns[key];
				cooldowns[key] = new CooldownInstance(
					AddSignedTickOffset(cooldown.StartTick, tickOffset),
					cooldown.DurationTicks,
					tickDelta);
			}
			keysToRemove.Clear();
			snapshotDirty = true;
		}

		/// <summary>
		/// Writes cooldown reconcile state for this tick.
		/// </summary>
		/// <param name="reconcileData">Mutable unified reconcile payload.</param>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			reconcileData.Cooldowns = CreateReconcileSnapshot();
		}

		/// <summary>
		/// Restores cooldowns from authoritative reconcile state.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The owner always reconciles — this is the authority for its own cooldown simulation, and
		/// the correction that removes a cooldown it mispredicted.
		/// </para>
		/// <para>
		/// A non-owner only reconciles a forwarded object, because that is the only mode in which
		/// it runs the ability simulation at all and therefore the only mode in which a peer's
		/// cooldowns mean anything to it. With forwarding off an observer never receives this
		/// reconcile, so the guard states the contract rather than defending against a message that
		/// cannot arrive — and it keeps the reconcile path aligned with what the spawn payload
		/// already decided: <c>AbilityController.WritePayload</c> writes the cooldown block with
		/// <c>includeEntries: isOwner</c>, framed with a zero count for everyone else, because
		/// observers never read a peer's cooldowns. Without this guard the two paths disagreed about
		/// the same fact the moment forwarding was switched on. See <see cref="ObserverSyncMode"/>.
		/// </para>
		/// </remarks>
		/// <param name="rd">Unified reconcile payload.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			if (!base.IsOwner && !ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject))
			{
				return;
			}

			RestoreFromReconcile(rd.Cooldowns);
		}

		/// <inheritdoc/>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			lastReplicateTick = TimeManager.UNSET_TICK;
			hasSeenFirstReplicate = false;
			resolveAuthoritativeWarningLogged = false;
			preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
			cooldowns.Clear();
			keysToRemove.Clear();
		}

		/// <summary>
		/// Reads cooldown data from a network reader, discarding any entries that have
		/// already expired relative to <paramref name="currentTick"/>.
		/// Wire format: uint payloadReferenceTick, int count, then per cooldown:
		/// long abilityID, uint startTick, uint durationTicks.
		/// </summary>
		/// <remarks>
		/// This is a <b>payload / initial-sync</b> path (e.g., spawning, scene load),
		/// not a prediction path. Prediction uses the delta serializer registered in
		/// <see cref="CooldownReconcileEntry"/> via <c>WriteArrayDelta</c>/<c>ReadArrayDelta</c>.
		/// </remarks>
		/// <param name="reader">The network reader.</param>
		/// <param name="currentTick">Current network tick used to translate and discard expired entries.</param>
		/// <summary>
		/// True when this controller belongs to the local player's own spawned object.
		/// </summary>
		/// <remarks>
		/// Null-safe where <c>base.IsOwner</c> is not. <c>IsOwner</c> resolves through the cached
		/// <see cref="NetworkObject"/>, which does not exist before the object is spawned or after it
		/// has been despawned, so reading it throws rather than answering "no".
		/// <para>
		/// Every use of this gates a LOCAL-UI announcement, and an object with no network identity has
		/// no local player to announce to — so "not spawned" and "not owner" want the same answer.
		/// <see cref="Read"/> and <see cref="RestoreFromReconcile"/> are the ones that made this matter:
		/// they are plain deserialisation entry points, perfectly callable — and unit-testable — without
		/// a spawned object, until the ownership gate at the end of each of them dereferenced a
		/// NetworkObject that was not there.
		/// </para>
		/// </remarks>
		private bool IsLocalOwner => base.NetworkObject != null && base.IsOwner;

		/// <summary>
		/// Width of the byte count that frames this controller's cooldown block.
		/// </summary>
		/// <remarks>
		/// Four bytes, written unpacked so the width is fixed and the slot can be reserved before
		/// the length is known. A packed integer would vary in size and could not be backfilled.
		/// </remarks>
		private const int COOLDOWN_PAYLOAD_LENGTH_BYTES = 4;

		public void Read(Reader reader, uint currentTick)
		{
			const int maxPayloadCooldowns = 4096;

			cooldowns.Clear();
			keysToRemove.Clear();

			uint payloadReferenceTick = reader.ReadUInt32();

			/* Where this block ends, whatever happens below. This runs nested inside
			 * AbilityController's own frame, on the shared spawn payload buffer that FishNet fills
			 * for every NetworkBehaviour with no per-behaviour framing — so an early return here
			 * used to leave the outer reader misaligned. The length is validated against what the
			 * reader actually holds first, because a frame that exists to survive a corrupt payload
			 * must not trust its own length: Reader.Position is a plain field with no bounds check. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("CooldownController",
					$"Read: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in the " +
					"payload. The stream cannot be resynchronised; discarding the remainder.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				reader.Position += remainingBytes;
				return;
			}
			int cooldownBlockLength = (int)declaredLength;
			int cooldownBlockEnd = reader.Position + cooldownBlockLength;

			bool deferPayloadTranslation = currentTick == TimeManager.UNSET_TICK &&
				payloadReferenceTick != TimeManager.UNSET_TICK;
			int tickOffset = deferPayloadTranslation ? 0 :
				GetSignedTickOffset(payloadReferenceTick, currentTick, nameof(Read));

			int cooldownCount = reader.ReadInt32();
			if (cooldownCount < 0 || cooldownCount > maxPayloadCooldowns)
			{
				Log.Error("CooldownController",
					$"Read: cooldown count {cooldownCount} is outside [0, {maxPayloadCooldowns}]. Aborting payload read.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				/* Seek, do not drain. The per-entry sizes a drain would need come from the count
				 * that was just rejected, so the old capped drain silently desynchronised the
				 * stream for every count above the cap. */
				reader.Position = cooldownBlockEnd;
				return;
			}
			preReplicatePayloadReferenceTick = deferPayloadTranslation
				? payloadReferenceTick
				: TimeManager.UNSET_TICK;

			float td = TickDelta;
			for (int i = 0; i < cooldownCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				uint startTick = AddSignedTickOffset(reader.ReadUInt32(), tickOffset);
				uint durationTicks = reader.ReadUInt32();

				// Skip cooldowns that have already expired by the time we receive them.
				if (currentTick != TimeManager.UNSET_TICK)
				{
					int elapsedTicks = (int)(currentTick - startTick);
					if (elapsedTicks >= 0 && (uint)elapsedTicks >= durationTicks)
					{
						continue;
					}
				}

				CooldownInstance cooldown = new CooldownInstance(startTick, durationTicks, td);
				cooldowns[abilityID] = cooldown;
			}

			/* Belt and braces on the success path too. If the two sides ever disagree about the
			 * shape of this block the frame absorbs it here rather than corrupting whatever the
			 * outer reader parses next, and says so once instead of failing invisibly. */
			if (reader.Position != cooldownBlockEnd)
			{
				Log.Error("CooldownController",
					$"Read consumed {reader.Position - (cooldownBlockEnd - cooldownBlockLength)} of " +
					$"{cooldownBlockLength} framed bytes. Seeking to the end of the block; the cooldown " +
					"state read above may be incomplete.");
				reader.Position = cooldownBlockEnd;
			}

			snapshotDirty = true;

			/* Restored cooldowns are ANNOUNCED, not just stored. This is the relog / zone-change
			 * path: the server hands back the bar's live cooldowns in the spawn payload, and
			 * writing them straight into the dictionary meant nothing downstream ever heard about
			 * them. The hotkey bar only starts its sweep when an add or update event arrives, so
			 * after every relog the player saw no cooldown overlay at all while the server went on
			 * refusing the casts — the worst shape this can take, because the UI actively says the
			 * ability is ready.
			 *
			 * Fired after the loop so a handler cannot observe a half-filled dictionary, and gated
			 * on ownership exactly as AddCooldown is: these events drive the LOCAL player's UI, and
			 * every remote character's payload comes through here too. */
			if (!IsLocalOwner || ICooldownController.OnAddCooldown == null)
			{
				return;
			}

			foreach (KeyValuePair<long, CooldownInstance> restored in cooldowns)
			{
				ICooldownController.OnAddCooldown.Invoke(restored.Key, restored.Value);
			}
		}

		/// <summary>
		/// Writes cooldown data to a network writer.
		/// Wire format: uint payloadReferenceTick, int count, then per cooldown:
		/// long abilityID, uint startTick, uint durationTicks.
		/// </summary>
		/// <remarks>
		/// This is a <b>payload / initial-sync</b> path. See <see cref="Read"/> remarks.
		/// </remarks>
		/// <param name="writer">The network writer.</param>
		public void Write(Writer writer) => Write(writer, includeEntries: true);

		/// <summary>
		/// Writes cooldown data, optionally as an empty block.
		/// </summary>
		/// <remarks>
		/// <paramref name="includeEntries"/> is false for connections that are not this
		/// character's owner. Cooldowns gate the owner's own activations and drive the owner's
		/// hotkey bar; an observer never reads a peer's — <c>IsOnCooldown</c> is only consulted
		/// inside the caster's own replicate, which observers do not run for peers. The one case
		/// where an observer does run it, forwarded mode, is served by the absolute reconcile
		/// FishNet sends on the tick an observer is added, which carries the cooldown array.
		/// Writing the same framed block with a zero count keeps <see cref="Read"/> unchanged.
		/// </remarks>
		/// <param name="writer">The network writer.</param>
		/// <param name="includeEntries">False to write an empty, still-framed block.</param>
		public void Write(Writer writer, bool includeEntries)
		{
			writer.WriteUInt32(GetCurrentDomainTick());

			/* Everything below is framed by a byte count so Read can resynchronise after rejecting
			 * an untrustworthy count. See COOLDOWN_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(COOLDOWN_PAYLOAD_LENGTH_BYTES);
			int cooldownBlockStart = writer.Position;

			if (!includeEntries)
			{
				writer.WriteInt32(0);
				writer.InsertUInt32Unpacked((uint)(writer.Position - cooldownBlockStart),
					cooldownBlockStart - COOLDOWN_PAYLOAD_LENGTH_BYTES);
				return;
			}

			writer.WriteInt32(cooldowns.Count);
			foreach (KeyValuePair<long, CooldownInstance> cooldown in cooldowns)
			{
				writer.WriteInt64(cooldown.Key);
				writer.WriteUInt32(cooldown.Value.StartTick);
				writer.WriteUInt32(cooldown.Value.DurationTicks);
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - cooldownBlockStart),
				cooldownBlockStart - COOLDOWN_PAYLOAD_LENGTH_BYTES);
		}

		/// <summary>
		/// Returns the current domain tick for payload serialization. Prefers the
		/// authoritative resolve from <see cref="ResolveAuthoritativeTick"/> when the
		/// NetworkObject is initialised, falling back to <see cref="lastReplicateTick"/>.
		/// </summary>
		private uint GetCurrentDomainTick()
		{
			// base.TimeManager dereferences _networkObjectCache, which is null until the
			// NetworkObject is initialized. Guard through the null-safe NetworkObject accessor
			// so we report "no domain yet" (lastReplicateTick) instead of throwing during
			// pre-spawn payload writes.
			if (base.NetworkObject == null || base.TimeManager == null)
			{
				return lastReplicateTick;
			}

			return ResolveAuthoritativeTick(base.TimeManager.LocalTick);
		}

		/// <summary>
		/// Computes the signed tick offset from a source reference tick to a target reference tick.
		/// Returns 0 if either tick is <see cref="TimeManager.UNSET_TICK"/> or the delta exceeds
		/// the supported signed range.
		/// </summary>
		internal static int GetSignedTickOffset(uint sourceReferenceTick, uint targetReferenceTick, string context)
		{
			if (sourceReferenceTick == TimeManager.UNSET_TICK || targetReferenceTick == TimeManager.UNSET_TICK)
			{
				return 0;
			}

			long delta = (long)targetReferenceTick - sourceReferenceTick;
			if (delta < int.MinValue || delta > int.MaxValue)
			{
				Log.Warning("CooldownController",
					$"{context}: tick offset from {sourceReferenceTick} to {targetReferenceTick} is outside the supported signed range; leaving serialized cooldown ticks unchanged.");
				return 0;
			}

			return (int)delta;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		/// <summary>
		/// Adds a signed tick offset to an unsigned tick value, wrapping on overflow.
		/// </summary>
		internal static uint AddSignedTickOffset(uint tick, int tickOffset)
		{
			return unchecked((uint)((long)tick + tickOffset));
		}

		/// <summary>
		/// Removes all cooldowns that have expired as of <paramref name="currentTick"/>.
		/// Because <see cref="CooldownInstance"/> is immutable, this only removes entries —
		/// it never mutates them. Safe to call during replay since the same tick always
		/// produces the same expiration result.
		/// <para>
		/// Performance note: SortedDictionary iteration is O(n log n) per full traversal
		/// (each MoveNext is O(log n)) and provides no early-exit benefit since it is
		/// ordered by ability ID, not expiry tick. Acceptable because typical cooldown
		/// counts per character are small (< 20). If profiling shows this as a hotspot,
		/// consider a secondary structure keyed by expiry tick.
		/// </para>
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		public void ExpireElapsed(uint currentTick)
		{
			keysToRemove.Clear();
			if (cooldowns.Count == 0)
			{
				return;
			}

			foreach (KeyValuePair<long, CooldownInstance> pair in cooldowns)
			{
				if (!pair.Value.IsOnCooldown(currentTick))
				{
					keysToRemove.Add(pair.Key);
				}
			}

			for (int i = 0; i < keysToRemove.Count; i++)
			{
				RemoveCooldown(keysToRemove[i]);
			}
			keysToRemove.Clear();
		}

		/// <summary>
		/// Checks if an ability is on cooldown at the given tick.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>True if on cooldown, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOnCooldown(long id, uint currentTick)
		{
			return cooldowns.TryGetValue(id, out CooldownInstance cd) && cd.IsOnCooldown(currentTick);
		}

		/// <summary>
		/// Maps a raw authoritative tick to the current replicate-domain tick when available.
		/// </summary>
		/// <param name="serverTick">Fallback authoritative tick.</param>
		/// <returns>The current replicate-domain tick if one can be derived, otherwise <paramref name="serverTick"/>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint ResolveAuthoritativeTick(uint serverTick)
		{
			uint replicateReferenceTick = lastReplicateTick;
			if (predictionController != null)
			{
				if (predictionController.PendingReplicateTickSnapshot != TimeManager.UNSET_TICK)
				{
					replicateReferenceTick = predictionController.PendingReplicateTickSnapshot;
				}
				else if (predictionController.CurrentReplicateTickSnapshot != TimeManager.UNSET_TICK)
				{
					replicateReferenceTick = predictionController.CurrentReplicateTickSnapshot;
				}
			}

			if (replicateReferenceTick == TimeManager.UNSET_TICK)
			{
				if (!hasSeenFirstReplicate && !resolveAuthoritativeWarningLogged)
				{
					// Expected until the first replicate arrives — reconcile corrects it.
					Log.Debug("CooldownController",
						$"ResolveAuthoritativeTick called before first OnReplicate. serverTick={serverTick} returned untranslated. StartTick will be corrected by reconcile.");
					resolveAuthoritativeWarningLogged = true;
				}
				// CONSERVATIVE HARDENING: prefer the live TimeManager.LocalTick over the caller's
				// raw serverTick so every pre-replicate cooldown is anchored in the SAME LocalTick
				// domain that TranslatePreReplicateCooldownTicks assumes for its uniform offset. The
				// null guard keeps this safe before the NetworkObject is initialized.
				return (base.NetworkObject != null && base.TimeManager != null)
					? base.TimeManager.LocalTick
					: serverTick;
			}

			return replicateReferenceTick;
		}

		/// <summary>
		/// Tries to get the remaining cooldown time in seconds for an ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <param name="cooldown">Remaining cooldown time in seconds.</param>
		/// <returns>True if found and still on cooldown, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetCooldown(long id, uint currentTick, out float cooldown)
		{
			if (cooldowns.TryGetValue(id, out CooldownInstance cooldownInstance) &&
				cooldownInstance.IsOnCooldown(currentTick))
			{
				cooldown = cooldownInstance.RemainingTime(currentTick);
				return true;
			}
			cooldown = 0.0f;
			return false;
		}

		/// <summary>
		/// Tries to get the full cooldown instance for an ability or consumable.
		/// </summary>
		/// <param name="id">Ability or consumable template ID.</param>
		/// <param name="instance">The cooldown instance.</param>
		/// <returns>True if a cooldown is tracked for the id.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetCooldownInstance(long id, out CooldownInstance instance)
		{
			return cooldowns.TryGetValue(id, out instance);
		}

		/// <summary>
		/// Adds a cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Cooldown instance.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddCooldown(long id, CooldownInstance cooldown)
		{
			bool existed = cooldowns.ContainsKey(id);
			cooldowns[id] = cooldown;
			snapshotDirty = true;

			if (!IsLocalOwner)
			{
				return;
			}

			if (existed)
			{
				ICooldownController.OnUpdateCooldown?.Invoke(id, cooldown);
			}
			else
			{
				ICooldownController.OnAddCooldown?.Invoke(id, cooldown);
			}
		}

		/// <summary>
		/// Removes the cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveCooldown(long id)
		{
			cooldowns.Remove(id);
			snapshotDirty = true;

			// Skip event invocation when invoked from a replayed prediction tick to
			// avoid duplicate UI / ECA dispatch. Non-replay callers (live owner ExpireElapsed
			// pass, external code) still fire the event.
			if (IsLocalOwner && !isReplayingTick)
			{
				ICooldownController.OnRemoveCooldown?.Invoke(id);
			}
		}

		/// <summary>
		/// Clears all cooldowns.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Clear()
		{
			cooldowns.Clear();
			preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
			snapshotDirty = true;
		}

		/// <summary>
		/// Creates a reconcile snapshot of all active cooldowns.
		/// Returns the cached array when cooldowns haven't changed since the last call.
		/// Returns null when no cooldowns are active.
		/// </summary>
		/// <remarks>
		/// Always allocates a fresh array when dirty, even if the length matches.
		/// The delta serializer holds a reference to the previous tick's snapshot;
		/// mutating in-place would silently update that reference, making prev == next
		/// and masking the change (zero bytes sent when bytes should have been sent).
		/// </remarks>
		public CooldownReconcileEntry[] CreateReconcileSnapshot()
		{
			if (cooldowns.Count == 0)
			{
				cachedSnapshot = null;
				snapshotDirty = false;
				return null;
			}

			if (!snapshotDirty && cachedSnapshot != null)
			{
				return cachedSnapshot;
			}

			// Always allocate fresh — never reuse the old array in-place.
			cachedSnapshot = new CooldownReconcileEntry[cooldowns.Count];

			int i = 0;
			foreach (KeyValuePair<long, CooldownInstance> kvp in cooldowns)
			{
				cachedSnapshot[i++] = new CooldownReconcileEntry
				{
					AbilityID = kvp.Key,
					StartTick = kvp.Value.StartTick,
					DurationTicks = kvp.Value.DurationTicks,
				};
			}
			snapshotDirty = false;
			return cachedSnapshot;
		}

		/// <summary>
		/// Restores cooldown state from a reconcile snapshot, replacing current entries only when they differ.
		/// </summary>
		public void RestoreFromReconcile(CooldownReconcileEntry[] entries)
		{
			bool changed = cooldowns.Count != (entries == null ? 0 : entries.Length);
			if (!changed && entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					CooldownReconcileEntry entry = entries[i];
					if (!cooldowns.TryGetValue(entry.AbilityID, out CooldownInstance cooldown) ||
						cooldown.StartTick != entry.StartTick ||
						cooldown.DurationTicks != entry.DurationTicks)
					{
						changed = true;
						break;
					}
				}
			}

			if (!changed)
			{
				return;
			}

			// Build a set of ability IDs that exist in the reconcile snapshot;
			// any current cooldown NOT in this set is being removed.
			reconcileIDs.Clear();
			if (entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					reconcileIDs.Add(entries[i].AbilityID);
				}
			}

			/* What was on cooldown BEFORE this reconcile, so a restored entry can be announced as
			 * an update rather than an add when the bar was already sweeping it. Without the
			 * distinction a correction that merely nudges a start tick would re-fire an add on
			 * every reconcile. */
			preReconcileIDs.Clear();

			// Fire removal events for entries being removed (before clearing). Gated on
			// ownership like the add/update announcements below — these events drive the LOCAL
			// player's hotkey bar, and reconcile deserialisation also runs for objects the local
			// player does not own.
			foreach (long existingID in cooldowns.Keys)
			{
				preReconcileIDs.Add(existingID);

				if (IsLocalOwner && !reconcileIDs.Contains(existingID))
				{
					ICooldownController.OnRemoveCooldown?.Invoke(existingID);
				}
			}

			cooldowns.Clear();
			snapshotDirty = true;
			if (entries == null)
			{
				return;
			}

			float td = TickDelta;
			for (int i = 0; i < entries.Length; i++)
			{
				cooldowns[entries[i].AbilityID] = new CooldownInstance(
					entries[i].StartTick,
					entries[i].DurationTicks,
					td);
			}

			/* Restored cooldowns are announced for the same reason as in Read: a reconcile that
			 * hands back a cooldown the client had dropped left the dictionary correct and the UI
			 * silent, because the hotkey bar's sweep is started by these events and by nothing
			 * else. Fired after the dictionary is whole, and gated on ownership like AddCooldown —
			 * reconcile also runs for predicted objects the local player does not own. */
			if (!IsLocalOwner)
			{
				return;
			}

			foreach (KeyValuePair<long, CooldownInstance> restored in cooldowns)
			{
				if (preReconcileIDs.Contains(restored.Key))
				{
					ICooldownController.OnUpdateCooldown?.Invoke(restored.Key, restored.Value);
				}
				else
				{
					ICooldownController.OnAddCooldown?.Invoke(restored.Key, restored.Value);
				}
			}
		}
	}
}