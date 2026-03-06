using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Comparison operators for numeric condition evaluation.
	/// </summary>
	public enum ComparisonOperator
	{
		LessThan,
		LessOrEqual,
		Equal,
		GreaterOrEqual,
		GreaterThan
	}

	/// <summary>
	/// Determines which character a condition evaluates against.
	/// </summary>
	public enum ConditionSubject
	{
		/// <summary>The NPC itself.</summary>
		Self,
		/// <summary>The NPC's current combat target.</summary>
		Target
	}

	/// <summary>
	/// Abstract base class for AI ability conditions. Each condition evaluates a boolean
	/// predicate that determines whether an ability rotation entry should be selected.
	/// Conditions are ScriptableObject assets that can be shared across multiple rotations.
	/// <para>
	/// Subclasses override <see cref="Evaluate"/> to implement specific checks such as
	/// health thresholds, buff presence, distance comparisons, etc.
	/// </para>
	/// </summary>
	public abstract class AIAbilityCondition : ScriptableObject
	{
		/// <summary>
		/// Evaluates the condition against the current combat context.
		/// </summary>
		/// <param name="controller">The AI controller of the NPC.</param>
		/// <param name="self">The NPC's character.</param>
		/// <param name="target">The NPC's current target (may be null).</param>
		/// <returns>True if the condition is met.</returns>
		public abstract bool Evaluate(AIController controller, ICharacter self, ICharacter target);

		/// <summary>
		/// Helper to compare two float values using the given operator.
		/// </summary>
		/// <param name="lhs">Left-hand side value.</param>
		/// <param name="op">Comparison operator.</param>
		/// <param name="rhs">Right-hand side value.</param>
		/// <returns>True if the comparison holds.</returns>
		protected static bool Compare(float lhs, ComparisonOperator op, float rhs)
		{
			switch (op)
			{
				case ComparisonOperator.LessThan:       return lhs < rhs;
				case ComparisonOperator.LessOrEqual:    return lhs <= rhs;
				case ComparisonOperator.Equal:          return Mathf.Approximately(lhs, rhs);
				case ComparisonOperator.GreaterOrEqual: return lhs >= rhs;
				case ComparisonOperator.GreaterThan:    return lhs > rhs;
				default: return false;
			}
		}
	}
}
