using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;

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
		/// The list of ability IDs that this pet has learned.
		/// </summary>
		public List<int> Abilities { get; set; }

		/// <summary>
		/// Called when the pet is awakened. Initializes the abilities list.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();
			Abilities = new List<int>();
		}

		/// <summary>
		/// Resets the pet's state, clearing owner and abilities.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			PetOwner = null;
			Abilities.Clear();
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
		/// Allows the pet to learn a set of abilities. (Implementation needed)
		/// </summary>
		/// <param name="abilities">List of ability IDs to learn.</param>
		public void LearnAbilities(List<int> abilities)
		{
			// Implementation for learning abilities should be added here.
		}
	}
}