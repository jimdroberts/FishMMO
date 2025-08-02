using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that evaluates whether an ability object's hit count satisfies a specified comparison.
	/// </summary>
	[CreateAssetMenu(fileName = "New Hit Count Condition", menuName = "FishMMO/Triggers/Conditions/Ability/Hit Count", order = 20)]
	public sealed class HitCountCondition : BaseCondition
	{
		/// <summary>
		/// The hit count value to compare against the ability object's hit count.
		/// </summary>
		[Tooltip("The HitCount value to compare against.")]
		public int RequiredHitCount;

		/// <summary>
		/// The type of comparison to perform with the ability object's hit count.
		/// </summary>
		[Tooltip("The type of comparison to perform with the AbilityObject's HitCount.")]
		public HitCountComparisonType ComparisonType;

		/// <summary>
		/// Evaluates whether the ability object's hit count satisfies the specified comparison type and value.
		/// </summary>
		/// <param name="initiator">The character initiating the evaluation.</param>
		/// <param name="eventData">The event data containing the ability object.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Try to get the ability collision event data. If not present, log a warning and return false.
			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject == null)
				{
					Log.Warning("HitCountCondition", $"No AbilityObject found in AbilityHitEventData for initiator {initiator?.Name}.");
					return false;
				}

				// Perform the comparison based on the selected type
				switch (ComparisonType)
				{
					case HitCountComparisonType.GreaterThan:
						return abilityObject.HitCount > RequiredHitCount;
					case HitCountComparisonType.GreaterThanOrEqualTo:
						return abilityObject.HitCount >= RequiredHitCount;
					case HitCountComparisonType.LessThan:
						return abilityObject.HitCount < RequiredHitCount;
					case HitCountComparisonType.LessThanOrEqualTo:
						return abilityObject.HitCount <= RequiredHitCount;
					case HitCountComparisonType.EqualTo:
						return abilityObject.HitCount == RequiredHitCount;
					case HitCountComparisonType.NotEqualTo:
						return abilityObject.HitCount != RequiredHitCount;
					default:
						Log.Error("HitCountCondition", $"Unknown ComparisonType {ComparisonType}.");
						return false;
				}
			}
			else
			{
				// This condition only makes sense for AbilityHitEventData as it requires an AbilityObject
				Log.Warning("HitCountCondition", $"EventData is not of type AbilityHitEventData for initiator {initiator?.Name}.");
				return false;
			}
		}

		/// <summary>
		/// Returns a formatted description of the hit count condition for UI display.
		/// </summary>
		/// <returns>A string describing the comparison and required hit count.</returns>
		public override string GetFormattedDescription()
		{
			string comparison = ComparisonType.ToString().Replace("OrEqualTo", " or equal to").Replace("GreaterThan", "> ").Replace("LessThan", "< ").Replace("EqualTo", "==").Replace("NotEqualTo", "!=");
			return $"Ability HitCount must be {ComparisonType.ToString().Replace("OrEqualTo", " or equal to").Replace("GreaterThan", "> ").Replace("LessThan", "< ").Replace("EqualTo", "equal to").Replace("NotEqualTo", "not equal to")} {RequiredHitCount}.";
		}
	}
}