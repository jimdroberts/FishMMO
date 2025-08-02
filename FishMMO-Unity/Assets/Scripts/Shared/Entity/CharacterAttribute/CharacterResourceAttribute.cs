using System.Runtime.CompilerServices;

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
		public CharacterResourceAttribute(int templateID, int initialValue, float currentValue, int modifier) : base(templateID, initialValue, modifier)
		{
			this.currentValue = currentValue;
		}

		/// <summary>
		/// Adds the specified value to the current resource value, clamping to the maximum (FinalValue).
		/// Triggers attribute update if the value changes.
		/// </summary>
		/// <param name="value">Amount to add to the current value.</param>
		public void AddToCurrentValue(float value)
		{
			float tmp = currentValue;
			currentValue += value;
			if (currentValue == tmp)
			{
				return;
			}
			if (currentValue > this.FinalValue)
			{
				currentValue = this.FinalValue;
			}
			Internal_OnAttributeChanged(this);
		}

		/// <summary>
		/// Sets the current resource value directly. Optionally triggers attribute update.
		/// </summary>
		/// <param name="value">The new current value.</param>
		/// <param name="updateInternal">If true, triggers attribute update event.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetCurrentValue(float value, bool updateInternal = true)
		{
			currentValue = value;
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
			currentValue -= amount;
			if (currentValue <= 0.001f)
			{
				currentValue = 0.0f;
			}
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
			currentValue += amount;
			if (currentValue >= FinalValue)
			{
				currentValue = FinalValue;
			}
			Internal_OnAttributeChanged(this);
		}

		/// <summary>
		/// Called when the attribute is updated. Invokes base logic and event notification.
		/// </summary>
		/// <param name="attribute">The attribute that was changed.</param>
		protected override void Internal_OnAttributeChanged(CharacterAttribute attribute)
		{
			base.Internal_OnAttributeChanged(attribute);
		}
	}
}