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

		/// <summary>
		/// Maximum node depth a tree may descend to in one evaluation.
		/// </summary>
		/// <remarks>
		/// Generous enough that no sane hand-authored tree reaches it, small enough that hitting
		/// it costs nothing. A tree deeper than this is a cycle, not a design.
		/// </remarks>
		public const int MAX_EVALUATION_DEPTH = 64;

		/// <summary>
		/// Current recursion depth of the evaluation in progress.
		/// </summary>
		/// <remarks>
		/// Static because evaluation is single-threaded and strictly nested — one NPC's tree is
		/// fully evaluated before the next NPC's begins, on the server's main thread.
		/// </remarks>
		private static int evaluationDepth;

		/// <summary>
		/// True when the evaluation in progress has descended past
		/// <see cref="MAX_EVALUATION_DEPTH"/>, which means the tree contains a cycle.
		/// </summary>
		public static bool DepthExceeded { get; private set; }

		/// <summary>
		/// Resets the depth counter at the start of a tree evaluation.
		/// </summary>
		public static void BeginEvaluation()
		{
			evaluationDepth = 0;
			DepthExceeded = false;
		}

		/// <summary>
		/// Clears evaluation state once a tree evaluation finishes.
		/// </summary>
		public static void EndEvaluation()
		{
			evaluationDepth = 0;
		}

		/// <summary>
		/// Enters a child node, refusing to descend when the depth limit has been reached.
		/// </summary>
		/// <remarks>
		/// Composite and decorator nodes call this before evaluating a child. Returning false
		/// turns a cyclic tree into a failed evaluation — the NPC falls back to its state machine
		/// — instead of a stack overflow that takes the server process down with it.
		/// </remarks>
		/// <returns>True when it is safe to descend.</returns>
		protected static bool TryDescend()
		{
			if (evaluationDepth >= MAX_EVALUATION_DEPTH)
			{
				if (!DepthExceeded)
				{
					DepthExceeded = true;
					FishMMO.Logging.Log.Error("AIBehaviorNode",
						$"Behavior tree evaluation exceeded {MAX_EVALUATION_DEPTH} levels. " +
						"The tree almost certainly contains a cycle — a node connected to one of " +
						"its own ancestors. Evaluation was aborted; fix the asset in the Behavior " +
						"Tree Editor.");
				}
				return false;
			}

			evaluationDepth++;
			return true;
		}

		/// <summary>
		/// Leaves a child node.
		/// </summary>
		protected static void Ascend()
		{
			if (evaluationDepth > 0)
			{
				evaluationDepth--;
			}
		}

		/// <summary>
		/// Evaluates a child node with depth accounting.
		/// </summary>
		/// <param name="child">The child to evaluate. Null returns Failure.</param>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>The child's result, or Failure when the depth limit was hit.</returns>
		protected static AINodeResult EvaluateChild(AIBehaviorNode child, AIController controller)
		{
			if (child == null || !TryDescend())
			{
				return AINodeResult.Failure;
			}

			try
			{
				return child.Evaluate(controller);
			}
			finally
			{
				Ascend();
			}
		}

#if UNITY_EDITOR
		[HideInInspector]
		public Vector2 EditorPosition;

		[TextArea(1, 3)]
		[Tooltip("Designer note — has no runtime effect.")]
		public string EditorComment;
#endif
	}
}