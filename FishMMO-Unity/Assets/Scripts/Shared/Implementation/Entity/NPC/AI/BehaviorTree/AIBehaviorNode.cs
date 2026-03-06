using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Result of a behavior tree node evaluation.
	/// </summary>
	public enum AINodeResult
	{
		/// <summary>The node completed successfully.</summary>
		Success,
		/// <summary>The node failed.</summary>
		Failure,
		/// <summary>The node is still running (e.g., waiting for an action to finish).</summary>
		Running
	}

	/// <summary>
	/// Abstract base class for all behavior tree nodes. Nodes are ScriptableObject assets
	/// so designers can build trees entirely in the Unity inspector without code.
	/// <para>
	/// Behavior trees sit above the state machine layer and decide which <see cref="BaseAIState"/>
	/// the NPC should transition to. The state machine then handles the actual movement, combat,
	/// and animation logic.
	/// </para>
	/// <para>
	/// <b>Node types:</b>
	/// <list type="bullet">
	///   <item><see cref="AISelector"/>  — Tries children left-to-right, returns first success.</item>
	///   <item><see cref="AISequence"/>  — Runs children left-to-right, fails on first failure.</item>
	///   <item><see cref="AIInverter"/>  — Inverts the child's result.</item>
	///   <item><see cref="AIRepeater"/> — Repeats the child a configurable number of times.</item>
	///   <item><see cref="AIConditionNode"/> — Leaf that checks an <see cref="AIAbilityCondition"/>.</item>
	///   <item><see cref="AIStateTransitionNode"/> — Leaf that transitions to a <see cref="BaseAIState"/>.</item>
	/// </list>
	/// </para>
	/// </summary>
	public abstract class AIBehaviorNode : ScriptableObject
	{
		/// <summary>
		/// Evaluate this node for the given NPC.
		/// Called once per behavior tree tick (not every frame).
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>The result of this node's evaluation.</returns>
		public abstract AINodeResult Evaluate(AIController controller);

#if UNITY_EDITOR
		[TextArea(1, 3)]
		[Tooltip("Designer note — has no runtime effect.")]
		public string EditorComment;
#endif
	}
}