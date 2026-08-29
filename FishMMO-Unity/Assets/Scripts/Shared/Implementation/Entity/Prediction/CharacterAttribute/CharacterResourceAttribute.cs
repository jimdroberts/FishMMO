using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character resource attribute (e.g., health, mana, stamina) that can be consumed or regenerated.
	/// Extends CharacterAttribute to add current value tracking and resource-specific logic.
	/// </summary>
	public class CharacterResourceAttribute : CharacterAttribute
	{
		/// <summary>
		/// The current value of the resource (e.g., current health or mana).
		/// </summary>
		private float currentValue;

		/// <summary>
		/// Gets the current value of the resource.
		/// </summary>
		public float CurrentValue { get { return currentValue; } }

		/// <summary>
		/// Returns a string representation of the resource attribute (e.g., "Health: 50/100").
		/// </summary>
		public override string ToString()
		{
			return Template.Name + ": " + (int)currentValue + "/" + FinalValue;
		}

		/// <summary>
		/// Constructs a new CharacterResourceAttribute with the given template ID, initial value, current value, and modifier.
		/// </summary>
		/// <param name="templateID">The template ID for this resource attribute.</param>
		/// <param name="initialValue">The initial base value.</param>
		/// <param name="currentValue">The starting current value.</param>
		/// <param name="modifier">The initial modifier value.</param>
		public CharacterResourceAttribute(ICharacterAttributeController characterAttributeController, int templateID, int initialValue, float currentValue, int modifier) : base(characterAttributeController, templateID, initialValue, modifier)
		{
			this.currentValue = ClampCurrentValue(currentValue);
		}

		/// <summary>
		/// Adds the specified value to the current resource value, clamping to the maximum (FinalValue).
		/// Triggers attribute update if the value changes.
		/// </summary>
		/// <param name="value">Amount to add to the current value.</param>
		public void AddToCurrentValue(float value)
		{
			float tmp = currentValue;
			currentValue = ClampCurrentValue(currentValue + value);
			if (currentValue == tmp)
			{
				return;
			}
			Internal_OnAttributeChanged(this);
		}

		/// <summary>
		/// Sets the current resource value directly. Optionally triggers attribute update.
		/// </summary>
		/// <param name="value">The new current value.</param>
		/// <param name="clampFinalValue">If true, clamps the value to FinalValue. If false, only clamps to zero.</param>
		/// <param name="updateInternal">If true, triggers attribute update event.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetCurrentValue(float value, bool updateInternal = true)
		{
			float clamped = ClampCurrentValue(value);
			if (currentValue == clamped)
			{
				return;
			}

			currentValue = clamped;

			/* Before the updateInternal branch, not inside it. That flag suppresses the change
			 * NOTIFICATION, which callers turn off to avoid re-entrancy — it does not mean the
			 * value did not change, and persistence has to follow the value. */
			PersistenceDirty = true;

			if (updateInternal)
			{
				Internal_OnAttributeChanged(this);
			}
			//UnityEngine.Log.Debug($"Set {Template.Name} to {value} - [{currentValue}/{FinalValue}]");
		}

		/// <summary>
		/// Consumes the specified amount from the current resource value, clamping to zero.
		/// Triggers attribute update event.
		/// </summary>
		/// <param name="amount">Amount to consume.</param>
		public void Consume(float amount)
		{
			float clamped = ClampCurrentValue(currentValue - amount);
			if (currentValue == clamped)
			{
				return;
			}

			currentValue = clamped;
			//UnityEngine.Log.Debug($"Consumed {amount} {Template.Name} - [{currentValue}/{FinalValue}]");
			Internal_OnAttributeChanged(this);
		}

		/// <summary>
		/// Gains the specified amount to the current resource value, clamping to the maximum (FinalValue).
		/// Triggers attribute update event.
		/// </summary>
		/// <param name="amount">Amount to gain.</param>
		public void Gain(float amount)
		{
			float clamped = ClampCurrentValue(currentValue + amount);
			if (currentValue == clamped)
			{
				return;
			}

			currentValue = clamped;
			Internal_OnAttributeChanged(this);
		}

		/// <summary>
		/// Clamps a resource value to the valid range of [0, FinalValue].
		/// </summary>
		/// <param name="value">The resource value to clamp.</param>
		/// <param name="clampFinalValue">If true, clamps the value to FinalValue. If false, only clamps to zero.</param>
		/// <returns>The clamped resource value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		/// <summary>Clamps a resource value to [0, FinalValue] once the character is fully loaded. Before load the upper bound is left open so temporary final-value overrides (buffs, loading) are not lost.</summary>
		private float ClampCurrentValue(float value)
		{
			if (value <= 0.001f)
			{
				return 0.0f;
			}

			// If the character isn't fully loaded yet, we want to allow setting current value above final value so that 
			// it can be properly clamped once the character is loaded and controllers are active. This is necessary to 
			// prevent issues with loading characters that have current resource values above their final values due to 
			// buffs or other effects, which would otherwise get clamped down to final value during loading and then fail 
			// to properly update when the character is fully loaded and the correct final value is set.
			bool clampFinalValue = this.characterAttributeController.Character.Flags.IsFlagged(CharacterFlags.IsLoaded);

			if (clampFinalValue && value >= FinalValue)
			{
				return FinalValue;
			}

			return value;
		}

		/// <summary>
		/// Called when the attribute is updated. Invokes base logic and event notification.
		/// </summary>
		/// <param name="attribute">The attribute that was changed.</param>
		protected override void Internal_OnAttributeChanged(CharacterAttribute attribute)
		{
			currentValue = ClampCurrentValue(currentValue);
			base.Internal_OnAttributeChanged(attribute);
		}
	}
}