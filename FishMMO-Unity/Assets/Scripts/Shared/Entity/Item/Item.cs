using System;
using System.Text;

namespace FishMMO.Shared
{
	public class Item
	{
		public ItemGenerator Generator;
		public ItemEquippable Equippable;
		public ItemStackable Stackable;

		public event Action OnDestroy;
		public BaseItemTemplate Template { get; private set; }
		public ulong InstanceID { get; private set; }
		public int Slot { get; set; }
		public bool IsGenerated { get { return Generator != null; } }
		public bool IsEquippable { get { return Equippable != null; } }
		public bool IsStackable { get { return Stackable != null; } }

		public Item(ulong instanceID, int templateID)
		{
			InstanceID = instanceID;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize();
		}
		public Item(ulong instanceID, int templateID, uint amount)
		{
			InstanceID = instanceID;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize(amount);
		}
		public Item(ulong instanceID, int templateID, uint amount, int seed)
		{
			InstanceID = instanceID;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

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
				this.Stackable = new ItemStackable();
				this.Stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
			}
			if (generate)
			{
				Generator = new ItemGenerator();
			}
			if (Template as EquippableItemTemplate != null)
			{
				Equippable = new ItemEquippable();
				Equippable.Initialize(this);
			}
			Generator?.Initialize(this, seed);
		}

		public void Destroy()
		{
			if (Generator != null)
			{
				Generator.Destroy();
			}
			if (Equippable != null)
			{
				Equippable.Destroy();
			}
			/*if (Stackable != null)
			{
				Stackable.OnDestroy();
			}*/
			OnDestroy?.Invoke();
		}

		public bool IsMatch(Item other)
		{
			return Template.ID == other.Template.ID &&
					(IsGenerated && other.IsGenerated && Generator.Seed == other.Generator.Seed ||
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
			sb.Append(InstanceID);
			sb.Append("</color>");

			Generator?.Tooltip(sb);
			return sb.ToString();
		}
	}
}