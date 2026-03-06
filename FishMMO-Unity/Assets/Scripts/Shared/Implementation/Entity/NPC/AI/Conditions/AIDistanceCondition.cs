using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that evaluates based on the distance between the NPC and its current target.
	/// <para>
	/// Examples:<br/>
	/// - "Distance ≤ 3" → in melee range, use a cleave ability.<br/>
	/// - "Distance ≥ 15" → far away, use a snipe ability.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Distance Condition", menuName = "FishMMO/Character/NPC/AI/Conditions/Distance Condition")]
	public class AIDistanceCondition : AIAbilityCondition
	{
		/// <summary>
		/// The comparison operator to use against the distance value.
		/// </summary>
		[Tooltip("How to compare the distance value.")]
		public ComparisonOperator Operator = ComparisonOperator.LessOrEqual;

		/// <summary>
		/// Distance threshold in world units.
		/// </summary>
		[Tooltip("Distance threshold in world units.")]
		public float Distance = 5f;

		/// <summary>
		/// Evaluates whether the current distance to the target satisfies the comparison.
		/// Returns false if there is no target.
		/// </summary>
		public override bool Evaluate(AIController controller, ICharacter self, ICharacter target)
		{
			if (controller.Target == null)
				return false;

			float sqrDist = controller.GetSqrDistanceToTarget();
			float dist = Mathf.Sqrt(sqrDist);
			return Compare(dist, Operator, Distance);
		}
	}
}
