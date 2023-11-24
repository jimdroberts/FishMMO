using UnityEngine;

namespace FishMMO.Shared
{
	public interface ITooltip
	{
		Sprite Icon { get; }
		string Name { get; }
		string Tooltip();
	}
}