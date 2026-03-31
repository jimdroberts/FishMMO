using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_SERVER
using FishNet.Broadcast;
#endif
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
		private readonly List<int> keysToRemove = new List<int>();

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
		/// Cached reconcile snapshot, reused across ticks when buffs haven't changed.
		/// Invalidated by <see cref="Apply(BaseBuffTemplate)"/>, <see cref="Remove"/>,
		/// <see cref="RemoveAll"/>, <see cref="RestoreFromReconcile"/>, and <see cref="Tick"/>.
		/// </summary>
		private BuffReconcileEntry[] cachedSnapshot;

		/// <summary>
		/// When true, <see cref="cachedSnapshot"/> is stale and must be rebuilt.
		/// </summary>
		private bool snapshotDirty = true;

		/// <summary>
		/// Fixed seconds-per-tick, cached from <c>TimeManager.TickDelta</c> in
		/// <see cref="OnStartNetwork"/>. Used for converting float durations to tick counts.
		/// </summary>
		private float tickDelta;

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				tickDelta = (float)base.TimeManager.TickDelta;
			}
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
			Tick(input.GetTick());
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
			RestoreFromReconcile(rd.Buffs);
		}

		/// <summary>
		/// Reads the buff state from the network payload and applies each buff to the character.
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
					}
				}
				return;
			}
			for (int i = 0; i < buffCount; ++i)
			{
				int templateID   = reader.ReadInt32();
				uint expiryTick   = reader.ReadUInt32();
				uint nextTickTick = reader.ReadUInt32();
				int stacks        = reader.ReadInt32();
				int tickCount     = reader.ReadInt32();

				Buff buff = new Buff(templateID, expiryTick, nextTickTick, tickDelta, stacks, tickCount);
				Apply(buff);
			}
		}

		/// <summary>
		/// Writes the current buff state to the network payload for synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
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
			}
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
			foreach (var pair in buffs)
			{
				Buff buff = pair.Value;

				IBuffController.OnBuffTick?.Invoke(buff, currentTick);

				if (!buff.HasExpired(currentTick))
				{
					if (buff.TryTick(Character, currentTick, tickDelta))
					{
						// NextTickTick and TickCount changed — reconcile snapshot is stale.
						snapshotDirty = true;
					}
				}
				else
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
						keysToRemove.Add(pair.Key);
					}
				}
			}

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
		/// <b>Tick source:</b> Uses <c>TimeManager.LocalTick</c> which tracks the current
		/// simulation tick. During reconcile replay this equals the replayed tick, not wall-clock time.
		/// Outside the prediction pipeline (e.g., server broadcast handlers for observer buffs),
		/// LocalTick reflects real server time — this is intentional. Observer buff visuals are
		/// non-deterministic by design and do not participate in reconcile; using a different tick
		/// source here would add complexity with no correctness benefit.
		/// </remarks>
		public void Apply(BaseBuffTemplate template)
		{
			if (template == null) return;

			// Apply must NOT be called during reconcile replay — LocalTick during
			// replay reflects the replayed tick, not the original application tick,
			// which would compute a wrong ExpiryTick and desync client/server.
			// If this fires, the caller must plumb the deterministic tick from
			// CharacterReplicateData.GetTick() instead of relying on LocalTick.
			if (base.PredictionManager != null && base.PredictionManager.IsReconciling)
				Log.Warning("BuffController", "Apply called during reconcile replay — ExpiryTick will be wrong.");

			snapshotDirty = true;

			uint currentTick = base.TimeManager?.LocalTick ?? 0u;

			bool isNew = false;
			if (!buffs.TryGetValue(template.ID, out Buff buffInstance))
			{
				// New buff: constructor is the single source of truth for ExpiryTick.
				// ResetDuration is NOT called here — it only runs for existing buffs below.
				isNew = true;
				buffInstance = new Buff(template.ID, currentTick, tickDelta);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);

				if (template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(buffInstance);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(buffInstance);
				}
				Character.Invoke(onBuffApplyTriggers, new BuffEventData(Character, buffInstance));
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

			template.OnApplyFX(buffInstance, Character);

#if UNITY_SERVER
			if (base.IsServerStarted)
			{
				SendBuffAddUpdate(template.ID);
			}
#endif
		}

		/// <summary>
		/// Applies a pre-constructed buff instance to the character if not already present.
		/// Restores attribute modifiers for the base application and each existing stack
		/// (e.g., from DB or network payload). Stacks are not incremented because they are already set.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
		public void Apply(Buff buff)
		{
			if (buff == null) return;

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
				// Remove all remaining stack modifiers before removing the base effect
				while (buffInstance.Stacks > 0)
				{
					buffInstance.RemoveStack(Character);
				}

				buffInstance.Remove(Character);
				buffs.Remove(buffID);

				if (buffInstance.Template.IsDebuff)
				{
					IBuffController.OnRemoveDebuff?.Invoke(buffInstance);
				}
				else
				{
					IBuffController.OnRemoveBuff?.Invoke(buffInstance);
				}
				Character.Invoke(onBuffRemoveTriggers, new BuffEventData(Character, buffInstance));

#if UNITY_SERVER
				if (base.IsServerStarted)
				{
					SendBuffRemoveUpdate(buffID);
				}
#endif
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
			// Collect keys to remove (reuse keysToRemove to avoid allocation)
			keysToRemove.Clear();
			foreach (var pair in buffs)
			{
				if (!pair.Value.Template.IsPermanent)
				{
					keysToRemove.Add(pair.Key);
				}
			}

			for (int i = 0; i < keysToRemove.Count; i++)
			{
				int key = keysToRemove[i];
				if (buffs.TryGetValue(key, out Buff buff))
				{
					// Remove all stack modifiers
					while (buff.Stacks > 0)
					{
						buff.RemoveStack(Character);
					}

					buff.Remove(Character);
					buffs.Remove(key);

					if (!ignoreInvokeRemove)
					{
						if (buff.Template.IsDebuff)
						{
							IBuffController.OnRemoveDebuff?.Invoke(buff);
						}
						else
						{
							IBuffController.OnRemoveBuff?.Invoke(buff);
						}
					}
				}
			}
			keysToRemove.Clear();
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
					TemplateID   = kvp.Value.Template.ID,
					ExpiryTick   = kvp.Value.ExpiryTick,
					NextTickTick = kvp.Value.NextTickTick,
					Stacks       = kvp.Value.Stacks,
					TickCount    = kvp.Value.TickCount,
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
		public void RestoreFromReconcile(BuffReconcileEntry[] entries)
		{
			snapshotDirty = true;
			// Build a set of what the server wants removed — start with all current buff IDs.
			reconcileKeysToRemove.Clear();
			foreach (int id in buffs.Keys)
			{
				reconcileKeysToRemove.Add(id);
			}

			if (entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					ref BuffReconcileEntry entry = ref entries[i];
					reconcileKeysToRemove.Remove(entry.TemplateID);

					if (buffs.TryGetValue(entry.TemplateID, out Buff existing))
					{
						// Buff exists on both sides — patch timing and fix stacks if diverged.
						if (existing.Stacks != entry.Stacks)
						{
							while (existing.Stacks > entry.Stacks)
							{
								existing.RemoveStack(Character);
							}
							while (existing.Stacks < entry.Stacks)
							{
								existing.AddStack(Character);
							}
						}
						existing.ExpiryTick = entry.ExpiryTick;
						existing.NextTickTick = entry.NextTickTick;
						existing.TickCount = entry.TickCount;
					}
					else
					{
						// Server has a buff we don't — construct with 0 stacks then AddStack
						// incrementally so each OnApplyStack sees the correct Stacks value.
						// Note: this path only runs for genuinely missing buffs (the
						// TryGetValue check above prevents re-application for buffs that
						// already exist on both sides). Stack modifiers are applied once
						// when the buff is first added, not on every reconcile tick.
						Buff buff = new Buff(
							entry.TemplateID,
							entry.ExpiryTick,
							entry.NextTickTick,
							tickDelta,
							0,
							entry.TickCount);

						if (buff.Template == null)
						{
							continue;
						}

						buff.Apply(Character);
						buffs[buff.Template.ID] = buff;

						for (int s = 0; s < entry.Stacks; s++)
						{
							buff.AddStack(Character);
						}
					}
				}
			}

			// Remove buffs the server doesn't have.
			foreach (int key in reconcileKeysToRemove)
			{
				if (buffs.TryGetValue(key, out Buff toRemove))
				{
					while (toRemove.Stacks > 0)
					{
						toRemove.RemoveStack(Character);
					}
					toRemove.Remove(Character);
					buffs.Remove(key);
				}
			}
			reconcileKeysToRemove.Clear();
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

			RemoveAll(ignoreInvokeRemove: true);
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character is started on the client. Registers broadcast listeners for buff updates.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<BuffAddBroadcast>(OnClientBuffAddBroadcastReceived);
			ClientManager.RegisterBroadcast<BuffAddMultipleBroadcast>(OnClientBuffAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<BuffRemoveBroadcast>(OnClientBuffRemoveBroadcastReceived);
			ClientManager.RegisterBroadcast<BuffRemoveMultipleBroadcast>(OnClientBuffRemoveMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverBuffAddBroadcast>(OnClientCharacterObserverBuffAddBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverBuffRemoveBroadcast>(OnClientCharacterObserverBuffRemoveBroadcastReceived);
		}

		/// <summary>
		/// Called when the character is stopped on the client. Unregisters buff update listeners.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<BuffAddBroadcast>(OnClientBuffAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<BuffAddMultipleBroadcast>(OnClientBuffAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<BuffRemoveBroadcast>(OnClientBuffRemoveBroadcastReceived);
				ClientManager.UnregisterBroadcast<BuffRemoveMultipleBroadcast>(OnClientBuffRemoveMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverBuffAddBroadcast>(OnClientCharacterObserverBuffAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverBuffRemoveBroadcast>(OnClientCharacterObserverBuffRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// Resolves a target buff controller from the client character cache.
		/// </summary>
		private static bool TryGetCachedBuffController(long characterID, out IBuffController buffController)
		{
			buffController = null;
			if (characterID <= 0) return false;

			if (!BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) ||
				character == null)
			{
				return false;
			}

			return character.TryGet(out buffController);
		}

		/// <summary>
		/// Handles a broadcast from the server to add a single buff.
		/// </summary>
		private void OnClientBuffAddBroadcastReceived(BuffAddBroadcast msg, Channel channel)
		{
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(msg.TemplateID);
			if (template != null)
			{
				Apply(template);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to add multiple buffs.
		/// </summary>
		private void OnClientBuffAddMultipleBroadcastReceived(BuffAddMultipleBroadcast msg, Channel channel)
		{
			if (msg.Buffs == null) return;
			foreach (BuffAddBroadcast subMsg in msg.Buffs)
			{
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(subMsg.TemplateID);
				if (template != null)
				{
					Apply(template);
				}
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to remove a single buff.
		/// </summary>
		private void OnClientBuffRemoveBroadcastReceived(BuffRemoveBroadcast msg, Channel channel)
		{
			Remove(msg.TemplateID);
		}

		/// <summary>
		/// Handles a broadcast from the server to remove multiple buffs.
		/// </summary>
		private void OnClientBuffRemoveMultipleBroadcastReceived(BuffRemoveMultipleBroadcast msg, Channel channel)
		{
			if (msg.Buffs == null) return;
			foreach (BuffRemoveBroadcast subMsg in msg.Buffs)
			{
				Remove(subMsg.TemplateID);
			}
		}

		/// <summary>
		/// Handles observer-targeted add buff updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverBuffAddBroadcastReceived(CharacterObserverBuffAddBroadcast msg, Channel channel)
		{
			if (!TryGetCachedBuffController(msg.CharacterID, out IBuffController buffController) ||
				msg.Buffs == null)
			{
				return;
			}

			foreach (BuffAddBroadcast subMsg in msg.Buffs)
			{
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(subMsg.TemplateID);
				if (template != null)
				{
					buffController.Apply(template);
				}
			}
		}

		/// <summary>
		/// Handles observer-targeted remove buff updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverBuffRemoveBroadcastReceived(CharacterObserverBuffRemoveBroadcast msg, Channel channel)
		{
			if (!TryGetCachedBuffController(msg.CharacterID, out IBuffController buffController) ||
				msg.Buffs == null)
			{
				return;
			}

			foreach (BuffRemoveBroadcast subMsg in msg.Buffs)
			{
				buffController.Remove(subMsg.TemplateID);
			}
		}
#endif

#if UNITY_SERVER
		/// <summary>
		/// Sends an add-buff update to observers only.
		/// The owner receives buff state through CSP reconcile.
		/// </summary>
		private void SendBuffAddUpdate(int templateID)
		{
			if (Character == null) return;

			CharacterObserverBuffAddBroadcast observerBroadcast = new CharacterObserverBuffAddBroadcast()
			{
				CharacterID = Character.ID,
				Buffs = new List<BuffAddBroadcast>(1)
				{
					new BuffAddBroadcast() { TemplateID = templateID },
				},
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Sends a remove-buff update to observers only.
		/// The owner receives buff state through CSP reconcile.
		/// </summary>
		private void SendBuffRemoveUpdate(int templateID)
		{
			if (Character == null) return;

			CharacterObserverBuffRemoveBroadcast observerBroadcast = new CharacterObserverBuffRemoveBroadcast()
			{
				CharacterID = Character.ID,
				Buffs = new List<BuffRemoveBroadcast>(1)
				{
					new BuffRemoveBroadcast() { TemplateID = templateID },
				},
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Broadcasts the payload to all current observers of the character, excluding the owner.
		/// </summary>
		private static void BroadcastToObserversOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null || character.Observers == null) return;

			NetworkConnection owner = character.Owner;
			foreach (NetworkConnection observer in character.Observers)
			{
				if (observer == null || observer == owner) continue;
				observer.Broadcast(broadcast, true, channel);
			}
		}
#endif
	}
}