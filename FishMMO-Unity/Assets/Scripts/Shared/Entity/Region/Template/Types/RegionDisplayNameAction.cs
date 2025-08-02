using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that displays a 2D label with the region's name and custom styling when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Display Name Action", menuName = "FishMMO/Region/Region Display Name", order = 1)]
	public class RegionDisplayNameAction : RegionAction
	{
		/// <summary>
		/// Event invoked to display a 2D label with custom styling and position.
		/// </summary>
		public static event Action<string, FontStyle, Font, int, Color, float, bool, bool, Vector2> OnDisplay2DLabel;

		/// <summary>
		/// The color of the displayed label text.
		/// </summary>
		public Color DisplayColor;

		/// <summary>
		/// The font style of the label text.
		/// </summary>
		public FontStyle Style;

		/// <summary>
		/// The font used for the label text.
		/// </summary>
		public Font Font;

		/// <summary>
		/// The size of the label font.
		/// </summary>
		public int FontSize;

		/// <summary>
		/// The lifetime (in seconds) for which the label is displayed.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// If true, the label color will fade out over its lifetime.
		/// </summary>
		public bool FadeColor;

		/// <summary>
		/// If true, the label will increase its Y position (move upward) over time.
		/// </summary>
		public bool IncreaseY;

		/// <summary>
		/// Invokes the region action, displaying a styled 2D label with the region's name for the owning client.
		/// </summary>
		/// <param name="character">The player character triggering the action.</param>
		/// <param name="region">The region whose name will be displayed.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// Only display the label if:
			// - The region and character are valid
			// - The character is the owner of the network object
			if (region == null ||
				character == null ||
				!character.NetworkObject.IsOwner)
			{
				return;
			}

			// Only display the label if not reconciling (to avoid duplicate or unwanted displays).
			if (!isReconciling)
			{
				// The label is displayed at a fixed position near the top of the screen (20% down from the top).
				OnDisplay2DLabel?.Invoke(region.Name, Style, Font, FontSize, DisplayColor, LifeTime, FadeColor, IncreaseY, new Vector2(0.0f, Screen.height * 0.2f));
			}
		}
	}
}