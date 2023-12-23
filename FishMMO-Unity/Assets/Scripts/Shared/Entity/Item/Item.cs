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
		public long InstanceID { get; private set; }
		public int Slot { get; set; }
		public bool IsGenerated { get { return Generator != null; } }
		public bool IsEquippable { get { return Equippable != null; } }
		public bool IsStackable { get { return Stackable != null; } }

		public Item(long instanceID, BaseItemTemplate template, uint amount)
		{
			InstanceID = instanceID;
			Template = template;

			Initialize(amount);
		}
		public Item(long instanceID, int templateID, uint amount)
		{
			InstanceID = instanceID;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize(amount);
		}

		private void Initialize(uint amount)
		{
			if (Template.MaxStackSize > 1)
			{
				this.Stackable = new ItemStackable();
				this.Stackable.Initialize(this, amount.Clamp(1, Template.MaxStackSize));
			}
			if (Template.Generate)
			{
				Generator = new ItemGenerator();
			}
			if (Template as EquippableItemTemplate != null)
			{
				Equippable = new ItemEquippable();
				Equippable.Initialize(this);
			}

			var longBytes = BitConverter.GetBytes(InstanceID);

			// Get integers from the first and the last 4 bytes of long
			int[] ints = new int[] {
				BitConverter.ToInt32(longBytes, 0),
				BitConverter.ToInt32(longBytes, 4)
			};
			if (ints != null && ints.Length > 1)
			{
				int seed = ints[1] > 0 ? ints[1] : ints[0];
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
				sb.Append("<color=#a66ef5>InstanceID: ");
				sb.Append(InstanceID);
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