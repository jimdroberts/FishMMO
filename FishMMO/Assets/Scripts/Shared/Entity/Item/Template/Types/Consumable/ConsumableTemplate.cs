public abstract class ConsumableTemplate : BaseItemTemplate
{
	public ConsumableType ConsumableType;
	public uint ChargeCost = 1;
	public float Cooldown;

	public bool CanConsume(Character character, Item item)
	{
		return character != null &&
			   item != null &&
			   item.IsStackable &&
			   item.stackable.amount > 1 &&
			   !character.CooldownController.IsOnCooldown(ConsumableType.ToString());
	}

	public virtual bool Invoke(Character character, Item item)
	{
		if (CanConsume(character, item))
		{
			if (Cooldown > 0.0f)
			{
				character.CooldownController.AddCooldown(ConsumableType.ToString(), new CooldownInstance(Cooldown));
			}
			if (item.IsStackable && item.stackable.amount > ChargeCost)
			{
				//consume charges
				item.stackable.Remove(ChargeCost);

				if (item.stackable.amount < 1)
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