using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Root container for a behavior tree. Assign to <see cref="AIController.BehaviorTree"/>
	/// to give an NPC high-level decision-making above the state machine.
	/// <para>
	/// The tree is evaluated once per AI tick (governed by AI LOD). If the root node
	/// returns <see cref="AINodeResult.Success"/>, the tree produced a state transition
	/// and the current state's <c>UpdateState</c> is skipped for that tick.
	/// If it returns <see cref="AINodeResult.Failure"/>, the current state continues normally.
	/// </para>
	/// <para>
	/// <b>Example tree:</b>
	/// <code>
	/// AIBehaviorTree (root = Selector)
	///   ├─ Sequence: [Condition: IsDead] → [StateTransition: DeadState]
	///   ├─ Sequence: [Condition: HP ≤ 30%] → [StateTransition: RetreatState]
	///   ├─ Sequence: [Condition: EnemyNearby] → [StateTransition: AttackState]
	///   └─ StateTransition: WanderState
	/// </code>
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Behavior Tree", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Behavior Tree")]
	public class AIBehaviorTree : ScriptableObject
	{
		/// <summary>
		/// The root node of the behavior tree. Typically a <see cref="AISelector"/> or
		/// <see cref="AISequence"/>.
		/// </summary>
		[Tooltip("Root node of the behavior tree. Typically a Selector.")]
		public AIBehaviorNode Root;

		/// <summary>
		/// How often (in seconds) the tree is re-evaluated. Lower values make the NPC
		/// more responsive but cost more CPU. Defaults to 0.5 seconds.
		/// </summary>
		[Tooltip("Seconds between behavior tree evaluations.")]
		public float TickRate = 0.5f;

		/// <summary>
		/// Evaluates the tree starting from the root.
		/// </summary>
		/// <param name="controller">The NPC's AI controller.</param>
		/// <returns>The result of the root node evaluation.</returns>
		public AINodeResult Evaluate(AIController controller)
		{
			if (Root == null)
				return AINodeResult.Failure;

			return Root.Evaluate(controller);
		}
	}
}