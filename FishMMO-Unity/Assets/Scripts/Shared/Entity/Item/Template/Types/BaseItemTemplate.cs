using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class BaseItemTemplate : CachedScriptableObject<BaseItemTemplate>, ITooltip, ICachedObject
	{
		public bool IsIdentifiable;
		public bool Generate;
		public uint MaxStackSize = 1;
		public int Price;
		// Use this for item generation.
		public int[] IconPools;
		public Sprite icon;
		public Mesh Mesh;
		[Tooltip("The base attributes that are added to the item after the ItemGenerator has completed.")]
		public List<ItemAttributeTemplate> Attributes;

		public string Name { get { return this.name; } }
		public bool IsStackable { get { return MaxStackSize > 1; } }
		public Sprite Icon { get { return this.icon; } }

		public virtual string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, false, "f5ad6e", "120%"));
				sb.Append("\r\n______________________________\r\n");
				if (Price > 0)
				{
					sb.Append(RichText.Format("Price", Price, true, "a66ef5FF"));
				}
				return sb.ToString();
			}
		}

		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return Tooltip();
		}

		public virtual string GetFormattedDescription()
		{
			return "";
		}
	}
}