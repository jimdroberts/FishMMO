using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Takes the conversation lines the core chat intake has accepted: what is left of a player's line once it has been
	/// rate-gated, sanitised, offered to the slash commands, and passed the mute and flood mute. The Chat module's channel
	/// system implements it (2026-11-20); without that module no conversation line goes anywhere, while commands and
	/// system messages still work.
	/// </summary>
	public interface IChatLineHandler : IServerBehaviour
	{
		/// <summary>Routes one accepted line to its channel. Main thread.</summary>
		/// <param name="conn">The sender's connection.</param>
		/// <param name="sender">The sending character, resolved from the server's own connection map.</param>
		/// <param name="msg">The line, its leading channel word (if any) already removed into <paramref name="command"/>.</param>
		/// <param name="command">The leading slash word (for example "/w"), or empty for plain text.</param>
		/// <param name="receivedTicks">The server's receipt time, UTC ticks (the audit record).</param>
		/// <param name="maxMessageLength">The core intake's message length cap.</param>
		void HandleChatLine(NetworkConnection conn, IPlayerCharacter sender, ChatBroadcast msg, string command, long receivedTicks, int maxMessageLength);
	}
}
