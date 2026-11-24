using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>The auction house's counter: an NPC (or anything else) a player uses to open the auction house.</summary>
	public interface IAuctioneer : IInteractable
	{
	}

	/// <summary>
	/// The Auctioneer: opens the auction house window. Every Auctioneer in the world opens the same market.
	/// </summary>
	/// <remarks>
	/// Its interaction trigger ("Auctioneer Interact") runs <see cref="SendAuctioneerBroadcastAction"/>. Every auction
	/// request names the Auctioneer it is made at, and the server refuses one made out of its range, the same way the
	/// mailbox and the bank are guarded.
	/// </remarks>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Auctioneer : Interactable, IAuctioneer
	{
		/// <summary>The display title above the Auctioneer.</summary>
		public override string Title { get { return "Auctioneer"; } }

		/// <summary>Goldenrod, like the other services (banker, mailbox).</summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.goldenrod); } }
	}
}
