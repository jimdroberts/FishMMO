using System;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public class Item
	{
		public ItemGenerator Generator;
		public ItemEquippable Equippable;
		public ItemStackable Stackable;

		public event Action OnDestroy;
		public BaseItemTemplate Template { get; private set; }
		public long ID { get; set; }
		public int Slot { get; set; }
		public bool IsGenerated { get { return Generator != null; } }
		public bool IsEquippable { get { return Equippable != null; } }
		public bool IsStackable { get { return Stackable != null; } }

		public Item(BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;
			if (Template.MaxStackSize > 1)
			{
				this.Stackable = new ItemStackable();
				this.Stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
			}
		}
		public Item(long id, int seed, BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;
			if (Template.MaxStackSize > 1)
			{
				this.Stackable = new ItemStackable();
				this.Stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
			}
			Initialize(id, seed);
		}
		public Item(long id, int seed, int templateID, uint amount)
		{
			Slot = -1;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);
			if (Template.MaxStackSize > 1)
			{
				this.Stackable = new ItemStackable();
				this.Stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
			}
			Initialize(id, seed);
		}

		public void Initialize(long id, int seed)
		{
			ID = id;

			if (Template as EquippableItemTemplate != null)
			{
				Equippable = new ItemEquippable();
				Equippable.Initialize(this);
			}

			if (Template.Generate)
			{
				Generator = new ItemGenerator();
				if (seed == 0)
				{
					var longBytes = BitConverter.GetBytes(ID);

					// Get integers from the first and the last 4 bytes of long
					int[] ints = new int[] {
						BitConverter.ToInt32(longBytes, 0),
						BitConverter.ToInt32(longBytes, 4)
					};
					if (ints != null && ints.Length > 1)
					{
						// we can use the ID of the item as a unique seed value instead of generating a seed value per item
						seed = ints[1] > 0 ? ints[1] : ints[0];
					}
				}
				Generator?.Initialize(this, seed);
			}
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
			string tooltip = "";
			var sb = ZString.CreateStringBuilder();
			try
			{
				sb.Append("<color=#a66ef5>ID: ");
				sb.Append(ID);
				sb.AppendLine();
				sb.Append("Slot: ");
				sb.Append(Slot);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append(Template.Tooltip());
				sb.AppendLine();
				Generator?.Tooltip(ref sb);
				tooltip = sb.ToString();
			}
			finally
			{
				sb.Dispose();
			}
			return tooltip;
		}
	}
}