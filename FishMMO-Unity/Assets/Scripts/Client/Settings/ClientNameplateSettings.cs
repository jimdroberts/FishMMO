using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// How overhead nameplates look: how large, how strongly, how solid a background, which rows
	/// are worth reading, and how many may be on screen at once.
	/// </summary>
	/// <remarks>
	/// <para><b>Separate from <see cref="ClientWorldLabelSettings"/> on purpose.</b> The two are
	/// drawn by different layers with separate budgets, and they answer different questions for a
	/// player: damage numbers are feedback you read for a second, and a nameplate is furniture you
	/// look past all day. Wanting one loud and the other faint is an ordinary preference that a
	/// single shared opacity could not express.</para>
	///
	/// <para><b>What is deliberately still shared</b> is the draw distance and the hide-behind-
	/// geometry toggle. Both are statements about projected world UI as a whole rather than about
	/// nameplates, and a second copy of each would let a player set two answers to one question and
	/// then wonder which won.</para>
	///
	/// <para><b>The three rules that decide WHICH nameplates are up</b> — the two ranges and the
	/// own-name toggle — stay in <see cref="ClientWorldLabelSettings"/> with their original
	/// configuration keys. They are read by <see cref="ClientNameplateDisplay"/> rather than by the
	/// layer, and moving their keys would silently reset the choice of every existing install for
	/// nothing but tidiness.</para>
	///
	/// <para><b>Every default here is what the layer draws with no configuration at all</b>, so a
	/// fresh install looks exactly as it did before these controls existed and only a player who
	/// moves something changes anything.</para>
	/// </remarks>
	public static class ClientNameplateSettings
	{
		/// <summary>Nameplate opacity a fresh install uses.</summary>
		public const float DefaultOpacity = 1.0f;

		/// <summary>
		/// Faintest nameplates offered.
		/// </summary>
		/// <remarks>
		/// A fifth, not zero: an invisible nameplate is what the two ranges and the own-name toggle
		/// are for, and a slider that reached zero would give a player a second, much less
		/// discoverable way to turn nameplates off — one they cannot see the effect of while
		/// dragging it.
		/// </remarks>
		public const float MinimumOpacity = 0.2f;

		/// <summary>Strongest nameplates offered.</summary>
		public const float MaximumOpacity = 1.0f;

		/// <summary>Nameplate size multiplier a fresh install uses.</summary>
		public const float DefaultScale = 1.0f;

		/// <summary>Smallest nameplate size multiplier offered.</summary>
		public const float MinimumScale = 0.5f;

		/// <summary>Largest nameplate size multiplier offered.</summary>
		public const float MaximumScale = 2.0f;

		/// <summary>Background strength a fresh install uses: the style's own opacity, unmodified.</summary>
		public const float DefaultBackgroundOpacity = 1.0f;

		/// <summary>
		/// Faintest background offered.
		/// </summary>
		/// <remarks>
		/// Zero IS a setting here, unlike the plate opacity above: it means no background at all,
		/// text straight over the world, which is a look people ask for by name. The text stays,
		/// so nothing disappears and the effect of the slider is visible all the way down.
		/// </remarks>
		public const float MinimumBackgroundOpacity = 0.0f;

		/// <summary>Strongest background offered.</summary>
		public const float MaximumBackgroundOpacity = 1.0f;

		/// <summary>How many nameplates a fresh install draws at once.</summary>
		/// <remarks>Sixty-four, matching <c>UITKNameplateLayer.MaxVisiblePlates</c> as authored.</remarks>
		public const int DefaultMaxVisible = 64;

		/// <summary>
		/// Fewest nameplates offered.
		/// </summary>
		/// <remarks>
		/// Eight rather than the world labels' sixteen. A nameplate budget is spent on the nearest
		/// plates first, so a low setting is a legitimate "only the few things around me" choice
		/// rather than the failure a low label budget would be mid-fight.
		/// </remarks>
		public const int MinimumMaxVisible = 8;

		/// <summary>Most nameplates offered.</summary>
		public const int MaximumMaxVisible = 256;

		/// <summary>Whether a fresh install shows the guild row.</summary>
		public const bool DefaultShowGuild = true;

		/// <summary>Whether a fresh install shows the title row.</summary>
		public const bool DefaultShowTitles = true;

		/// <summary>
		/// Raised when any of these change, so the layer can re-read them.
		/// </summary>
		/// <remarks>
		/// Its own event rather than <see cref="ClientWorldLabelSettings.OnChanged"/>: the layer
		/// listens to both — it still takes the draw distance and the occlusion toggle from there —
		/// and two events cost one more subscription while keeping "who owns this setting" a
		/// question with one answer.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>The chosen nameplate opacity.</summary>
		public static float Opacity => ClientSettings.GetFloat(
			ClientSettings.NameplateOpacityKey, DefaultOpacity, MinimumOpacity, MaximumOpacity);

		/// <summary>The chosen nameplate size multiplier.</summary>
		public static float Scale => ClientSettings.GetFloat(
			ClientSettings.NameplateScaleKey, DefaultScale, MinimumScale, MaximumScale);

		/// <summary>The chosen background strength, as a multiplier on each style's own opacity.</summary>
		public static float BackgroundOpacity => ClientSettings.GetFloat(
			ClientSettings.NameplateBackgroundOpacityKey, DefaultBackgroundOpacity,
			MinimumBackgroundOpacity, MaximumBackgroundOpacity);

		/// <summary>The chosen nameplate budget.</summary>
		public static int MaxVisible => Mathf.Clamp(
			ClientSettings.GetInt(ClientSettings.NameplateMaxVisibleKey, DefaultMaxVisible),
			MinimumMaxVisible,
			MaximumMaxVisible);

		/// <summary>Whether the guild row is drawn.</summary>
		public static bool ShowGuild => ClientSettings.GetBool(
			ClientSettings.NameplateShowGuildKey, DefaultShowGuild);

		/// <summary>Whether the title row is drawn.</summary>
		public static bool ShowTitles => ClientSettings.GetBool(
			ClientSettings.NameplateShowTitlesKey, DefaultShowTitles);

		/// <summary>Writes the nameplate opacity and notifies the layer.</summary>
		public static void SetOpacity(float value)
		{
			ClientSettings.Set(ClientSettings.NameplateOpacityKey,
				Clamp(value, DefaultOpacity, MinimumOpacity, MaximumOpacity));
			Raise();
		}

		/// <summary>Writes the nameplate size multiplier and notifies the layer.</summary>
		public static void SetScale(float value)
		{
			ClientSettings.Set(ClientSettings.NameplateScaleKey,
				Clamp(value, DefaultScale, MinimumScale, MaximumScale));
			Raise();
		}

		/// <summary>Writes the background strength and notifies the layer.</summary>
		public static void SetBackgroundOpacity(float value)
		{
			ClientSettings.Set(ClientSettings.NameplateBackgroundOpacityKey,
				Clamp(value, DefaultBackgroundOpacity, MinimumBackgroundOpacity, MaximumBackgroundOpacity));
			Raise();
		}

		/// <summary>Writes the nameplate budget and notifies the layer.</summary>
		public static void SetMaxVisible(int value)
		{
			ClientSettings.Set(ClientSettings.NameplateMaxVisibleKey,
				Mathf.Clamp(value, MinimumMaxVisible, MaximumMaxVisible));
			Raise();
		}

		/// <summary>Writes the guild row setting and notifies the layer.</summary>
		public static void SetShowGuild(bool value)
		{
			ClientSettings.Set(ClientSettings.NameplateShowGuildKey, value);
			Raise();
		}

		/// <summary>Writes the title row setting and notifies the layer.</summary>
		public static void SetShowTitles(bool value)
		{
			ClientSettings.Set(ClientSettings.NameplateShowTitlesKey, value);
			Raise();
		}

		/// <summary>
		/// Whether a row of a given kind is drawn at all.
		/// </summary>
		/// <param name="slot">The row's slot.</param>
		/// <returns>False when the player has turned that kind of row off.</returns>
		/// <remarks>
		/// Answered here rather than in the renderer so "which rows does the player want" has one
		/// home, and so the rule can be read without a panel. Only the two optional rows are
		/// refusable: a nameplate with its name row turned off is not a nameplate, and a status
		/// row is transient enough that hiding it would mostly hide nothing.
		/// </remarks>
		public static bool IsRowVisible(NameplateSlot slot)
		{
			return IsRowVisible(slot, ShowGuild, ShowTitles);
		}

		/// <summary>
		/// Whether a row of a given kind is drawn, against settings the caller has already read.
		/// </summary>
		/// <param name="slot">The row's slot.</param>
		/// <param name="showGuild">Whether guild rows are wanted.</param>
		/// <param name="showTitles">Whether title rows are wanted.</param>
		/// <returns>False when that kind of row is turned off.</returns>
		/// <remarks>
		/// The renderer asks the question for every row of every plate it draws, and reading the
		/// configuration store that often would undo the caching the rest of that loop is built
		/// around. It caches the two answers and calls this; the overload above is the same rule for
		/// anyone who is not in a hot loop, so there is still only one definition of it.
		/// </remarks>
		public static bool IsRowVisible(NameplateSlot slot, bool showGuild, bool showTitles)
		{
			switch (slot)
			{
				case NameplateSlot.GuildName:
					return showGuild;
				case NameplateSlot.InteractableType:
					return showTitles;
				default:
					return true;
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
				Log.Error("ClientNameplateSettings", "A nameplate-settings subscriber threw.", ex);
			}
		}
	}
}
