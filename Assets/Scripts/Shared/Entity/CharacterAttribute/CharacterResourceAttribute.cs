public class CharacterResourceAttribute : CharacterAttribute
{
	private int currentValue;

	public int CurrentValue { get { return currentValue; } }

	public override string ToString()
	{
		return Template.Name + ": " + currentValue + "/" + FinalValue;
	}

	public CharacterResourceAttribute(int templateID, int initialValue, int currentValue, int modifier) : base(templateID, initialValue, modifier)
	{
		this.currentValue = currentValue;
	}

	public void AddToCurrentValue(int value)
	{
		int tmp = currentValue;
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

	public void SetCurrentValue(int value)
	{
		currentValue = value;
		Internal_OnAttributeChanged(this);
	}

	public void Consume(int amount)
	{
		currentValue -= amount;
		if (currentValue < 0)
		{
			currentValue = 0;
		}
		Internal_OnAttributeChanged(this);
	}

	public void Gain(int amount)
	{
		currentValue += amount;
		if (currentValue >= FinalValue)
		{
			currentValue = FinalValue;
		}
		Internal_OnAttributeChanged(this);
	}

	protected override void Internal_OnAttributeChanged(CharacterAttribute attribute)
	{
		base.Internal_OnAttributeChanged(attribute);
	}
}