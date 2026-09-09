using Cysharp.Text;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a tooltip row means, so a renderer can lay it out rather than guess from its text.
	/// </summary>
	public enum TooltipRowKind : byte
	{
		/// <summary>The subject's name. One per tooltip, first.</summary>
		Title = 0,
		/// <summary>A short qualifier under the title: the kind of thing this is.</summary>
		Subtitle,
		/// <summary>Flowing prose. Wraps.</summary>
		Body,
		/// <summary>Names the group of rows that follows.</summary>
		Header,
		/// <summary>A label and a value, laid out as two columns.</summary>
		Stat,
		/// <summary>Something the character must have or be.</summary>
		Requirement,
		/// <summary>Something the subject does.</summary>
		Effect,
		/// <summary>Guidance addressed to the player, not a fact about the subject.</summary>
		Hint,
		/// <summary>A horizontal rule.</summary>
		Separator,
	}

	/// <summary>
	/// How a row should read: neutral fact, an improvement, a penalty, or background detail.
	/// </summary>
	/// <remarks>
	/// Deliberately not a colour. The renderer owns the palette — the same content is drawn into a
	/// hover tooltip, an inline details pane and a crafting preview, and those do not agree on what
	/// "good" looks like. A tone survives that; a hex string baked into a sentence does not, which
	/// is exactly what the string-concatenating tooltip system this replaces produced.
	/// </remarks>
	public enum TooltipTone : byte
	{
		/// <summary>An ordinary fact.</summary>
		Neutral = 0,
		/// <summary>Better for the player.</summary>
		Good,
		/// <summary>Worse for the player.</summary>
		Bad,
		/// <summary>Secondary detail.</summary>
		Muted,
		/// <summary>Something the player cannot currently satisfy.</summary>
		Unmet,
		/// <summary>Worth noticing, without being good or bad.</summary>
		Accent,
	}

	/// <summary>
	/// Character styling a row asks for, on top of its tone.
	/// </summary>
	/// <remarks>
	/// UI Toolkit's <c>Label</c> renders rich text, so all of these have a real representation in
	/// both output paths: as USS on a rendered row, and as tags in the flattened string.
	/// </remarks>
	[Flags]
	public enum TooltipStyle : byte
	{
		/// <summary>No extra styling.</summary>
		None = 0,
		/// <summary>Bold.</summary>
		Bold = 1 << 0,
		/// <summary>Italic.</summary>
		Italic = 1 << 1,
		/// <summary>Underlined.</summary>
		Underline = 1 << 2,
		/// <summary>Struck through — used for a value something else has replaced.</summary>
		Strikethrough = 1 << 3,
		/// <summary>Larger than the surrounding text.</summary>
		Large = 1 << 4,
		/// <summary>Smaller than the surrounding text.</summary>
		Small = 1 << 5,
	}

	/// <summary>
	/// One row of tooltip content, kept as data rather than as formatted text.
	/// </summary>
	public struct TooltipRow : IComparable<TooltipRow>
	{
		/// <summary>What the row means.</summary>
		public TooltipRowKind Kind;

		/// <summary>The row's text, or the left column of a <see cref="TooltipRowKind.Stat"/>.</summary>
		public string Label;

		/// <summary>The right column of a stat row. Null for every other kind.</summary>
		public string Value;

		/// <summary>
		/// The change this row's value represents, already formatted with its sign (e.g. "+1.2s").
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="Value"/> because a preview draws it as its own chip beside the
		/// value, in a colour taken from <see cref="DeltaTone"/>. Folding it into the value string
		/// is what made the old crafting preview unreadable: "Cooldown: 3.7s (+1.2s)" in one
		/// colour tells the player nothing at a glance.
		/// </remarks>
		public string Delta;

		/// <summary>How the delta reads: an improvement, a penalty, or nothing in particular.</summary>
		public TooltipTone DeltaTone;

		/// <summary>How the row itself reads.</summary>
		public TooltipTone Tone;

		/// <summary>Character styling on top of the tone.</summary>
		public TooltipStyle Style;

		/// <summary>
		/// An explicit colour for this row, overriding the one its tone resolves to.
		/// </summary>
		/// <remarks>
		/// For content that owns its own colour as data — an item's rarity, a faction's standing,
		/// a resource attribute's bar colour. Everything else should use a tone and let the
		/// renderer decide.
		/// </remarks>
		public Color? Color;

		/// <summary>A background highlight behind the row's text, when it has one.</summary>
		public Color? Highlight;

		/// <summary>An icon to draw beside the row, when it has one.</summary>
		public Sprite Icon;

		/// <summary>Sort priority. Lower sorts first; ties keep insertion order.</summary>
		public int Priority;

		/// <summary>Insertion index, used to keep the sort stable.</summary>
		internal int Sequence;

		/// <summary>Sorts by priority, then by insertion order.</summary>
		public int CompareTo(TooltipRow other)
		{
			int byPriority = Priority.CompareTo(other.Priority);
			return byPriority != 0 ? byPriority : Sequence.CompareTo(other.Sequence);
		}
	}

	/// <summary>
	/// Standard sort priorities, so independently authored sections interleave predictably.
	/// </summary>
	/// <remarks>
	/// Tooltip content is assembled by several objects that cannot see each other — a template, its
	/// events, the conditions on both, an item's generator. Each adds rows with a priority and the
	/// content sorts once at the end. Naming the bands here is what stops that from being a pile of
	/// magic numbers whose ordering nobody can predict.
	/// </remarks>
	public static class TooltipPriority
	{
		/// <summary>The subject's name.</summary>
		public const int Title = 0;
		/// <summary>What kind of thing it is.</summary>
		public const int Subtitle = 5;
		/// <summary>Flavour and description.</summary>
		public const int Description = 10;
		/// <summary>Identity: ids, slots, ownership.</summary>
		public const int Identity = 20;
		/// <summary>The numbers.</summary>
		public const int Stats = 30;
		/// <summary>Attribute rolls and generated values.</summary>
		public const int Attributes = 40;
		/// <summary>What it costs to use.</summary>
		public const int ResourceCost = 50;
		/// <summary>How it picks what it affects.</summary>
		public const int Targeting = 55;
		/// <summary>What the character must have or be.</summary>
		public const int Requirements = 60;
		/// <summary>What it does.</summary>
		public const int Effects = 70;
		/// <summary>The parts a crafted subject is made of.</summary>
		public const int Composition = 75;
		/// <summary>What it costs to buy or craft.</summary>
		public const int Price = 80;
		/// <summary>Classification that belongs at the bottom.</summary>
		public const int Footer = 90;
		/// <summary>Guidance addressed to the player.</summary>
		public const int Hint = 900;
	}

	/// <summary>
	/// A tooltip as structured data: an icon, a title, and typed rows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tooltips used to exist only as one rich-text string, assembled by every template type in its
	/// own <c>BuildTooltip</c> and handed to a single <c>Label</c>. That made the content
	/// impossible to work with anywhere else: the crafting panel could not lay stats out in
	/// columns, could not show what an added effect changed, and could not mark a requirement the
	/// character fails, because by the time it had the tooltip everything was already
	/// <c>&lt;color&gt;</c> tags in a sentence. Two systems produced tooltips this way — items and
	/// abilities — and neither could reuse a line of the other's.
	/// </para>
	/// <para>
	/// The rows carry meaning instead, and every producer in the game builds one of these.
	/// <see cref="ToRichText"/> flattens them for anything that genuinely needs a string, and the
	/// client renders them as real elements.
	/// </para>
	/// </remarks>
	public sealed class TooltipContent
	{
		/// <summary>Rows in insertion order; <see cref="Sort"/> puts them in display order.</summary>
		public readonly List<TooltipRow> Rows = new List<TooltipRow>();

		/// <summary>The subject's icon, when it has one.</summary>
		public Sprite Icon;

		/// <summary>The subject's name, mirrored from the title row for convenience.</summary>
		public string Title;

		/// <summary>An accent colour for the whole tooltip — an item's rarity, an ability's school.</summary>
		public Color? Accent;

		/// <summary>Next insertion sequence, so equal priorities keep their authored order.</summary>
		private int sequence;

		/// <summary>True when nothing has been added.</summary>
		public bool IsEmpty => Rows.Count == 0;

		/// <summary>Adds a row.</summary>
		public TooltipContent Add(TooltipRow row)
		{
			row.Sequence = sequence++;
			Rows.Add(row);
			return this;
		}

		/// <summary>Adds the subject's name.</summary>
		public TooltipContent AddTitle(string text, int priority = TooltipPriority.Title, Color? color = null)
		{
			Title = text;
			if (color.HasValue)
			{
				Accent = color;
			}
			return Add(new TooltipRow
			{
				Kind = TooltipRowKind.Title,
				Label = text,
				Priority = priority,
				Color = color,
				Style = TooltipStyle.Large | TooltipStyle.Bold,
			});
		}

		/// <summary>Adds the short qualifier under the title.</summary>
		public TooltipContent AddSubtitle(string text, int priority = TooltipPriority.Subtitle, TooltipTone tone = TooltipTone.Muted)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Subtitle, Label = text, Priority = priority, Tone = tone });
		}

		/// <summary>Adds a paragraph of prose.</summary>
		public TooltipContent AddBody(string text, int priority = TooltipPriority.Description, TooltipTone tone = TooltipTone.Neutral, TooltipStyle style = TooltipStyle.None)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Body, Label = text, Priority = priority, Tone = tone, Style = style });
		}

		/// <summary>Adds a group header.</summary>
		public TooltipContent AddHeader(string text, int priority)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Header, Label = text, Priority = priority, Style = TooltipStyle.Bold });
		}

		/// <summary>Adds a two-column stat row, optionally carrying what it changed by.</summary>
		public TooltipContent AddStat(string label, string value, int priority = TooltipPriority.Stats, string delta = null, TooltipTone deltaTone = TooltipTone.Neutral, TooltipTone tone = TooltipTone.Neutral, Color? color = null)
		{
			return Add(new TooltipRow
			{
				Kind = TooltipRowKind.Stat,
				Label = label,
				Value = value,
				Delta = delta,
				DeltaTone = deltaTone,
				Tone = tone,
				Color = color,
				Priority = priority,
			});
		}

		/// <summary>Adds a requirement, toned by whether the character meets it.</summary>
		public TooltipContent AddRequirement(string text, int priority = TooltipPriority.Requirements, TooltipTone tone = TooltipTone.Neutral)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Requirement, Label = text, Priority = priority, Tone = tone });
		}

		/// <summary>Adds something the subject does.</summary>
		public TooltipContent AddEffect(string text, int priority = TooltipPriority.Effects, TooltipTone tone = TooltipTone.Neutral)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Effect, Label = text, Priority = priority, Tone = tone });
		}

		/// <summary>Adds guidance addressed to the player.</summary>
		public TooltipContent AddHint(string text, int priority = TooltipPriority.Hint)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Hint, Label = text, Priority = priority, Tone = TooltipTone.Muted, Style = TooltipStyle.Italic });
		}

		/// <summary>Adds a horizontal rule.</summary>
		public TooltipContent AddSeparator(int priority)
		{
			return Add(new TooltipRow { Kind = TooltipRowKind.Separator, Priority = priority });
		}

		/// <summary>Appends every row of <paramref name="other"/>, keeping its priorities.</summary>
		/// <remarks>
		/// How a composite subject is assembled — an item and its generated attributes, an ability
		/// and each event crafted onto it. The rows interleave by priority afterwards, so a
		/// component's stats land in the stats block rather than after everything the parent wrote.
		/// </remarks>
		public TooltipContent Append(TooltipContent other)
		{
			if (other == null)
			{
				return this;
			}

			for (int i = 0; i < other.Rows.Count; ++i)
			{
				Add(other.Rows[i]);
			}
			return this;
		}

		/// <summary>Sorts rows by priority, keeping authored order within a priority.</summary>
		public void Sort()
		{
			Rows.Sort();
		}

		/// <summary>Drops every row.</summary>
		public void Clear()
		{
			Rows.Clear();
			Title = null;
			Icon = null;
			Accent = null;
			sequence = 0;
		}

		/// <summary>
		/// Flattens the rows to a rich-text string, for the places that genuinely need one.
		/// </summary>
		/// <remarks>
		/// The structured renderer is the real output path. This exists for logs, for tests, and
		/// for any caller that has only a plain label to write into.
		/// </remarks>
		public string ToRichText()
		{
			Sort();

			/* Not a `using` declaration: AppendRow takes the builder by ref, and a using
			 * variable cannot be passed that way. Disposed in the finally instead. */
			Utf16ValueStringBuilder sb = ZString.CreateStringBuilder();
			try
			{
				bool first = true;
				for (int i = 0; i < Rows.Count; ++i)
				{
					if (!first)
					{
						sb.AppendLine();
					}
					first = false;
					AppendRow(ref sb, Rows[i]);
				}
				return sb.ToString();
			}
			finally
			{
				sb.Dispose();
			}
		}

		/// <summary>Writes one row as rich text.</summary>
		private static void AppendRow(ref Utf16ValueStringBuilder sb, TooltipRow row)
		{
			if (row.Kind == TooltipRowKind.Separator)
			{
				sb.Append("______________________________");
				return;
			}

			string text = row.Label;
			if (row.Kind == TooltipRowKind.Stat && !string.IsNullOrEmpty(row.Value))
			{
				text = ZString.Concat(row.Label, ": ", row.Value);
			}
			if (!string.IsNullOrEmpty(row.Delta))
			{
				text = ZString.Concat(text, " (", row.Delta, ")");
			}
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			string color = row.Color.HasValue
				? TooltipPalette.ToHex(row.Color.Value)
				: TooltipPalette.ColorFor(row.Kind, row.Tone);

			string size = null;
			if ((row.Style & TooltipStyle.Large) != 0) size = "140%";
			else if ((row.Style & TooltipStyle.Small) != 0) size = "85%";
			else if (row.Kind == TooltipRowKind.Requirement || row.Kind == TooltipRowKind.Effect) size = "120%";

			if (row.Highlight.HasValue)
			{
				sb.Append("<mark=");
				sb.Append(TooltipPalette.ToHex(row.Highlight.Value));
				sb.Append('>');
			}
			if (!string.IsNullOrEmpty(size))
			{
				sb.Append("<size=");
				sb.Append(size);
				sb.Append('>');
			}
			if (!string.IsNullOrEmpty(color))
			{
				sb.Append("<color=");
				sb.Append(color);
				sb.Append('>');
			}
			if ((row.Style & TooltipStyle.Bold) != 0) sb.Append("<b>");
			if ((row.Style & TooltipStyle.Italic) != 0) sb.Append("<i>");
			if ((row.Style & TooltipStyle.Underline) != 0) sb.Append("<u>");
			if ((row.Style & TooltipStyle.Strikethrough) != 0) sb.Append("<s>");

			sb.Append(text);

			if ((row.Style & TooltipStyle.Strikethrough) != 0) sb.Append("</s>");
			if ((row.Style & TooltipStyle.Underline) != 0) sb.Append("</u>");
			if ((row.Style & TooltipStyle.Italic) != 0) sb.Append("</i>");
			if ((row.Style & TooltipStyle.Bold) != 0) sb.Append("</b>");
			if (!string.IsNullOrEmpty(color)) sb.Append("</color>");
			if (!string.IsNullOrEmpty(size)) sb.Append("</size>");
			if (row.Highlight.HasValue) sb.Append("</mark>");
		}
	}
}
