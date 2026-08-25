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
		/// <summary>
		/// Reserved. Always zero on the wire.
		/// </summary>
		/// <remarks>
		/// A channel switch is never a direct dial. The scene server releases the character and
		/// drops the connection; the client returns through the world server, which reads the
		/// destination from the character row and issues its own <c>WorldSceneConnectBroadcast</c>.
		/// A client therefore has no use for the hosting scene server's port, and sending it
		/// published which scene servers host which instances to every player who opened the
		/// picker. <c>SceneChannelSystem</c> leaves this at zero and ignores whatever a client
		/// echoes back in <see cref="SceneChannelSelectBroadcast"/>: the only field a selection is
		/// resolved from is <see cref="SceneHandle"/>.
		/// </remarks>
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