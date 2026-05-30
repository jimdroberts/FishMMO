using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the application, ticking, and removal of buffs for a character, including network synchronization.
	/// For player characters, ticking is driven by AbilityController.Replicate for prediction determinism.
	/// For NPCs, uses FishNet TimeManager.OnTick for tick-aligned simulation.
	/// </summary>
	public class BuffController : CharacterBehaviour, IBuffController, IPredictableController
	{
		private const uint MissingRawTick = 0u;

		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs before <see cref="AbilityController"/> so buff effects are applied
		/// before ability activation and processing in the same tick.
		/// </summary>
		public int Order => 80;

		[Header("ECA - Buffs")]
		[Tooltip("Triggers invoked when a buff or debuff is applied to this character.")]
		[SerializeField]
		private List<Trigger> onBuffApplyTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when a buff or debuff is removed from this character.")]
		[SerializeField]
		private List<Trigger> onBuffRemoveTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnBuffApplyTriggers => onBuffApplyTriggers;
		/// <inheritdoc />
		public List<Trigger> OnBuffRemoveTriggers => onBuffRemoveTriggers;

		/// <summary>
		/// Internal dictionary mapping buff template IDs to active buff instances.
		/// </summary>
		private SortedDictionary<int, Buff> buffs = new SortedDictionary<int, Buff>();

		/// <summary>
		/// Public accessor for the character's active buffs.
		/// </summary>
		public SortedDictionary<int, Buff> Buffs { get { return buffs; } }

		/// <summary>
		/// Reusable list of keys to remove after update loop (avoids allocation each frame).
		/// </summary>
		private readonly List<int> keysToRemove = new List<int>(); // used by Tick() only

		/// <summary>
		/// Reusable list for tracking buff IDs to remove during RemoveAll. Separate from keysToRemove
		/// to avoid contention if RemoveAll is called from within a Tick callback (e.g., a buff's OnTick triggers a dispel).
		/// This buffer is only used for RemoveAll, which is not called from Tick, so it won't interfere with keysToRemove usage in Tick.
		/// </summary>
		private readonly List<int> removeAllBuffer = new List<int>(); // used by RemoveAll() only

		/// <summary>
		/// Reusable list for <see cref="RemoveRandom"/> eligible-candidate collection.
		/// Separate from <see cref="keysToRemove"/> to avoid contention if called from a Tick callback.
		/// </summary>
		private readonly List<int> eligibleBuffer = new List<int>();

		/// <summary>
		/// Reusable set for tracking buff IDs to remove during <see cref="RestoreFromReconcile"/>.
		/// Separate from <see cref="keysToRemove"/> to avoid contention and provides O(1) removal.
		/// </summary>
		private readonly HashSet<int> reconcileKeysToRemove = new HashSet<int>();

		/// <summary>
		/// Snapshot of buff instances used by <see cref="Tick"/> to iterate without
		/// touching the live <see cref="buffs"/> dictionary. A buff's OnTick handler may
		/// re-enter <see cref="Apply"/> or <see cref="Remove"/> (e.g., a dispel buff that
		/// strips another buff on tick), which would otherwise throw
		/// <c>InvalidOperationException</c> from <see cref="SortedDictionary{TKey,TValue}"/>.
		/// </summary>
		private readonly List<Buff> tickIterationBuffer = new List<Buff>();

		/// <summary>
		/// Reusable list of buffs whose events fired during <see cref="RestoreFromReconcile"/>.
		/// Events are invoked AFTER the buffs collection is fully patched so subscribers cannot
		/// observe a half-patched state if they re-enter the controller.
		/// </summary>
		private readonly List<Buff> reconcileAddedEvents = new List<Buff>();

		/// <summary>
		/// Reusable list of buffs whose remove events fired during <see cref="RestoreFromReconcile"/>.
		/// See <see cref="reconcileAddedEvents"/> for ordering rationale.
		/// </summary>
		private readonly List<Buff> reconcileRemovedEvents = new List<Buff>();

		/// <summary>
		/// Cached reconcile snapshot, reused across ticks when buffs haven't changed.
		/// Invalidated by <see cref="Apply(BaseBuffTemplate, PredictionTick)"/>, <see cref="Remove"/>,
		/// <see cref="RemoveAll"/>, <see cref="RestoreFromReconcile"/>, and <see cref="Tick"/>.
		/// </summary>
		private BuffReconcileEntry[] cachedSnapshot;

		/// <summary>
		/// The replicate input tick captured at the start of each <see cref="OnReplicate"/> call.
		/// Used by <see cref="ApplyAuthoritative"/> to stamp <see cref="Buff.ExpiryTick"/> in the
		/// replicate-tick domain rather than <c>TimeManager.LocalTick</c>.
		///
		/// <para>
		/// <b>Why this matters:</b> FishNet queues client inputs and the server drains them
		/// one per tick. When the queue is depleted (client lag of K ticks),
		/// <c>input.GetTick()</c> falls K ticks behind <c>LocalTick</c>. A buff stamped with
		/// <c>ExpiryTick = LocalTick + D</c> would not expire until the replicate tick reaches
		/// <c>LocalTick + D</c>, which takes <c>D + K</c> server ticks - K ticks too long.
		/// Stamping with <c>lastReplicateTick + D</c> keeps the expiry in the replicate domain
		/// and the wall-clock duration is always exactly <c>D * tickDelta</c> seconds.
		/// </para>
		///
		/// <para>
		/// Region physics triggers fire from KCCPlayer (Order 110), after BuffController
		/// (Order 80) has already set this field for the current tick, so the value is
		/// always current for the physics-triggered authoritative path.
		/// </para>
		/// </summary>
		private uint lastReplicateTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// The <c>TimeManager.LocalTick</c> observed when <see cref="lastReplicateTick"/> was captured.
		/// Used to preserve the raw-authoritative-to-replicate tick offset for events that fire
		/// outside the immediate prediction pipeline step.
		/// </summary>
		private uint lastReplicateLocalTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// When true, <see cref="cachedSnapshot"/> is stale and must be rebuilt.
		/// </summary>
		private bool snapshotDirty = true;

		/// <summary>
		/// True while <see cref="OnReplicate"/> is executing a replayed (reconcile replay) tick.
		/// Mutation helpers (<see cref="Apply(BaseBuffTemplate, PredictionTick)"/>, <see cref="Apply(Buff, bool)"/>,
		/// <see cref="Remove"/>) and the per-tick <see cref="IBuffController.OnBuffTick"/>
		/// dispatch check this flag to suppress UI / ECA events and FX during replay.
		/// Deterministic state mutations (stack changes, expiry, NextTickTick advance) still run
		/// every replay tick so the dictionary stays in lock-step with the authoritative server.
		/// </summary>
		private bool isReplayingTick;

		/// <summary>
		/// Fixed seconds-per-tick, cached from <c>TimeManager.TickDelta</c> in
		/// <see cref="OnStartNetwork"/>. Used for converting float durations to tick counts.
		/// </summary>
		private float tickDelta;

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();
			RefreshTickDelta();
		}

		/// <summary>
		/// Reads the current TickDelta from TimeManager. FishNet does not change TickDelta at
		/// runtime so this is only called from <see cref="OnStartNetwork"/>. TimeManager is
		/// required for deterministic buff expiry — falling back to a hardcoded constant would
		/// silently desync clients running at non-default tick rates (per §3.2).
		/// </summary>
		private void RefreshTickDelta()
		{
			if (base.TimeManager == null)
			{
				throw new System.InvalidOperationException(
					"BuffController.RefreshTickDelta: TimeManager is null. " +
					"Cannot compute deterministic tick delta — networked object must be spawned first.");
			}
			tickDelta = (float)base.TimeManager.TickDelta;
		}

		/// <summary>
		/// Buffs do not contribute owner input into <see cref="CharacterReplicateData"/>.
		/// </summary>
		/// <param name="input">Unified replicate input for this tick.</param>
		public void PopulateInput(ref CharacterReplicateData input)
		{
		}

		/// <summary>
		/// Runs deterministic buff simulation for the prediction tick.
		/// </summary>
		/// <param name="input">Unified replicate input containing the network tick.</param>
		/// <param name="state">Current replicate execution state.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			// Latch the replicate tick before Tick() so that any ApplyAuthoritative call
			// within the same pipeline step (e.g. Region physics triggers from KCCPlayer)
			// stamps ExpiryTick in the replicate domain rather than TimeManager.LocalTick.
			uint inputTick = input.GetTick();
			if (lastReplicateTick == TimeManager.UNSET_TICK &&
				inputTick != TimeManager.UNSET_TICK &&
				base.TimeManager != null)
			{
				TranslatePreReplicateBuffTicks(GetSignedTickOffset(base.TimeManager.LocalTick, inputTick,
					nameof(TranslatePreReplicateBuffTicks)));
			}
			lastReplicateTick = inputTick;
			lastReplicateLocalTick = base.TimeManager != null ? base.TimeManager.LocalTick : lastReplicateTick;

			// Gate event emission for replayed ticks. The deterministic state mutation
			// (expiry, stack updates, NextTickTick advance) still runs every replay tick; only
			// UI/ECA/FX dispatch is suppressed so subscribers don't see duplicate events.
			bool wasReplaying = isReplayingTick;
			isReplayingTick = state.ContainsReplayed();
			ICharacterAttributeController attributeController = null;
			bool suppressAttributeNotifications = isReplayingTick &&
				Character != null &&
				Character.TryGet(out attributeController);
			if (suppressAttributeNotifications)
			{
				attributeController.BeginNotificationSuppression();
			}
			try { Tick(inputTick); }
			finally
			{
				try
				{
					if (suppressAttributeNotifications && attributeController != null)
					{
						attributeController.EndNotificationSuppression();
					}
				}
				finally
				{
					isReplayingTick = wasReplaying;
				}
			}
		}

		/// <summary>
		/// Converts buffs created before the first replicate tick from raw LocalTick space into
		/// the replicate-tick domain used by <see cref="Tick"/>.
		/// </summary>
		/// <param name="tickOffset">Signed offset from raw LocalTick space to replicate-tick space.</param>
		private void TranslatePreReplicateBuffTicks(int tickOffset)
		{
			if (tickOffset == 0 || buffs.Count == 0)
			{
				return;
			}

			foreach (Buff buff in buffs.Values)
			{
				if (buff.ExpiryTick != TimeManager.UNSET_TICK)
				{
					buff.ExpiryTick = AddSignedTickOffset(buff.ExpiryTick, tickOffset);
				}
				if (buff.NextTickTick != TimeManager.UNSET_TICK)
				{
					buff.NextTickTick = AddSignedTickOffset(buff.NextTickTick, tickOffset);
				}
			}
			snapshotDirty = true;
		}

		/// <summary>
		/// Writes buff reconcile state for this tick.
		/// </summary>
		/// <param name="reconcileData">Mutable unified reconcile payload.</param>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			reconcileData.Buffs = CreateReconcileSnapshot();
		}

		/// <summary>
		/// Restores buffs from authoritative reconcile state.
		/// </summary>
		/// <param name="rd">Unified reconcile payload.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			RestoreFromReconcile(rd.Buffs, rd.GetTick());
		}

		/// <summary>
		/// Reads the buff state from the network payload and applies each buff to the character.
		/// Payload ticks are translated from the writer's reference tick into this controller's
		/// current tick domain so remaining buff duration is preserved across spawn sync.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			const int maxPayloadBuffs = 4096;

			// Payload sync is authoritative. Clear any previous local state first so
			// stale buffs from an earlier spawn, scene, or character state do not survive.
			RemoveAll(ignoreInvokeRemove: true);
			cachedSnapshot = null;
			snapshotDirty = true;

			uint payloadReferenceTick = reader.ReadUInt32();
			uint currentReferenceTick = GetCurrentDomainTick();
			int tickOffset = GetSignedTickOffset(payloadReferenceTick, currentReferenceTick, nameof(ReadPayload));

			int buffCount = reader.ReadInt32();
			if (buffCount < 0 || buffCount > maxPayloadBuffs)
			{
				// Reader is shared with subsequent payload fields — drain the remaining
				// buff bytes to keep the stream position valid. Cap iterations to
				// maxPayloadBuffs to prevent a corrupted count from hanging the tick.
				if (buffCount > 0)
				{
					int drainCount = System.Math.Min(buffCount, maxPayloadBuffs);
					for (int d = 0; d < drainCount; d++)
					{
						reader.ReadInt32();  // templateID
						reader.ReadUInt32(); // expiryTick
						reader.ReadUInt32(); // nextTickTick
						reader.ReadInt32();  // stacks
						reader.ReadInt32();  // tickCount
						reader.ReadInt32();  // cumulativeTickMultiplier
					}
				}
				return;
			}
			for (int i = 0; i < buffCount; ++i)
			{
				int templateID = reader.ReadInt32();
				uint expiryTick = reader.ReadUInt32();
				uint nextTickTick = reader.ReadUInt32();
				if (expiryTick != TimeManager.UNSET_TICK)
				{
					expiryTick = AddSignedTickOffset(expiryTick, tickOffset);
				}
				if (nextTickTick != TimeManager.UNSET_TICK)
				{
					nextTickTick = AddSignedTickOffset(nextTickTick, tickOffset);
				}
				int stacks = reader.ReadInt32();
				int tickCount = reader.ReadInt32();
				int cumulativeTickMultiplier = reader.ReadInt32();

				Buff buff = new Buff(templateID, expiryTick, nextTickTick, tickDelta, stacks, tickCount);
				buff.CumulativeTickMultiplier = cumulativeTickMultiplier;
				Apply(buff, suppressFX: false);
			}
		}

		/// <summary>
		/// Writes the current buff state to the network payload for synchronization.
		/// The first field is the current reference tick for the serialized absolute buff ticks.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			writer.WriteUInt32(GetCurrentDomainTick());

			if (buffs.Count < 1)
			{
				writer.WriteInt32(0);
				return;
			}

			writer.WriteInt32(buffs.Count);
			foreach (Buff buff in buffs.Values)
			{
				writer.WriteInt32(buff.Template.ID);
				writer.WriteUInt32(buff.ExpiryTick);
				writer.WriteUInt32(buff.NextTickTick);
				writer.WriteInt32(buff.Stacks);
				writer.WriteInt32(buff.TickCount);
				writer.WriteInt32(buff.CumulativeTickMultiplier);
			}
		}

		private uint GetCurrentDomainTick()
		{
			if (base.TimeManager == null)
			{
				return lastReplicateTick;
			}

			return ResolveAuthoritativeTick(base.TimeManager.LocalTick);
		}

		internal static int GetSignedTickOffset(uint sourceReferenceTick, uint targetReferenceTick, string context)
		{
			if (sourceReferenceTick == TimeManager.UNSET_TICK || targetReferenceTick == TimeManager.UNSET_TICK)
			{
				return 0;
			}

			long delta = (long)targetReferenceTick - sourceReferenceTick;
			if (delta < int.MinValue || delta > int.MaxValue)
			{
				Log.Warning("BuffController",
					$"{context}: tick offset from {sourceReferenceTick} to {targetReferenceTick} is outside the supported signed range; leaving serialized buff ticks unchanged.");
				return 0;
			}

			return (int)delta;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static uint AddSignedTickOffset(uint tick, int tickOffset)
		{
			return unchecked((uint)((long)tick + tickOffset));
		}

		/// <summary>
		/// Deterministic buff tick. Evaluates expiry and tick conditions for all active buffs,
		/// triggers effects, removes expired stacks, and queues fully expired buffs for removal.
		/// Tick-based timing (<see cref="Buff.ExpiryTick"/>, <see cref="Buff.NextTickTick"/>)
		/// produces zero float drift; <see cref="snapshotDirty"/> is only set when state
		/// actually changes, restoring the delta serializer's <c>ReferenceEquals</c> fast-path.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		public void Tick(uint currentTick)
		{
			// Snapshot the current buff set into a reusable buffer before iterating. A buff's
			// OnTick handler may call Apply()/Remove() on this same controller (dispels, chain
			// debuffs, region triggers), which mutates the SortedDictionary and would throw
			// InvalidOperationException if we iterated buffs.Values directly.
			tickIterationBuffer.Clear();
			foreach (Buff b in buffs.Values)
			{
				tickIterationBuffer.Add(b);
			}

			for (int idx = 0; idx < tickIterationBuffer.Count; idx++)
			{
				Buff buff = tickIterationBuffer[idx];

				// A re-entrant Remove() may have already pulled this buff out of the
				// dictionary; skip the stale snapshot entry rather than ticking a removed buff.
				if (!buffs.ContainsKey(buff.Template.ID))
				{
					continue;
				}

				// Propagate the latest tick-delta so RemainingSeconds (UI) stays accurate
				// even for buffs constructed before TimeManager was ready.
				buff.SetTickDelta(tickDelta);

				// Suppress per-tick UI dispatch during replay.
				if (!isReplayingTick)
				{
					IBuffController.OnBuffTick?.Invoke(buff, currentTick);
				}

				// Fire the periodic effect BEFORE the expiry check so a buff that both
				// ticks and expires on the same absolute tick still delivers its final
				// effect. Without this, the last tick of any buff whose Duration is an
				// exact multiple of TickRate is silently skipped.
				if (buff.TryTick(Character, currentTick, tickDelta))
				{
					// NextTickTick, TickCount, and CumulativeTickMultiplier changed.
					snapshotDirty = true;
				}

				if (buff.HasExpired(currentTick))
				{
					if (buff.Stacks > 0)
					{
						// Structural change: topmost stack removed, duration reset to full.
						snapshotDirty = true;
						buff.RemoveStack(Character);
						buff.ResetDuration(currentTick, tickDelta);
					}
					else
					{
						keysToRemove.Add(buff.Template.ID);
					}
				}
			}
			tickIterationBuffer.Clear();

			// Remove() sets snapshotDirty for each expired buff.
			for (int i = 0; i < keysToRemove.Count; i++)
			{
				Remove(keysToRemove[i]);
			}
			keysToRemove.Clear();
		}

		/// <summary>
		/// Applies a buff to the character by template, creating a new instance if needed and handling stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <remarks>
		/// <b>Tick source:</b> Use the explicit tick overload to make the source deterministic.
		/// </remarks>
		/// <summary>
		/// Applies a buff using the provided absolute network tick as the application time.
		/// This should be used by prediction-path callers to compute ExpiryTick deterministically.
		/// </summary>
		public void Apply(BaseBuffTemplate template, PredictionTick currentTick)
		{
			if (template == null) return;

			snapshotDirty = true;

			bool isNew = false;
			if (!buffs.TryGetValue(template.ID, out Buff buffInstance))
			{
				// New buff: constructor is the single source of truth for ExpiryTick.
				// ResetDuration is NOT called here — it only runs for existing buffs below.
				isNew = true;
				buffInstance = new Buff(template.ID, currentTick, tickDelta);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);

				// Skip event/ECA dispatch when applied during a replayed prediction tick.
				if (!isReplayingTick)
				{
					if (template.IsDebuff)
					{
						IBuffController.OnAddDebuff?.Invoke(buffInstance);
					}
					else
					{
						IBuffController.OnAddBuff?.Invoke(buffInstance);
					}
					// Include tick payload so actions triggered by buff apply can use the deterministic tick.
					BuffEventData bed = new BuffEventData(Character, buffInstance);
					bed.Add(new TickEventData(Character, currentTick));
					Character.Invoke(onBuffApplyTriggers, bed);
				}
			}

			if (template.MaxStacks > 0 && buffInstance.Stacks < template.MaxStacks)
			{
				buffInstance.AddStack(Character);
			}

			// Only refresh duration for existing buffs. New buffs already have the
			// correct ExpiryTick from the constructor — calling ResetDuration again
			// would be redundant at best, and wrong if the two code paths ever diverge.
			if (!isNew)
			{
				buffInstance.ResetDuration(currentTick, tickDelta);
			}

			// Skip FX dispatch during replay (FX are one-shot; replaying them duplicates effects).
			if (!isReplayingTick)
			{
				template.OnApplyFX(buffInstance, Character);
			}
		}

		/// <summary>
		/// Applies a buff from a server-authoritative context (Region triggers, Shrine interactions,
		/// and any ECA action that lacks a TickEventData and falls back to a raw tick).
		///
		/// <para>
		/// <paramref name="serverTick"/> is accepted as a fallback for callers that fire before
		/// the first <see cref="OnReplicate"/> (e.g., spawn-time application). Once
		/// <see cref="OnReplicate"/> has run at least once, the raw tick is translated by the
		/// observed <c>LocalTick</c> to replicate-tick offset so that <see cref="Buff.ExpiryTick"/>
		/// is stamped in the replicate-tick domain.
		/// This prevents the buff from lasting longer than its intended duration when the client's
		/// input queue is depleted and <c>input.GetTick()</c> lags behind
		/// <c>TimeManager.LocalTick</c>.
		/// </para>
		/// </summary>
		public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick)
		{
			uint tick = ResolveAuthoritativeTick(serverTick);
			Apply(template, new PredictionTick(tick));
		}

		/// <summary>
		/// Maps a raw authoritative tick to the current replicate-domain tick when available.
		/// </summary>
		/// <param name="serverTick">Fallback authoritative tick.</param>
		/// <returns>The mapped replicate-domain tick if one can be derived, otherwise <paramref name="serverTick"/>.</returns>
		public uint ResolveAuthoritativeTick(uint serverTick)
		{
			if (lastReplicateTick == TimeManager.UNSET_TICK ||
				lastReplicateLocalTick == TimeManager.UNSET_TICK)
			{
				return serverTick;
			}

			if (serverTick == TimeManager.UNSET_TICK || serverTick == MissingRawTick)
			{
				return lastReplicateTick;
			}

			int tickOffset = GetSignedTickOffset(lastReplicateLocalTick, serverTick, nameof(ResolveAuthoritativeTick));
			return AddSignedTickOffset(lastReplicateTick, tickOffset);
		}

		/// <summary>
		/// Applies a pre-constructed buff instance to the character if not already present.
		/// Restores attribute modifiers for the base application and each existing stack
		/// (e.g., from DB or network payload). Stacks are not incremented because they are already set.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
		public void Apply(Buff buff, bool suppressFX = false)
		{
			if (buff == null) return;
			if (buff.Template == null)
			{
				// Template was not resolved (missing asset, stale save, unknown ID).
				// Without it we cannot dispatch OnApply/OnRemove or determine debuff routing,
				// so we drop the buff instead of NRE'ing on buff.Template.ID below.
				Log.Warning("BuffController", "Apply(Buff): Template is null. Dropping orphaned buff instance.");
				return;
			}

			if (!buffs.ContainsKey(buff.Template.ID))
			{
				snapshotDirty = true;
				buff.Apply(Character);
				buffs.Add(buff.Template.ID, buff);

				for (int i = 0; i < buff.Stacks; ++i)
				{
					buff.Template.OnApplyStack(buff, Character);
				}

				if (buff.Template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(buff);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(buff);
				}

				// FX are suppressed during reconcile restoration to avoid redundant sound/VFX
				// on every rollback tick. Payload restore (ReadPayload) passes suppressFX=false
				// so that buffs appearing on initial character load still play their effects.
				if (!suppressFX)
				{
					buff.Template.OnApplyFX(buff, Character);
				}
			}
		}

		/// <summary>
		/// Removes a buff by template ID, cleaning up all stack modifiers and the base application,
		/// then invoking removal events.
		/// </summary>
		/// <param name="buffID">The template ID of the buff to remove.</param>
		public void Remove(int buffID)
		{
			if (buffs.TryGetValue(buffID, out Buff buffInstance))
			{
				snapshotDirty = true;
				BaseBuffTemplate template = buffInstance.Template;
				if (!TryRemoveBuffEffects(buffInstance, buffID, nameof(Remove)))
				{
					return;
				}
				buffs.Remove(buffID);

				// Gate UI/ECA dispatch when invoked from a replayed tick.
				if (!isReplayingTick && template != null)
				{
					if (template.IsDebuff)
					{
						IBuffController.OnRemoveDebuff?.Invoke(buffInstance);
					}
					else
					{
						IBuffController.OnRemoveBuff?.Invoke(buffInstance);
					}
					Character.Invoke(onBuffRemoveTriggers, new BuffEventData(Character, buffInstance));
				}
			}
		}

		/// <summary>
		/// Removes a random non-permanent buff or debuff, filtered by inclusion flags.
		/// Uses a single pass to build eligible candidates, avoiding retry loops.
		/// </summary>
		/// <remarks>
		/// Uses a dedicated <see cref="eligibleBuffer"/> instead of the shared
		/// <see cref="keysToRemove"/> to avoid clearing mid-iteration if called
		/// from within a <see cref="Tick"/> callback (e.g., a buff's OnTick triggers a dispel).
		/// </remarks>
		/// <param name="rng">The random number generator to use.</param>
		/// <param name="includeBuffs">Whether to include buffs in the selection.</param>
		/// <param name="includeDebuffs">Whether to include debuffs in the selection.</param>
		public void RemoveRandom(DeterministicRNG rng, bool includeBuffs = false, bool includeDebuffs = false)
		{
			if (rng == null || buffs.Count < 1) return;

			eligibleBuffer.Clear();
			foreach (var pair in buffs)
			{
				Buff buff = pair.Value;
				if (buff.Template.IsPermanent) continue;
				if (includeBuffs && !buff.Template.IsDebuff)
				{
					eligibleBuffer.Add(pair.Key);
				}
				else if (includeDebuffs && buff.Template.IsDebuff)
				{
					eligibleBuffer.Add(pair.Key);
				}
			}

			if (eligibleBuffer.Count > 0)
			{
				int index = rng.Next(0, eligibleBuffer.Count);
				Remove(eligibleBuffer[index]);
			}
		}

		/// <summary>
		/// Removes all non-permanent buffs from the character, cleaning up all stack modifiers.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		public void RemoveAll(bool ignoreInvokeRemove = false)
		{
			snapshotDirty = true;
			// Use a dedicated buffer so that a RemoveAll() triggered from within a Tick() OnTick
			// callback does not clear the keysToRemove list that Tick() is currently iterating.
			removeAllBuffer.Clear();
			foreach (var pair in buffs)
			{
				Buff buff = pair.Value;
				if (buff == null || buff.Template == null || !buff.Template.IsPermanent)
				{
					removeAllBuffer.Add(pair.Key);
				}
			}

			for (int i = 0; i < removeAllBuffer.Count; i++)
			{
				int key = removeAllBuffer[i];
				if (buffs.TryGetValue(key, out Buff buff))
				{
					BaseBuffTemplate template = buff.Template;
					if (!TryRemoveBuffEffects(buff, key, nameof(RemoveAll)))
					{
						continue;
					}
					buffs.Remove(key);

					if (!ignoreInvokeRemove && !isReplayingTick && template != null)
					{
						if (template.IsDebuff)
						{
							IBuffController.OnRemoveDebuff?.Invoke(buff);
						}
						else
						{
							IBuffController.OnRemoveBuff?.Invoke(buff);
						}
						Character.Invoke(onBuffRemoveTriggers, new BuffEventData(Character, buff));
					}
				}
			}
			removeAllBuffer.Clear();
		}

		/// <summary>
		/// Creates a reconcile snapshot of all active buffs.
		/// Returns the cached array when buffs haven't changed since the last call.
		/// Returns null when no buffs are active.
		/// </summary>
		/// <remarks>
		/// Always allocates a fresh array when dirty, even if the length matches.
		/// The delta serializer holds a reference to the previous tick's snapshot;
		/// mutating in-place would silently update that reference, making prev == next
		/// and masking the change (zero bytes sent when bytes should have been sent).
		/// </remarks>
		public BuffReconcileEntry[] CreateReconcileSnapshot()
		{
			if (buffs.Count == 0)
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
			cachedSnapshot = new BuffReconcileEntry[buffs.Count];

			int i = 0;
			foreach (KeyValuePair<int, Buff> kvp in buffs)
			{
				cachedSnapshot[i++] = new BuffReconcileEntry
				{
					TemplateID = kvp.Value.Template.ID,
					ExpiryTick = kvp.Value.ExpiryTick,
					NextTickTick = kvp.Value.NextTickTick,
					Stacks = kvp.Value.Stacks,
					TickCount = kvp.Value.TickCount,
					CumulativeTickMultiplier = kvp.Value.CumulativeTickMultiplier,
				};
			}
			snapshotDirty = false;
			return cachedSnapshot;
		}

		/// <summary>
		/// Restores buff state from a reconcile snapshot using a diff-first approach.
		/// Only modifies buffs that actually differ from the authoritative state, avoiding
		/// redundant Remove+Apply cycles that would churn attribute modifiers and fire
		/// non-idempotent side effects (sound, VFX, DB writes) on every reconcile tick.
		/// </summary>
		/// <remarks>
		/// For new buffs, the constructor receives 0 stacks and then <see cref="Buff.AddStack"/>
		/// is called incrementally. This matches the normal Apply path where each stack sees
		/// the correct <see cref="Buff.Stacks"/> value at the time of application.
		/// Calling <c>OnApplyStack</c> directly with the final Stacks value pre-set would
		/// produce different results if any template inspects <c>buff.Stacks</c> to scale modifiers.
		/// </remarks>
		/// <param name="entries">Authoritative buff snapshot.</param>
		/// <param name="reconcileTick">Replicate tick associated with the reconcile snapshot.</param>
		public void RestoreFromReconcile(BuffReconcileEntry[] entries, uint reconcileTick)
		{
			bool changed = false;
			reconcileKeysToRemove.Clear();
			reconcileAddedEvents.Clear();
			reconcileRemovedEvents.Clear();
			foreach (int id in buffs.Keys)
			{
				reconcileKeysToRemove.Add(id);
			}

			if (entries != null && entries.Length > 0)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					ref BuffReconcileEntry entry = ref entries[i];
					reconcileKeysToRemove.Remove(entry.TemplateID);

					if (buffs.TryGetValue(entry.TemplateID, out Buff existing))
					{
						if (existing.Template == null)
						{
							reconcileKeysToRemove.Add(entry.TemplateID);
							Log.Warning("BuffController", $"RestoreFromReconcile: existing buff template {entry.TemplateID} is missing; removing stale buff instead of resurrecting it.");
							continue;
						}

						if (existing.Stacks != entry.Stacks)
						{
							changed = true;
							while (existing.Stacks > entry.Stacks)
							{
								existing.RemoveStack(Character);
							}
							while (existing.Stacks < entry.Stacks)
							{
								existing.AddStack(Character);
							}
						}
						if (existing.ExpiryTick != entry.ExpiryTick)
						{
							existing.ExpiryTick = entry.ExpiryTick;
							changed = true;
						}
						if (existing.NextTickTick != entry.NextTickTick)
						{
							existing.NextTickTick = entry.NextTickTick;
							changed = true;
						}
						if (existing.TickCount != entry.TickCount)
						{
							existing.TickCount = entry.TickCount;
							changed = true;
						}
						if (existing.CumulativeTickMultiplier != entry.CumulativeTickMultiplier)
						{
							existing.CumulativeTickMultiplier = entry.CumulativeTickMultiplier;
							changed = true;
						}
					}
					else
					{
						Buff buff = new Buff(
							entry.TemplateID,
							entry.ExpiryTick,
							entry.NextTickTick,
							tickDelta,
							0,
							entry.TickCount);
						buff.CumulativeTickMultiplier = entry.CumulativeTickMultiplier;

						if (buff.Template == null)
						{
							continue;
						}

						buff.Apply(Character);
						buffs[buff.Template.ID] = buff;
						changed = true;

						for (int s = 0; s < entry.Stacks; s++)
						{
							buff.AddStack(Character);
						}

						// Queue the add event for after the patch loop completes so subscribers
						// cannot observe a half-restored buffs collection if they re-enter.
						// FX are intentionally NOT replayed here — Apply(Buff, suppressFX:false)
						// in ReadPayload handles initial character load.
						reconcileAddedEvents.Add(buff);
					}
				}
			}

			foreach (int key in reconcileKeysToRemove)
			{
				if (buffs.TryGetValue(key, out Buff toRemove))
				{
					if (TryRemoveBuffEffects(toRemove, key, nameof(RestoreFromReconcile)))
					{
						buffs.Remove(key);
						changed = true;
						if (toRemove.Template != null)
						{
							reconcileRemovedEvents.Add(toRemove);
						}
					}
				}
			}
			reconcileKeysToRemove.Clear();

			if (changed)
			{
				snapshotDirty = true;
			}

			// Fire add/remove events ONCE per reconcile (not per resimulated tick) so the UI
			// duration bar, sound system, and ECA buff triggers see authoritative
			// add/removes that were not locally predicted. Without this, a buff created
			// purely by the server's authoritative state silently appears in the
			// dictionary but no listener ever learns about it.
			for (int i = 0; i < reconcileAddedEvents.Count; i++)
			{
				Buff added = reconcileAddedEvents[i];
				if (added.Template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(added);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(added);
				}
				BuffEventData eventData = new BuffEventData(Character, added);
				if (reconcileTick != TimeManager.UNSET_TICK)
				{
					eventData.Add(new TickEventData(Character, new PredictionTick(reconcileTick)));
				}
				Character.Invoke(onBuffApplyTriggers, eventData);
			}
			reconcileAddedEvents.Clear();

			for (int i = 0; i < reconcileRemovedEvents.Count; i++)
			{
				Buff removed = reconcileRemovedEvents[i];
				if (removed.Template.IsDebuff)
				{
					IBuffController.OnRemoveDebuff?.Invoke(removed);
				}
				else
				{
					IBuffController.OnRemoveBuff?.Invoke(removed);
				}
				BuffEventData eventData = new BuffEventData(Character, removed);
				if (reconcileTick != TimeManager.UNSET_TICK)
				{
					eventData.Add(new TickEventData(Character, new PredictionTick(reconcileTick)));
				}
				Character.Invoke(onBuffRemoveTriggers, eventData);
			}
			reconcileRemovedEvents.Clear();
		}

		/// <summary>
		/// Resets the buff controller state, properly removing all buffs to undo
		/// attribute modifiers. Without this, <c>buffs.Clear()</c> alone would leave
		/// phantom modifiers on the attribute controller after a reconnect or scene transfer.
		/// </summary>
		/// <param name="asServer">Whether the reset is being performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			lastReplicateTick = TimeManager.UNSET_TICK;
			lastReplicateLocalTick = TimeManager.UNSET_TICK;

			RemoveAll(ignoreInvokeRemove: true);
		}

		private bool TryRemoveBuffEffects(Buff buff, int buffID, string context)
		{
			if (buff == null)
			{
				return true;
			}

			if (buff.Template == null)
			{
				Log.Warning("BuffController", $"{context}: template {buffID} is missing; dropping stale buff without effect cleanup.");
				return true;
			}

			while (buff.Stacks > 0)
			{
				int stacksBefore = buff.Stacks;
				try
				{
					buff.RemoveStack(Character);
				}
				catch (System.Exception ex)
				{
					Log.Warning("BuffController", $"{context}: OnRemoveStack threw for template {buffID}; keeping buff tracked to avoid orphaned modifiers. Exception: {ex}");
					if (buff.Stacks == stacksBefore)
					{
						return false;
					}
				}
			}

			try
			{
				buff.Remove(Character);
				return true;
			}
			catch (System.Exception ex)
			{
				Log.Warning("BuffController", $"{context}: OnRemove threw for template {buffID}; keeping buff tracked to avoid orphaned modifiers. Exception: {ex}");
				return false;
			}
		}
	}
}