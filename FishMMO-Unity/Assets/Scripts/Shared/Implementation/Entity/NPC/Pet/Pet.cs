using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a pet NPC, including owner, abilities, and network payload logic.
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
		/// The list of ability template IDs that this pet has learned.
		/// Named PetAbilityIDs to avoid shadowing <see cref="NPC.Abilities"/> (List&lt;AbilityTemplate&gt;).
		/// </summary>
		public List<int> PetAbilityIDs { get; set; }

		/// <summary>
		/// Called when the pet is awakened. Initializes the abilities list.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();
			PetAbilityIDs = new List<int>();
		}

		/// <summary>
		/// Resets the pet's state, clearing owner and abilities.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			PetOwner = null;
			PetAbilityIDs.Clear();
		}

		/// <summary>
		/// Reads the pet's payload from the network, including owner ID. Triggers OnReadID event.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			long ownerID = reader.ReadInt64();

			// Notify listeners that the owner ID has been read for this pet.
			OnReadID?.Invoke(ownerID, this);
		}

		/// <summary>
		/// Writes the pet's payload to the network, including owner ID.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);

			writer.WriteInt64(PetOwner.ID);
		}

		/// <summary>
		/// Copies the given ability IDs into this pet's <see cref="PetAbilityIDs"/>.
		/// </summary>
		/// <param name="abilities">List of ability template IDs to learn.</param>
		public void LearnAbilities(List<int> abilities)
		{
			if (abilities == null)
				return;

			PetAbilityIDs.Clear();
			PetAbilityIDs.AddRange(abilities);
		}
	}
}