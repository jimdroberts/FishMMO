using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Assigns a generated name to a scene object using prefix and suffix caches.
	/// </summary>
	/// <summary>
	/// Assigns a generated name to a scene object using prefix and suffix caches.
	/// Handles network payloads for name synchronization.
	/// </summary>
	public class SceneObjectNamer : NetworkBehaviour
	{
		/// <summary>
		/// Cache of possible name prefixes.
		/// </summary>
		public NameCache Prefix;
		/// <summary>
		/// Cache of possible name suffixes.
		/// </summary>
		public NameCache Suffix;

		/// <summary>
		/// The selected prefix index for this object's name.
		/// </summary>
		private int prefixID;
		/// <summary>
		/// The selected suffix index for this object's name.
		/// </summary>
		private int suffixID;

		/// <summary>
		/// Called when the server starts. Randomizes prefix and suffix IDs for the object name.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			// Randomly select prefix and suffix IDs from available caches.
			prefixID = Prefix == null || Prefix.Names == null ? -1 : Random.Range(0, Prefix.Names.Count);
			suffixID = Suffix == null || Suffix.Names == null ? -1 : Random.Range(0, Suffix.Names.Count);
		}

		/// <summary>
		/// Reads the prefix and suffix IDs from the network and sets the object name accordingly.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="reader">Network reader for payload.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			prefixID = reader.ReadInt32();
			suffixID = reader.ReadInt32();

			// Validate prefix
			if (prefixID < 0 ||
				Prefix == null ||
				Prefix.Names == null ||
				Prefix.Names.Count < 1)
			{
				return;
			}
			string characterName = Prefix.Names[prefixID];

			// Validate suffix
			if (suffixID < 0 ||
				Suffix == null ||
				Suffix.Names == null ||
				Suffix.Names.Count < 1)
			{
				return;
			}
			characterName += $" {Suffix.Names[suffixID]}";

			// Assign the generated name to the GameObject
			this.gameObject.name = characterName.Trim();

#if !UNITY_SERVER
			// If on client, update the character's name label if present
			ICharacter character = transform.GetComponent<ICharacter>();
			if (character != null)
			{
				character.CharacterNameLabel.text = this.gameObject.name;
			}
#endif
		}

		/// <summary>
		/// Writes the prefix and suffix IDs to the network for synchronization.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="writer">Network writer for payload.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt32(prefixID);
			writer.WriteInt32(suffixID);
		}
	}
}