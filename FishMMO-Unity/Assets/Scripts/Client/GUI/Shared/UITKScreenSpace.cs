using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Screen-to-panel conversion and edge clamping for the cursor-anchored widgets — the
	/// tooltip, the dropdown and the context menu.
	/// </summary>
	/// <remarks>
	/// This exists because the same two mistakes were made independently in each of them.
	/// <para>
	/// The first is the Y axis. The Input System reports the pointer with Y measured from the
	/// bottom of the screen; UI Toolkit lays out from the top. Handing a raw
	/// <c>Mouse.current.position</c> to <see cref="RuntimePanelUtils.ScreenToPanel"/> therefore
	/// mirrors the widget about the horizontal centre of the screen — hover something near the
	/// top and the tooltip appears near the bottom.
	/// </para>
	/// <para>
	/// The second is measurement. An element's <c>resolvedStyle</c> size is NaN until the layout
	/// pass that follows the frame it was added or re-cloned in, so a clamp computed at the
	/// moment of positioning has nothing to clamp against and silently does nothing. The clamp
	/// here reports whether it could be applied, and the caller defers a single re-clamp to the
	/// element's next <see cref="GeometryChangedEvent"/> when it could not.
	/// </para>
	/// <para>
	/// Sizes are compared against the container's <c>contentRect</c> rather than
	/// <c>Screen.width</c>/<c>Screen.height</c>: <c>PanelSettings</c> scales the panel against a
	/// reference resolution, so at any other resolution the two spaces differ and a clamp in
	/// pixels leaves the widget short of, or past, the edge it was meant to sit inside.
	/// </para>
	/// </remarks>
	public static class UITKScreenSpace
	{
		/// <summary>
		/// Reads the pointer position in panel coordinates.
		/// </summary>
		/// <param name="panel">Panel to convert into.</param>
		/// <param name="position">The pointer position in panel points.</param>
		/// <returns>True when a mouse exists and the position could be converted.</returns>
		public static bool TryGetPointerPanelPosition(IPanel panel, out Vector2 position)
		{
			position = Vector2.zero;

			Mouse mouse = Mouse.current;
			if (panel == null || mouse == null)
			{
				return false;
			}

			Vector2 screenPosition = mouse.position.ReadValue();

			// Input System reports Y from the bottom; UI Toolkit lays out from the top.
			position = RuntimePanelUtils.ScreenToPanel(
				panel,
				new Vector2(screenPosition.x, Screen.height - screenPosition.y));
			return true;
		}

		/// <summary>
		/// Positions <paramref name="element"/> at <paramref name="desired"/> and keeps it inside
		/// <paramref name="container"/>, deferring the clamp when the element has not been laid
		/// out yet.
		/// </summary>
		/// <param name="container">Element the position is measured against, normally the panel root.</param>
		/// <param name="element">Absolutely-positioned element to move.</param>
		/// <param name="desired">Preferred top-left corner, in panel points.</param>
		/// <param name="flip">
		/// When true, an element that would overhang an edge is placed on the other side of
		/// <paramref name="desired"/> instead of being slid back along it. That is what a menu or
		/// a tooltip opened near the bottom of the screen wants: sliding it up would put it under
		/// the cursor, which is the one place it must not be.
		/// </param>
		public static void PlaceClamped(VisualElement container, VisualElement element, Vector2 desired, bool flip = false)
		{
			if (container == null || element == null)
			{
				return;
			}

			if (TryClamp(container, element, desired, flip))
			{
				return;
			}

			/* Not measurable yet. Write the unclamped position so the widget is at least at the
			 * cursor for the frame it appears on, and re-run the clamp once the layout that
			 * gives the element a size has happened. RegisterCallbackOnce rather than
			 * RegisterCallback: this runs on the frame a tree is (re-)cloned, and a permanent
			 * handler would re-clamp on every later resize using a stale anchor. */
			element.style.left = desired.x;
			element.style.top = desired.y;
			element.RegisterCallbackOnce<GeometryChangedEvent>((evt) => TryClamp(container, element, desired, flip));
		}

		/// <summary>
		/// Writes a clamped position, if both the element and the container have a resolved size.
		/// </summary>
		/// <returns>False when either size is still unresolved and nothing was written.</returns>
		private static bool TryClamp(VisualElement container, VisualElement element, Vector2 desired, bool flip)
		{
			float width = element.resolvedStyle.width;
			float height = element.resolvedStyle.height;
			Rect content = container.contentRect;

			if (float.IsNaN(width) || float.IsNaN(height) ||
				float.IsNaN(content.width) || float.IsNaN(content.height) ||
				content.width <= 0.0f || content.height <= 0.0f)
			{
				return false;
			}

			float x = desired.x;
			float y = desired.y;

			if (flip)
			{
				if (x + width > content.width)
				{
					x = desired.x - width;
				}
				if (y + height > content.height)
				{
					y = desired.y - height;
				}
			}

			element.style.left = Mathf.Clamp(x, 0.0f, Mathf.Max(0.0f, content.width - width));
			element.style.top = Mathf.Clamp(y, 0.0f, Mathf.Max(0.0f, content.height - height));
			return true;
		}
	}
}
