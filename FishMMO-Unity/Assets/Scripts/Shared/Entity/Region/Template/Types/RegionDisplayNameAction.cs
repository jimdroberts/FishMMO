using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Display Name Action", menuName = "Region/Region Display Name", order = 1)]
	public class RegionDisplayNameAction : RegionAction
	{
		public static event Action<string, FontStyle, Font, int, Color, float, bool, bool, Vector2> OnDisplay2DLabel;

		public Color DisplayColor;
		public FontStyle Style;
		public Font Font;
		public int FontSize;
		public float LifeTime;
		public bool FadeColor;
		public bool IncreaseY;

		public override void Invoke(IPlayerCharacter character, Region region)
		{
			if (region == null ||
				character == null || !character.NetworkObject.IsOwner)
			{
				return;
			}

			OnDisplay2DLabel?.Invoke(region.Name, Style, Font, FontSize, DisplayColor, LifeTime, FadeColor, IncreaseY, new Vector2(0.0f, Screen.height * 0.2f));
		}
	}
}