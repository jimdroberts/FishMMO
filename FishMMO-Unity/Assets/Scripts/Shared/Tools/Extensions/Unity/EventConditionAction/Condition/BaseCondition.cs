using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class BaseCondition : CachedScriptableObject<BaseCondition>, ICachedObject, ICondition, ITooltip
	{
		[SerializeField]
		private Sprite icon;

		public string Name { get { return this.name; } }
		public Sprite Icon { get { return this.icon; } }

		public abstract string GetFormattedDescription();
		public abstract string Tooltip();
		public abstract string Tooltip(List<ITooltip> combineList);

		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);
	}
}