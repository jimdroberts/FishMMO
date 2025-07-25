using UnityEngine;
using System.Collections.Generic;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public abstract class BaseAction : CachedScriptableObject<BaseAction>, ICachedObject, IAction, ITooltip
	{
		[SerializeField]
		private Sprite icon;
		public string Description;

		public string Name { get { return this.name; } }
		public Sprite Icon { get { return this.icon; } }

		public virtual string Tooltip()
		{
			return PrimaryTooltip(null);
		}

		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		public virtual string GetFormattedDescription()
		{
			return Description;
		}

		private string PrimaryTooltip(List<ITooltip> combineList)
		{
 			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, true, "f5ad6e", "140%"));

				if (!string.IsNullOrWhiteSpace(Description))
				{
					sb.AppendLine();
					sb.Append(RichText.Format(GetFormattedDescription(), true, "a66ef5FF"));
				}
				return sb.ToString();
			}
		}

		public abstract void Execute(ICharacter initiator, EventData eventData);
	}
}