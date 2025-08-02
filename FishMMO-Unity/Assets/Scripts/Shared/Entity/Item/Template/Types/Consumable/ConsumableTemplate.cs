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
		/// Determines if the specified character can consume the given item.
		/// Checks for valid character, item, stackable status, sufficient charges, and cooldown.
		/// </summary>
		/// <param name="character">The player character attempting to consume.</param>
		/// <param name="item">The item to be consumed.</param>
		/// <returns>True if the item can be consumed, false otherwise.</returns>
		public bool CanConsume(IPlayerCharacter character, Item item)
		{
			return character != null &&
				   item != null &&
				   item.IsStackable &&
				   item.Stackable.Amount > 1 &&
				   character.TryGet(out ICooldownController cooldownController) &&
				   !cooldownController.IsOnCooldown(ID);
		}

		/// <summary>
		/// Attempts to consume the item, applying cooldown and reducing charges as needed.
		/// Destroys the item if charges are depleted.
		/// </summary>
		/// <param name="character">The player character consuming the item.</param>
		/// <param name="item">The item to be consumed.</param>
		/// <returns>True if the item was successfully consumed, false otherwise.</returns>
		public virtual bool Invoke(IPlayerCharacter character, Item item)
		{
			if (CanConsume(character, item) &&
				character.TryGet(out ICooldownController cooldownController))
			{
				if (Cooldown > 0.0f)
				{
					cooldownController.AddCooldown(ID, new CooldownInstance(Cooldown));
				}
				if (item.IsStackable && item.Stackable.Amount > ChargeCost)
				{
					// Consume charges from the item.
					item.Stackable.Remove(ChargeCost);

					if (item.Stackable.Amount < 1)
					{
						item.Destroy();
					}
				}
				else
				{
					item.Destroy();
				}
				return true;
			}
			return false;
		}
	}
}