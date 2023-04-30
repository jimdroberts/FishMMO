using System;
using System.Text;

public class Item
{
	public int templateID;
	public ulong instanceID;

	public ItemGenerator generator;
	public ItemEquippable equippable;
	public ItemStackable stackable;

	public event Action OnDestroy;

	public BaseItemTemplate Template { get { return BaseItemTemplate.Cache[templateID]; } }
	public bool IsGenerated { get { return generator != null; } }
	public bool IsEquippable { get { return equippable != null; } }
	public bool IsStackable { get { return stackable != null; } }

	public Item(ulong instanceID, int templateID)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;

		Initialize();
	}
	public Item(ulong instanceID, int templateID, uint amount)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;

		Initialize(amount);
	}
	public Item(ulong instanceID, int templateID, uint amount, int seed)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;

		Initialize(amount, seed, true);
	}

	private void Initialize()
	{
		Initialize(0, 0, false);
	}
	private void Initialize(uint amount)
	{
		Initialize(amount, 0, false);
	}
	private void Initialize(uint amount, int seed, bool generate)
	{
		if (Template.MaxStackSize > 1)
		{
			this.stackable = new ItemStackable();
			this.stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
		}
		if (generate)
		{
			generator = new ItemGenerator();
		}
		if (Template as EquippableItemTemplate != null)
		{
			equippable = new ItemEquippable();
			equippable.Initialize(this);
		}
		generator?.Initialize(this, seed);
	}

	public void Destroy()
	{
		if (generator != null)
		{
			generator.Destroy();
		}
		if (equippable != null)
		{
			equippable.Destroy();
		}
		/*if (stackable != null)
		{
			stackable.OnDestroy();
		}*/
		OnDestroy?.Invoke();
	}

	public bool IsMatch(Item other)
	{
		return templateID == other.templateID &&
				(IsGenerated && other.IsGenerated && generator.Seed == other.generator.Seed ||
				!IsGenerated && !other.IsGenerated);
	}

	public string Tooltip()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("<size=120%><color=#f5ad6e>");
		sb.Append(Template.Name);
		sb.Append("</color></size>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>InstanceID: ");
		sb.Append(instanceID);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>TemplateID: ");
		sb.Append(templateID);
		sb.Append("</color>");

		generator?.Tooltip(sb);
		return sb.ToString();
	}
}