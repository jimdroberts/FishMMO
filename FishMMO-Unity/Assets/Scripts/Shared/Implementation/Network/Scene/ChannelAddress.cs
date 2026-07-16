using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable struct representing a scene channel, including server connection info, scene identity, and population.
	/// Used for channel selection and network communication.
	/// </summary>
	[Serializable]
	public struct ChannelAddress
	{
		/// <summary>Port number for the scene server hosting this channel.</summary>
		public ushort Port;
		/// <summary>Handle identifying the specific scene instance for this channel.</summary>
		public int SceneHandle;
		/// <summary>Name of the scene this channel belongs to.</summary>
		public string SceneName;
		/// <summary>Current number of characters in this channel.</summary>
		public int CharacterCount;
	}
}