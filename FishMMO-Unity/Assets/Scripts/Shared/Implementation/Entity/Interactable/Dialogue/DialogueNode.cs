using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a node in a dialogue tree. Serialized inline on a DialogueTemplate asset.
	/// Contains speaker text, ECA conditions/actions, and branching choices.
	/// </summary>
	[Serializable]
	public class DialogueNode
	{
		/// <summary>
		/// Unique identifier for this node within the dialogue tree.
		/// </summary>
		public int NodeId;

		/// <summary>
		/// Editor-only display name for this node in the dialogue tree editor.
		/// </summary>
		public string NodeName;

		/// <summary>
		/// Optional speaker name override. If empty, the NPC's default name is used.
		/// </summary>
		public string SpeakerName;

		/// <summary>
		/// The dialogue text displayed to the player at this node.
		/// </summary>
		[TextArea(3, 8)]
		public string Text;

		/// <summary>
		/// Conditions that must be met for this node to be accessible.
		/// If any condition fails, the node is skipped or unavailable.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions executed when the player enters this node.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnEnterActions = new List<BaseAction>();

		/// <summary>
		/// Actions executed when the player leaves this node.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnExitActions = new List<BaseAction>();

		/// <summary>
		/// List of choices available to the player at this node.
		/// </summary>
		public List<DialogueChoice> Choices = new List<DialogueChoice>();

		/// <summary>
		/// Editor-only position for the visual dialogue tree editor.
		/// </summary>
		[HideInInspector]
		public Vector2 EditorPosition;
	}
}