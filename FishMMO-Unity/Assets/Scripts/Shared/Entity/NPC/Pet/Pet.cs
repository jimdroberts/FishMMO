using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public class Pet : NPC
	{
		public static Action<long, Pet> OnReadID;

		public PetAbilityTemplate PetAbilityTemplate;
		public ICharacter PetOwner;
		public List<int> Abilities { get; set; }

        public override void OnAwake()
        {
            base.OnAwake();

			Abilities = new List<int>();
		}

        public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			PetOwner = null;
			Abilities.Clear();
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			long ownerID = reader.ReadInt64();

			OnReadID?.Invoke(ownerID, this);
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);

			writer.WriteInt64(PetOwner.ID);
		}

		public void LearnAbilities(List<int> abilities)
		{
		}
	}
}