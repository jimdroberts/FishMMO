using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class BaseAction : CachedScriptableObject<BaseAction>, ICachedObject, IAction, ITooltip
	{
		[SerializeField]
		private Sprite icon;

		public string Name { get { return this.name; } }
		public Sprite Icon { get { return this.icon; } }

		public abstract string GetFormattedDescription();
		public abstract string Tooltip();
		public abstract string Tooltip(List<ITooltip> combineList);

		public abstract void Execute(ICharacter initiator, EventData eventData);
	}
}