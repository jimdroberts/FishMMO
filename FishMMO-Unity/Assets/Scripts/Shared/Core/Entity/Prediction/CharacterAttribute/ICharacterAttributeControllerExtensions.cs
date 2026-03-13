using System.Runtime.CompilerServices;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Extension methods for ICharacterAttributeController that provide convenience accessors
	/// for resource attribute percentages. These are derived from the interface's TryGet methods
	/// and do not require implementation-specific knowledge.
	/// </summary>
	public static class ICharacterAttributeControllerExtensions
	{
		/// <summary>
		/// Gets the current health percentage (CurrentValue / FinalValue).
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <param name="controller">The attribute controller.</param>
		/// <returns>Current health percentage as a float in the range [0.0, 1.0].</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetHealthResourceAttributeCurrentPercentage(this ICharacterAttributeController controller)
		{
			if (controller.TryGetHealthAttribute(out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}

		/// <summary>
		/// Gets the current mana percentage (CurrentValue / FinalValue).
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <param name="controller">The attribute controller.</param>
		/// <returns>Current mana percentage as a float in the range [0.0, 1.0].</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetManaResourceAttributeCurrentPercentage(this ICharacterAttributeController controller)
		{
			if (controller.TryGetManaAttribute(out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}

		/// <summary>
		/// Gets the current stamina percentage (CurrentValue / FinalValue).
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <param name="controller">The attribute controller.</param>
		/// <returns>Current stamina percentage as a float in the range [0.0, 1.0].</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetStaminaResourceAttributeCurrentPercentage(this ICharacterAttributeController controller)
		{
			if (controller.TryGetStaminaAttribute(out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}
	}
}
