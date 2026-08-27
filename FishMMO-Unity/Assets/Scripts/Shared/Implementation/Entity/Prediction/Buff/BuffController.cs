using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
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
		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs after <see cref="KCCPlayer"/> so movement/camera state is current,
		/// and before cooldowns, attributes, and ability activation.
		/// </summary>
		public int Order => 85;

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
		/// Region physics triggers and ability object callbacks can fire before
		/// BuffController has set this field for the current tick. <see cref="ResolveAuthoritativeTick"/>
		/// therefore prefers the prediction driver's pending/current snapshots when available.
		/// </para>
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
		/// Payload reference tick for buffs read before this controller has a usable local
		/// or replicate reference tick. The first valid replicate pass consumes this so
		/// late-join payload ticks can still be translated from the writer's domain.
		/// </summary>
		private uint preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;

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

		/// <summary>
		/// Cached reference to the character prediction controller for resolving authoritative ticks.
		/// </summary>
		private CharacterPredictionController predictionController;

		#region Observed buffs (target frame)

		/// <summary>
		/// Backing store for <see cref="ObservedBuffs"/>.
		/// </summary>
		private ObservedBuffEntry[] observedBuffs = System.Array.Empty<ObservedBuffEntry>();

		/// <inheritdoc />
		public IReadOnlyList<ObservedBuffEntry> ObservedBuffs => observedBuffs;

		/// <inheritdoc />
		public float ObservedBuffsReceivedTime { get; private set; }

		/// <summary>
		/// Set on the server whenever the visible buff set structurally changed, consumed once per
		/// replicate tick.
		/// </summary>
		/// <remarks>
		/// Coalescing to one push per tick is what keeps this cheap. A dispel, a boss phase change
		/// or a re-application cascade can add and remove several buffs inside one tick, and a
		/// push per change would be several observer RPCs for one logical event.
		/// </remarks>
		private bool observedBuffsDirty;

		/// <summary>
		/// Server-side scratch list used to build the observer payload without allocating per push.
		/// </summary>
		private readonly List<ObservedBuffEntry> observedBuffBuffer = new List<ObservedBuffEntry>();

		/// <summary>
		/// Marks the observer-facing buff list as needing a push on the next server tick.
		/// </summary>
		/// <remarks>
		/// Called from every path that structurally changes <see cref="buffs"/> — the same set of
		/// call sites that set <see cref="snapshotDirty"/> — including the ones that run during a
		/// replayed tick, because a replay changes the authoritative set just as much as a first
		/// execution does. The push itself is gated on the server and on a non-replayed tick.
		/// </remarks>
		private void MarkObservedBuffsDirty()
		{
			observedBuffsDirty = true;
		}

		/// <summary>
		/// Builds and pushes the server-filtered observer buff list, if it changed this tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is the whole trust boundary for the target-frame feature.</b> The client is never
		/// asked what it may see and is never sent anything it may not: the list is assembled here,
		/// on the server, from the server's own buff dictionary, and every entry marked
		/// <see cref="BaseBuffTemplate.HiddenFromOthers"/> is dropped before it reaches the wire.
		/// A client that ignores its own UI code still learns nothing extra.
		/// </para>
		/// <para>
		/// Delivery is a broadcast scoped to this NetworkObject's observers — the same set that can
		/// target it. A player who comes into view later is served by <see cref="OnSpawnServer"/>,
		/// which replays the current list to that one connection; the buffered-RPC behaviour the
		/// previous <c>ObserversRpc(BufferLast)</c> provided implicitly.
		/// </para>
		/// </remarks>
		private void PushObservedBuffs()
		{
			observedBuffsDirty = false;

			BuildObservedBuffEntries();
			BroadcastObservedBuffs(observedBuffBuffer.ToArray());
		}

		/// <summary>
		/// Fills <see cref="observedBuffBuffer"/> with the server-filtered visible buff list.
		/// </summary>
		private void BuildObservedBuffEntries()
		{
			observedBuffBuffer.Clear();
			float delta = tickDelta > 0f ? tickDelta : 1f / 30f;
			uint currentTick = GetCurrentDomainTick();

			foreach (Buff buff in buffs.Values)
			{
				BaseBuffTemplate template = buff?.Template;
				if (template == null || template.HiddenFromOthers)
				{
					continue;
				}

				float remaining = 0f;
				if (!template.IsPermanent &&
					buff.ExpiryTick != TimeManager.UNSET_TICK &&
					currentTick != TimeManager.UNSET_TICK)
				{
					int remainingTicks = (int)(buff.ExpiryTick - currentTick);
					remaining = remainingTicks > 0 ? remainingTicks * delta : 0f;
				}

				observedBuffBuffer.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = remaining,
					TotalSeconds = template.Duration,
				});
			}
		}

		/// <summary>
		/// Replays the current visible buff list to a client that starts observing this character
		/// after the last change.
		/// </summary>
		/// <remarks>
		/// The change-gated broadcast reaches whoever is observing when the set CHANGES; without
		/// this, a player targeting a character they just walked up to would see an empty buff bar
		/// until the next buff event on that character. This restores the replay-to-late-joiners
		/// behaviour the previous <c>ObserversRpc(BufferLast)</c> carried. An empty list is skipped
		/// because an empty bar is what the client already assumes.
		/// </remarks>
		public override void OnSpawnServer(NetworkConnection connection)
		{
			base.OnSpawnServer(connection);

			if (buffs.Count == 0 || base.NetworkManager == null || base.NetworkObject == null)
			{
				return;
			}

			BuildObservedBuffEntries();
			if (observedBuffBuffer.Count == 0)
			{
				return;
			}

			base.NetworkManager.ServerManager.Broadcast(connection, new CharacterBuffsBroadcast
			{
				CharacterObjectID = base.NetworkObject.ObjectId,
				Buffs = observedBuffBuffer.ToArray(),
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends the server-filtered observer buff list to everyone who can see this character.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Applied locally before sending, which is what the previous <c>ObserversRpc</c> achieved
		/// with <c>RunLocally</c>. A broadcast is never delivered back to its sender, so without the
		/// local call the server's own <see cref="observedBuffs"/> would stay empty and anything
		/// server side that inspects a character's visible buffs would read nothing.
		/// </para>
		/// <para>
		/// Scoped to this character's observers, so interest management bounds the traffic. Sent
		/// reliably: a dropped buff list leaves an observer showing a stale set until the next
		/// change, which — unlike a dropped ability cast — has no self-correcting replacement.
		/// </para>
		/// </remarks>
		/// <param name="entries">The visible buffs.</param>
		private void BroadcastObservedBuffs(ObservedBuffEntry[] entries)
		{
			ApplyObservedBuffs(entries);

			if (base.NetworkManager == null || base.NetworkObject == null || !base.IsServerStarted)
			{
				return;
			}

			base.NetworkManager.ServerManager.Broadcast(base.NetworkObject, new CharacterBuffsBroadcast
			{
				CharacterObjectID = base.NetworkObject.ObjectId,
				Buffs = entries ?? System.Array.Empty<ObservedBuffEntry>(),
			}, true, Channel.Reliable);
		}

		/// <summary>Stores a received buff list and notifies listeners.</summary>
		private void ApplyObservedBuffs(ObservedBuffEntry[] entries)
		{
			observedBuffs = entries ?? System.Array.Empty<ObservedBuffEntry>();
			ObservedBuffsReceivedTime = Time.unscaledTime;

			IBuffController.OnObservedBuffsChanged?.Invoke(this);
		}

		/// <summary>True once this client has registered the shared buff handler.</summary>
		/// <remarks>
		/// Registered once per client rather than per character. A per-character registration would
		/// invoke one delegate per character in the scene for every buff change anyone makes, so a
		/// 200-player scene would run 200 handlers to deliver one update.
		/// </remarks>
		private static bool buffsBroadcastRegistered;

		/// <summary>Registers the shared buff handler for this client.</summary>
		internal static void RegisterBuffsBroadcast(FishNet.Managing.NetworkManager networkManager)
		{
			if (buffsBroadcastRegistered || networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<CharacterBuffsBroadcast>(OnBuffsBroadcast);
			buffsBroadcastRegistered = true;
		}

		/// <summary>Applies a buff broadcast to whichever character it names.</summary>
		/// <remarks>
		/// Unlike resources, the owner is <b>not</b> skipped. The observed list is a server-filtered
		/// view — hidden buffs removed, durations in seconds — and it is what the owner's own UI
		/// reads for its buff bar, so excluding the owner would leave the local player unable to see
		/// their own buffs.
		/// </remarks>
		private static void OnBuffsBroadcast(CharacterBuffsBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CharacterObjectID, out FishNet.Object.NetworkObject nob) ||
				nob == null)
			{
				return;
			}

			BuffController controller = nob.GetComponent<BuffController>();
			controller?.ApplyObservedBuffs(msg.Buffs);
		}

		#endregion

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();
			RefreshTickDelta();
			predictionController = GetComponent<CharacterPredictionController>();

			/* Register the shared buff handler the first time any character starts on this client.
			 * Never unregistered: ClientManager does not clear handlers on stop, so a per-character
			 * unregister would have to be reference counted or the first despawn would switch off
			 * buff display for every remaining character. */
			if (base.IsClientStarted)
			{
				RegisterBuffsBroadcast(base.NetworkManager);
			}
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
				uint sourceReferenceTick = preReplicatePayloadReferenceTick != TimeManager.UNSET_TICK
					? preReplicatePayloadReferenceTick
					: base.TimeManager.LocalTick;
				TranslatePreReplicateBuffTicks(GetSignedTickOffset(sourceReferenceTick, inputTick,
					nameof(TranslatePreReplicateBuffTicks)));
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
			}
			lastReplicateTick = inputTick;

			if (inputTick != TimeManager.UNSET_TICK)
			{
				hasSeenFirstReplicate = true;
			}

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

			/* Push the observer-facing buff list here rather than from OnReplicate. This runs once
			 * per tick, only on the server (CharacterPredictionController.CreateReconcile gates on
			 * IsServerStarted && IsSpawned), and — critically — OUTSIDE the [Replicate] method, so
			 * the RPC is never dispatched from inside a replay of a past tick. Coalescing to one
			 * push per tick matters: a dispel, a boss phase change or a re-application cascade can
			 * add and remove several buffs within a single tick. */
			if (observedBuffsDirty)
			{
				PushObservedBuffs();
			}
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
		/// <summary>
		/// Width of the byte count that frames this behaviour's spawn payload.
		/// </summary>
		/// <remarks>
		/// Four bytes, written unpacked so the width is fixed and the slot can be reserved before
		/// the length is known. A packed integer would vary in size and could not be backfilled.
		/// </remarks>
		private const int BUFF_PAYLOAD_LENGTH_BYTES = 4;

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			const int maxPayloadBuffs = 4096;

			// Payload sync is authoritative. Clear any previous local state first so
			// stale buffs from an earlier spawn, scene, or character state do not survive.
			RemoveAll(ignoreInvokeRemove: true);
			cachedSnapshot = null;
			snapshotDirty = true;
			MarkObservedBuffsDirty();

			uint payloadReferenceTick = reader.ReadUInt32();

			/* Where this behaviour's data ends, whatever happens below. Every early exit seeks
			 * here before returning so the shared payload reader is left where the next
			 * NetworkBehaviour expects it — see WritePayload. The length is validated against
			 * what the reader actually holds first: this frame exists to survive a payload that
			 * cannot be trusted, which makes the frame's own length the one value that must be
			 * checked rather than believed. Reader.Position is a plain field with no bounds
			 * check, so a length that overflows int or overruns the buffer would turn a
			 * recoverable abort into an out-of-range read for whoever reads next. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("BuffController",
					$"ReadPayload: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in the " +
					"spawn payload. The stream cannot be resynchronised; discarding the remainder.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				reader.Position += remainingBytes;
				return;
			}
			int buffBlockLength = (int)declaredLength;
			int buffBlockEnd = reader.Position + buffBlockLength;

			uint currentReferenceTick = GetCurrentDomainTick();
			bool deferPayloadTranslation = currentReferenceTick == TimeManager.UNSET_TICK &&
				payloadReferenceTick != TimeManager.UNSET_TICK;

			// INVARIANT: deferring payload translation is only legal when there is NO LocalTick
			// anchor available (TimeManager not yet present). In production TimeManager is always
			// present once the object is spawned, so GetCurrentDomainTick() resolves to a valid
			// LocalTick and deferPayloadTranslation MUST be false. If it were ever true with a live
			// TimeManager, payload buffs (anchored to payloadReferenceTick) and ApplyAuthoritative
			// buffs (anchored to LocalTick) would receive DIFFERENT pre-replicate offsets, splitting
			// the single uniform translation in TranslatePreReplicateBuffTicks and desyncing expiry
			// between client and server. Assert the invariant so a future regression surfaces loudly.
			if (deferPayloadTranslation && base.NetworkObject != null && base.TimeManager != null)
			{
				Log.Error("BuffController",
					"ReadPayload deferred payload tick translation while TimeManager was live. " +
					"This splits the pre-replicate anchor domain (payload vs. LocalTick) and will desync " +
					"buff expiry. Forcing immediate LocalTick-anchored translation to preserve determinism.");
				currentReferenceTick = base.TimeManager.LocalTick;
				deferPayloadTranslation = false;
			}

			int tickOffset = deferPayloadTranslation ? 0 :
				GetSignedTickOffset(payloadReferenceTick, currentReferenceTick, nameof(ReadPayload));

			int buffCount = reader.ReadInt32();
			if (buffCount < 0 || buffCount > maxPayloadBuffs)
			{
				Log.Error("BuffController",
					$"ReadPayload: buff count {buffCount} is outside [0, {maxPayloadBuffs}]. Aborting payload read.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				/* Seek, do not drain. The per-entry sizes a drain would need are derived from the
				 * count that was just rejected, so a capped drain silently desynchronised the
				 * stream for any count above the cap; the frame written by WritePayload is the
				 * only thing that can resynchronise it. */
				reader.Position = buffBlockEnd;
				return;
			}
			preReplicatePayloadReferenceTick = deferPayloadTranslation
				? payloadReferenceTick
				: TimeManager.UNSET_TICK;
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

			/* Belt and braces on the success path too. If the two sides ever disagree about the
			 * shape of this block the frame absorbs it here rather than corrupting the behaviour
			 * after this one, and says so once instead of failing invisibly. */
			if (reader.Position != buffBlockEnd)
			{
				Log.Error("BuffController",
					$"ReadPayload consumed {reader.Position - (buffBlockEnd - buffBlockLength)} of " +
					$"{buffBlockLength} framed bytes. Seeking to the end of the block; the buff " +
					"state read above may be incomplete.");
				reader.Position = buffBlockEnd;
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

			/* Everything below is framed by a byte count.
			 *
			 * FishNet packs every NetworkBehaviour's payload into one buffer with no per-behaviour
			 * framing, so a reader that stops early leaves every behaviour after it reading from
			 * the wrong offset. ReadPayload used to defend against an untrustworthy buff count by
			 * draining it, but the drain had to be capped at maxPayloadBuffs to stop an
			 * adversarial count stalling the main thread — which left every count above that cap
			 * desynchronising the stream anyway. A length cannot be drained past; it can be
			 * seeked to. See BUFF_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(BUFF_PAYLOAD_LENGTH_BYTES);
			int buffBlockStart = writer.Position;

			/* Filtered per connection. The owner needs the full set — its own hidden buffs are its
			 * prediction state, restored from this payload on spawn and reconnect. Everyone else
			 * gets the same visibility rule the broadcast path applies: HiddenFromOthers never
			 * leaves the server. This used to write the full dictionary to every connection, which
			 * let a packet-inspecting client read buffs no UI would ever show it. Observers no
			 * longer simulate their peers, so they have no simulation need for the hidden entries
			 * either — the filtered list is both the private one and the sufficient one.
			 *
			 * Safe to vary by connection: FishNet builds the spawn message per receiving
			 * connection (ServerObjects.Observers calls WriteSpawn(nob, writer, conn) inside the
			 * per-connection rebuild), so no two receivers share this buffer. */
			bool includeHidden = PayloadVisibility.IsOwner(this, conn);

			if (buffs.Count < 1)
			{
				writer.WriteInt32(0);
			}
			else
			{
				int visibleCount = 0;
				foreach (Buff buff in buffs.Values)
				{
					if (includeHidden || buff.Template == null || !buff.Template.HiddenFromOthers)
					{
						visibleCount++;
					}
				}

				writer.WriteInt32(visibleCount);
				foreach (Buff buff in buffs.Values)
				{
					if (!includeHidden && buff.Template != null && buff.Template.HiddenFromOthers)
					{
						continue;
					}
					writer.WriteInt32(buff.Template.ID);
					writer.WriteUInt32(buff.ExpiryTick);
					writer.WriteUInt32(buff.NextTickTick);
					writer.WriteInt32(buff.Stacks);
					writer.WriteInt32(buff.TickCount);
					writer.WriteInt32(buff.CumulativeTickMultiplier);
				}
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - buffBlockStart),
				buffBlockStart - BUFF_PAYLOAD_LENGTH_BYTES);
		}

		/// <inheritdoc />
		public uint GetCurrentDomainTick()
		{
			// base.TimeManager dereferences _networkObjectCache, which is null until the
			// NetworkObject is initialized (e.g. while ReadPayload runs during spawn sync).
			// Guard through the null-safe NetworkObject accessor so we report "no domain yet"
			// (lastReplicateTick, UNSET pre-first-replicate) instead of throwing. This is the
			// signal ReadPayload uses to defer payload translation until the first replicate.
			if (base.NetworkObject == null || base.TimeManager == null)
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
					IBuffController.OnBuffTick?.Invoke(this, buff, currentTick);
				}

				// Fire the periodic effect BEFORE the expiry check so a buff that both
				// ticks and expires on the same absolute tick still delivers its final
				// effect. Without this, the last tick of any buff whose Duration is an
				// exact multiple of TickRate is silently skipped.
				if (buff.TryTick(Character, currentTick, tickDelta, isReplayingTick))
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
						MarkObservedBuffsDirty();
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
		/// Applies a buff using the provided prediction tick as the application time.
		/// This should be used by prediction-path callers to compute ExpiryTick deterministically
		/// rather than using <c>TimeManager.LocalTick</c>.
		/// Creates a new buff instance if needed and handles stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <param name="currentTick">The prediction tick at the time of application.</param>
		/// <param name="caster">The character applying the buff, snapshotted for attribution. May be null.</param>
		public void Apply(BaseBuffTemplate template, PredictionTick currentTick, ICharacter caster = null)
		{
			// The prediction path already holds a replicate-domain tick (it came from
			// CharacterReplicateData.GetPredictionTick), so pass its raw value straight
			// into the single apply core.
			ApplyResolved(template, currentTick.Value, caster);
		}

		/// <summary>
		/// Single apply core. The <paramref name="replicateDomainTick"/> MUST already be in the
		/// replicate-input domain that <see cref="Tick(uint)"/> evaluates expiry against — either
		/// because it came from a <see cref="PredictionTick"/> (prediction path) or because it was
		/// mapped through <see cref="ResolveAuthoritativeTick"/> (authoritative path). This is the
		/// ONLY sanctioned place in the controller that fabricates a <see cref="PredictionTick"/>
		/// for the buff-apply <see cref="TickEventData"/>; doing it here (rather than at each call
		/// site) keeps the "raw uint must be replicate-domain before it becomes a PredictionTick"
		/// contract in one auditable location.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <param name="replicateDomainTick">Application tick, guaranteed to be in the replicate domain.</param>
		/// <param name="caster">The character applying the buff, snapshotted for attribution. May be null.</param>
		private void ApplyResolved(BaseBuffTemplate template, uint replicateDomainTick, ICharacter caster = null)
		{
			if (template == null) return;


			// Dead characters cannot receive buffs or debuffs.
			if (Character.IsFlagged(CharacterFlags.IsDead)) return;
			bool isNew = false;
			bool changed = false;
			if (!buffs.TryGetValue(template.ID, out Buff buffInstance))
			{
				// New buff: constructor is the single source of truth for ExpiryTick.
				// ResetDuration is NOT called here — it only runs for existing buffs below.
				isNew = true;
				buffInstance = new Buff(template.ID, replicateDomainTick, tickDelta);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);
				changed = true;

				// Skip event/ECA dispatch when applied during a replayed prediction tick.
				if (!isReplayingTick)
				{
					if (template.IsDebuff)
					{
						IBuffController.OnAddDebuff?.Invoke(this, buffInstance);
					}
					else
					{
						IBuffController.OnAddBuff?.Invoke(this, buffInstance);
					}
					// Include tick payload so actions triggered by buff apply can use the deterministic tick.
					// replicateDomainTick is guaranteed replicate-domain by both entry points, so this is a
					// legitimate (and the only) PredictionTick fabrication in the apply path.
					BuffEventData bed = new BuffEventData(Character, buffInstance);
					bed.Add(new TickEventData(Character, new PredictionTick(replicateDomainTick)));
					Character.Invoke(onBuffApplyTriggers, bed);
				}
			}

			/* Snapshot on every application, new or refreshed, so a DoT re-applied by a second
			 * attacker credits the one currently sustaining it rather than whoever happened to
			 * land the first stack. SetCaster ignores nulls, so a refresh from a source with no
			 * initiator leaves existing attribution intact. */
			buffInstance.SetCaster(caster);

			if (template.MaxStacks > 0 && buffInstance.Stacks < template.MaxStacks)
			{
				buffInstance.AddStack(Character);
				changed = true;
			}

			// Only refresh duration for existing buffs. New buffs already have the
			// correct ExpiryTick from the constructor — calling ResetDuration again
			// would be redundant at best, and wrong if the two code paths ever diverge.
			if (!isNew)
			{
				uint expectedExpiry = Buff.GetExpiryTick(template, replicateDomainTick, tickDelta);
				if (expectedExpiry != buffInstance.ExpiryTick)
				{
					buffInstance.ResetDuration(replicateDomainTick, tickDelta);
					changed = true;
				}
			}

			// Skip FX dispatch during replay (FX are one-shot; replaying them duplicates effects).
			if (!isReplayingTick)
			{
				template.OnApplyFX(buffInstance, Character);
			}

			if (changed)
			{
				snapshotDirty = true;
				MarkObservedBuffsDirty();
			}
		}

		/// <summary>
		/// Applies a buff from a server-authoritative context (Region triggers, Shrine interactions,
		/// and any ECA action that lacks a TickEventData and falls back to a raw tick).
		///
		/// <para>
		/// <paramref name="serverTick"/> is accepted as a fallback for callers that fire before
		/// the first <see cref="OnReplicate"/> (e.g., spawn-time application). Once
		/// <see cref="OnReplicate"/> has run at least once, raw authoritative ticks collapse to
		/// the current replicate-domain tick. They must not preserve elapsed <c>LocalTick</c>
		/// drift because <see cref="Tick(uint)"/> evaluates expiry against
		/// <c>input.GetTick()</c>, which can lag behind or stall relative to
		/// <c>TimeManager.LocalTick</c>.
		/// </para>
		/// </summary>
		public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick, ICharacter caster = null)
		{
			// Map the raw authoritative tick into the replicate domain BEFORE applying so the
			// PredictionTick contract holds: ApplyResolved only ever receives replicate-domain ticks.
			ApplyResolved(template, ResolveAuthoritativeTick(serverTick), caster);
		}

		/// <summary>
		/// Maps a raw authoritative tick to the current replicate-domain tick when available.
		/// </summary>
		/// <param name="serverTick">Fallback authoritative tick.</param>
		/// <returns>The current replicate-domain tick if one can be derived, otherwise <paramref name="serverTick"/>.</returns>
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
					Log.Debug("BuffController",
						$"ResolveAuthoritativeTick called before first OnReplicate. serverTick={serverTick} returned untranslated. ExpiryTick will be corrected by reconcile.");
					resolveAuthoritativeWarningLogged = true;
				}
				// LOAD-BEARING fallback — NOT a bug. Before the first replicate there is no
				// replicate-domain reference yet, so we return the raw authoritative tick
				// (TimeManager.LocalTick in production). Every pre-replicate buff is therefore
				// anchored in the SAME raw-LocalTick domain. When the first replicate arrives,
				// OnReplicate calls TranslatePreReplicateBuffTicks with the single uniform offset
				// (firstInputTick - LocalTickAtFirstReplicate), which shifts ALL such buffs into
				// the replicate domain at once. Because each buff's raw expiry already embeds its
				// own apply-LocalTick, one uniform offset is correct for every buff regardless of
				// when it was applied. Returning anything other than this consistent LocalTick
				// anchor here (e.g. a fabricated replicate tick) would break that uniform-offset
				// translation. See AuthoritativeTickTranslationTests
				// .PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain.
				//
				// CONSERVATIVE HARDENING: prefer the live TimeManager.LocalTick over the caller's
				// serverTick. GetCurrentDomainTick already passes LocalTick, but ApplyAuthoritative
				// forwards a RAW server tick. Substituting LocalTick here guarantees every
				// pre-replicate buff is anchored in the SAME LocalTick domain that
				// TranslatePreReplicateBuffTicks assumes when it later applies the uniform
				// (firstInputTick - LocalTickAtFirstReplicate) offset. We deliberately do NOT record
				// preReplicatePayloadReferenceTick here: the existing OnReplicate translation anchors
				// on LocalTick-at-first-replicate (W_f), which is the correct uniform source for every
				// buff regardless of its individual apply time. The null guard keeps this safe before
				// the NetworkObject is initialized (returns the raw serverTick as a last resort).
				return (base.NetworkObject != null && base.TimeManager != null)
					? base.TimeManager.LocalTick
					: serverTick;
			}

			return replicateReferenceTick;
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

			// Dead characters cannot receive buffs or debuffs.
			if (Character.IsFlagged(CharacterFlags.IsDead)) return;

			if (!buffs.ContainsKey(buff.Template.ID))
			{
				snapshotDirty = true;
				MarkObservedBuffsDirty();
				buff.Apply(Character);
				buffs.Add(buff.Template.ID, buff);

				for (int i = 0; i < buff.Stacks; ++i)
				{
					buff.Template.OnApplyStack(buff, Character);
				}

				if (buff.Template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(this, buff);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(this, buff);
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
				MarkObservedBuffsDirty();
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
						IBuffController.OnRemoveDebuff?.Invoke(this, buffInstance);
					}
					else
					{
						IBuffController.OnRemoveBuff?.Invoke(this, buffInstance);
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
			MarkObservedBuffsDirty();
			preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
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
							IBuffController.OnRemoveDebuff?.Invoke(this, buff);
						}
						else
						{
							IBuffController.OnRemoveBuff?.Invoke(this, buff);
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
				MarkObservedBuffsDirty();
			}

			// Fire remove events BEFORE add events so that subscribers iterating the
			// active buff collection during an "add" handler see the post-remove state.
			// This matches the natural order: old buffs are removed, then new buffs are added.
			for (int i = 0; i < reconcileRemovedEvents.Count; i++)
			{
				Buff removed = reconcileRemovedEvents[i];
				if (removed.Template.IsDebuff)
				{
					IBuffController.OnRemoveDebuff?.Invoke(this, removed);
				}
				else
				{
					IBuffController.OnRemoveBuff?.Invoke(this, removed);
				}
				BuffEventData eventData = new BuffEventData(Character, removed);
				if (reconcileTick != TimeManager.UNSET_TICK)
				{
					eventData.Add(new TickEventData(Character, new PredictionTick(reconcileTick)));
				}
				Character.Invoke(onBuffRemoveTriggers, eventData);
			}
			reconcileRemovedEvents.Clear();

			for (int i = 0; i < reconcileAddedEvents.Count; i++)
			{
				Buff added = reconcileAddedEvents[i];
				if (added.Template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(this, added);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(this, added);
				}
				BuffEventData eventData = new BuffEventData(Character, added);
				if (reconcileTick != TimeManager.UNSET_TICK)
				{
					eventData.Add(new TickEventData(Character, new PredictionTick(reconcileTick)));
				}
				Character.Invoke(onBuffApplyTriggers, eventData);
			}
			reconcileAddedEvents.Clear();
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
			hasSeenFirstReplicate = false;
			resolveAuthoritativeWarningLogged = false;
			preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;

			RemoveAll(ignoreInvokeRemove: true);
		}

		/// <summary>
		/// Safely removes a buff's effects from the character by draining all stacks and calling
		/// <see cref="Buff.Remove"/>. Returns false if an exception occurs during effect cleanup,
		/// indicating the buff should remain tracked to avoid orphaned attribute modifiers.
		/// </summary>
		/// <param name="buff">The buff instance to clean up.</param>
		/// <param name="buffID">The template ID of the buff (for warning logging).</param>
		/// <param name="context">Name of the calling method (for warning logging).</param>
		/// <returns>True if effects were fully removed; false if an exception occurred.</returns>
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