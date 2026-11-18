using System.Runtime.CompilerServices;
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
	/// pass that follows the frame it was added in, so a clamp computed at the moment of
	/// positioning has nothing to clamp against and silently does nothing. A widget that is reused
	/// has the opposite problem: panels keep their trees across hide and show, so a menu reopened
	/// with new entries measures at the size its previous entries gave it, and a clamp that trusts
	/// that alone leaves it hanging off the edge. The clamp here is applied at once with whatever
	/// size can be read, and again on the element's next <see cref="GeometryChangedEvent"/>.
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
		/// <paramref name="container"/>, clamping again once the element's layout settles.
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

			if (!clampRequests.TryGetValue(element, out ClampRequest request))
			{
				request = new ClampRequest();
				clampRequests.Add(element, request);
				element.RegisterCallback<GeometryChangedEvent>(OnClampedElementGeometryChanged);
			}
			request.Container = container;
			request.Desired = desired;
			request.Flip = flip;
			request.Armed = true;

			/* Not measurable yet: write the unclamped position so the widget is at least at the
			 * cursor for the frame it appears on. Either way the request stays armed, and the next
			 * layout clamps against the size the element actually settles at. */
			if (!TryClamp(container, element, desired, flip))
			{
				element.style.left = desired.x;
				element.style.top = desired.y;
			}
		}

		/// <summary>The latest placement asked of an element.</summary>
		private sealed class ClampRequest
		{
			public VisualElement Container;
			public Vector2 Desired;
			public bool Flip;
			public bool Armed;
		}

		/// <summary>
		/// One request per placed element, held weakly so a destroyed widget takes its entry with it.
		/// </summary>
		/// <remarks>
		/// One permanent handler per element that reads its latest request, rather than a one-shot
		/// handler per placement: a one-shot handler that never fired, because the size did not
		/// change, would fire on some later resize with the anchor of a placement long replaced.
		/// </remarks>
		private static readonly ConditionalWeakTable<VisualElement, ClampRequest> clampRequests = new ConditionalWeakTable<VisualElement, ClampRequest>();

		/// <summary>Clamps an armed element against its settled size, then disarms it.</summary>
		private static void OnClampedElementGeometryChanged(GeometryChangedEvent evt)
		{
			if (!(evt.currentTarget is VisualElement element) ||
				!clampRequests.TryGetValue(element, out ClampRequest request) ||
				!request.Armed)
			{
				return;
			}

			if (TryClamp(request.Container, element, request.Desired, request.Flip))
			{
				request.Armed = false;
			}
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
