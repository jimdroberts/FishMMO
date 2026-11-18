using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that displays the region name as a 2D label when a character enters a region.
	/// Client-only: suppressed on server and during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class DisplayRegionNameAction : BaseAction
	{
		/// <summary>
		/// Raised on the client when a region name label should be displayed.
		/// </summary>
		public static event Action<string, FontStyle, Font, int, Color, float, bool, bool, Vector2> OnDisplay2DLabel;

		/// <summary>
		/// The color of the displayed text.
		/// </summary>
		public Color DisplayColor;

		/// <summary>
		/// The font style of the displayed text.
		/// </summary>
		public FontStyle Style;

		/// <summary>
		/// The font used for the displayed text.
		/// </summary>
		public Font Font;

		/// <summary>
		/// The font size of the displayed text.
		/// </summary>
		public int FontSize;

		/// <summary>
		/// How long the label is displayed in seconds.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Whether the label color fades over its lifetime.
		/// </summary>
		public bool FadeColor;

		/// <summary>
		/// Whether the label position increases on the Y-axis over its lifetime.
		/// </summary>
		public bool IncreaseY;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
			if (initiator == null)
			{
				return;
			}

			if (!initiator.NetworkObject.IsOwner)
			{
				return;
			}

			if (eventData == null || !eventData.TryGet(out RegionEventData regionData))
			{
				return;
			}

			if (regionData.IsReconciling)
			{
				return;
			}

			if (regionData.Region == null)
			{
				return;
			}

			OnDisplay2DLabel?.Invoke(
				regionData.Region.Name,
				Style,
				Font,
				FontSize,
				DisplayColor,
				LifeTime,
				FadeColor,
				IncreaseY,
				new Vector2(0.0f, Screen.height * 0.2f)
			);
#endif
		}
	}
}