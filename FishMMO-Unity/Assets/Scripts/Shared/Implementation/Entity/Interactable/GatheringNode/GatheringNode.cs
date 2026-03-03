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