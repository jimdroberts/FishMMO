using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A shared nameplate look, authored once and referenced by every plate that wants it.
	/// </summary>
	/// <remarks>
	/// The alternative — a style authored inline on every prefab — means a change to how bosses
	/// read is a change to every boss prefab. A plate references an asset instead, so "bosses get a
	/// gold border" is one file. <see cref="Nameplate"/> still allows an inline style for the plate
	/// that is genuinely one of a kind.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Nameplate Style", menuName = "FishMMO/UI/Nameplate Style", order = 1)]
	public sealed class NameplateStyleAsset : ScriptableObject
	{
		[SerializeField]
		[Tooltip("How plates referencing this asset are drawn. Sizes are points at the style's reference font size.")]
		private NameplateStyle style = NameplateStyle.Default;

		/// <summary>The look this asset describes.</summary>
		public NameplateStyle Style => style;

		/// <summary>
		/// Bumped whenever the asset is edited, so a running client can pick the change up.
		/// </summary>
		/// <remarks>
		/// Plates are drawn from a cached copy of the resolved style — resolving it per frame per
		/// plate would mean a struct copy and four colour blends for a value that changes about
		/// once a session. This counter is what tells the renderer the cache is stale, and moving
		/// it on <c>OnValidate</c> is what makes editing the asset in play mode do anything.
		/// </remarks>
		public int Revision { get; private set; }

		/// <summary>Replaces the style at runtime, for tools and tests.</summary>
		/// <param name="value">The new style.</param>
		public void SetStyle(NameplateStyle value)
		{
			style = value;
			++Revision;
		}

		private void OnValidate()
		{
			++Revision;
		}

		private void Reset()
		{
			style = NameplateStyle.Default;
		}
	}

	/// <summary>
	/// Ready-made looks, as a starting point for a developer's own assets.
	/// </summary>
	/// <remarks>
	/// Written as code rather than shipped as assets so they cannot drift from the defaults they
	/// are derived from: each one is <see cref="NameplateStyle.Default"/> with the few fields that
	/// make it what it is, so a change to the default plate carries into all of them.
	/// </remarks>
	public static class NameplateStylePresets
	{
		/// <summary>
		/// A boss: a heavier plate with a border in the standing colour and a larger name.
		/// </summary>
		public static NameplateStyle Boss
		{
			get
			{
				NameplateStyle style = NameplateStyle.Default;
				style.FontSize = 0.32f;
				style.ShowBorder = true;
				style.BorderWidth = 2.0f;
				style.BorderBlend = 0.85f;
				style.BackgroundBlend = 0.4f;
				style.BackgroundOpacity = 0.72f;
				style.PaddingHorizontal = 9.0f;
				style.PaddingVertical = 4.0f;
				style.MinWidth = 90.0f;
				return style;
			}
		}

		/// <summary>
		/// An elite or rare: the default plate with a thin border, so it reads as a step above
		/// the things around it without shouting.
		/// </summary>
		public static NameplateStyle Elite
		{
			get
			{
				NameplateStyle style = NameplateStyle.Default;
				style.ShowBorder = true;
				style.BorderWidth = 1.0f;
				style.BackgroundOpacity = 0.65f;
				return style;
			}
		}

		/// <summary>
		/// A quiet plate for scenery a player can use — a chest, a node, a door: no background,
		/// no standing colour, just legible text over the world.
		/// </summary>
		public static NameplateStyle Plain
		{
			get
			{
				NameplateStyle style = NameplateStyle.Default;
				style.NameTint = NameplateTint.Fixed;
				style.ShowBackground = false;
				style.ShowBorder = false;
				return style;
			}
		}
	}
}
