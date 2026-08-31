using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
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
		/// Reproduces the ability objects the spawn payload said were already in the air.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Here rather than in <see cref="OnStartCharacter"/>, because that hook is OWNER ONLY.</b>
		/// <c>PlayerCharacter.TryInitializeLocalClient</c> returns immediately unless
		/// <c>base.IsOwner</c>, and it is the only thing in the project that fans
		/// <c>OnStartCharacter</c> out over a character's behaviours — so the drain never ran on an
		/// observer at all. The payload block exists precisely for the observer case ("anyone who
		/// came into range a moment later saw an empty sky while the server had a fireball crossing
		/// it"), and the server writes up to
		/// <see cref="MAX_PAYLOAD_IN_FLIGHT_OBJECTS"/> entries to every receiver, so the bytes were
		/// being spent and then dropped on the floor by everyone except the caster itself.
		/// </para>
		/// <para>
		/// <c>OnStartClient</c> is the hook that actually means "a client finished spawning this
		/// character": FishNet reads every behaviour's payload during <c>InitializeEarly</c> and
		/// only then runs the start callbacks, so <see cref="KnownAbilities"/> — which
		/// <see cref="MaterializePendingInFlightObjects"/> resolves each entry through, and which
		/// every receiver gets, not just the owner — is populated by the time this runs. It fires
		/// once per client per spawn, for the owner and observers alike, which is exactly the
		/// audience the payload was written for.
		/// </para>
		/// <para>
		/// The drain clears the pending list, so a later <see cref="OnStartCharacter"/> on the owner
		/// finds nothing left to do whichever order the two callbacks happen to run in.
		/// </para>
		/// </remarks>
		public override void OnStartClient()
		{
			base.OnStartClient();

			/* Everyone who read a spawn payload gets the objects that were already in the air,
			 * owner included. A fresh connection has predicted nothing, so it needs the catch-up
			 * exactly as much as an observer does. */
			MaterializePendingInFlightObjects();
		}

		/// <summary>
		/// Called when the character starts. On the owning client, registers network broadcast
		/// handlers for learning abilities and events, then fires <see cref="OnReset"/> and
		/// <see cref="OnAddAbility"/> for all known abilities.
		/// </summary>
		/// <remarks>
		/// <b>Owner only.</b> <c>PlayerCharacter.TryInitializeLocalClient</c> is the sole caller and
		/// it returns unless <c>base.IsOwner</c>, so nothing an observer needs may live here — see
		/// <see cref="OnStartClient"/>, which is where the in-flight drain moved for that reason.
		/// </remarks>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			/* No ownership branch, because there is no other branch to be in. The `!IsOwner` arm
			 * this used to carry — which disabled the component — could never run: the only caller
			 * refuses a non-owner before it gets here, so the test read as a guard while guarding
			 * nothing. Reading it as one is what put the in-flight drain above on a callback no
			 * observer ever reaches; it is gone so the next reader is not told the same thing. */
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
		/// Reproduces the ability objects that were already in flight when this client started
		/// observing the caster.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Each one is spawned at the pose the server launched it from and then fast-forwarded by
		/// the difference between this client's estimate of the server tick and the tick the
		/// object started on, less the interpolation this client renders its peers behind — the
		/// same arithmetic a live activation broadcast uses, so a streamed object and a witnessed
		/// one end up in the same place.
		/// </para>
		/// <para>
		/// An object whose remaining life has already run out during transit destroys itself
		/// inside <c>FastForward</c> without dispatching its OnDestroy events, which is correct:
		/// it no longer exists on the server and its effects have already played there.
		/// </para>
		/// </remarks>
		private void MaterializePendingInFlightObjects()
		{
			if (pendingInFlightObjects.Count == 0)
			{
				return;
			}

			NetworkObject nob = base.NetworkObject;
			FishNet.Managing.Timing.TimeManager timeManager = nob != null ? nob.TimeManager : null;

			/* AbilityObject.Spawn throws without a TimeManager — deterministic simulation has no
			 * meaning without a fixed tick delta. Dropping the catch-up costs a few visuals;
			 * throwing here would abort the rest of the character's start. */
			if (timeManager == null)
			{
				pendingInFlightObjects.Clear();
				return;
			}

			for (int i = 0; i < pendingInFlightObjects.Count; ++i)
			{
				InFlightAbilityObject entry = pendingInFlightObjects[i];

				if (!KnownAbilities.TryGetValue(entry.AbilityID, out Ability ability))
				{
					continue;
				}

				AbilityObject spawned = AbilityObject.Spawn(ability, Character, AbilitySpawner,
					new TargetInfo(null, entry.Position),
					entry.Position, entry.Rotation * Vector3.forward,
					entry.Seed, new PredictionTick(entry.SpawnTick),
					new AbilitySpawnPose(entry.Position, entry.Rotation));

				if (spawned != null)
				{
					spawned.FastForward(ComputeObserverFastForwardTicks(timeManager.Tick, entry.ServerStartTick,
						LagCompensationTick.SpectatorInterpolationTicks));
				}
			}

			pendingInFlightObjects.Clear();
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
		/// Shape byte written as the first thing inside the framed block.
		/// </summary>
		/// <remarks>
		/// The owner's payload carries the deterministic RNG; nobody else's does. One byte says
		/// which shape follows, so the reader never has to guess and a future third shape does not
		/// need a new frame.
		/// </remarks>
		private const byte ABILITY_PAYLOAD_SHAPE_OWNER = 0x01;

		/// <summary>
		/// Ability objects already in flight that one spawn payload will carry.
		/// </summary>
		/// <remarks>
		/// A cap rather than a complete list. This is a catch-up hint for someone who just came
		/// into range, not authoritative state — anything past a handful of simultaneous
		/// projectiles from one caster is visual noise that will have expired before it matters,
		/// and the cap is what stops a pathological caster from making every observer-add
		/// expensive.
		/// </remarks>
		private const int MAX_PAYLOAD_IN_FLIGHT_OBJECTS = 8;

		/// <summary>
		/// Ability objects with less than this much life left are left out of the spawn payload.
		/// </summary>
		/// <remarks>
		/// Reproducing one costs an Instantiate and its whole spawn-event chain for a visual that
		/// is about to vanish, and the fast-forward would very likely destroy it on arrival
		/// anyway.
		/// </remarks>
		private const float MIN_STREAMED_REMAINING_LIFE = 0.25f;

		/// <summary>
		/// One ability object that was already flying when this client started observing the caster.
		/// </summary>
		private struct InFlightAbilityObject
		{
			/// <summary>Ability the object belongs to.</summary>
			public long AbilityID;
			/// <summary>Deterministic spawn seed.</summary>
			public int Seed;
			/// <summary>Replicate tick the object spawned on, in the caster's domain.</summary>
			public uint SpawnTick;
			/// <summary>Server tick the object started on, for the fast-forward.</summary>
			public uint ServerStartTick;
			/// <summary>World position the object spawned at.</summary>
			public Vector3 Position;
			/// <summary>World rotation the object spawned with.</summary>
			public Quaternion Rotation;
		}

		/// <summary>
		/// In-flight objects read from the spawn payload, waiting to be reproduced.
		/// </summary>
		/// <remarks>
		/// Not reproduced inside <c>ReadPayload</c>: that runs while the object is still being
		/// spawned, before the character is assembled, and <c>AbilityObject.Spawn</c> needs a live
		/// caster with a TimeManager. They are materialised from <c>OnStartCharacter</c> instead.
		/// </remarks>
		private readonly List<InFlightAbilityObject> pendingInFlightObjects = new List<InFlightAbilityObject>();

		/// <summary>
		/// Scratch list used by <see cref="WritePayload"/> to gather in-flight objects.
		/// </summary>
		private readonly List<InFlightAbilityObject> inFlightWriteBuffer = new List<InFlightAbilityObject>();

		/// <summary>
		/// Reads the ability controller's state from the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			const int maxPayloadAbilities = 2048;
			const int maxPayloadAbilityEvents = 512;

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

			/* Cleared up front, not on the success path. Every abort below seeks to the frame end
			 * and returns, and anything left here from an earlier read would then be materialised
			 * as if this payload had asked for it. */
			pendingInFlightObjects.Clear();

			/* Which shape follows. Only the owner's payload carries the deterministic RNG — see
			 * WritePayload for why an observer must not be handed a peer's generator state. */
			byte shape = reader.ReadUInt8Unpacked();
			if ((shape & ABILITY_PAYLOAD_SHAPE_OWNER) != 0)
			{
				// Read the AbilitySeedGenerator seed
				abilitySeed = reader.ReadInt32();

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
			}

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

			/* Objects that were already in the air when this client was added as an observer.
			 *
			 * Only recorded here. Reproducing them needs a live caster with a TimeManager, and
			 * this runs mid-spawn while the character is still being assembled, so
			 * OnStartCharacter drains the list once everything exists. */
			int inFlightCount = reader.ReadUInt8Unpacked();
			if (inFlightCount > MAX_PAYLOAD_IN_FLIGHT_OBJECTS)
			{
				Log.Error("AbilityController",
					$"ReadPayload: in-flight object count {inFlightCount} exceeds limit " +
					$"{MAX_PAYLOAD_IN_FLIGHT_OBJECTS}. Skipping in-flight state.");
				reader.Position = abilityBlockEnd;
				return;
			}
			for (int i = 0; i < inFlightCount; ++i)
			{
				// Read into locals: the field order below is the wire order, and nothing about it
				// should depend on how an object initialiser happens to be evaluated.
				long inFlightAbilityID = reader.ReadInt64();
				int inFlightSeed = reader.ReadInt32();
				uint inFlightSpawnTick = reader.ReadUInt32();
				uint inFlightServerStartTick = reader.ReadUInt32();
				Vector3 inFlightPosition = reader.ReadVector3();
				Quaternion inFlightRotation = reader.ReadQuaternion64();

				pendingInFlightObjects.Add(new InFlightAbilityObject()
				{
					AbilityID = inFlightAbilityID,
					Seed = inFlightSeed,
					SpawnTick = inFlightSpawnTick,
					ServerStartTick = inFlightServerStartTick,
					Position = inFlightPosition,
					Rotation = inFlightRotation,
				});
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

			//Log.Debug($"Writing AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			/* Everything below is framed by a byte count.
			 *
			 * FishNet packs every NetworkBehaviour's payload into one buffer with no per-behaviour
			 * framing — ManagedObjects.WritePayload walks the whole list into a single writer, and
			 * the reader walks it back the same way. A reader that stops early therefore does not
			 * merely lose its own state: every behaviour after it in NetworkBehaviours reads from
			 * the wrong offset.
			 *
			 * Today nothing on any shipped prefab reads a payload after this controller — on the
			 * NPC prefabs SceneObjectNamer, the Interactable and NPC itself all precede it, and on
			 * the playable prefabs the behaviours that follow override neither payload method — so
			 * the frame currently protects against a reordering rather than a live corruption. It
			 * stays because component order is authored per prefab and nothing enforces it.
			 *
			 * ReadPayload has three defensive aborts for counts that cannot be trusted, and no way
			 * to drain past them — the sizes it would need to skip are themselves derived from the
			 * count it just rejected. The length recorded here is what lets it resynchronise
			 * instead: seek to the end of this block and hand a valid stream to whoever reads
			 * next. See ABILITY_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(ABILITY_PAYLOAD_LENGTH_BYTES);
			int abilityBlockStart = writer.Position;

			/* The deterministic RNG is OWNER-ONLY.
			 *
			 * It used to go to every observer, where it is both useless and dangerous. Useless
			 * because an observer never runs the seed forward: it is handed the per-cast Seed in
			 * each AbilityActivatedBroadcast and reproduces the object from that. Dangerous
			 * because xoshiro128** is not a cryptographic generator — 128 bits of state is the
			 * whole generator, so a modified client holding a peer's state can compute every seed
			 * that peer will ever cast with, and anything the ability system rolls from those
			 * seeds (crit chance, proc rolls, spread) becomes predictable for someone else's
			 * character. abilitySeed travels with it for the same reason: the generator is derived
			 * from it.
			 *
			 * A shape byte, not a silent difference in length: the reader must know which shape it
			 * is reading rather than infer it from its own idea of ownership, which is a thing the
			 * two peers could disagree about during a possession change. */
			bool isOwner = PayloadVisibility.IsOwner(this, conn);
			writer.WriteUInt8Unpacked(isOwner ? ABILITY_PAYLOAD_SHAPE_OWNER : (byte)0);

			if (isOwner)
			{
				// Current seed and full generator state — see ReadPayload for why not just the seed.
				writer.WriteInt32(abilitySeed);
				writer.WriteInt32(currentSeed);
				abilitySeedGenerator.CaptureState(out uint rngS0, out uint rngS1, out uint rngS2, out uint rngS3);
				writer.WriteUInt32(rngS0);
				writer.WriteUInt32(rngS1);
				writer.WriteUInt32(rngS2);
				writer.WriteUInt32(rngS3);
			}

			/* The ability LIST goes to everyone, unlike the generator above and the cooldowns below.
			 *
			 * It is what lets an observer reproduce a cast at all: AbilityActivatedBroadcast carries
			 * an ability id and nothing else — casts are frequent and learns are rare, so the
			 * template and its events belong here once rather than on every cast — and
			 * TryGetAbilityForVisuals has to resolve that id or the cast draws nothing.
			 * AbilityLearnedObserverBroadcast covers abilities learned AFTER this observer arrived;
			 * this block is the seed for everything learned before, which a late joiner has no other
			 * source for.
			 *
			 * So an observer learns the caster's whole spellbook at spawn rather than one ability at
			 * a time as it sees them used. That is a real disclosure and it is accepted rather than
			 * overlooked: it is the set of things the character can visibly do, and closing it would
			 * mean lazily seeding on first cast, which trades a permanent property for a one-cast
			 * delay on the first use of every ability. What must NOT travel is state that predicts
			 * the future — the generator above — or that reveals what the caster can do NEXT, which
			 * is why the cooldown entries below are owner-only. */
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
				cooldownController.Write(writer, includeEntries: isOwner);
			}

			/* Ability objects that are already in the air.
			 *
			 * Everything above describes what the caster CAN do; none of it says anything about
			 * what is happening right now. A projectile mid-flight reached observers only through
			 * the activation broadcast that launched it, which is sent once, to whoever was
			 * observing at that instant — so anyone who came into range a moment later saw an
			 * empty sky while the server had a fireball crossing it, and the first they knew of it
			 * was the damage. This list closes that window for whoever is being added right now.
			 *
			 * Sent to every receiver including the owner: a fresh connection has predicted
			 * nothing, so it needs the catch-up exactly as much as an observer does.
			 *
			 * The pose travels for every spawn mode, unlike the activation broadcast, which omits
			 * it for Camera spawns and sends the aim instead. That trick works there because the
			 * message is written at the moment of the cast, while the aim still exists; a live
			 * AbilityObject has long since discarded the aim and retains only the pose it
			 * resolved. There is nothing left to derive from. */
			CollectInFlightAbilityObjects(inFlightWriteBuffer);
			writer.WriteUInt8Unpacked((byte)inFlightWriteBuffer.Count);
			for (int i = 0; i < inFlightWriteBuffer.Count; ++i)
			{
				InFlightAbilityObject entry = inFlightWriteBuffer[i];
				writer.WriteInt64(entry.AbilityID);
				writer.WriteInt32(entry.Seed);
				writer.WriteUInt32(entry.SpawnTick);
				writer.WriteUInt32(entry.ServerStartTick);
				writer.WriteVector3(entry.Position);
				/* 64-bit, matching the activation broadcast's SpawnRotation. This pose is used as
				 * both the spawn transform and the aim direction the object flies along, so the
				 * 32-bit form's 1.24-degree worst case put a reproduced projectile up to about a
				 * metre off the server's line over its flight. At most eight entries, once, in a
				 * spawn payload. */
				writer.WriteQuaternion64(entry.Rotation);
			}
			inFlightWriteBuffer.Clear();

			writer.InsertUInt32Unpacked((uint)(writer.Position - abilityBlockStart),
				abilityBlockStart - ABILITY_PAYLOAD_LENGTH_BYTES);
		}

		/// <summary>
		/// Gathers the caster's currently alive ability objects for the spawn payload.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Root objects only (<c>ID == 0</c>). Children are produced by the ability's own
		/// OnSpawn events — a multiply or split action — and those events run again when the
		/// receiver reproduces the root, so sending them would spawn each child twice.
		/// </para>
		/// <para>
		/// Objects that cannot be reproduced from a pose alone are skipped rather than sent and
		/// dropped: a <c>RequiresTarget</c> ability refuses to spawn without a target transform,
		/// and a live object no longer holds the target it was launched at.
		/// </para>
		/// </remarks>
		/// <param name="into">Cleared, then filled with at most <see cref="MAX_PAYLOAD_IN_FLIGHT_OBJECTS"/> entries.</param>
		private void CollectInFlightAbilityObjects(List<InFlightAbilityObject> into)
		{
			into.Clear();

			if (KnownAbilities == null || KnownAbilities.Count == 0)
			{
				return;
			}

			/* Resolved lazily and through NetworkObject, which is null-safe on an unspawned
			 * behaviour where base.TimeManager would throw. Nothing reads it unless there is at
			 * least one object to describe. */
			uint serverTick = 0u;
			bool haveServerTick = false;

			foreach (Ability ability in KnownAbilities.Values)
			{
				if (ability.Objects == null || ability.Objects.Count == 0)
				{
					continue;
				}
				if (ability.Template == null || ability.Template.RequiresTarget)
				{
					continue;
				}

				foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
				{
					if (container == null || !container.TryGetValue(0, out AbilityObject root))
					{
						continue;
					}
					if (root == null || root.IsDestroyed)
					{
						continue;
					}

					// Nearly over: not worth an Instantiate and a spawn-event chain.
					float totalLifeTime = root.TotalLifeTime;
					if (totalLifeTime > 0.0f && root.RemainingLifeTime < MIN_STREAMED_REMAINING_LIFE)
					{
						continue;
					}

					if (!haveServerTick)
					{
						NetworkObject nob = base.NetworkObject;
						serverTick = nob != null && nob.TimeManager != null ? nob.TimeManager.LocalTick : 0u;
						haveServerTick = true;
					}

					into.Add(new InFlightAbilityObject()
					{
						AbilityID = ability.ID,
						Seed = root.SpawnSeed,
						SpawnTick = root.SpawnTick.Value,
						/* The server tick this object STARTED on, not the current one. The
						 * receiver compares it against its own estimate of the server tick and
						 * fast-forwards by the difference, which is what places the object where
						 * the server holds it rather than at its launch point. */
						ServerStartTick = unchecked(serverTick - root.ElapsedTicks),
						Position = root.SpawnPosition,
						Rotation = root.SpawnRotation,
					});

					if (into.Count >= MAX_PAYLOAD_IN_FLIGHT_OBJECTS)
					{
						return;
					}
				}
			}
		}
	}
}