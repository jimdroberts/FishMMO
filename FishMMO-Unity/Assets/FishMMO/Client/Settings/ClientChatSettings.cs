using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// How large the text in the chat log is drawn, so that a player who cannot comfortably read
	/// the default can make it larger without moving every other panel on the screen.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this is not the interface scale.</b> The UI tab already offers one, and it is
	/// the wrong control for this. It works by dividing the panel asset's authored reference
	/// resolution, so it resizes every panel and every word in every one of them, and it stops at
	/// 1.5x. A player who can read the rest of the HUD and not the chat log gets nothing from it:
	/// turning it up pushes panels off the screen to fix one list of text, and 1.5x of a 12px line
	/// is 18px whether or not 18px is the size they needed.</para>
	///
	/// <para><b>Points, not a multiplier.</b> The control names the size it sets, so "16 pt" means
	/// sixteen points the way it would in any other application. A multiplier over the default was
	/// the other option and it reads worse: the number a player wants to see is the size they are
	/// choosing, not the ratio between it and a default they are not thinking about.</para>
	///
	/// <para><b>What it deliberately does not cover.</b> The chat tabs and the channel selector
	/// stay at their authored sizes. Both sit in containers of fixed height — the tab strip is 24
	/// units and the selector 20 — so a font that grew inside either would clip rather than
	/// enlarge, and a setting that visibly breaks the panel is worse than one with a boundary.
	/// They are chrome; the message log and the input field are the text a player reads and
	/// writes, and those are what this covers.</para>
	///
	/// <para><b>Why the range stops where it does.</b> The panel is a fixed 222 units tall, a
	/// number chosen against the bands above and below it that <c>UIChat.uss</c> documents at
	/// length. At twenty points the log shows roughly eight lines, and past that it stops being a
	/// log and becomes a window onto one. The ceiling is a property of the panel's height rather
	/// than of legibility, which is why it is here and not at some larger round number.</para>
	/// </remarks>
	public static class ClientChatSettings
	{
		/// <summary>Chat text size a fresh install uses, in points.</summary>
		/// <remarks>
		/// Twelve, matching what <c>UIChat.uss</c> authored before this setting existed, so a
		/// fresh install draws exactly what it drew before.
		/// </remarks>
		public const float DefaultFontSize = 12.0f;

		/// <summary>Smallest chat text size offered, in points.</summary>
		public const float MinimumFontSize = 10.0f;

		/// <summary>Largest chat text size offered, in points.</summary>
		public const float MaximumFontSize = 20.0f;

		/// <summary>
		/// Raised when the chat text size changes.
		/// </summary>
		/// <remarks>
		/// The panel subscribes so that a change reaches the messages already on screen. Without
		/// that it would take effect only on the next row built — and since a log is only ever
		/// appended to, the lines a player is looking at while they drag the slider are exactly
		/// the ones that would not change.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>The chosen chat text size, in points.</summary>
		public static float FontSize => ClientSettings.GetFloat(
			ClientSettings.ChatFontSizeKey, DefaultFontSize, MinimumFontSize, MaximumFontSize);

		/// <summary>Writes the chat text size and notifies the panel.</summary>
		/// <remarks>
		/// Rounded, because the slider is continuous and a size is not. A stored 14.37 would render
		/// identically to 14 and would be a value no control could produce again — so the file
		/// would disagree with the read-out beside the slider that named it.
		/// </remarks>
		public static void SetFontSize(float value)
		{
			ClientSettings.Set(ClientSettings.ChatFontSizeKey,
				Mathf.Round(Clamp(value, DefaultFontSize, MinimumFontSize, MaximumFontSize)));
			Raise();
		}

		// ── Line format ──────────────────────────────────────────────

		/// <summary>How a chat timestamp is written.</summary>
		public enum TimestampFormat
		{
			/// <summary><c>14:05</c></summary>
			Hours24 = 0,
			/// <summary><c>14:05:09</c></summary>
			Hours24Seconds = 1,
			/// <summary><c>2:05 PM</c></summary>
			Hours12 = 2,
			/// <summary><c>2:05:09 PM</c></summary>
			Hours12Seconds = 3,
		}

		/// <summary>Option labels for <see cref="TimestampFormat"/>, indexed by its value.</summary>
		public static readonly string[] TimestampFormatLabels =
		{
			"24-hour (14:05)",
			"24-hour with seconds (14:05:09)",
			"12-hour (2:05 PM)",
			"12-hour with seconds (2:05:09 PM)",
		};

		/// <summary>.NET format strings for <see cref="TimestampFormat"/>, indexed by its value.</summary>
		/// <remarks>Invariant culture is applied when formatting so "PM" is "PM" on every machine.</remarks>
		private static readonly string[] TimestampFormatStrings =
		{
			"HH:mm",
			"HH:mm:ss",
			"h:mm tt",
			"h:mm:ss tt",
		};

		/// <summary>Timestamps are off on a fresh install, so the log reads exactly as it did.</summary>
		public const bool DefaultShowTimestamps = false;

		/// <summary>Channel tags are on on a fresh install, as they always were.</summary>
		public const bool DefaultShowChannelTags = true;

		/// <summary>Grouping is on on a fresh install, as it always was.</summary>
		public const bool DefaultGroupMessages = true;

		/// <summary>
		/// Raised when anything about how a chat line is laid out changes: timestamps, their
		/// format, channel tags or grouping.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="OnChanged"/> because the panel answers it by rebuilding every
		/// row, where a font change only restyles them — and the font slider raises on every step
		/// of a drag.
		/// </remarks>
		public static event Action OnFormatChanged;

		/// <summary>Whether every chat line starts with the local time it arrived.</summary>
		public static bool ShowTimestamps => ClientSettings.GetBool(
			ClientSettings.ChatShowTimestampsKey, DefaultShowTimestamps);

		/// <summary>The chosen timestamp format; an out-of-range stored value reads as the default.</summary>
		public static TimestampFormat Format
		{
			get
			{
				int value = ClientSettings.GetInt(ClientSettings.ChatTimestampFormatKey, (int)TimestampFormat.Hours24);
				return value >= 0 && value < TimestampFormatStrings.Length
					? (TimestampFormat)value
					: TimestampFormat.Hours24;
			}
		}

		/// <summary>Whether a line names its channel, e.g. <c>[Tell]</c>.</summary>
		public static bool ShowChannelTags => ClientSettings.GetBool(
			ClientSettings.ChatShowChannelTagsKey, DefaultShowChannelTags);

		/// <summary>
		/// Whether consecutive lines from the same sender on the same channel drop the repeated
		/// name. Off, every line carries its sender.
		/// </summary>
		public static bool GroupMessages => ClientSettings.GetBool(
			ClientSettings.ChatGroupMessagesKey, DefaultGroupMessages);

		/// <summary>Writes the timestamp toggle and notifies the panel.</summary>
		public static void SetShowTimestamps(bool value)
		{
			ClientSettings.Set(ClientSettings.ChatShowTimestampsKey, value);
			RaiseFormat();
		}

		/// <summary>Writes the timestamp format and notifies the panel.</summary>
		public static void SetFormat(TimestampFormat value)
		{
			int index = (int)value;
			if (index < 0 || index >= TimestampFormatStrings.Length)
			{
				index = (int)TimestampFormat.Hours24;
			}
			ClientSettings.Set(ClientSettings.ChatTimestampFormatKey, index);
			RaiseFormat();
		}

		/// <summary>Writes the channel tag toggle and notifies the panel.</summary>
		public static void SetShowChannelTags(bool value)
		{
			ClientSettings.Set(ClientSettings.ChatShowChannelTagsKey, value);
			RaiseFormat();
		}

		/// <summary>Writes the grouping toggle and notifies the panel.</summary>
		public static void SetGroupMessages(bool value)
		{
			ClientSettings.Set(ClientSettings.ChatGroupMessagesKey, value);
			RaiseFormat();
		}

		/// <summary>Writes a time in the chosen format, e.g. <c>[14:05]</c>.</summary>
		/// <param name="localTime">The local time the message arrived.</param>
		/// <param name="format">The format to write it in.</param>
		public static string FormatTimestamp(DateTime localTime, TimestampFormat format)
		{
			int index = (int)format;
			if (index < 0 || index >= TimestampFormatStrings.Length)
			{
				index = 0;
			}
			return "[" + localTime.ToString(TimestampFormatStrings[index], System.Globalization.CultureInfo.InvariantCulture) + "]";
		}

		/// <summary>Notifies format subscribers, reporting rather than propagating a handler's failure.</summary>
		private static void RaiseFormat()
		{
			try
			{
				OnFormatChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientChatSettings", "A chat-format subscriber threw.", ex);
			}
		}

		/// <summary>Clamps a value, rejecting the non-finite ones a hand-edited file can carry.</summary>
		private static float Clamp(float value, float fallback, float minimum, float maximum)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				value = fallback;
			}
			return Mathf.Clamp(value, minimum, maximum);
		}

		/// <summary>Notifies subscribers, reporting rather than propagating a handler's failure.</summary>
		private static void Raise()
		{
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientChatSettings", "A chat-settings subscriber threw.", ex);
			}
		}
	}
}
