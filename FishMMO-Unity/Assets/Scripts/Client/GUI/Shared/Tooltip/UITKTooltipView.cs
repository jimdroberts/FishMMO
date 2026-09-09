using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws <see cref="TooltipContent"/> as real UI Toolkit elements.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The single output path for every description in the game. A tooltip used to be one
	/// <c>Label</c> holding a rich-text blob, which meant a stat could not be aligned, a change
	/// could not be shown beside the value it changed, and nothing could be reused anywhere but
	/// under the cursor.
	/// </para>
	/// <para>
	/// Rows become elements here, so the same content renders as a hover tooltip, as the details
	/// pane in the abilities panel, and as the crafting preview — the three places that need it —
	/// with the layout coming from USS rather than from tags baked into a string. Rich text is
	/// still honoured inside a row's text: UI Toolkit's <c>Label</c> supports <c>&lt;b&gt;</c>,
	/// <c>&lt;i&gt;</c>, <c>&lt;u&gt;</c>, <c>&lt;color&gt;</c>, <c>&lt;size&gt;</c> and
	/// <c>&lt;mark&gt;</c>, so content that genuinely needs inline formatting can carry it.
	/// </para>
	/// </remarks>
	public static class UITKTooltipView
	{
		/// <summary>USS class on the container holding rendered rows.</summary>
		public const string VIEW_CLASS = "tip";

		/// <summary>Fills <paramref name="container"/> with the rows of <paramref name="content"/>.</summary>
		/// <param name="container">The element to fill. Its children are replaced.</param>
		/// <param name="content">The content to draw. Null or empty clears the container.</param>
		/// <param name="showIcon">Whether to draw the content's icon beside the title.</param>
		public static void Render(VisualElement container, TooltipContent content, bool showIcon = true)
		{
			if (container == null)
			{
				return;
			}

			container.Clear();
			if (content == null || content.IsEmpty)
			{
				return;
			}

			container.AddToClassList(VIEW_CLASS);
			content.Sort();

			/* The icon rides with the title rather than above it, so a tooltip for something with
			 * no icon does not open with an empty square. */
			bool titleDrawn = false;
			List<TooltipRow> rows = content.Rows;

			for (int i = 0; i < rows.Count; ++i)
			{
				TooltipRow row = rows[i];

				if (row.Kind == TooltipRowKind.Title && !titleDrawn)
				{
					titleDrawn = true;
					container.Add(BuildTitle(row, showIcon ? content.Icon : null));
					continue;
				}

				VisualElement element = BuildRow(row);
				if (element != null)
				{
					container.Add(element);
				}
			}
		}

		/// <summary>Builds the title line, with the subject's icon when it has one.</summary>
		private static VisualElement BuildTitle(TooltipRow row, Sprite icon)
		{
			VisualElement header = new VisualElement();
			header.AddToClassList("tip-titlerow");
			header.pickingMode = PickingMode.Ignore;

			if (icon != null)
			{
				VisualElement image = new VisualElement();
				image.AddToClassList("tip-icon");
				image.style.backgroundImage = new StyleBackground(icon);
				image.pickingMode = PickingMode.Ignore;
				header.Add(image);
			}

			Label title = MakeLabel(row.Label, "tip-title", row);
			header.Add(title);
			return header;
		}

		/// <summary>Builds one row, or null for a row with nothing to draw.</summary>
		private static VisualElement BuildRow(TooltipRow row)
		{
			switch (row.Kind)
			{
				case TooltipRowKind.Separator:
					{
						VisualElement rule = new VisualElement();
						rule.AddToClassList("tip-sep");
						rule.pickingMode = PickingMode.Ignore;
						return rule;
					}

				case TooltipRowKind.Stat:
					return BuildStat(row);

				case TooltipRowKind.Header:
					return MakeLabel(row.Label, "tip-header", row);

				case TooltipRowKind.Subtitle:
					return MakeLabel(row.Label, "tip-subtitle", row);

				case TooltipRowKind.Requirement:
					return MakeLabel(row.Label, "tip-requirement", row);

				case TooltipRowKind.Effect:
					return MakeLabel(row.Label, "tip-effect", row);

				case TooltipRowKind.Hint:
					return MakeLabel(row.Label, "tip-hint", row);

				case TooltipRowKind.Title:
					return MakeLabel(row.Label, "tip-title", row);

				default:
					return string.IsNullOrEmpty(row.Label) ? null : MakeLabel(row.Label, "tip-body", row);
			}
		}

		/// <summary>
		/// Builds a stat as three columns: what it is, what it is, and what changed it.
		/// </summary>
		/// <remarks>
		/// The delta is its own element with its own colour. That is the whole reason the tooltip
		/// system became data: a crafting preview has to be able to say "Cooldown 3.7s" in the
		/// normal colour and "+1.2s" in red beside it, and no amount of string concatenation makes
		/// that scannable.
		/// </remarks>
		private static VisualElement BuildStat(TooltipRow row)
		{
			VisualElement statRow = new VisualElement();
			statRow.AddToClassList("tip-stat");
			statRow.pickingMode = PickingMode.Ignore;

			Label label = MakeLabel(row.Label, "tip-stat__label", row, applyTone: false);
			label.style.color = Tint(TooltipPalette.Label);
			statRow.Add(label);

			VisualElement values = new VisualElement();
			values.AddToClassList("tip-stat__values");
			values.pickingMode = PickingMode.Ignore;

			if (!string.IsNullOrEmpty(row.Value))
			{
				values.Add(MakeLabel(row.Value, "tip-stat__value", row));
			}

			if (!string.IsNullOrEmpty(row.Delta))
			{
				Label delta = new Label(row.Delta);
				delta.AddToClassList("tip-stat__delta");
				delta.enableRichText = true;
				delta.pickingMode = PickingMode.Ignore;
				delta.style.color = Tint(TooltipPalette.Resolve(TooltipRowKind.Stat, row.DeltaTone));
				values.Add(delta);
			}

			statRow.Add(values);
			return statRow;
		}

		/// <summary>Builds a label carrying a row's tone, styling and highlight.</summary>
		/// <param name="text">The text to draw.</param>
		/// <param name="ussClass">The USS class that gives it its layout and size.</param>
		/// <param name="row">The row the text came from.</param>
		/// <param name="applyTone">Whether to colour the text from the row's tone.</param>
		private static Label MakeLabel(string text, string ussClass, TooltipRow row, bool applyTone = true)
		{
			Label label = new Label(text);
			label.AddToClassList(ussClass);

			/* Rich text stays on. A row's text may legitimately contain tags — an item name with a
			 * coloured suffix, a condition that highlights the attribute it needs — and the model
			 * carries those through untouched. */
			label.enableRichText = true;
			label.pickingMode = PickingMode.Ignore;

			if (applyTone)
			{
				Color color = row.Color ?? TooltipPalette.Resolve(row.Kind, row.Tone);
				label.style.color = Tint(color);
			}

			if (row.Highlight.HasValue)
			{
				label.style.backgroundColor = new StyleColor(row.Highlight.Value);
			}

			ApplyStyle(label, row.Style);
			return label;
		}

		/// <summary>Applies a row's character styling to a label.</summary>
		private static void ApplyStyle(Label label, TooltipStyle style)
		{
			if (style == TooltipStyle.None)
			{
				return;
			}

			bool bold = (style & TooltipStyle.Bold) != 0;
			bool italic = (style & TooltipStyle.Italic) != 0;
			if (bold && italic)
			{
				label.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
			}
			else if (bold)
			{
				label.style.unityFontStyleAndWeight = FontStyle.Bold;
			}
			else if (italic)
			{
				label.style.unityFontStyleAndWeight = FontStyle.Italic;
			}

			/* Underline and strikethrough have no USS property in UI Toolkit, so they go back
			 * through rich text — which the label has enabled. */
			if ((style & TooltipStyle.Underline) != 0)
			{
				label.text = $"<u>{label.text}</u>";
			}
			if ((style & TooltipStyle.Strikethrough) != 0)
			{
				label.text = $"<s>{label.text}</s>";
			}

			if ((style & TooltipStyle.Large) != 0)
			{
				label.AddToClassList("tip--large");
			}
			else if ((style & TooltipStyle.Small) != 0)
			{
				label.AddToClassList("tip--small");
			}
		}

		/// <summary>A style colour from a palette colour.</summary>
		private static StyleColor Tint(Color color)
		{
			return new StyleColor(color);
		}
	}
}
