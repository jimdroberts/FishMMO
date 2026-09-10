using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Gathering node interactable that grants items from a loot table when gathered.
	/// Tracks remaining uses and despawns when depleted.
	/// Configured via a <see cref="GatheringNodeTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class GatheringNode : Interactable, IGatheringNode
	{
		/// <summary>
		/// Template defining the drop table and gather parameters.
		/// </summary>
		public GatheringNodeTemplate Template;

		/// <summary>
		/// Achievement to increment when a player gathers from this node.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		GatheringNodeTemplate IGatheringNode.Template => Template;

		/// <inheritdoc />
		AchievementTemplate IGatheringNode.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Remaining uses before the node is depleted.
		/// Initialized from <see cref="GatheringNodeTemplate.MaxUses"/> on awake.
		/// </summary>
		/// <remarks>
		/// Server state, and deliberately not on the wire. It used to ride the spawn payload —
		/// four bytes per node per observer — to answer a question a client cannot ask honestly:
		/// nothing refreshes it after the payload, so partial depletion was invisible to anyone
		/// already watching, and the only client use was a "&gt; 0" test whose answer is implied by
		/// the node still existing (<see cref="GatheringNodeAction"/> despawns it on reaching
		/// zero). See <see cref="AllowsGathering"/>.
		/// </remarks>
		public int RemainingUses { get; set; }

		private string title = "Gathering Node";

		/// <summary>
		/// Display title shown above the gathering node.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the gathering node UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.forestGreen); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
				RemainingUses = Template.MaxUses;
			}
		}

		/// <summary>
		/// The charge half of the gathering rule, as a pure function so it can be reasoned about
		/// and tested without a NetworkManager.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Truth table:
		/// <list type="bullet">
		/// <item><description>not authoritative, any count → true (the server answers)</description></item>
		/// <item><description>authoritative, count &gt; 0 → true</description></item>
		/// <item><description>authoritative, count &lt;= 0 → false</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// A peer that does not own the count does not get to answer with it. The count is not
		/// replicated — it moves whenever anyone gathers, and there is no message that carries the
		/// new value — so a client asking it would be asking a number frozen at whatever it held
		/// when the node was spawned.
		/// </para>
		/// </remarks>
		/// <param name="remainingUses">The node's remaining charges.</param>
		/// <param name="chargesAreAuthoritative">True when this peer owns the charge count.</param>
		public static bool AllowsGathering(int remainingUses, bool chargesAreAuthoritative)
		{
			return !chargesAreAuthoritative || remainingUses > 0;
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}

			/* Charges are asked only where they are true. The server is the only peer that sees
			 * the decrement in GatheringNodeAction, and the server is where every interaction is
			 * re-checked (InteractableSystem gates on this same method), so refusing a depleted
			 * node stays authoritative while a client stops consulting a value it cannot keep
			 * current. */
			return AllowsGathering(RemainingUses, base.IsServerStarted);
		}

		/// <summary>
		/// Restores the node's charges when this instance returns to the pool.
		/// </summary>
		/// <remarks>
		/// <see cref="OnAwake"/> is where <see cref="RemainingUses"/> is seeded, and Unity calls
		/// Awake once per instance — not once per spawn. A pooled node therefore came back out of
		/// the pool with the zero its previous life ended on, and <see cref="CanInteract"/> refuses
		/// a node with no uses left: every gathering node in a scene became permanently depleted
		/// the first time it was exhausted, for the remaining life of the server process.
		/// </remarks>
		/// <param name="asServer">True when the reset is for the server instance.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			RemainingUses = Template != null ? Template.MaxUses : 0;
		}
	}
}