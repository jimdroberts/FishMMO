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
	/// Controls the application, ticking, and removal of buffs for a character, including network
	/// synchronization.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Who simulates.</b> The server and the owning client. Both are ticked from
	/// <see cref="OnReplicate"/>, which <see cref="CharacterPredictionController"/> drives from its
	/// own <c>[Replicate]</c> method — an NPC's runs on the server only, a player's on the server
	/// and on the owner. State forwarding is authored OFF on every prefab, so an observer never
	/// runs Replicate or OnReconcile for somebody else's character.
	/// </para>
	/// <para>
	/// <b>What observers do.</b> They hold the same <see cref="Buffs"/> entries everyone else does,
	/// so Inspect, the target frame and aggro logic read real state rather than a display
	/// projection, and they count those durations down locally from
	/// <c>TimeManager.OnTick</c> — the peer's controller is a spawned NetworkBehaviour with a
	/// perfectly good TimeManager, it just has no replicate to drive it. What they do NOT do is
	/// APPLY them: the attribute broadcast already carries every buff's contribution inside
	/// <c>ExternalModifier</c>, and the resource broadcast already carries the result of every
	/// damage-over-time tick, so running the effects here would count both twice. See
	/// <c>SimulatesBuffEffects</c>.
	/// </para>
	/// </remarks>
	public class BuffController : CharacterBehaviour, IBuffController, IPredictableController, IModelReadyHandler
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
		/// Set whenever the visible buff set structurally changed — a buff added, removed, or its
		/// stack count moved. Consumed once per replicate tick.
		/// </summary>
		/// <remarks>
		/// Coalescing to one push per tick is what keeps this cheap. A dispel, a boss phase change
		/// or a re-application cascade can add and remove several buffs inside one tick, and a
		/// push per change would be several observer messages for one logical event.
		/// </remarks>
		private bool observedBuffsDirty;

		/// <summary>
		/// Server-side scratch list used to build the observer payload without allocating per push.
		/// Always holds the FULL visible strip; the delta is derived from it.
		/// </summary>
		private readonly List<ObservedBuffEntry> observedBuffBuffer = new List<ObservedBuffEntry>();

		/// <summary>
		/// The full visible strip as of the last push, keyed by template id — what this character's
		/// existing observers are currently holding, and the baseline the delta is measured against.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Per CHARACTER, not per observer, which is what keeps one serialized message fannable out
		/// to every observer. That only works because every entry is absolute and self-describing:
		/// see the remarks on <see cref="CharacterBuffsBroadcast"/>.
		/// </para>
		/// <para>
		/// Empty means "no usable baseline", which forces the next push to be a full set. That is
		/// the state after a spawn and after <see cref="ResetObservedBuffBaseline"/>, and it is why
		/// a pooled NPC cannot inherit the previous occupant's strip as a baseline and send a delta
		/// against buffs its observers never saw.
		/// </para>
		/// </remarks>
		private readonly Dictionary<int, ObservedBuffEntry> lastPushedObservedBuffs = new Dictionary<int, ObservedBuffEntry>();

		/// <summary>True once <see cref="lastPushedObservedBuffs"/> reflects a push that went out.</summary>
		private bool hasObservedBuffBaseline;

		/// <summary>Server-side scratch: entries added or restacked since the last push.</summary>
		private readonly List<ObservedBuffEntry> observedBuffDeltaBuffer = new List<ObservedBuffEntry>();

		/// <summary>Server-side scratch: template ids that left the strip since the last push.</summary>
		private readonly List<int> observedBuffRemovedBuffer = new List<int>();

		/// <summary>Client-side scratch used to merge a delta into the current strip.</summary>
		private readonly List<ObservedBuffEntry> observedBuffMergeBuffer = new List<ObservedBuffEntry>();

		/// <summary>
		/// The character's prediction controller, cached in <see cref="OnStartNetwork"/>.
		/// </summary>
		/// <remarks>
		/// Supplies the pending replicate tick that buff timing is stamped against, so a buff
		/// applied between replicates lands in the right tick domain rather than the previous one.
		/// </remarks>
		private CharacterPredictionController predictionController;

		/// <summary>
		/// Drops the delta baseline so the next push states the whole strip.
		/// </summary>
		/// <remarks>
		/// Called when this character's observers can no longer be assumed to hold what was last
		/// sent — a despawn, or a pooled object being reused as somebody else.
		/// </remarks>
		private void ResetObservedBuffBaseline()
		{
			lastPushedObservedBuffs.Clear();
			hasObservedBuffBaseline = false;
		}

		/// <summary>
		/// Marks the observer-facing buff list as needing a push on the next tick.
		/// </summary>
		/// <remarks>
		/// Called from every path that STRUCTURALLY changes <see cref="buffs"/> — a buff added,
		/// removed, or its stack count changed — including the ones that run during a replayed
		/// tick, because a replay changes the authoritative set just as much as a first execution
		/// does. A change to remaining duration alone goes through
		/// <see cref="MarkObservedBuffsTimingDirty"/> instead.
		/// </remarks>
		private void MarkObservedBuffsDirty()
		{
			observedBuffsDirty = true;
		}

		/// <summary>
		/// True when the observed list is worth (re)sending this tick.
		/// </summary>
		private bool ShouldPushObservedBuffs()
		{
			/* Structural changes only. Timing no longer travels: every peer counts its own bars
			 * down from its own TimeManager, so the periodic "the numbers have drifted" resend —
			 * and the baseline bookkeeping that decided when to send it — has nothing left to
			 * correct and is gone. */
			return observedBuffsDirty;
		}


		/// <summary>
		/// Seconds remaining on <paramref name="buff"/>, in the shape observers are sent.
		/// </summary>
		/// <param name="buff">The buff being described.</param>
		/// <param name="template">The buff's template, already resolved by the caller.</param>
		/// <param name="currentTick">Current domain tick.</param>
		/// <param name="delta">Seconds per tick.</param>
		private static float ComputeObservedRemaining(Buff buff, BaseBuffTemplate template, uint currentTick, float delta)
		{
			if (template.IsPermanent ||
				buff.ExpiryTick == TimeManager.UNSET_TICK ||
				currentTick == TimeManager.UNSET_TICK)
			{
				return 0f;
			}

			int remainingTicks = (int)(buff.ExpiryTick - currentTick);
			return remainingTicks > 0 ? remainingTicks * delta : 0f;
		}

		/// <summary>
		/// Builds and pushes the server-filtered observer buff list, if it changed this tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Assembled on the server, from the server's own buff dictionary.</b> There is no
		/// per-buff visibility filter: buffs are not hidden from other players, so what a character
		/// is carrying is exactly what its observers are told. The filter that used to sit here
		/// (<c>HiddenFromOthers</c>) was authored on no buff in the project and has been removed.
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
			/* Which flag brought us here decides the SHAPE of the message, so read them before the
			 * reset. A structural change is describable as a delta; a timing resync is not — every
			 * visible buff's remaining duration has moved, so there is nothing to leave out. */
			bool structural = observedBuffsDirty;

			observedBuffsDirty = false;

			BuildObservedBuffEntries();


			bool fullSet = !structural || !hasObservedBuffBaseline;
			if (fullSet)
			{
				ObservedBuffEntry[] entries = observedBuffBuffer.ToArray();
				AdoptObservedBuffBaseline();
				BroadcastObservedBuffs(entries, System.Array.Empty<int>(), true);
				return;
			}

			BuildObservedBuffDelta();

			/* Structurally dirty but nothing structural actually differs — a buff added and removed
			 * inside one tick, or a stack that went up and back down. The strip observers hold is
			 * still correct, so adopt the rebuilt baseline and send nothing. */
			if (observedBuffDeltaBuffer.Count == 0 && observedBuffRemovedBuffer.Count == 0)
			{
				AdoptObservedBuffBaseline();
				return;
			}

			ObservedBuffEntry[] changed = observedBuffDeltaBuffer.ToArray();
			int[] removed = observedBuffRemovedBuffer.ToArray();
			AdoptObservedBuffBaseline();
			BroadcastObservedBuffs(changed, removed, false);
		}

		/// <summary>
		/// Fills <see cref="observedBuffDeltaBuffer"/> and <see cref="observedBuffRemovedBuffer"/>
		/// from <see cref="observedBuffBuffer"/> against <see cref="lastPushedObservedBuffs"/>.
		/// </summary>
		/// <remarks>
		/// Structural comparison only — see <see cref="ObservedBuffEntry.StructurallyEquals"/> for
		/// why remaining duration is excluded. An entry that is structurally unchanged is left out
		/// even though its remaining duration has moved; the receiver counts that down itself, and
		/// the timing gate sends a full set when its belief drifts too far.
		/// </remarks>
		private void BuildObservedBuffDelta()
		{
			observedBuffDeltaBuffer.Clear();
			observedBuffRemovedBuffer.Clear();

			for (int i = 0; i < observedBuffBuffer.Count; ++i)
			{
				ObservedBuffEntry entry = observedBuffBuffer[i];
				if (!lastPushedObservedBuffs.TryGetValue(entry.TemplateID, out ObservedBuffEntry previous) ||
					!entry.StructurallyEquals(previous))
				{
					observedBuffDeltaBuffer.Add(entry);
				}
			}

			foreach (KeyValuePair<int, ObservedBuffEntry> kvp in lastPushedObservedBuffs)
			{
				bool stillPresent = false;
				for (int i = 0; i < observedBuffBuffer.Count; ++i)
				{
					if (observedBuffBuffer[i].TemplateID == kvp.Key)
					{
						stillPresent = true;
						break;
					}
				}
				if (!stillPresent)
				{
					observedBuffRemovedBuffer.Add(kvp.Key);
				}
			}
		}

		/// <summary>
		/// Adopts the full strip in <see cref="observedBuffBuffer"/> as the delta baseline.
		/// </summary>
		/// <remarks>
		/// Called on every push, including the ones that send nothing: after a push the observers'
		/// strip and this baseline agree, and a baseline left behind would re-send changes that
		/// were already delivered.
		/// </remarks>
		private void AdoptObservedBuffBaseline()
		{
			lastPushedObservedBuffs.Clear();
			for (int i = 0; i < observedBuffBuffer.Count; ++i)
			{
				ObservedBuffEntry entry = observedBuffBuffer[i];
				lastPushedObservedBuffs[entry.TemplateID] = entry;
			}
			hasObservedBuffBaseline = true;
		}

		/// <summary>
		/// Fills the OWNER's own <see cref="ObservedBuffs"/> from its own simulation, sending
		/// nothing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The owner is excluded from <see cref="CharacterBuffsBroadcast"/> (see
		/// <see cref="BroadcastObservedBuffs"/>) because it already holds the authoritative-by-
		/// reconcile buff dictionary this list is derived from. But the target frame reads
		/// <see cref="ObservedBuffs"/> uniformly for whatever is targeted, including the local
		/// player targeting themselves, so the list still has to exist locally — it is just built
		/// here, from <see cref="buffs"/>, for zero bytes.
		/// </para>
		/// <para>
		/// Runs outside the replicate body, on a non-replayed tick, so the change event is raised
		/// once per real tick rather than once per replayed tick.
		/// </para>
		/// </remarks>
		private void RefreshObservedBuffsLocally()
		{
			observedBuffsDirty = false;

			BuildObservedBuffEntries();
			ApplyObservedBuffs(observedBuffBuffer.ToArray());
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
				if (template == null)
				{
					continue;
				}

				observedBuffBuffer.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = ComputeObservedRemaining(buff, template, currentTick, delta),
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
		/// <para>
		/// The owner is skipped: it is not sent the observed list at all (see
		/// <see cref="BroadcastObservedBuffs"/>) because it builds its own from the simulation
		/// dictionary the spawn payload just handed it.
		/// </para>
		/// </remarks>
		public override void OnSpawnServer(NetworkConnection connection)
		{
			base.OnSpawnServer(connection);

			if (buffs.Count == 0 || base.NetworkManager == null || base.NetworkObject == null)
			{
				return;
			}

			if (PayloadVisibility.IsOwner(this, connection))
			{
				return;
			}

			/* The same gate every other observer push carries. A forwarded object delivers buffs
			 * through the reconcile and its observers build FX from the simulation dictionary, so
			 * this list would be a second, competing source for the same state — the one place the
			 * broadcast and reconcile transports were not held mutually exclusive. */
			if (!ObserverSyncMode.ShouldBroadcastToObservers(base.NetworkObject))
			{
				return;
			}

			BuildObservedBuffEntries();
			if (observedBuffBuffer.Count == 0)
			{
				return;
			}

			/* A full set, always. This connection has no strip to merge into and no baseline in
			 * common with the delta stream — and deliberately does NOT touch
			 * lastPushedObservedBuffs, which describes what the EXISTING observers hold and is
			 * already correct for them. Bringing one late observer up to the same state is exactly
			 * what makes the shared baseline true for everyone again. */
			base.NetworkManager.ServerManager.Broadcast(connection, new CharacterBuffsBroadcast
			{
				CharacterObjectID = base.NetworkObject.ObjectId,
				IsFullSet = true,
				Buffs = observedBuffBuffer.ToArray(),
				Removed = System.Array.Empty<int>(),
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends the server-filtered observer buff list to everyone who can see this character,
		/// except its owner.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Applied locally before sending, which is what the previous <c>ObserversRpc</c> achieved
		/// with <c>RunLocally</c>. A broadcast is never delivered back to its sender, so without the
		/// local call the server's own buff container would not reflect the push and anything
		/// server side that inspects a character's visible buffs — <c>PartySystem</c> does — would
		/// read nothing.
		/// </para>
		/// <para>
		/// <b>The owner is excluded.</b> It receives its buff state twice already: as the
		/// authoritative reconcile array every tick, and as the full simulation block of the spawn
		/// payload. A third copy, in seconds, of a list it can derive locally for nothing is pure
		/// cost — and this list is the LOSSY one, so the owner must not read it in preference to
		/// its own simulation. <see cref="RefreshObservedBuffsLocally"/> fills the owner's
		/// <see cref="ObservedBuffs"/> instead. Excluding a connection from an object's observer set
		/// has one safe spelling — see <see cref="ObserverBroadcastScope"/>, which explains why
		/// <c>ServerManager.BroadcastExcept</c> cannot be handed <c>NetworkObject.Observers</c>.
		/// </para>
		/// <para>
		/// Sent reliably: a dropped buff list leaves an observer showing a stale set until the next
		/// change, which — unlike a dropped ability cast — has no self-correcting replacement.
		/// </para>
		/// </remarks>
		/// <param name="entries">The changed buffs, or the whole strip when <paramref name="fullSet"/>.</param>
		/// <param name="removed">Template ids that left the strip; empty when <paramref name="fullSet"/>.</param>
		/// <param name="fullSet">True when <paramref name="entries"/> states the entire visible strip.</param>
		private void BroadcastObservedBuffs(ObservedBuffEntry[] entries, int[] removed, bool fullSet)
		{
			/* The local apply happens either way: it is what keeps this peer's own ObservedBuffs
			 * (and, on a client, the observer FX diff) in step, and it costs nothing on the wire.
			 *
			 * It is given the FULL strip, never the delta. PartySystem and the target frame read
			 * ObservedBuffs as a complete list, and the server is not a receiver of its own
			 * broadcast, so there is no merge on this side to reconstruct it from. */
			ApplyObservedBuffs(observedBuffBuffer.ToArray());

			/* Forwarded objects deliver buffs through the reconcile instead, and their observers
			 * build FX from the simulation dictionary. Sending this as well would give every
			 * observed buff two independent effect instances — see ObserverSyncMode. */
			if (!ObserverSyncMode.ShouldBroadcastToObservers(base.NetworkObject))
			{
				return;
			}

			ObserverBroadcastScope.BroadcastToObserversExceptOwner(base.NetworkObject, new CharacterBuffsBroadcast
			{
				CharacterObjectID = base.NetworkObject != null ? base.NetworkObject.ObjectId : 0,
				IsFullSet = fullSet,
				Buffs = entries ?? System.Array.Empty<ObservedBuffEntry>(),
				Removed = removed ?? System.Array.Empty<int>(),
			}, Channel.Reliable);
		}

		/// <summary>Stores a received buff list, drives observer FX, and notifies listeners.</summary>
		/// <remarks>
		/// The FX diff runs BEFORE the field is replaced, because the previous list is what says
		/// which templates left.
		/// </remarks>
		private void ApplyObservedBuffs(ObservedBuffEntry[] entries)
		{
			ObservedBuffEntry[] next = entries ?? System.Array.Empty<ObservedBuffEntry>();

			SyncObservedBuffFX(next);

			/* The real container is the point of the message, not a side effect of it. A local
			 * client is required to hold an observed character's actual state — Inspect and
			 * faction/aggro read it, not just the renderer — so what arrives here is materialised
			 * into `buffs` rather than kept only as a display list. */
			MaterializeObservedBuffs(next);

			IBuffController.OnObservedBuffsChanged?.Invoke(this);
		}

		/// <summary>
		/// Reconciles a tracking-only peer's <see cref="Buffs"/> with what the server just sent.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Not routed through <see cref="Apply"/> and <see cref="Remove"/>.</b> Those spawn and
		/// despawn FX and raise the add/remove events, and both are already handled for this peer —
		/// <see cref="SyncObservedBuffFX"/> ran a moment ago against the previous list, and
		/// <c>OnObservedBuffsChanged</c> fires below. Going through them would double every effect
		/// instance and every UI notification.
		/// </para>
		/// <para>
		/// The expiry tick is rebased into THIS peer's tick domain: the sender's remaining seconds
		/// are converted against the local clock, because tick domains are per-client and an
		/// absolute tick from the server means nothing here. Zero remaining is permanent, so it maps
		/// to <c>UNSET_TICK</c>, which <c>Buff.HasExpired</c> reads as "never" — mapping it to the
		/// current tick instead would expire every permanent buff on the next observer tick.
		/// </para>
		/// <para>
		/// Skipped entirely on the server and the owner: they run the real simulation, and this
		/// message is a projection of what they already hold.
		/// </para>
		/// </remarks>
		/// <param name="entries">The visible buff set as the server described it.</param>
		private void MaterializeObservedBuffs(ObservedBuffEntry[] entries)
		{
			if (SimulatesBuffEffects)
			{
				return;
			}

			uint currentTick = GetCurrentDomainTick();
			float delta = tickDelta > 0f ? tickDelta : 1f / 30f;

			materializeSeenBuffer.Clear();

			for (int i = 0; i < entries.Length; ++i)
			{
				ObservedBuffEntry entry = entries[i];
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(entry.TemplateID);
				if (template == null)
				{
					// Unresolvable template: nothing to display and nothing to tick.
					continue;
				}

				materializeSeenBuffer.Add(entry.TemplateID);

				uint expiryTick = TimeManager.UNSET_TICK;
				if (entry.RemainingSeconds > 0f && currentTick != TimeManager.UNSET_TICK)
				{
					int remainingTicks = Mathf.Max(1, Mathf.CeilToInt(entry.RemainingSeconds / delta));
					expiryTick = currentTick + (uint)remainingTicks;
				}

				if (buffs.TryGetValue(entry.TemplateID, out Buff existing))
				{
					existing.Stacks = entry.Stacks;
					existing.ExpiryTick = expiryTick;
					existing.SetTickDelta(delta);
					continue;
				}

				Buff materialized = new Buff(entry.TemplateID, expiryTick, TimeManager.UNSET_TICK,
					delta, entry.Stacks, 0);
				if (materialized.Template != null)
				{
					buffs.Add(entry.TemplateID, materialized);
				}
			}

			/* Anything the server did not name is gone. Removed directly for the same reason the
			 * additions are added directly, and only ever on a tracking-only peer — this can never
			 * drop a buff the owner is simulating. */
			materializeRemoveBuffer.Clear();
			foreach (int templateID in buffs.Keys)
			{
				if (!materializeSeenBuffer.Contains(templateID))
				{
					materializeRemoveBuffer.Add(templateID);
				}
			}
			for (int i = 0; i < materializeRemoveBuffer.Count; ++i)
			{
				buffs.Remove(materializeRemoveBuffer[i]);
			}

			materializeSeenBuffer.Clear();
			materializeRemoveBuffer.Clear();
		}

		/// <summary>Scratch set of template ids named by the message being materialised.</summary>
		private readonly HashSet<int> materializeSeenBuffer = new HashSet<int>();

		/// <summary>Scratch list of template ids to drop, so the dictionary is not mutated mid-iteration.</summary>
		private readonly List<int> materializeRemoveBuffer = new List<int>();

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
		/// Like resources, the owner is skipped — on the SEND side, by
		/// <see cref="BroadcastObservedBuffs"/>. The owner's buff and debuff strips are driven by
		/// the <see cref="IBuffController"/> lifecycle events off its own simulation, and its
		/// self-target frame by <see cref="RefreshObservedBuffsLocally"/>; nothing on the owner
		/// reads this message, so it was bytes spent to be discarded on arrival.
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
			if (controller == null)
			{
				return;
			}

			if (msg.IsFullSet)
			{
				controller.ApplyObservedBuffs(msg.Buffs ?? System.Array.Empty<ObservedBuffEntry>());
				return;
			}

			controller.MergeObservedBuffs(msg.Buffs, msg.Removed);
		}

		/// <summary>
		/// Applies a delta to the current strip: entries replace or append by template id, removed
		/// ids drop out, everything else keeps the remaining duration it already had.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Hands the merged FULL strip to <see cref="ApplyObservedBuffs"/> rather than duplicating
		/// its work, so the FX diff and the change event see exactly what they saw when every
		/// message was a full set.
		/// </para>
		/// <para>
		/// <b>Retained entries need no ageing any more.</b> They are read back out of
		/// <see cref="Buffs"/>, whose expiry ticks are in THIS peer's own tick domain and are
		/// advanced every tick — so a bar the delta did not mention is already correct. The
		/// arithmetic that used to re-base each carried-over entry by the age of the last message
		/// existed only because the entries were remembered rather than simulated.
		/// </para>
		/// </remarks>
		/// <param name="changed">Entries added or restacked. May be null or empty.</param>
		/// <param name="removed">Template ids that left. May be null or empty.</param>
		private void MergeObservedBuffs(ObservedBuffEntry[] changed, int[] removed)
		{
			observedBuffMergeBuffer.Clear();

			uint currentTick = GetCurrentDomainTick();

			foreach (Buff buff in buffs.Values)
			{
				BaseBuffTemplate template = buff?.Template;
				if (template == null)
				{
					continue;
				}

				if (removed != null && System.Array.IndexOf(removed, template.ID) >= 0)
				{
					continue;
				}

				// A changed entry supersedes the held one; it is appended below in sender order.
				if (ContainsTemplate(changed, template.ID))
				{
					continue;
				}

				observedBuffMergeBuffer.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = buff.RemainingSeconds(currentTick),
					TotalSeconds = template.Duration,
				});
			}

			if (changed != null)
			{
				for (int i = 0; i < changed.Length; ++i)
				{
					observedBuffMergeBuffer.Add(changed[i]);
				}
			}

			ApplyObservedBuffs(observedBuffMergeBuffer.ToArray());
			observedBuffMergeBuffer.Clear();
		}

		/// <summary>True when <paramref name="entries"/> names <paramref name="templateID"/>.</summary>
		private static bool ContainsTemplate(ObservedBuffEntry[] entries, int templateID)
		{
			if (entries == null)
			{
				return false;
			}
			for (int i = 0; i < entries.Length; ++i)
			{
				if (entries[i].TemplateID == templateID)
				{
					return true;
				}
			}
			return false;
		}


		#region Buff FX

		/// <summary>
		/// The FX instance currently showing for each template, keyed by template ID.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>One instance per template, not per stack and not per application.</b> Before this,
		/// <c>OnApplyFX</c> was fired and forgotten on every apply — so a five-stack buff span five
		/// looping effects, a re-applied one span another on every refresh, and nothing ever
		/// destroyed any of them. The instance outlived the buff on the owner and on every observer.
		/// </para>
		/// <para>
		/// A key may map to a null value: the template may have no FX prefab at all, or the
		/// character's model may not have loaded yet. The KEY is what says "this template is
		/// showing"; the value is only what has to be destroyed when it stops.
		/// </para>
		/// </remarks>
		private readonly Dictionary<int, GameObject> buffFXInstances = new Dictionary<int, GameObject>();

		/// <summary>Scratch for iterating <see cref="buffFXInstances"/> while mutating it.</summary>
		private readonly List<int> fxTeardownBuffer = new List<int>();

		/// <summary>Scratch set for the observed-buff FX diff.</summary>
		private readonly HashSet<int> fxDiffBuffer = new HashSet<int>();

		/// <summary>
		/// True when this peer is the authoritative simulation for this character.
		/// </summary>
		/// <remarks>
		/// Null-safe: <see cref="NetworkBehaviour.IsServerStarted"/> dereferences the NetworkObject
		/// cache, which is null on an unspawned component (every EditMode test constructs one).
		/// There is no host mode in this project, so "server started" and "not a client" are the
		/// same statement.
		/// </remarks>
		private bool IsAuthoritativePeer => base.NetworkObject != null && base.IsServerStarted;

		/// <summary>True when this peer is the client owning this character.</summary>
		private bool IsOwningClient => base.NetworkObject != null && base.IsOwner;

		/// <summary>
		/// True when this peer runs the buff SIMULATION — attribute modifiers, periodic effects,
		/// ECA triggers — rather than merely tracking what the server says is applied.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server and the owning client simulate. An observer tracks: it holds the same
		/// <see cref="Buffs"/> entries so Inspect, the target frame and aggro logic read real state
		/// rather than a display projection, and it counts their durations down locally so bars move
		/// without a message per tick — but it must not APPLY them.
		/// </para>
		/// <para>
		/// <b>Why applying would be wrong, not merely redundant.</b> The attribute broadcast an
		/// observer already receives carries <c>ExternalModifier</c>, which is defined as the sum of
		/// buff, equipment and region contributions. A buff applied locally on top of that counts
		/// its own modifier twice — the same double-count the 2026-08-28 audit fixed on the spawn
		/// payload. The same argument covers periodic effects: a damage-over-time tick would drain a
		/// peer's health locally while <c>CharacterResourcesBroadcast</c> is already delivering the
		/// drained value.
		/// </para>
		/// </remarks>
		private bool SimulatesBuffEffects => IsAuthoritativePeer || IsOwningClient;

		/// <summary>True once this controller subscribed the observer-side duration tick.</summary>
		private bool observerTickSubscribed;

		/// <summary>
		/// Starts or stops the observer-side duration tick to match this peer's role.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An observer's <see cref="BuffController"/> is a spawned NetworkBehaviour with a perfectly
		/// good <c>TimeManager</c> — what it lacks is a DRIVER, because state forwarding is off so
		/// FishNet never invokes the replicate body for a character this peer does not own. The
		/// buffs therefore sat in the dictionary and never advanced. Subscribing to
		/// <c>TimeManager.OnTick</c> directly is the same fix <c>KCCPlatform</c> uses for the same
		/// reason: an object with no owner still needs somebody to step it on a client.
		/// </para>
		/// <para>
		/// Ownership can change at runtime, so this is idempotent and re-evaluated rather than
		/// decided once — a character that gains an owner must stop double-advancing its buffs from
		/// both here and the replicate.
		/// </para>
		/// </remarks>
		private void RefreshObserverTickSubscription()
		{
			bool wanted = base.TimeManager != null && base.IsClientStarted && !SimulatesBuffEffects;

			if (wanted == observerTickSubscribed)
			{
				return;
			}

			if (wanted)
			{
				base.TimeManager.OnTick += ObserverTimeManager_OnTick;
			}
			else
			{
				base.TimeManager.OnTick -= ObserverTimeManager_OnTick;
			}
			observerTickSubscribed = wanted;
		}

		/// <summary>
		/// Advances an observed character's buff DURATIONS, and nothing else.
		/// </summary>
		/// <remarks>
		/// Deliberately not <see cref="Tick"/>. That method dispatches periodic effects through
		/// <c>Buff.TryTick</c>, which mutates resources on every peer that runs it — correct for the
		/// owner, whose prediction has to stay in step with the server, and a double-count here
		/// where the server's resource broadcast is already authoritative. This only expires what
		/// has run out, so a bar empties on time and the icon disappears without waiting for a
		/// message.
		/// </remarks>
		private void ObserverTimeManager_OnTick()
		{
			if (SimulatesBuffEffects || buffs.Count == 0)
			{
				return;
			}

			/* The controller's own domain tick, not TimeManager.LocalTick directly — the same
			 * source every other buff path compares against, so expiry here cannot disagree with
			 * expiry anywhere else. It also has a defined answer before the object is spawned,
			 * where LocalTick would dereference a null TimeManager. */
			uint currentTick = GetCurrentDomainTick();
			if (currentTick == TimeManager.UNSET_TICK)
			{
				return;
			}

			/* Snapshot before iterating: Remove mutates the dictionary. Reuses the same buffer the
			 * authoritative tick uses — the two never run on the same peer. */
			tickIterationBuffer.Clear();
			foreach (Buff b in buffs.Values)
			{
				tickIterationBuffer.Add(b);
			}

			for (int i = 0; i < tickIterationBuffer.Count; ++i)
			{
				Buff buff = tickIterationBuffer[i];
				if (buff?.Template == null || !buffs.ContainsKey(buff.Template.ID))
				{
					continue;
				}

				buff.SetTickDelta(tickDelta);

				if (!buff.HasExpired(currentTick))
				{
					continue;
				}

				if (buff.Stacks > 0)
				{
					/* The stack count is decremented directly rather than through
					 * Buff.RemoveStack, which calls Template.OnRemoveStack first — this peer never
					 * applied that stack's contribution, so reversing it would subtract something
					 * it never added. Resetting the duration without decrementing would be worse
					 * still: the buff would refill its own bar forever and never expire. */
					--buff.Stacks;
					buff.ResetDuration(currentTick, tickDelta);
					continue;
				}

				/* Removed directly, and its effect torn down explicitly. Remove() defers FX to
				 * SyncObservedBuffFX on this peer, which only runs when a message arrives — a buff
				 * that ran out locally has no message coming, so its effect would keep playing over
				 * a character that is no longer carrying it. */
				int expiredID = buff.Template.ID;
				buffs.Remove(expiredID);
				DespawnBuffFX(expiredID);
			}

			tickIterationBuffer.Clear();
		}

		/// <summary>
		/// True when the observed-buff list — rather than the simulation — is what should drive
		/// this character's buff FX.
		/// </summary>
		/// <remarks>
		/// Exactly one source drives FX on any given peer. The server shows nothing. The owner has
		/// a real simulation and drives FX from it, so the observed list it builds locally must not
		/// spawn a second copy. Everyone else has no simulation for this character at all, and the
		/// observed list is the only thing that says which buffs it is carrying.
		/// </remarks>
		private bool DrivesFXFromObservedBuffs => !IsAuthoritativePeer && !IsOwningClient;

		/// <summary>
		/// Shows the FX for <paramref name="template"/> if it is not already showing.
		/// </summary>
		/// <param name="template">The template whose FX to show.</param>
		/// <param name="buff">
		/// The simulated buff, or null when this is driven by the observed list — an observer holds
		/// no <see cref="Buff"/> for somebody else's character.
		/// </param>
		private void SpawnBuffFX(BaseBuffTemplate template, Buff buff)
		{
			if (template == null || Character == null || IsAuthoritativePeer)
			{
				return;
			}

			/* A dead character shows no buff FX. Buffs are stripped from the dead on the server
			 * (CharacterSystem's OnKilled subscriber) and that removal propagates here as a
			 * structural change, but the flag can arrive first. */
			if (Character.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			if (buffFXInstances.TryGetValue(template.ID, out GameObject existing) && existing != null)
			{
				// Already showing. A second stack, or a refresh, is not a second effect.
				return;
			}

			buffFXInstances[template.ID] = template.OnApplyFX(buff, Character);
		}

		/// <summary>
		/// Stops showing the FX for <paramref name="templateID"/>, if any.
		/// </summary>
		/// <param name="templateID">The template that stopped applying.</param>
		private void DespawnBuffFX(int templateID)
		{
			if (!buffFXInstances.TryGetValue(templateID, out GameObject instance))
			{
				return;
			}
			buffFXInstances.Remove(templateID);

			if (instance == null)
			{
				// Nothing was ever spawned (no FX prefab), or the model reload already destroyed it.
				return;
			}

			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			if (template != null)
			{
				template.OnRemoveFX(instance, Character);
			}
			else
			{
				// The template is gone (unloaded asset, stale ID); the instance still must not leak.
				Destroy(instance);
			}
		}

		/// <summary>
		/// Stops showing every tracked FX. Used on teardown, reset, and character stop.
		/// </summary>
		private void DespawnAllBuffFX()
		{
			if (buffFXInstances.Count == 0)
			{
				return;
			}

			fxTeardownBuffer.Clear();
			foreach (int templateID in buffFXInstances.Keys)
			{
				fxTeardownBuffer.Add(templateID);
			}
			for (int i = 0; i < fxTeardownBuffer.Count; ++i)
			{
				DespawnBuffFX(fxTeardownBuffer[i]);
			}
			fxTeardownBuffer.Clear();
			buffFXInstances.Clear();
		}

		/// <summary>
		/// Brings the tracked FX set in line with a newly received observed-buff list.
		/// </summary>
		/// <remarks>
		/// This is the observers' entire FX story. They never run the buff simulation for somebody
		/// else's character, so nothing on the apply/remove path fires for them; the arrival of a
		/// new observed list IS the event. Diffing rather than rebuilding matters because most
		/// pushes change one entry out of several, and a rebuild would restart every looping effect
		/// the character is already showing.
		/// </remarks>
		/// <param name="next">The list about to become <see cref="ObservedBuffs"/>.</param>
		private void SyncObservedBuffFX(ObservedBuffEntry[] next)
		{
			if (!DrivesFXFromObservedBuffs)
			{
				return;
			}

			fxDiffBuffer.Clear();
			for (int i = 0; i < next.Length; ++i)
			{
				fxDiffBuffer.Add(next[i].TemplateID);
			}

			// Removals first, so a template that left and one that arrived cannot fight over
			// whatever the FX parents itself to.
			/* Diffed against what is actually SHOWING rather than against a remembered list. The
			 * FX instances are the authority on what this peer has spawned, so nothing can drift
			 * out of step with them — and it is one less parallel container. */
			fxTeardownBuffer.Clear();
			foreach (int showingID in buffFXInstances.Keys)
			{
				if (!fxDiffBuffer.Contains(showingID))
				{
					fxTeardownBuffer.Add(showingID);
				}
			}
			for (int i = 0; i < fxTeardownBuffer.Count; ++i)
			{
				DespawnBuffFX(fxTeardownBuffer[i]);
			}
			fxTeardownBuffer.Clear();

			for (int i = 0; i < next.Length; ++i)
			{
				int templateID = next[i].TemplateID;
				if (buffFXInstances.ContainsKey(templateID))
				{
					continue;
				}
				SpawnBuffFX(BaseBuffTemplate.Get<BaseBuffTemplate>(templateID), null);
			}

			fxDiffBuffer.Clear();
		}

		/// <summary>
		/// Re-creates buff FX after the character's model (re)loads.
		/// </summary>
		/// <remarks>
		/// <see cref="BaseCharacter.InstantiateRaceModelFromIndex"/> destroys the children of
		/// <see cref="ICharacter.MeshRoot"/> before attaching the new model, and buff FX is parented
		/// there — so every instance showing at that moment is destroyed by the model swap, and any
		/// buff applied BEFORE the model finished loading never had a parent to attach to in the
		/// first place. Both cases are the same repair: anything tracked without a live instance is
		/// spawned again. <see cref="EquipmentVisualController"/> re-equips from the same callback
		/// for the same reason.
		/// </remarks>
		public void OnModelReady()
		{
			if (buffFXInstances.Count == 0 || IsAuthoritativePeer || Character == null)
			{
				return;
			}

			fxTeardownBuffer.Clear();
			foreach (KeyValuePair<int, GameObject> pair in buffFXInstances)
			{
				if (pair.Value == null)
				{
					fxTeardownBuffer.Add(pair.Key);
				}
			}

			for (int i = 0; i < fxTeardownBuffer.Count; ++i)
			{
				int templateID = fxTeardownBuffer[i];
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
				if (template == null)
				{
					buffFXInstances.Remove(templateID);
					continue;
				}
				buffs.TryGetValue(templateID, out Buff buff);
				buffFXInstances[templateID] = template.OnApplyFX(buff, Character);
			}
			fxTeardownBuffer.Clear();
		}

		/// <inheritdoc />
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			StopObserverTick();
			DespawnAllBuffFX();
		}

		/// <summary>
		/// Drops the observer-side duration tick if it is running.
		/// </summary>
		/// <remarks>
		/// TimeManager holds the delegate, so a controller that despawned while subscribed keeps
		/// being ticked — on a pooled character that is somebody else's buffs advancing on a
		/// recycled object.
		/// </remarks>
		private void StopObserverTick()
		{
			if (!observerTickSubscribed)
			{
				return;
			}
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= ObserverTimeManager_OnTick;
			}
			observerTickSubscribed = false;
		}

		/// <inheritdoc />
		public override void OnDestroying()
		{
			StopObserverTick();
			DespawnAllBuffFX();

			base.OnDestroying();
		}

		#endregion

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();
			RefreshTickDelta();
			predictionController = GetComponent<CharacterPredictionController>();

			// An observed character has no replicate to advance its buffs; see the method's remarks.
			RefreshObserverTickSubscription();
		}

		/// <summary>
		/// Re-evaluates who advances this character's buffs when ownership moves.
		/// </summary>
		/// <remarks>
		/// A character that GAINS an owner starts running the replicate, so the observer tick has to
		/// stop or its buffs advance twice per tick. One that LOSES its owner is the reverse: the
		/// replicate stops and the durations would freeze. Deciding this once at spawn would be
		/// wrong in both directions.
		/// </remarks>
		public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prevOwner)
		{
			base.OnOwnershipClient(prevOwner);
			RefreshObserverTickSubscription();

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

			/* The owner's own observed list, built locally for zero bytes. The server does the
			 * equivalent from OnCreateReconcile, which does not run on a client. Gated on a
			 * non-replayed tick so the change event is raised once per real tick rather than once
			 * per replayed tick during a rollback. */
			if (!state.ContainsReplayed() &&
				!IsAuthoritativePeer &&
				IsOwningClient &&
				ShouldPushObservedBuffs())
			{
				RefreshObservedBuffsLocally();
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
			if (ShouldPushObservedBuffs())
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
			/* The owner always reconciles — this is the authority for its own buff simulation.
			 *
			 * A non-owner only reconciles a forwarded object, because that is the mode in which the
			 * reconcile is how buffs reach observers. With forwarding off an observer holds the
			 * display list instead and drives its effects from that diff; letting both run would
			 * spawn two effect instances for one buff and leak the one nothing tracks.
			 * See ObserverSyncMode. */
			if (!base.IsOwner && !ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject))
			{
				return;
			}

			RestoreFromReconcile(rd.Buffs, rd.GetTick());
		}

		/// <summary>
		/// Width of the byte count that frames this behaviour's spawn payload.
		/// </summary>
		/// <remarks>
		/// Four bytes, written unpacked so the width is fixed and the slot can be reserved before
		/// the length is known. A packed integer would vary in size and could not be backfilled.
		/// </remarks>
		private const int BUFF_PAYLOAD_LENGTH_BYTES = 4;

		/// <summary>Payload shape: the character's full simulation state, for its owner.</summary>
		private const byte BUFF_PAYLOAD_SHAPE_SIMULATION = 0;

		/// <summary>Payload shape: the server-filtered display list, for everyone else.</summary>
		private const byte BUFF_PAYLOAD_SHAPE_OBSERVED = 1;

		/// <summary>Sanity cap on the entry count read from either payload shape.</summary>
		private const int MAX_PAYLOAD_BUFFS = 4096;

		/// <summary>
		/// Reads the buff state from the network payload.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Two shapes, chosen by the server.</b> The owner receives its simulation — absolute
		/// ticks, hidden buffs, tick counters — because it is about to go on predicting it. Nobody
		/// else does. An observer used to receive the same block and feed it through
		/// <c>Apply(buff, suppressFX: false)</c>, which instantiated the FX prefab and pushed the
		/// buff's attribute modifiers into that observer's local copy of somebody else's character
		/// — and then, because state forwarding is off and observers never tick, left them there
		/// forever. A poison that killed its victim was still slowing them, on every onlooker's
		/// screen, for as long as they stayed in view. It was also inconsistent: a buff gained while
		/// you were ALREADY watching arrived over <see cref="CharacterBuffsBroadcast"/> as an icon
		/// with no FX at all, so what an observer saw depended on when they happened to arrive.
		/// </para>
		/// <para>
		/// The observer block therefore carries what an observer can actually use: template, stacks
		/// and remaining seconds — the same shape the broadcast carries, arriving through the same
		/// <see cref="ApplyObservedBuffs"/> path, so both routes produce identical icons and
		/// identical FX.
		/// </para>
		/// <para>
		/// <b>Never bare-return.</b> FishNet packs every behaviour's payload into one buffer with no
		/// per-behaviour framing, so a reader that stops early leaves every behaviour after this one
		/// decoding from the wrong offset. Every exit below seeks to the end of this behaviour's
		/// frame first.
		/// </para>
		/// </remarks>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			// Payload sync is authoritative. Clear any previous local state first so
			// stale buffs from an earlier spawn, scene, or character state do not survive.
			/* preserveFX: this is a REFRESH, not a teardown. Whatever is still applicable is about to
			 * be re-applied from the payload, and tearing the effects down first makes every buff's
			 * FX restart on every payload read — visible as a stutter on an observer, which now
			 * materialises real buffs and so actually has effects to lose. What is genuinely gone is
			 * despawned by the diff that follows. */
			RemoveAll(ignoreInvokeRemove: true, includePermanent: true, preserveFX: true);
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

			/* The shape flag lives INSIDE the frame, so a reader that cannot understand it can
			 * still skip exactly the right number of bytes. */
			if (buffBlockLength < 1)
			{
				Log.Error("BuffController",
					$"ReadPayload: framed block of {buffBlockLength} bytes cannot contain the shape flag. " +
					"Skipping this behaviour's payload.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				reader.Position = buffBlockEnd;
				return;
			}

			byte shape = reader.ReadUInt8Unpacked();
			bool consumedCleanly;
			switch (shape)
			{
				case BUFF_PAYLOAD_SHAPE_SIMULATION:
					consumedCleanly = ReadSimulationPayloadBlock(reader, payloadReferenceTick);
					break;
				case BUFF_PAYLOAD_SHAPE_OBSERVED:
					consumedCleanly = ReadObservedPayloadBlock(reader);
					break;
				default:
					Log.Error("BuffController",
						$"ReadPayload: unknown payload shape {shape}. Skipping this behaviour's payload.");
					preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
					consumedCleanly = false;
					break;
			}

			/* Belt and braces on the success path too. If the two sides ever disagree about the
			 * shape of this block the frame absorbs it here rather than corrupting the behaviour
			 * after this one, and says so once instead of failing invisibly. */
			if (reader.Position != buffBlockEnd)
			{
				if (consumedCleanly)
				{
					Log.Error("BuffController",
						$"ReadPayload consumed {reader.Position - (buffBlockEnd - buffBlockLength)} of " +
						$"{buffBlockLength} framed bytes. Seeking to the end of the block; the buff " +
						"state read above may be incomplete.");
				}
				reader.Position = buffBlockEnd;
			}
		}

		/// <summary>
		/// Reads the observer shape: the server's display list for a character this peer does not
		/// own.
		/// </summary>
		/// <remarks>
		/// Nothing here touches <see cref="buffs"/>. An observer holds no simulation for somebody
		/// else's character, so there is no tick domain to translate into and no attribute modifier
		/// to apply — only a list to draw and FX to show, both of which
		/// <see cref="ApplyObservedBuffs"/> handles.
		/// </remarks>
		/// <param name="reader">The payload reader, positioned after the shape flag.</param>
		/// <returns>True when the block was read to its end.</returns>
		private bool ReadObservedPayloadBlock(Reader reader)
		{
			preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;

			int count = reader.ReadInt32();
			if (count < 0 || count > MAX_PAYLOAD_BUFFS)
			{
				Log.Error("BuffController",
					$"ReadPayload: observed buff count {count} is outside [0, {MAX_PAYLOAD_BUFFS}]. Aborting payload read.");
				return false;
			}

			ObservedBuffEntry[] entries = count == 0
				? System.Array.Empty<ObservedBuffEntry>()
				: new ObservedBuffEntry[count];

			/* TotalSeconds is not on the wire — ObservedBuffEntry.ReadFrom resolves it from the
			 * template, which the receiver already has, and a client that cannot resolve the
			 * template cannot draw the icon either. */
			for (int i = 0; i < count; ++i)
			{
				entries[i] = ObservedBuffEntry.ReadFrom(reader);
			}

			ApplyObservedBuffs(entries);
			return true;
		}

		/// <summary>
		/// Reads the owner shape: the character's full buff simulation.
		/// </summary>
		/// <param name="reader">The payload reader, positioned after the shape flag.</param>
		/// <param name="payloadReferenceTick">The writer's reference tick for the absolute ticks below.</param>
		/// <returns>True when the block was read to its end.</returns>
		private bool ReadSimulationPayloadBlock(Reader reader, uint payloadReferenceTick)
		{
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
			if (buffCount < 0 || buffCount > MAX_PAYLOAD_BUFFS)
			{
				Log.Error("BuffController",
					$"ReadPayload: buff count {buffCount} is outside [0, {MAX_PAYLOAD_BUFFS}]. Aborting payload read.");
				preReplicatePayloadReferenceTick = TimeManager.UNSET_TICK;
				/* Seek, do not drain. The per-entry sizes a drain would need are derived from the
				 * count that was just rejected, so a capped drain silently desynchronised the
				 * stream for any count above the cap; the frame written by WritePayload is the
				 * only thing that can resynchronise it. The caller performs the seek. */
				return false;
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

			return true;
		}

		/// <summary>
		/// Writes this character's buff state into the spawn payload, in the shape the receiving
		/// connection can use.
		/// </summary>
		/// <remarks>
		/// The first field is the current reference tick for the serialized absolute buff ticks; it
		/// is only meaningful to the owner, and sits outside the frame because it predates it.
		/// See <see cref="ReadPayload"/> for the two shapes.
		/// </remarks>
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

			/* Shaped per connection, and the shape is on the wire so the reader is never left
			 * guessing which one it is holding.
			 *
			 * The OWNER gets its simulation: absolute ticks, tick counters, and its own
			 * every buff the owner is carrying, which is prediction state it must restore on spawn and
			 * reconnect. EVERYONE ELSE gets the display list — template, stacks, remaining seconds,
			 * hidden buffs already dropped. That is not merely a privacy filter (it is that too: the
			 * full dictionary used to go to every connection, so a packet-inspecting client could
			 * read buffs no UI would show it); it is the only shape an observer can correctly
			 * consume. Observers do not simulate their peers, so absolute ticks in the sender's
			 * replicate domain mean nothing to them, and a buff written into their simulation
			 * dictionary would never be ticked, never expire, and hold its attribute modifiers on
			 * their local copy of that character forever.
			 *
			 * Safe to vary by connection: FishNet builds the spawn message per receiving
			 * connection (ServerObjects.Observers calls WriteSpawn(nob, writer, conn) inside the
			 * per-connection rebuild), so no two receivers share this buffer. */
			bool isOwner = PayloadVisibility.IsOwner(this, conn);
			writer.WriteUInt8Unpacked(isOwner ? BUFF_PAYLOAD_SHAPE_SIMULATION : BUFF_PAYLOAD_SHAPE_OBSERVED);

			if (isOwner)
			{
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
			else
			{
				/* Built by the same method the broadcast uses, so a character looks identical
				 * whether you were watching when the buff landed or walked up afterwards. */
				BuildObservedBuffEntries();

				/* Same seven-byte entry the observer broadcast writes. Single-sourced deliberately:
				 * this block and CharacterBuffsBroadcast describe the same thing to the same
				 * receiver, and two hand-written copies of one wire format is how they drift. */
				writer.WriteInt32(observedBuffBuffer.Count);
				for (int i = 0; i < observedBuffBuffer.Count; ++i)
				{
					observedBuffBuffer[i].WriteTo(writer);
				}
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - buffBlockStart),
				buffBlockStart - BUFF_PAYLOAD_LENGTH_BYTES);
		}

		/// <inheritdoc />
		public uint GetCurrentDomainTick()
		{
			// base.TimeManager dereferences _networkObjectCache, which is null on a controller that
			// has never been spawned — a pooled instance before its first spawn, or a test. It is
			// NOT null while a client reads a spawn payload: ObjectCaching.Iterate calls
			// nob.InitializeEarly, which assigns the cache on every behaviour, before ReadPayload.
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
				/* isAuthoritative is what stops a DoT counting twice. The owner predicts the same
				 * tick the server executes, so both peers run the resource mutation — that is what
				 * keeps predicted health in step — but only the server's pass may have
				 * consequences: ECA tick triggers, threat, kill credit, achievements. Replay alone
				 * could not express that, because the owner's FIRST pass over a tick is not a
				 * replay and is still not authoritative. */
				if (buff.TryTick(Character, currentTick, tickDelta, isReplayingTick, IsAuthoritativePeer))
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
			/* Two kinds of change, deliberately separated.
			 *
			 * STRUCTURAL — a buff appeared, or its stack count moved — changes what observers are
			 * looking at and must be pushed. TIMING — the same buff, refreshed — does not: an aura
			 * or a Region stay-trigger refreshes its buff on EVERY tick, and treating that as a
			 * change pushed a reliable observer message thirty times a second for a bar that is
			 * simply pinned full. See ShouldPushObservedBuffs. */
			bool structuralChange = false;
			bool timingChange = false;
			if (!buffs.TryGetValue(template.ID, out Buff buffInstance))
			{
				// New buff: constructor is the single source of truth for ExpiryTick.
				// ResetDuration is NOT called here — it only runs for existing buffs below.
				isNew = true;
				buffInstance = new Buff(template.ID, replicateDomainTick, tickDelta);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);
				structuralChange = true;

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
					/* ECA triggers only on the authoritative peer. Actions are free to damage, grant
					 * items or move a character, and only ten of the fifty-five action types gate
					 * themselves with EcaAuthority — so dispatching on the owning client ran the
					 * rest of them a second time, locally. The static OnAddBuff/OnAddDebuff events
					 * above are UI and stay on every peer. */
					if (IsAuthoritativePeer)
					{
						BuffEventData bed = new BuffEventData(Character, buffInstance);
						bed.Add(new TickEventData(Character, new PredictionTick(replicateDomainTick)));
						Character.Invoke(onBuffApplyTriggers, bed);
					}
				}
			}

			/* Snapshot on every application, new or refreshed, so a DoT re-applied by a second
			 * attacker credits the one currently sustaining it rather than whoever happened to
			 * land the first stack. SetCaster ignores nulls, so a refresh from a source with no
			 * initiator leaves existing attribution intact. */
			buffInstance.SetCaster(caster);

			/* Only an EXISTING buff stacks. A new one already applied its modifier through
			 * Buff.Apply in the isNew branch above, so stacking it here as well applied the value
			 * twice on the very first cast — a MaxStacks=3 buff landed at 2x and reached 4x at full
			 * stacks. MaxStacks is the total number of applications, which is what
			 * ObservedBuffEntry.Stacks documents ("0 = one application"). */
			if (!isNew && template.MaxStacks > 0 && buffInstance.Stacks < template.MaxStacks)
			{
				buffInstance.AddStack(Character);
				structuralChange = true;
			}

			// Only refresh duration for existing buffs. New buffs already have the
			// correct ExpiryTick from the constructor — calling ResetDuration again
			// would be redundant at best, and wrong if the two code paths ever diverge.
			if (!isNew)
			{
				/* Both sides of this comparison are absolute ticks, so "differs" already means
				 * "differs by at least one whole tick" — a sub-tick refresh cannot reach here and
				 * cannot dirty the reconcile snapshot. That matters because a fresh snapshot array
				 * defeats the delta serialiser's ReferenceEquals shortcut for the whole reconcile
				 * payload, not just for buffs. */
				uint expectedExpiry = Buff.GetExpiryTick(template, replicateDomainTick, tickDelta);
				if (expectedExpiry != buffInstance.ExpiryTick)
				{
					buffInstance.ResetDuration(replicateDomainTick, tickDelta);
					timingChange = true;
				}
			}

			/* FX is per template and lives as long as the buff does; a refresh or a second stack
			 * adds nothing to show. Suppressed during replay, which re-runs every tick since the
			 * last authoritative state. */
			if (!isReplayingTick)
			{
				SpawnBuffFX(template, buffInstance);
			}

			if (structuralChange || timingChange)
			{
				snapshotDirty = true;
			}
			if (structuralChange)
			{
				MarkObservedBuffsDirty();
			}
			else if (timingChange)
			{
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

				/* Tracked everywhere, APPLIED only where this peer simulates. An observer holds the
				 * entry so Inspect, the target frame and aggro read real state, but the attribute
				 * broadcast it already receives carries this buff's contribution inside
				 * ExternalModifier — applying it here as well would count it twice. See
				 * SimulatesBuffEffects. */
				bool simulates = SimulatesBuffEffects;
				if (simulates)
				{
					buff.Apply(Character);
				}
				buffs.Add(buff.Template.ID, buff);

				for (int i = 0; simulates && i < buff.Stacks; ++i)
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
					SpawnBuffFX(buff.Template, buff);
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

				/* Only reverse what this peer applied. A tracking-only observer never ran
				 * Buff.Apply, so draining its stacks here would subtract modifiers it never added
				 * and leave the character permanently below its real values — the mirror image of
				 * the double-count Apply avoids. */
				if (SimulatesBuffEffects && !TryRemoveBuffEffects(buffInstance, buffID, nameof(Remove)))
				{
					return;
				}
				buffs.Remove(buffID);

				/* FX has exactly one owner per peer. Where the simulation drives it, removing the
				 * buff removes the effect. On a tracking-only peer SyncObservedBuffFX owns it
				 * instead, diffing each arriving message against the last — and ReadPayload opens
				 * with RemoveAll, so despawning here tore down every observed effect a moment
				 * before that diff decided it was still showing and spawned it again. */
				if (!DrivesFXFromObservedBuffs)
				{
					DespawnBuffFX(buffID);
				}

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
					// Authoritative peer only — see the note on the apply triggers.
					if (IsAuthoritativePeer)
					{
						Character.Invoke(onBuffRemoveTriggers, new BuffEventData(Character, buffInstance));
					}
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
		/// <param name="includePermanent">
		/// True to remove permanent buffs as well. Default false, so gameplay dispels leave them
		/// alone; the two lifecycle callers (<see cref="ResetState"/> and <see cref="ReadPayload"/>)
		/// pass true because a pooled object must not inherit the previous occupant's buffs — and a
		/// permanent buff carries attribute modifiers, so leaving one behind leaked them into the
		/// next character to use that instance.
		/// </param>
		public void RemoveAll(bool ignoreInvokeRemove = false, bool includePermanent = false, bool preserveFX = false)
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
				if (buff == null || buff.Template == null || includePermanent || !buff.Template.IsPermanent)
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
						/* The buff stays tracked so its attribute modifiers are not orphaned — but on
						 * a lifecycle teardown the FX must go regardless. This object is about to be
						 * pooled and respawned as somebody else, and a visual effect has no modifier
						 * to orphan; leaving it running would hang the previous occupant's aura on
						 * the next character to use this instance. */
						if (includePermanent)
						{
							if (!preserveFX) DespawnBuffFX(key);
						}
						continue;
					}
					buffs.Remove(key);
					if (!preserveFX)
					{
						DespawnBuffFX(key);
					}

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
						// Authoritative peer only — see the note on the apply triggers.
						if (IsAuthoritativePeer)
						{
							Character.Invoke(onBuffRemoveTriggers, new BuffEventData(Character, buff));
						}
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
				DespawnBuffFX(removed.Template.ID);

				/* No ECA dispatch here. This path runs only on the owning client (a reconcile is sent
				 * to the owner alone), and the server already fired these triggers when it removed
				 * the buff. Invoking them again here ran every non-self-gating action a second time
				 * on the client. The static UI events above still fire on this peer. */
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
				/* A buff the owner did not predict — applied by the server between reconciles —
				 * arrives here and nowhere else, so this is the only place its FX can start.
				 * Tracked per template, so a buff that merely survived the diff does not restart. */
				SpawnBuffFX(added.Template, added);

				// No ECA dispatch here either — see the note on the removal loop above.
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

			/* This object may be pooled and respawned as somebody else. A surviving delta baseline
			 * would let the next push describe the new occupant's strip as a difference from the
			 * previous one's — buffs its observers never saw, and removals for buffs that were
			 * never there. Dropping it forces the next push to be a full set. */
			ResetObservedBuffBaseline();

			RemoveAll(ignoreInvokeRemove: true, includePermanent: true);

			/* RemoveAll despawns the FX of everything it removes, and with includePermanent it now
			 * removes permanent buffs too — so nothing survives it by design. This is still here for
			 * the cases it cannot reach: an OBSERVER has no simulated buffs at all (its FX are driven
			 * from the observed list by SyncObservedBuffFX, not from `buffs`), and an FX whose
			 * template was unloaded leaves an instance keyed to a buff that is already gone. This
			 * object may be pooled and respawned as somebody else entirely, so the teardown is
			 * unconditional. */
			DespawnAllBuffFX();
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