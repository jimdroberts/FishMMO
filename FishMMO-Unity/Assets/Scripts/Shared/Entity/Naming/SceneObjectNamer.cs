using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public class SceneObjectNamer : NetworkBehaviour
	{
		public NameCache Prefix;
		public NameCache Suffix;

		private int prefixID;
		private int suffixID;

		public override void OnStartServer()
		{
			base.OnStartServer();

			prefixID = Prefix == null || Prefix.Names == null ? -1 : Random.Range(0, Prefix.Names.Count);
			suffixID = Suffix == null || Suffix.Names == null ? -1 : Random.Range(0, Suffix.Names.Count);
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			prefixID = reader.ReadInt32();
			suffixID = reader.ReadInt32();

			if (prefixID < 0 ||
				Prefix == null ||
				Prefix.Names == null ||
				Prefix.Names.Count < 1)
			{
				return;
			}
			string characterName = Prefix.Names[prefixID];

			if (suffixID < 0 ||
				Suffix == null ||
				Suffix.Names == null ||
				Suffix.Names.Count < 1)
			{
				return;
			}
			characterName += $" {Suffix.Names[suffixID]}";

			this.gameObject.name = characterName.Trim();

#if !UNITY_SERVER
			ICharacter character = transform.GetComponent<ICharacter>();
			if (character != null)
			{
				character.CharacterNameLabel.text = this.gameObject.name;
			}
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt32(prefixID);
			writer.WriteInt32(suffixID);
		}
	}
}