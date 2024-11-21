using UnityEngine;
using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public class Pet : NPC, ISceneObject
	{
		public static Action<long, Pet> OnReadID;

		public PetAbilityTemplate PetAbilityTemplate;
		public ICharacter PetOwner;
		public List<int> Abilities { get; set; }

        public override void OnAwake()
        {
            base.OnAwake();

			Abilities = new List<int>();
			
#if !UNITY_SERVER
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
		}
#else
			SceneObject.Register(this);
		}
#endif
		void OnDestroy()
		{
			SceneObject.Unregister(this);
		}

        public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			PetOwner = null;

#if !UNITY_SERVER
			SceneObject.Unregister(this);
#endif
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			long ownerID = reader.ReadInt64();

			OnReadID?.Invoke(ownerID, this);

			SceneObject.Register(this, ID);
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			writer.WriteInt64(PetOwner.ID);
		}

		public void LearnAbilities(List<int> abilities)
		{
		}
	}
}