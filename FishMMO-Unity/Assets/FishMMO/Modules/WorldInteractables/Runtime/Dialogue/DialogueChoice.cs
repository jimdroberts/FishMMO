using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a player choice within a dialogue node. Serialized inline on DialogueNode.
	/// Contains display text, conditions for availability, actions on selection, and a link to the next node.
	/// </summary>
	[Serializable]
	public class DialogueChoice
	{
		/// <summary>
		/// The text displayed for this choice in the dialogue UI.
		/// </summary>
		public string Text;

		/// <summary>
		/// The ID of the next dialogue node to transition to when this choice is selected.
		/// A value of -1 ends the dialogue.
		/// </summary>
		public int NextNodeId = -1;

		/// <summary>
		/// Conditions that must be met for this choice to be visible to the player.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions executed when the player selects this choice.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnSelectActions = new List<BaseAction>();
	}
}