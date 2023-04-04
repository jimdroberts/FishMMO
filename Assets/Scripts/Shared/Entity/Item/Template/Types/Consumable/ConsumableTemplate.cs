public abstract class ConsumableTemplate : BaseItemTemplate
{
	public ConsumableType ConsumableType;
	public float Cooldown;

	public bool CanConsume(Character character, Item item)
	{
		return character != null &&
			   item != null &&
			   item.stackSize > 1 &&
			   !character.CooldownController.IsOnCooldown(ConsumableType.ToString());
	}

	public virtual void OnConsume(Character character, Item item)
	{
		if (CanConsume(character, item))
		{
			character.CooldownController.AddCooldown(ConsumableType.ToString(), new CooldownInstance(Cooldown));
			--item.stackSize;
		}
	}
}