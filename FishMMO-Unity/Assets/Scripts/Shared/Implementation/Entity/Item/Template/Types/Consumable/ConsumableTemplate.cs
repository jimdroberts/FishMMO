using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for consumable item templates, defining type, charge cost, and cooldown behavior.
	/// Provides logic for consuming items and applying cooldowns.
	/// </summary>
	public abstract class ConsumableTemplate : BaseItemTemplate
	{
		/// <summary>
		/// The type of consumable (e.g., potion, scroll).
		/// </summary>
		public ConsumableType ConsumableType;

		/// <summary>
		/// The number of charges consumed per use.
		/// </summary>
		public uint ChargeCost = 1;

		/// <summary>
		/// The cooldown duration (in seconds) applied after consumption.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// The time required to activate this consumable (in seconds).
		/// A value of 0 means instant activation.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Determines if the specified character can consume the given item.
		/// Checks for valid character, item, stackable status, sufficient charges, and cooldown.
		/// </summary>
		/// <param name="character">The player character attempting to consume.</param>
		/// <param name="item">The item to be consumed.</param>
		/// <param name="currentTick">The current network tick for cooldown evaluation.</param>
		/// <returns>True if the item can be consumed, false otherwise.</returns>
		public bool CanConsume(IPlayerCharacter character, Item item, uint currentTick)
		{
			return character != null &&
				   item != null &&
				   item.IsStackable &&
				   item.Stackable.Amount >= ChargeCost &&
				   character.TryGet(out ICooldownController cooldownController) &&
				   !cooldownController.IsOnCooldown(ID, currentTick);
		}

		/// <summary>
		/// Attempts to consume the item, applying cooldown and reducing charges as needed.
		/// Destroys the item if charges are depleted.
		/// </summary>
		/// <param name="character">The player character consuming the item.</param>
		/// <param name="item">The item to be consumed.</param>
		/// <param name="currentTick">The current network tick for cooldown creation.</param>
		/// <returns>True if the item was successfully consumed, false otherwise.</returns>
		public virtual bool Invoke(IPlayerCharacter character, Item item, uint currentTick)
		{
			if (CanConsume(character, item, currentTick) &&
				character.TryGet(out ICooldownController cooldownController))
			{
				if (Cooldown > 0.0f)
				{
					cooldownController.AddCooldown(ID, new CooldownInstance(currentTick, Cooldown, (float)character.NetworkObject.TimeManager.TickDelta));
				}
				if (item.IsStackable)
				{
					// One path for every charge, including the last. ItemStackable.Remove is
					// saturating and destroys the item itself once the stack empties, so there is
					// nothing for a "this is the final charge" special case to do.
					//
					// That special case is what made consumables infinite: when Amount was exactly
					// ChargeCost the old code called Destroy() WITHOUT decrementing, and Destroy()
					// did not zero the stack, so CanConsume kept seeing a full charge and the item
					// could be used forever until the character relogged.
					item.Stackable.Remove(ChargeCost);
				}
				else
				{
					// A non-stackable consumable has exactly one use.
					item.Destroy();
				}
				return true;
			}
			return false;
		}
	}
}