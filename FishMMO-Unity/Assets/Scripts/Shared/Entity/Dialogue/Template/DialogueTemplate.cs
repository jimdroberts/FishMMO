using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A CachedScriptableObject that defines a complete dialogue tree.
	/// Contains all dialogue nodes, branching choices, and ECA conditions/actions.
	/// Assigned to NPC characters via NPCDialogueController.
	/// </summary>
	[CreateAssetMenu(fileName = "New Dialogue", menuName = "FishMMO/Dialogue/Dialogue Template", order = 0)]
	public class DialogueTemplate : CachedScriptableObject<DialogueTemplate>, ICachedObject
	{
		/// <summary>
		/// Optional icon for the dialogue, displayed in UI or editor.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// Description of the dialogue, used for editor notes.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		/// <summary>
		/// The node ID where the dialogue starts.
		/// </summary>
		public int StartNodeId;

		/// <summary>
		/// All dialogue nodes in this template.
		/// </summary>
		public List<DialogueNode> Nodes = new List<DialogueNode>();

		/// <summary>
		/// The name of the dialogue template (from the ScriptableObject asset name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Retrieves a dialogue node by its ID.
		/// </summary>
		/// <param name="nodeId">The node ID to look up.</param>
		/// <returns>The matching DialogueNode, or null if not found.</returns>
		public DialogueNode GetNode(int nodeId)
		{
			for (int i = 0; i < Nodes.Count; i++)
			{
				if (Nodes[i] != null && Nodes[i].NodeId == nodeId)
				{
					return Nodes[i];
				}
			}
			return null;
		}

		/// <summary>
		/// Generates the next available node ID for the dialogue tree.
		/// </summary>
		/// <returns>An unused node ID.</returns>
		public int GenerateNodeId()
		{
			int maxId = -1;
			for (int i = 0; i < Nodes.Count; i++)
			{
				if (Nodes[i] != null && Nodes[i].NodeId > maxId)
				{
					maxId = Nodes[i].NodeId;
				}
			}
			return maxId + 1;
		}

		/// <summary>
		/// Builds a lookup dictionary from node ID to DialogueNode for fast runtime access.
		/// </summary>
		/// <returns>Dictionary mapping node IDs to their DialogueNode instances.</returns>
		public Dictionary<int, DialogueNode> BuildNodeMap()
		{
			var map = new Dictionary<int, DialogueNode>(Nodes.Count);
			for (int i = 0; i < Nodes.Count; i++)
			{
				if (Nodes[i] != null)
				{
					map[Nodes[i].NodeId] = Nodes[i];
				}
			}
			return map;
		}
	}
}