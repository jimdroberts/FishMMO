using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// How far the panels above the hotkey bar move up to clear its extra stacked rows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bottom-centre block is a stack of separate UIDocuments — hotkey bar, resource bars,
	/// buff and debuff strips, cast bar, with the chat log's lower edge beside them — and separate
	/// documents cannot be laid out relative to each other, so every relationship between them is
	/// a fixed offset written in each panel's stylesheet against the first bar's measured top edge
	/// (see <c>UIResourceBar.uss</c>). Those offsets are right for one row. Each further row a
	/// stacked hotbar draws is one <see cref="HotbarRowPitch"/> taller, so every panel in the block
	/// adds that much to its own offset, and the chat log gives the same ground from its height so
	/// that its top edge — which the toast stack and the party frame are measured against — stays
	/// where it always was.
	/// </para>
	/// <para>
	/// <b>A margin, not the offset itself.</b> The inset is applied as an inline
	/// <c>margin-bottom</c> on the element the stylesheet anchors with <c>bottom</c>. For an
	/// absolutely positioned element Yoga adds the margin to the offset, so the stylesheet's own
	/// number keeps its meaning and nothing here has to know it. And a panel the player has dragged
	/// is anchored by an inline <c>top</c> with <c>bottom: auto</c>, for which a bottom margin moves
	/// nothing — a placed panel stays exactly where it was put, with no test for it here.
	/// </para>
	/// <para>
	/// Derived from settings rather than published by the hotkey bar, so no panel depends on which
	/// document happened to start first: each reads the inset when it starts and again on
	/// <see cref="ClientHotbarSettings.OnChanged"/>.
	/// </para>
	/// </remarks>
	public static class UITKHudLayout
	{
		/// <summary>
		/// Vertical distance from one stacked hotbar row to the next, in panel units.
		/// </summary>
		/// <remarks>
		/// A 48-unit slot plus the 3-unit gap between rows: <c>.hotkey-slot</c> and
		/// <c>.hotkey-row</c> in <c>UIHotkeyBar.uss</c>. Change either there and this with it.
		/// </remarks>
		public const float HotbarRowPitch = 51.0f;

		/// <summary>
		/// The chat log's authored height, from <c>.chat-panel</c> in <c>UIChat.uss</c>.
		/// </summary>
		/// <remarks>
		/// Needed because the log gives ground from its height rather than moving: a panel that moved
		/// up by the inset would carry its top edge into the toast stack above it.
		/// </remarks>
		public const float ChatAuthoredHeight = 222.0f;

		/// <summary>
		/// The shortest the chat log is allowed to become: its tab strip, its input and two lines.
		/// </summary>
		/// <remarks>
		/// A floor rather than a promise. A game that stacks five or more bars has asked for more of
		/// the screen's lower third than the log can give up; the log then keeps this much and sits
		/// behind the top rows, which is a layout that game has to design for itself.
		/// </remarks>
		public const float ChatMinimumHeight = 80.0f;

		/// <summary>
		/// How far the bottom-centre block moves up for the hotbar rows beyond the first.
		/// </summary>
		public static float HotbarStackInset =>
			Mathf.Max(0, ClientHotbarSettings.VisibleRows - 1) * HotbarRowPitch;

		/// <summary>
		/// Lifts a bottom-anchored element clear of the hotbar's extra rows.
		/// </summary>
		/// <param name="anchored">The element whose stylesheet positions it with <c>bottom</c>.</param>
		public static void ApplyStackInset(VisualElement anchored)
		{
			ApplyStackInset(anchored, HotbarStackInset);
		}

		/// <summary>
		/// <see cref="ApplyStackInset(VisualElement)"/> for an explicit inset, so the geometry of
		/// several stacked rows can be measured in a game that ships one.
		/// </summary>
		/// <param name="anchored">The element whose stylesheet positions it with <c>bottom</c>.</param>
		/// <param name="inset">How far to lift it, in panel units.</param>
		public static void ApplyStackInset(VisualElement anchored, float inset)
		{
			if (anchored == null)
			{
				return;
			}

			/* Cleared rather than written as zero at one row, so a single-bar game carries no inline
			 * style at all and every stylesheet stays the only thing positioning its panel. */
			anchored.style.marginBottom = inset > 0.0f ? new StyleLength(inset) : new StyleLength(StyleKeyword.Null);
		}

		/// <summary>
		/// Lifts the chat log's lower edge clear of the hotbar's extra rows, keeping its top edge.
		/// </summary>
		/// <param name="chatPanel">The <c>.chat-panel</c> element.</param>
		/// <param name="placed">
		/// True when the player has dragged the log somewhere. It then keeps its authored height:
		/// the margin already moves nothing for a top-anchored panel, and a log the player put
		/// elsewhere has no reason to shrink for a bar it no longer sits beside.
		/// </param>
		public static void ApplyChatInset(VisualElement chatPanel, bool placed)
		{
			ApplyChatInset(chatPanel, placed, HotbarStackInset);
		}

		/// <summary>
		/// <see cref="ApplyChatInset(VisualElement, bool)"/> for an explicit inset.
		/// </summary>
		public static void ApplyChatInset(VisualElement chatPanel, bool placed, float stackInset)
		{
			if (chatPanel == null)
			{
				return;
			}

			ApplyStackInset(chatPanel, stackInset);

			float inset = placed ? 0.0f : stackInset;
			chatPanel.style.height = inset > 0.0f
				? new StyleLength(Mathf.Max(ChatMinimumHeight, ChatAuthoredHeight - inset))
				: new StyleLength(StyleKeyword.Null);
		}
	}
}
