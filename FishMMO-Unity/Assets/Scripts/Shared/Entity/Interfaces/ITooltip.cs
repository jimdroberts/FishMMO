using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface ITooltip
	{
		Sprite Icon { get; }
		string Name { get; }
		string GetFormattedDescription();
		string Tooltip();
		string Tooltip(List<ITooltip> combineList);
	}
}