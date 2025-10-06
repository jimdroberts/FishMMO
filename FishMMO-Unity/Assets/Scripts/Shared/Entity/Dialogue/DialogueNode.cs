using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a node in a dialogue tree, containing text, choices, triggers, actions, and conditions.
	/// </summary>
	public class DialogueNode
	{
		/// <summary>
		/// The dialogue text displayed to the player at this node.
		/// </summary>
		public string Text;

		/// <summary>
		/// List of choices available to the player at this node.
		/// Each choice leads to a different dialogue node or outcome.
		/// </summary>
		public List<DialogueChoice> Choices = new List<DialogueChoice>();

		/// <summary>
		/// List of triggers executed when entering this node.
		/// Triggers may activate events, scripts, or other game logic.
		/// </summary>
		public List<Trigger> OnEnterTriggers = new List<Trigger>();

		/// <summary>
		/// List of actions executed when entering this node.
		/// Actions may include giving items, updating quest states, etc.
		/// </summary>
		public List<BaseAction> OnEnterActions = new List<BaseAction>();

		/// <summary>
		/// List of conditions that must be met for this node to be valid and accessible.
		/// Each condition is evaluated before displaying the node.
		/// </summary>
		public List<BaseCondition> Conditions = new List<BaseCondition>();
	}
}