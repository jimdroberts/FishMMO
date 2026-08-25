using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Serializing;
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

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				RemainingUses <= 0 ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
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

		/// <summary>
		/// Writes the gathering node's remaining uses to the network payload.
		/// </summary>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			writer.WriteInt32(RemainingUses);
		}

		/// <summary>
		/// Reads the gathering node's remaining uses from the network payload.
		/// </summary>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			RemainingUses = reader.ReadInt32();
		}
	}
}