using System;
using System.Collections.Generic;
using FishNet.Connection;
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
		public override void OnStartServer()
		{
			base.OnStartServer();

			LearnPersistedAbilities();
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
