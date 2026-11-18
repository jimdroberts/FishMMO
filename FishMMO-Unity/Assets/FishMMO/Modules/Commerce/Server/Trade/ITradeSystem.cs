using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API of the player-to-player trade system.
	/// </summary>
	/// <remarks>
	/// Other systems only ever need to ask one thing of it: whether a character is mid-trade,
	/// so a teleport, a scene hand-off or an instance return can close the session before it
	/// moves the character. Everything else — invitations, offers, acceptance, the atomic
	/// exchange — is driven by client broadcasts and the system's own range tick.
	/// </remarks>
	public interface ITradeSystem : IServerBehaviour
	{
		/// <summary>
		/// The range, in metres, beyond which an open trade is closed by the server.
		/// </summary>
		float MaxTradeDistance { get; }

		/// <summary>
		/// True while the character has a trade window open with someone.
		/// </summary>
		bool IsTrading(long characterID);

		/// <summary>
		/// Closes whatever trade the character is in, telling both parties why.
		/// </summary>
		/// <returns>True when a session was closed.</returns>
		bool CloseTradeFor(IPlayerCharacter character, FishMMO.Shared.TradeCloseReason reason);
	}
}
