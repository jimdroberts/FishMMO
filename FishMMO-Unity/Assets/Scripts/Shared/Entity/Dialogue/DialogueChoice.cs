using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a choice in a dialogue node, including text, next node, conditions, actions, and triggers.
	/// </summary>
	public class DialogueChoice
	{
		/// <summary>
		/// The text displayed for this choice in the dialogue UI.
		/// </summary>
		public string Text;

		/// <summary>
		/// The ID of the next dialogue node to transition to if this choice is selected.
		/// </summary>
		public int NextNodeId;

		/// <summary>
		/// List of conditions that must be met for this choice to be available to the player.
		/// Each condition is evaluated before displaying the choice.
		/// </summary>
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// List of actions to execute when this choice is selected by the player.
		/// Actions may include giving items, updating quest states, etc.
		/// </summary>
		public List<BaseAction> Actions = new List<BaseAction>();

		/// <summary>
		/// List of triggers to execute when this choice is selected.
		/// Triggers may activate events, scripts, or other game logic.
		/// </summary>
		public List<Trigger> Triggers = new List<Trigger>();
	}
}