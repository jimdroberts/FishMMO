using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Serializing;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a pet NPC, including owner, abilities, orders, and network payload logic.
	/// </summary>
	public class Pet : NPC
	{
		/// <summary>
		/// Event triggered when a pet's owner ID is read from the network payload.
		/// </summary>
		public static Action<long, Pet> OnReadID;

		/// <summary>
		/// The template defining this pet's abilities and behavior.
		/// </summary>
		public PetAbilityTemplate PetAbilityTemplate;

		/// <summary>
		/// The character that owns this pet.
		/// </summary>
		public ICharacter PetOwner;

		/// <summary>
		/// How willing this pet is to start a fight on its own.
		/// </summary>
		/// <remarks>
		/// Server-authoritative. Clients learn it from the spawn payload and from
		/// <see cref="PetStanceBroadcast"/>; they never set it locally, so a modified client
		/// cannot put its pet into a stance the server disagrees with.
		/// </remarks>
		public PetStance Stance { get; set; } = PetStance.Defensive;

		/// <summary>
		/// Whether the pet is heeling or holding position.
		/// </summary>
		public PetMovementOrder MovementOrder { get; set; } = PetMovementOrder.Follow;

		/// <summary>
		/// The list of ability template IDs that this pet has learned.
		/// The name avoids shadowing <see cref="NPC.Abilities"/> (List&lt;AbilityTemplate&gt;).
		/// </summary>
		public List<int> PetAbilityIDs { get; set; }

		/// <summary>
		/// Attribute values restored from the database, applied at spawn.
		/// </summary>
		/// <remarks>
		/// Staged rather than applied directly for the same reason <see cref="PetAbilityIDs"/> is:
		/// the pet system fills this in before <c>ServerManager.Spawn</c>, and the values can only
		/// be written once <see cref="NPC.OnStartServer"/> has built the attribute controller and
		/// rolled the prefab's own bonuses over it. See <see cref="ApplyPersistedState"/>.
		/// </remarks>
		public List<PetPersistedAttribute> PersistedAttributes { get; set; }

		/// <summary>
		/// Buffs restored from the database, applied at spawn.
		/// </summary>
		public List<PetPersistedBuff> PersistedBuffs { get; set; }

		/// <summary>
		/// Called when the pet is awakened. Initializes the abilities list.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			if (PetAbilityIDs == null)
			{
				PetAbilityIDs = new List<int>();
			}
		}

		/// <summary>
		/// Server spawn. Runs after <see cref="NPC.OnStartServer"/> has taught the prefab's own
		/// ability list, and adds anything restored from the database on top.
		/// </summary>
		/// <remarks>
		/// This is the last point at which restored state can be written and still reach clients
		/// in the spawn payload. FishNet invokes the start callbacks during
		/// <c>SpawnWithoutChecks</c> and only writes each observer's payload afterwards, in the
		/// observer rebuild — so anything applied here is serialised, and anything applied after
		/// <c>ServerManager.Spawn</c> returns is not.
		/// </remarks>
		public override void OnStartServer()
		{
			base.OnStartServer();

			LearnPersistedAbilities();
			ApplyPersistedState();
		}

		/// <summary>
		/// Writes restored attribute values and buffs onto this pet's controllers.
		/// </summary>
		/// <remarks>
		/// Attributes before buffs, matching the order the owner's own state is restored in:
		/// a buff's modifiers are re-applied by <see cref="IBuffController.Apply(Buff, bool)"/>
		/// on top of the base values, so writing the base values second would double-count them.
		/// </remarks>
		private void ApplyPersistedState()
		{
			if (!base.IsServerStarted)
			{
				return;
			}

			ApplyPersistedAttributes();
			ApplyPersistedBuffs();
		}

		/// <summary>
		/// Restores saved attribute values, distinguishing resources by template rather than by
		/// whether their current value happens to be non-zero.
		/// </summary>
		private void ApplyPersistedAttributes()
		{
			if (PersistedAttributes == null ||
				PersistedAttributes.Count < 1 ||
				!this.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			for (int i = 0; i < PersistedAttributes.Count; ++i)
			{
				PetPersistedAttribute attribute = PersistedAttributes[i];

				/* Asked of the template, never inferred from CurrentValue. A resource sitting at
				 * zero — a pet dismissed at death's door, an empty mana pool — is still a
				 * resource, and filing it as a plain attribute strands the real resource
				 * unrestored while planting a bogus base attribute in its place. */
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(attribute.TemplateID);
				if (template == null)
				{
					Log.Warning("Pet", $"{gameObject.name} has a persisted attribute template {attribute.TemplateID}, which no longer exists. Skipping.");
					continue;
				}

				if (template.IsResourceAttribute)
				{
					attributeController.SetResourceAttribute(attribute.TemplateID, attribute.Value, attribute.CurrentValue, null);
				}
				else
				{
					attributeController.SetAttribute(attribute.TemplateID, attribute.Value);
				}
			}
		}

		/// <summary>
		/// Rebuilds saved buffs, converting the persisted remaining seconds back into absolute
		/// ticks against this server's clock.
		/// </summary>
		private void ApplyPersistedBuffs()
		{
			if (PersistedBuffs == null ||
				PersistedBuffs.Count < 1 ||
				!this.TryGet(out IBuffController buffController))
			{
				return;
			}

			TimeManager timeManager = base.TimeManager;
			if (timeManager == null)
			{
				return;
			}

			float tickDelta = (float)timeManager.TickDelta;
			if (tickDelta <= 0f)
			{
				return;
			}

			uint currentTick = buffController.ResolveAuthoritativeTick(timeManager.LocalTick);

			for (int i = 0; i < PersistedBuffs.Count; ++i)
			{
				PetPersistedBuff persisted = PersistedBuffs[i];

				// At least one tick in the future: a buff restored with an expiry already behind
				// the clock is removed on the very tick it is applied, which reads as the buff
				// simply not surviving the despawn at all.
				uint expiryTick = currentTick + (uint)Math.Max(1.0, Math.Ceiling(persisted.RemainingTime / tickDelta));
				uint nextTickTick = currentTick + (uint)Math.Max(1.0, Math.Ceiling(persisted.TickTime / tickDelta));

				buffController.Apply(new Buff(persisted.TemplateID, expiryTick, nextTickTick, tickDelta, persisted.Stacks, persisted.TickCount));
			}
		}

		/// <summary>
		/// Resets the pet's state, clearing owner, orders, and abilities.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			PetOwner = null;
			PetAbilityTemplate = null;
			Stance = PetStance.Defensive;
			MovementOrder = PetMovementOrder.Follow;

			/* Replace rather than Clear. PetAbilityIDs is assigned by reference when a pet is
			 * restored from the database, so clearing it here would empty the caller's list too —
			 * including the copy the persistence path is about to write back. */
			PetAbilityIDs = new List<int>();

			// Restore payloads are per-spawn. Left in place they would be re-applied to whatever
			// pet next occupies this pool slot.
			PersistedAttributes = null;
			PersistedBuffs = null;
		}

		/// <summary>
		/// Reads the pet's payload from the network, including owner ID and current orders.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			long ownerID = reader.ReadInt64();
			Stance = (PetStance)reader.ReadUInt8Unpacked();
			MovementOrder = (PetMovementOrder)reader.ReadUInt8Unpacked();

			// Notify listeners that the owner ID has been read for this pet.
			OnReadID?.Invoke(ownerID, this);
		}

		/// <summary>
		/// Writes the pet's payload to the network, including owner ID and current orders.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);

			// PetOwner can legitimately be null for a frame if the owner despawned between spawn
			// and payload write; writing 0 lets the client resolve "no owner" instead of throwing
			// inside FishNet's serializer and dropping the whole spawn message.
			writer.WriteInt64(PetOwner != null ? PetOwner.ID : 0);
			writer.WriteUInt8Unpacked((byte)Stance);
			writer.WriteUInt8Unpacked((byte)MovementOrder);
		}

		/// <summary>
		/// Returns the pet to the pool immediately, skipping the corpse decay timer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A pet's lifetime is tied to its owner rather than to an <see cref="ObjectSpawner"/>, so
		/// it skips the corpse timer and despawns through the network manager directly.
		/// <see cref="NPC.ReturnToPool"/> routes through the spawner, which a pet does not have,
		/// so the previous implementation called it and silently did nothing.
		/// </para>
		/// <para>
		/// A genuine <c>override</c>, not the <c>new</c> it used to be. Method hiding only applies
		/// when the caller's static type is <see cref="Pet"/>; anything holding the pet as an
		/// <see cref="NPC"/> or an <see cref="ISpawnable"/> — which is how the spawner and several
		/// death paths see it — would silently get the NPC corpse behaviour instead, leaving the
		/// pet parked in the world as an immortal, AI-disabled corpse that nothing ever collects.
		/// </para>
		/// </remarks>
		public override void Despawn()
		{
			if (NetworkObject != null && NetworkObject.IsSpawned && base.IsServerStarted)
			{
				NetworkManager.ServerManager.Despawn(NetworkObject, FishNet.Object.DespawnType.Pool);
				return;
			}

			ReturnToPool();
		}

		/// <summary>
		/// Records the ability template IDs this pet should know.
		/// </summary>
		/// <remarks>
		/// Only records them. Call <see cref="LearnPersistedAbilities"/> to actually teach them to
		/// the ability controller — which is the step that was missing entirely: pet ability IDs
		/// were loaded from the database, held on this list, and saved back out without ever
		/// reaching an <see cref="IAbilityController"/>, so every pet spawned with an empty
		/// spellbook and could never attack.
		/// </remarks>
		/// <param name="abilities">List of ability template IDs to learn.</param>
		public void LearnAbilities(List<int> abilities)
		{
			if (abilities == null)
			{
				return;
			}

			PetAbilityIDs.Clear();
			PetAbilityIDs.AddRange(abilities);

			LearnPersistedAbilities();
		}

		/// <summary>
		/// Teaches every template ID in <see cref="PetAbilityIDs"/> to the pet's ability
		/// controller, skipping any it already knows.
		/// </summary>
		/// <remarks>
		/// Server-only in effect: the controller is populated authoritatively and replicated, the
		/// same way <see cref="NPC"/> teaches its inspector-configured list.
		/// </remarks>
		public void LearnPersistedAbilities()
		{
			if (!base.IsServerStarted)
			{
				return;
			}
			if (PetAbilityIDs == null || PetAbilityIDs.Count < 1)
			{
				return;
			}
			if (!this.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			for (int i = 0; i < PetAbilityIDs.Count; ++i)
			{
				int templateID = PetAbilityIDs[i];
				if (templateID <= 0)
				{
					continue;
				}

				if (abilityController.KnownAbilities.ContainsKey(templateID))
				{
					continue;
				}

				AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(templateID);
				if (template == null)
				{
					Log.Warning("Pet", $"{gameObject.name} has persisted ability template {templateID}, which no longer exists. Skipping.");
					continue;
				}

				// Template ID doubles as the instance ID, matching how NPC learns its abilities.
				abilityController.LearnAbility(new Ability(templateID, template));
			}
		}

		/// <summary>
		/// Records the abilities the pet currently knows into <see cref="PetAbilityIDs"/> so they
		/// survive a despawn / re-summon cycle.
		/// </summary>
		/// <remarks>
		/// Called by the pet system before persisting. Without it, abilities granted at summon
		/// time from the <see cref="PetAbilityTemplate"/> were never written to the database and a
		/// re-logged pet came back empty.
		/// </remarks>
		public void CaptureKnownAbilities()
		{
			if (!this.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			PetAbilityIDs.Clear();
			foreach (KeyValuePair<long, Ability> pair in abilityController.KnownAbilities)
			{
				Ability ability = pair.Value;
				if (ability != null && ability.Template != null)
				{
					PetAbilityIDs.Add(ability.Template.ID);
				}
			}
		}
	}
}
