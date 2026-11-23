using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// The core chat intake on a scene server: every line a player sends is rate-gated, sanitised and offered to the
	/// slash commands here, and system messages are sent from here. The channel handlers (<c>IChatHelper</c>) are the Chat
	/// module's, which takes the accepted conversation lines through <see cref="IChatLineHandler"/> .
	/// </summary>
	public interface IChatSystem : IServerBehaviour
	{
		/// <summary>
		/// Rate limit for chat messages per player (messages per second).
		/// </summary>
		float MessageRateLimit { get; }

		/// <summary>
		/// Maximum length allowed for chat messages.
		/// </summary>
		int MaxMessageLength { get; }

		/// <summary>Sends one system line to a connection (the System channel).</summary>
		void OnSendSystemMessage(FishNet.Connection.NetworkConnection conn, string message);
	}
}