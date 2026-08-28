using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class for AbilityController handling network payload serialization
	/// and client-side broadcast registration for abilities and knowledge.
	/// </summary>
	public partial class AbilityController
	{
		/// <summary>
		/// Reusable buffer for batch known-ability broadcast processing.
		/// </summary>
		private readonly List<BaseAbilityTemplate> knownAbilityBuffer = new List<BaseAbilityTemplate>();

		/// <summary>
		/// Reusable buffer for batch known-ability-event broadcast processing.
		/// </summary>
		private readonly List<AbilityEvent> knownAbilityEventBuffer = new List<AbilityEvent>();

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. On the owning client, registers network broadcast
		/// handlers for learning abilities and events, then fires <see cref="OnReset"/> and
		/// <see cref="OnAddAbility"/> for all known abilities.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				this.enabled = false;
			}
			else
			{
				ClientManager.RegisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);

				// invoke client reset event
				OnReset?.Invoke();

				foreach (Ability ability in KnownAbilities.Values)
				{
					// update our client with abilities
					OnAddAbility?.Invoke(ability);
				}
			}
		}

		/// <summary>
		/// Called when the character stops. On the owning client, unregisters all network
		/// broadcast handlers for abilities and events.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddBroadcastReceived(KnownAbilityAddBroadcast msg, Channel channel)
		{
			BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(msg.TemplateID);
			if (baseAbilityTemplate != null)
			{
				LearnBaseAbility(baseAbilityTemplate);

				OnAddKnownAbility?.Invoke(baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddMultipleBroadcastReceived(KnownAbilityAddMultipleBroadcast msg, Channel channel)
		{
			knownAbilityBuffer.Clear();
			foreach (KnownAbilityAddBroadcast knownAbility in msg.Abilities)
			{
				BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(knownAbility.TemplateID);
				if (baseAbilityTemplate != null)
				{
					knownAbilityBuffer.Add(baseAbilityTemplate);
				}
			}

			// Learn all templates before firing events so listeners that query
			// KnownBaseAbilities see a consistent, fully-populated set.
			LearnBaseAbilities(knownAbilityBuffer);

			foreach (BaseAbilityTemplate baseAbilityTemplate in knownAbilityBuffer)
			{
				OnAddKnownAbility?.Invoke(baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability event broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddBroadcastReceived(KnownAbilityEventAddBroadcast msg, Channel channel)
		{
			AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(msg.TemplateID);
			if (abilityEvent != null)
			{
				LearnAbilityEvent(abilityEvent);

				OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddMultipleBroadcastReceived(KnownAbilityEventAddMultipleBroadcast msg, Channel channel)
		{
			knownAbilityEventBuffer.Clear();
			foreach (KnownAbilityEventAddBroadcast knownAbilityEvent in msg.AbilityEvents)
			{
				AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(knownAbilityEvent.TemplateID);
				if (abilityEvent != null)
				{
					knownAbilityEventBuffer.Add(abilityEvent);
				}
			}

			// Learn all events before firing notifications so listeners that query
			// KnownAbilityEvents see a consistent, fully-populated set.
			LearnAbilityEvents(knownAbilityEventBuffer);

			foreach (AbilityEvent abilityEvent in knownAbilityEventBuffer)
			{
				OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			}
		}

		/// <summary>
		/// Server sent an add ability broadcast.
		/// </summary>
		private void OnClientAbilityAddBroadcastReceived(AbilityAddBroadcast msg, Channel channel)
		{
			AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
			if (abilityTemplate != null)
			{
				Ability newAbility = new Ability(msg.ID, abilityTemplate, msg.Events != null ? new List<int>(msg.Events) : null);
				LearnAbility(newAbility);

				OnAddAbility?.Invoke(newAbility);
			}
		}

		/// <summary>
		/// Server sent an add multiple ability broadcast.
		/// </summary>
		private void OnClientAbilityAddMultipleBroadcastReceived(AbilityAddMultipleBroadcast msg, Channel channel)
		{
			foreach (AbilityAddBroadcast ability in msg.Abilities)
			{
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(ability.TemplateID);
				if (abilityTemplate != null)
				{
					Ability newAbility = new Ability(ability.ID, abilityTemplate, ability.Events != null ? new List<int>(ability.Events) : null);
					LearnAbility(newAbility);

					OnAddAbility?.Invoke(newAbility);
				}
			}
		}
#endif

		/// <summary>
		/// Width of the byte count that frames this behaviour's spawn payload.
		/// </summary>
		/// <remarks>
		/// Four bytes, written unpacked so the width is fixed and the slot can be reserved before
		/// the length is known. A packed integer would vary in size and could not be backfilled.
		/// </remarks>
		private const int ABILITY_PAYLOAD_LENGTH_BYTES = 4;

		/// <summary>
		/// Reads the ability controller's state from the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			const int maxPayloadAbilities = 2048;
			const int maxPayloadAbilityEvents = 512;

			// Read the AbilitySeedGenerator seed
			abilitySeed = reader.ReadInt32();

			/* Where this behaviour's data ends, whatever happens below. Every early exit seeks
			 * here before returning so the shared payload reader is left where the next
			 * NetworkBehaviour expects it — see WritePayload.
			 *
			 * The length is checked against what the reader actually holds before it is used.
			 * This frame exists to survive a payload that cannot be trusted, which makes the
			 * frame's own length the one value that must be validated rather than believed:
			 * Reader.Position is a plain field with no bounds check, so a length that overflows
			 * int (anything >= 0x80000000 casts negative) or simply overruns the buffer would
			 * turn a recoverable abort into an out-of-range read for whoever reads next. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("AbilityController",
					$"ReadPayload: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in the " +
					"spawn payload. The stream cannot be resynchronised; discarding the remainder.");
				reader.Position += remainingBytes;
				return;
			}
			int abilityBlockLength = (int)declaredLength;
			int abilityBlockEnd = reader.Position + abilityBlockLength;

			/* The generator's CURRENT state, not a fresh one from abilitySeed. The server's
			 * generator has advanced once per cast since it was created; a client that connects
			 * (or reconnects) later and re-derives it from the seed starts at the server's
			 * INITIAL currentSeed, and the first reconcile then reports a mismatch that was
			 * never a misprediction. */
			int payloadCurrentSeed = reader.ReadInt32();
			uint rngS0 = reader.ReadUInt32();
			uint rngS1 = reader.ReadUInt32();
			uint rngS2 = reader.ReadUInt32();
			uint rngS3 = reader.ReadUInt32();
			if (abilitySeedGenerator == null)
			{
				abilitySeedGenerator = new DeterministicRNG(rngS0, rngS1, rngS2, rngS3);
			}
			else
			{
				abilitySeedGenerator.RestoreState(rngS0, rngS1, rngS2, rngS3);
			}
			currentSeed = payloadCurrentSeed;

			//Log.Debug($"Received AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			int abilityCount = reader.ReadInt32();
			if (abilityCount < 0)
			{
				Log.Error("AbilityController", $"ReadPayload: invalid ability count {abilityCount}. Treating as empty.");
				abilityCount = 0;
			}
			else if (abilityCount > maxPayloadAbilities)
			{
				Log.Error("AbilityController", $"ReadPayload: ability count {abilityCount} exceeds limit {maxPayloadAbilities}. Aborting payload read.");
				reader.Position = abilityBlockEnd;
				return;
			}

			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();
			templateToAbilityID?.Clear();

			List<int> abilityEvents = readPayloadAbilityEvents;
			for (int i = 0; i < abilityCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				int abilityTemplateID = reader.ReadInt32();

				abilityEvents.Clear();
				int abilityEventsCount = reader.ReadInt32();
				if (abilityEventsCount < 0)
				{
					Log.Error("AbilityController", $"ReadPayload: invalid ability event count {abilityEventsCount} for abilityID {abilityID}. Skipping ability.");
					continue;
				}
				if (abilityEventsCount > maxPayloadAbilityEvents)
				{
					Log.Error("AbilityController", $"ReadPayload: ability event count {abilityEventsCount} exceeds limit {maxPayloadAbilityEvents} for abilityID {abilityID}. Aborting payload read.");
					/* The event count is outside the valid range. Draining an adversarially large
					 * count would stall the main thread before the Reader threw, and the sizes to
					 * skip are derived from the count just rejected — so this behaviour's own
					 * state is unrecoverable. Seeking to the framed end still hands the next
					 * behaviour a valid stream. */
					reader.Position = abilityBlockEnd;
					return;
				}

				for (int j = 0; j < abilityEventsCount; ++j)
				{
					abilityEvents.Add(reader.ReadInt32());
				}

				// Validate the template exists before constructing the Ability.
				// A corrupted or outdated payload could contain an invalid template ID;
				// Ability.Initialize would NullRef on Template.Name if we proceeded.
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(abilityTemplateID);
				if (abilityTemplate == null)
				{
					Log.Error("AbilityController", $"ReadPayload: invalid ability template ID {abilityTemplateID} for abilityID {abilityID}. Skipping.");
					continue;
				}

				Ability ability = new Ability(abilityID, abilityTemplate, abilityEvents);

				LearnAbility(ability);
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				uint currentTick = cooldownController.ResolveAuthoritativeTick(base.TimeManager.LocalTick);
				cooldownController.Read(reader, currentTick);
			}

			/* Belt and braces on the success path too. If the two sides ever disagree about the
			 * shape of this block — a cooldown controller present on one peer and not the other,
			 * say — the frame still absorbs it here rather than corrupting the behaviour after
			 * this one, and says so once instead of failing invisibly. */
			if (reader.Position != abilityBlockEnd)
			{
				Log.Error("AbilityController",
					$"ReadPayload consumed {reader.Position - (abilityBlockEnd - abilityBlockLength)} of " +
					$"{abilityBlockLength} framed bytes. Seeking to the end of the block; the ability " +
					"state read above may be incomplete.");
				reader.Position = abilityBlockEnd;
			}
		}

		/// <summary>
		/// Writes the ability controller's state to the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			// Ensure the seed generator exists (shared with ResetState and CreateReconcile).
			EnsureAbilitySeedGenerator();

			// Write the ability RNG seed for the clients
			writer.WriteInt32(abilitySeed);

			//Log.Debug($"Writing AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			/* Everything below is framed by a byte count.
			 *
			 * FishNet packs every NetworkBehaviour's payload into one buffer with no per-behaviour
			 * framing — ManagedObjects.WritePayload walks the whole list into a single writer, and
			 * the reader walks it back the same way. A reader that stops early therefore does not
			 * merely lose its own state: every behaviour after it in NetworkBehaviours reads from
			 * the wrong offset. On an NPC that is Interactable.ReadPayload, which would register
			 * the object in SceneObject.Objects under a garbage ID and quietly make it
			 * uninteractable.
			 *
			 * ReadPayload has three defensive aborts for counts that cannot be trusted, and no way
			 * to drain past them — the sizes it would need to skip are themselves derived from the
			 * count it just rejected. The length recorded here is what lets it resynchronise
			 * instead: seek to the end of this block and hand a valid stream to whoever reads
			 * next. See ABILITY_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(ABILITY_PAYLOAD_LENGTH_BYTES);
			int abilityBlockStart = writer.Position;

			// Current seed and full generator state — see ReadPayload for why not just the seed.
			writer.WriteInt32(currentSeed);
			abilitySeedGenerator.CaptureState(out uint rngS0, out uint rngS1, out uint rngS2, out uint rngS3);
			writer.WriteUInt32(rngS0);
			writer.WriteUInt32(rngS1);
			writer.WriteUInt32(rngS2);
			writer.WriteUInt32(rngS3);

			// Write the abilities for the clients
			writer.WriteInt32(KnownAbilities.Count);
			foreach (Ability ability in KnownAbilities.Values)
			{
				writer.WriteInt64(ability.ID);
				writer.WriteInt32(ability.Template.ID);

				// Count includes the TypeOverride if present (serialized as an extra event ID).
				int eventCount = ability.AbilityEvents.Count;
				bool hasTypeOverride = ability.TypeOverride != null;
				if (hasTypeOverride)
				{
					eventCount++;
				}

				writer.WriteInt32(eventCount);
				foreach (int abilityEvent in ability.AbilityEvents.Keys)
				{
					writer.WriteInt32(abilityEvent);
				}
				if (hasTypeOverride)
				{
					writer.WriteInt32(ability.TypeOverride.ID);
				}
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				/* Cooldown entries go to the owner only. Observers never read a peer's cooldowns
				 * (see CooldownController.Write), and the block is still written — framed, zero
				 * count — so ReadPayload's shape is identical on every receiver. */
				cooldownController.Write(writer, includeEntries: PayloadVisibility.IsOwner(this, conn));
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - abilityBlockStart),
				abilityBlockStart - ABILITY_PAYLOAD_LENGTH_BYTES);
		}
	}
}