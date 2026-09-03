using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// How the world-anchored labels — nameplates, guild lines, damage and healing numbers — are
	/// drawn: how large, how far out, how strongly, how many at once, and whether walls hide them.
	/// Also which nameplates are up without being targeted: NPCs and other players inside their
	/// own ranges, and the player's own.
	/// </summary>
	/// <remarks>
	/// <para><b>These are the layer's own fields, made editable.</b>
	/// <see cref="UITKWorldLabelLayer"/> already carried every one of them as a serialized field
	/// tuned in ClientPreboot.unity. Each default below is the value authored there, so a fresh
	/// install draws exactly what it drew before this existed and only a player who moves a
	/// control changes anything.</para>
	///
	/// <para><b>Two of them are performance controls, not taste.</b> The draw distance and the
	/// visible-label cap bound the cost of the client's hottest UI loop — one projection, one
	/// style diff and one sort entry per label per frame. A machine that struggles in a crowded
	/// hub gets a real frame back by dropping either, which is the same reason the minimap's
	/// refresh rate is exposed.</para>
	///
	/// <para><b>Scale multiplies rather than replaces.</b> A label's font size is computed from
	/// its world-unit size and its distance from the camera, so a fixed point size would throw
	/// away the perspective behaviour that makes a distant nameplate small. The multiplier scales
	/// the clamp bounds along with the computed size, or a scale above one would be swallowed by
	/// the upper clamp the moment the player walked close enough to hit it.</para>
	/// </remarks>
	public static class ClientWorldLabelSettings
	{
		/// <summary>Label size multiplier a fresh install uses.</summary>
		public const float DefaultScale = 1.0f;

		/// <summary>Smallest label size multiplier offered.</summary>
		public const float MinimumScale = 0.5f;

		/// <summary>Largest label size multiplier offered.</summary>
		public const float MaximumScale = 2.0f;

		/// <summary>Draw distance a fresh install uses, in metres.</summary>
		/// <remarks>Eighty, matching <c>UITKWorldLabelLayer.MaxVisibleDistance</c> as authored.</remarks>
		public const float DefaultDistance = 80.0f;

		/// <summary>
		/// Shortest draw distance offered, in metres.
		/// </summary>
		/// <remarks>
		/// Ten rather than zero. Zero is the layer's "no limit" sentinel, so a slider that reached
		/// it would turn the cheapest setting into the most expensive one at the exact end of the
		/// travel a player drags to when they want less.
		/// </remarks>
		public const float MinimumDistance = 10.0f;

		/// <summary>Longest draw distance offered, in metres.</summary>
		public const float MaximumDistance = 200.0f;

		/// <summary>Label opacity a fresh install uses.</summary>
		public const float DefaultOpacity = 1.0f;

		/// <summary>Faintest labels offered. Above zero; invisible labels are what the toggles are for.</summary>
		public const float MinimumOpacity = 0.2f;

		/// <summary>Strongest labels offered.</summary>
		public const float MaximumOpacity = 1.0f;

		/// <summary>How many labels a fresh install draws at once.</summary>
		/// <remarks>Sixty-four, matching <c>UITKWorldLabelLayer.MaxVisibleLabels</c> as authored.</remarks>
		public const int DefaultMaxVisible = 64;

		/// <summary>
		/// Fewest labels offered.
		/// </summary>
		/// <remarks>
		/// Sixteen. Below that a player standing in a group loses their own target's nameplate to
		/// the budget, which reads as the game failing rather than as a setting doing its job.
		/// </remarks>
		public const int MinimumMaxVisible = 16;

		/// <summary>Most labels offered.</summary>
		public const int MaximumMaxVisible = 256;

		/// <summary>Whether a fresh install hides labels behind scene geometry.</summary>
		/// <remarks>
		/// Off, as authored. It costs a physics linecast per visible label per frame, so it is a
		/// setting a player opts into for the look of it, not one that should be paid for by
		/// default.
		/// </remarks>
		public const bool DefaultOcclude = false;

		/// <summary>
		/// How close an NPC must be, in metres, for its nameplate to show without being targeted
		/// on a fresh install.
		/// </summary>
		/// <remarks>
		/// Thirty: conversational distance in a town, and well inside the eighty-metre draw
		/// distance, so a nameplate that this rule turns on is never immediately culled by the
		/// layer's distance cutoff and drawn as nothing.
		/// </remarks>
		public const float DefaultNpcNameRange = 30.0f;

		/// <summary>
		/// Shortest NPC nameplate range offered, in metres.
		/// </summary>
		/// <remarks>
		/// Zero is a real setting here, not a sentinel: it means NPC nameplates show only on the
		/// current target, which is exactly what the client did before the range existed.
		/// </remarks>
		public const float MinimumNpcNameRange = 0.0f;

		/// <summary>Longest NPC nameplate range offered, in metres. Matches the draw distance ceiling.</summary>
		public const float MaximumNpcNameRange = MaximumDistance;

		/// <summary>
		/// How close another player must be, in metres, for their nameplate to show without being
		/// targeted on a fresh install.
		/// </summary>
		/// <remarks>
		/// The same thirty as NPCs. Other players' names used to be target-only, but only because
		/// the player prefabs never referenced their labels — nothing chose that behaviour, and a
		/// crowd of anonymous players in a hub reads as a bug rather than as a default.
		/// </remarks>
		public const float DefaultPlayerNameRange = 30.0f;

		/// <summary>Shortest player nameplate range offered, in metres. Zero means target only.</summary>
		public const float MinimumPlayerNameRange = 0.0f;

		/// <summary>Longest player nameplate range offered, in metres. Matches the draw distance ceiling.</summary>
		public const float MaximumPlayerNameRange = MaximumDistance;

		/// <summary>Whether a fresh install keeps the player's own nameplate up at all times.</summary>
		public const bool DefaultShowOwnName = true;

		/// <summary>
		/// Raised when any world label setting changes.
		/// </summary>
		/// <remarks>
		/// The layer subscribes and re-reads. It is a scene object that comes and goes with the
		/// client scenes, so it reads on enable as well — the event only carries a CHANGE, and a
		/// layer that waited for one would start on the authored values rather than the player's.
		/// <see cref="ClientNameplateDisplay"/> subscribes for the two nameplate rules, which it
		/// applies to characters rather than to the layer.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>The chosen label size multiplier.</summary>
		public static float Scale => ClientSettings.GetFloat(
			ClientSettings.WorldLabelScaleKey, DefaultScale, MinimumScale, MaximumScale);

		/// <summary>The chosen draw distance, in metres.</summary>
		public static float Distance => ClientSettings.GetFloat(
			ClientSettings.WorldLabelDistanceKey, DefaultDistance, MinimumDistance, MaximumDistance);

		/// <summary>The chosen label opacity.</summary>
		public static float Opacity => ClientSettings.GetFloat(
			ClientSettings.WorldLabelOpacityKey, DefaultOpacity, MinimumOpacity, MaximumOpacity);

		/// <summary>The chosen draw budget.</summary>
		public static int MaxVisible => Mathf.Clamp(
			ClientSettings.GetInt(ClientSettings.WorldLabelMaxVisibleKey, DefaultMaxVisible),
			MinimumMaxVisible,
			MaximumMaxVisible);

		/// <summary>Whether labels behind scene geometry are hidden.</summary>
		public static bool Occlude => ClientSettings.GetBool(ClientSettings.WorldLabelOccludeKey, DefaultOcclude);

		/// <summary>How close an NPC must be, in metres, for its nameplate to show untargeted. Zero: target only.</summary>
		public static float NpcNameRange => ClientSettings.GetFloat(
			ClientSettings.WorldLabelNpcNameRangeKey, DefaultNpcNameRange, MinimumNpcNameRange, MaximumNpcNameRange);

		/// <summary>How close another player must be, in metres, for their nameplate to show untargeted. Zero: target only.</summary>
		public static float PlayerNameRange => ClientSettings.GetFloat(
			ClientSettings.WorldLabelPlayerNameRangeKey, DefaultPlayerNameRange, MinimumPlayerNameRange, MaximumPlayerNameRange);

		/// <summary>Whether the player's own nameplate stays up at all times.</summary>
		public static bool ShowOwnName => ClientSettings.GetBool(ClientSettings.WorldLabelShowOwnNameKey, DefaultShowOwnName);

		/// <summary>Writes the label size multiplier and notifies the layer.</summary>
		public static void SetScale(float value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelScaleKey, Clamp(value, DefaultScale, MinimumScale, MaximumScale));
			Raise();
		}

		/// <summary>Writes the draw distance and notifies the layer.</summary>
		public static void SetDistance(float value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelDistanceKey, Clamp(value, DefaultDistance, MinimumDistance, MaximumDistance));
			Raise();
		}

		/// <summary>Writes the label opacity and notifies the layer.</summary>
		public static void SetOpacity(float value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelOpacityKey, Clamp(value, DefaultOpacity, MinimumOpacity, MaximumOpacity));
			Raise();
		}

		/// <summary>Writes the draw budget and notifies the layer.</summary>
		public static void SetMaxVisible(int value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelMaxVisibleKey,
				Mathf.Clamp(value, MinimumMaxVisible, MaximumMaxVisible));
			Raise();
		}

		/// <summary>Writes the occlusion setting and notifies the layer.</summary>
		public static void SetOcclude(bool value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelOccludeKey, value);
			Raise();
		}

		/// <summary>Writes the NPC nameplate range and notifies the nameplate display.</summary>
		public static void SetNpcNameRange(float value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelNpcNameRangeKey,
				Clamp(value, DefaultNpcNameRange, MinimumNpcNameRange, MaximumNpcNameRange));
			Raise();
		}

		/// <summary>Writes the player nameplate range and notifies the nameplate display.</summary>
		public static void SetPlayerNameRange(float value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelPlayerNameRangeKey,
				Clamp(value, DefaultPlayerNameRange, MinimumPlayerNameRange, MaximumPlayerNameRange));
			Raise();
		}

		/// <summary>Writes the own-nameplate setting and notifies the nameplate display.</summary>
		public static void SetShowOwnName(bool value)
		{
			ClientSettings.Set(ClientSettings.WorldLabelShowOwnNameKey, value);
			Raise();
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
				Log.Error("ClientWorldLabelSettings", "A world-label-settings subscriber threw.", ex);
			}
		}
	}
}
