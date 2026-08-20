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
		/// <summary>
		/// Identity of the scene instance this channel is: the <c>scenes.id</c> of its row.
		/// Process-local scene handles are not unique between scene servers and cannot be used
		/// here — see <see cref="FishMMO.Shared.IPlayerCharacter.SceneHandle"/>.
		/// </summary>
		public long SceneHandle;
		/// <summary>Name of the scene this channel belongs to.</summary>
		public string SceneName;
		/// <summary>Current number of characters in this channel.</summary>
		public int CharacterCount;
	}
}