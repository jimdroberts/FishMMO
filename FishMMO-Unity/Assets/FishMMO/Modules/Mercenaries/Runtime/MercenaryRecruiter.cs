using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>A mercenary for hire: an NPC (or anything else) a player speaks to, to hire the mercenary it offers.</summary>
	public interface IMercenaryRecruiter : IInteractable
	{
		/// <summary>The kit hired here.</summary>
		MercenaryKitTemplate Kit { get; }
	}

	/// <summary>
	/// A Mercenary (issue #135): a sellsword standing in town, titled "Mercenary" under their name. Speaking to them asks
	/// "Would you like to hire this mercenary?"; hiring takes them, with their kit, into the player's service.
	/// </summary>
	/// <remarks>
	/// Its interaction trigger ("Mercenary Interact") runs <see cref="SendMercenaryOfferAction"/>. The hire names this
	/// Mercenary, and the server refuses one made out of its range, the same way the auctioneer and the bank are guarded.
	/// <para>
	/// The one hired is this one: the hired mercenary appears where they stood, with their name, race and face
	/// (<see cref="SceneObjectNamer.CaptureName"/>, <see cref="NPC.RaceTemplate"/>, <see cref="NPC.AppearanceSeed"/>), and
	/// this one leaves their post through its spawner, which sends a new sellsword there in its respawn time. One player
	/// hires a given Mercenary: a second hire of them while the first is answered is refused.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class MercenaryRecruiter : Interactable, IMercenaryRecruiter
	{
		[Tooltip("The kit hired from this Mercenary.")]
		[SerializeField]
		private MercenaryKitTemplate kit;

		/// <inheritdoc />
		public MercenaryKitTemplate Kit => kit;

		/// <summary>The display title under the Mercenary's name.</summary>
		public override string Title { get { return "Mercenary"; } }

		/// <summary>Goldenrod, like the other services (banker, auctioneer).</summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.goldenrod); } }

		/// <summary>The name a player reads (the generated one when there is one).</summary>
		public string DisplayName
		{
			get
			{
				SceneObjectNamer namer = GetComponent<SceneObjectNamer>();
				return namer != null && !string.IsNullOrEmpty(namer.DisplayName) ? namer.DisplayName : GameObject.name;
			}
		}

#if UNITY_EDITOR
		/// <summary>Editor: sets the kit (the module's builder).</summary>
		public void SetKitForEditor(MercenaryKitTemplate value)
		{
			kit = value;
		}
#endif
	}
}
