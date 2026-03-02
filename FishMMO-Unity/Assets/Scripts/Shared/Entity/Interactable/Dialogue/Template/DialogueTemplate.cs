using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A CachedScriptableObject that defines a complete dialogue tree.
	/// Contains all dialogue nodes, branching choices, and ECA conditions/actions.
	/// Assigned to NPCs via <see cref="DialogueInteractable"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "New Dialogue", menuName = "FishMMO/Dialogue/Dialogue Template", order = 0)]
	public class DialogueTemplate : CachedScriptableObject<DialogueTemplate>, ICachedObject
	{
		/// <summary>
		/// Maximum number of trackable choices per template using a short bitmask.
		/// </summary>
		public const int MaxTrackedChoices = 16;

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
		/// When true, the server preserves dialogue choice selections across interactions
		/// so the client cannot repeat or abuse quest/story-driven dialogue choices.
		/// </summary>
		[Tooltip("When enabled, the server remembers which choices each character has made in this dialogue.")]
		public bool CacheDialogueChoices;

		/// <summary>
		/// All dialogue nodes in this template.
		/// </summary>
		public List<DialogueNode> Nodes = new List<DialogueNode>();

		/// <summary>
		/// Lazily-built node lookup dictionary for fast runtime access.
		/// </summary>
		private Dictionary<int, DialogueNode> nodeMap;

		/// <summary>
		/// The name of the dialogue template (from the ScriptableObject asset name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Retrieves a dialogue node by its ID. Uses the cached node map for O(1) lookup.
		/// </summary>
		/// <param name="nodeId">The node ID to look up.</param>
		/// <returns>The matching DialogueNode, or null if not found.</returns>
		public DialogueNode GetNode(int nodeId)
		{
			Dictionary<int, DialogueNode> map = GetNodeMap();
			map.TryGetValue(nodeId, out DialogueNode node);
			return node;
		}

		/// <summary>
		/// Returns the cached node map, building it on first access.
		/// </summary>
		/// <returns>Dictionary mapping node IDs to their DialogueNode instances.</returns>
		public Dictionary<int, DialogueNode> GetNodeMap()
		{
			if (nodeMap == null)
			{
				nodeMap = BuildNodeMap();
			}
			return nodeMap;
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

		/// <summary>
		/// Computes the flat bit index for a specific choice within this template.
		/// Iterates all nodes and their choices in order to produce a deterministic index.
		/// </summary>
		/// <param name="nodeId">The node containing the choice.</param>
		/// <param name="choiceIndex">The index of the choice within the node's Choices list.</param>
		/// <returns>The bit position (0–15), or -1 if not found or exceeds <see cref="MaxTrackedChoices"/>.</returns>
		public int GetChoiceBitIndex(int nodeId, int choiceIndex)
		{
			int bitIndex = 0;
			for (int i = 0; i < Nodes.Count; i++)
			{
				if (Nodes[i] == null)
				{
					continue;
				}

				for (int j = 0; j < Nodes[i].Choices.Count; j++)
				{
					if (Nodes[i].NodeId == nodeId && j == choiceIndex)
					{
						return bitIndex < MaxTrackedChoices ? bitIndex : -1;
					}
					bitIndex++;
				}
			}
			return -1;
		}
	}
}