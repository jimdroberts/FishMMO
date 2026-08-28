using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared
{
	/// <summary>
	/// Everything a scene needs in order to present itself: its player-facing name, the image
	/// shown while it loads, the baked overhead map the world map draws, the world-space rectangle
	/// that map covers, and the labels and landmarks placed on it.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this asset exists.</b> Per-scene presentation used to be spread across a
	/// <c>WorldSceneSettings</c> component (the transition image), the scene name itself (which is
	/// what character creation and the loading screen showed the player — <c>"RestBay"</c>, not
	/// <c>"Rest Bay"</c>), and nowhere at all (the map). One asset per scene gives all of it a
	/// single home that is versioned with the scene and reviewable in a diff.</para>
	///
	/// <para><b>What deliberately stays out.</b> Gameplay data — <c>MaxClients</c>, spawn and
	/// respawn positions, teleporters, boundaries — remains on <see cref="WorldSceneDetails"/>.
	/// Those are read on the server, where none of the contents of this asset mean anything, and
	/// mixing a capacity limit into the same asset as client map art invites a change to one to
	/// be reviewed as a change to the other.</para>
	///
	/// <para><b>Why the map image is an <see cref="AssetReference"/> and the sprites are not.</b>
	/// <see cref="WorldSceneDetails"/> holds a reference to this asset and is read by the scene
	/// server, so anything hard-referenced here is pulled into a dedicated server build. A baked
	/// overhead map is megabytes per scene and would be the largest thing in a build that never
	/// draws a frame; an addressable reference is a GUID string and costs nothing. The transition
	/// image and the landmark icons are small and are hard references for authoring convenience,
	/// matching what <c>WorldSceneSettings</c> already did.</para>
	///
	/// <para><b>Bounds are optional.</b> A definition with an empty <see cref="MapBoundsSize"/>
	/// falls back to the scene's <c>IBoundary</c> and terrain extents at runtime — see
	/// <see cref="MapBoundsResolver"/>. Authoring bounds is for scenes whose playable area is a
	/// deliberate subset of what a boundary happens to cover.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "WorldMapDefinition", menuName = "FishMMO/World Map Definition")]
	public class WorldMapDefinition : ScriptableObject
	{
		/// <summary>
		/// Name of the Unity scene this definition describes. Must match the scene file name,
		/// because that is the key <see cref="WorldSceneDetailsCache.Scenes"/> uses.
		/// </summary>
		[Header("Identity")]
		[Tooltip("Scene file name this definition describes. Must match exactly.")]
		public string SceneName;

		/// <summary>
		/// Player-facing name of the scene. Falls back to <see cref="SceneName"/> when empty.
		/// </summary>
		[Tooltip("Player-facing name shown on the map, the loading screen and character creation.")]
		public string DisplayName;

		/// <summary>
		/// Flavour text shown under the name on the loading screen and the world map header.
		/// </summary>
		[TextArea(2, 4)]
		[Tooltip("Optional flavour text shown on the loading screen and the world map header.")]
		public string Description;

		/// <summary>
		/// The image displayed while this scene loads.
		/// </summary>
		/// <remarks>
		/// Moved here from <c>WorldSceneSettings</c>. It is a per-scene presentation choice made by
		/// the same person choosing the map art, and having it on a component in the scene meant it
		/// could only be found by opening the scene.
		/// </remarks>
		[Tooltip("Image displayed while this scene loads.")]
		public Sprite SceneTransitionImage;

		// ── Map image ───────────────────────────────────────────────

		/// <summary>
		/// The baked overhead map texture, loaded on demand by the client.
		/// </summary>
		[Header("Map Image")]
		[Tooltip("Baked overhead capture of the scene. Produced by FishMMO/World Map/Bake Maps.")]
		public AssetReferenceTexture2D MapImage;

		/// <summary>
		/// Tint multiplied into the map image when it is drawn.
		/// </summary>
		[Tooltip("Tint multiplied into the map image when drawn.")]
		public Color MapTint = Color.white;

		/// <summary>
		/// Colour drawn behind the map, visible where the image has no coverage.
		/// </summary>
		[Tooltip("Colour drawn behind the map image.")]
		public Color MapBackground = new Color(0.03f, 0.05f, 0.07f, 1.0f);

		// ── Bounds ──────────────────────────────────────────────────

		/// <summary>
		/// Centre of the rectangle the map image covers, in world space. Y is ignored.
		/// </summary>
		[Header("Bounds (leave size zero to derive from the scene)")]
		[Tooltip("Centre of the mapped area in world space. Y is ignored.")]
		public Vector3 MapBoundsCenter;

		/// <summary>
		/// Size of the rectangle the map image covers, in world metres. Y is ignored. A zero size
		/// means "derive from the scene's boundary and terrain at bake time".
		/// </summary>
		[Tooltip("Size of the mapped area in world metres. Y is ignored. Zero derives from the scene.")]
		public Vector3 MapBoundsSize;

		/// <summary>
		/// True when <see cref="MapBoundsCenter"/> and <see cref="MapBoundsSize"/> were derived
		/// from the scene rather than authored, and may therefore be re-derived.
		/// </summary>
		/// <remarks>
		/// Cleared by <see cref="OnValidate"/> whenever the values are edited in the inspector, so
		/// touching either field in the editor is what promotes derived bounds to authored ones —
		/// there is no separate box to tick and no way to edit the bounds and have the edit thrown
		/// away by the next rebuild.
		/// </remarks>
		[HideInInspector]
		public bool BoundsAreDerived;

		/// <summary>Centre last written by a derive, used to notice a hand edit.</summary>
		[HideInInspector]
		[SerializeField]
		private Vector3 derivedBoundsCenter;

		/// <summary>Size last written by a derive, used to notice a hand edit.</summary>
		[HideInInspector]
		[SerializeField]
		private Vector3 derivedBoundsSize;

		/// <summary>
		/// Records bounds worked out from the scene, marking them re-derivable.
		/// </summary>
		/// <param name="rect">The derived rectangle, on the XZ plane.</param>
		public void SetDerivedBounds(Rect rect)
		{
			MapBoundsCenter = new Vector3(rect.center.x, 0.0f, rect.center.y);
			MapBoundsSize = new Vector3(rect.width, 0.0f, rect.height);
			derivedBoundsCenter = MapBoundsCenter;
			derivedBoundsSize = MapBoundsSize;
			BoundsAreDerived = true;
		}

		/// <summary>
		/// Rotation of the map relative to world north, in degrees clockwise.
		/// </summary>
		/// <remarks>
		/// Zero means +Z on the map points up. A scene laid out along a diagonal can be rotated so
		/// its coastline runs across the map rather than corner to corner, without moving anything
		/// in the world.
		/// </remarks>
		[Tooltip("Rotation of the map relative to world north, in degrees clockwise. Zero puts +Z up.")]
		public float NorthOffsetDegrees;

		// ── Zoom ────────────────────────────────────────────────────

		/// <summary>Smallest half-extent, in world metres, the minimap may be zoomed in to.</summary>
		[Header("Zoom")]
		[Tooltip("Closest minimap zoom, as the camera's orthographic half-size in metres.")]
		public float MinimapMinimumRange = 12.0f;

		/// <summary>Largest half-extent, in world metres, the minimap may be zoomed out to.</summary>
		/// <remarks>
		/// Clamped hard at runtime rather than merely defaulted. The minimap is a live camera, so
		/// its range is the one map value a modified client could widen for real information; the
		/// renderer re-applies this bound every time it renders.
		/// </remarks>
		[Tooltip("Furthest minimap zoom, as the camera's orthographic half-size in metres.")]
		public float MinimapMaximumRange = 60.0f;

		/// <summary>The half-extent the minimap opens at.</summary>
		[Tooltip("Minimap zoom used before the player has changed it.")]
		public float MinimapDefaultRange = 25.0f;

		// ── Fog of war ──────────────────────────────────────────────

		/// <summary>
		/// Whether unexplored parts of this scene are hidden until the player walks near them.
		/// </summary>
		/// <remarks>
		/// Off for scenes where concealment makes no sense: a one-room shop interior, an arena, a
		/// tutorial area. A scene with fog disabled reads as fully explored everywhere.
		/// </remarks>
		[Header("Fog of War")]
		[Tooltip("Hide unexplored areas until the player walks near them.")]
		public bool FogOfWarEnabled = true;

		/// <summary>
		/// Size of one reveal cell in world metres. Zero uses the client default.
		/// </summary>
		[Tooltip("Size of one fog reveal cell in metres. Zero uses the client default (4m).")]
		public float FogCellSize;

		// ── Authored content ────────────────────────────────────────

		/// <summary>Named areas drawn across the map, baked from <see cref="MapRegionLabel"/>.</summary>
		[Header("Authored Content (baked from the scene)")]
		public List<MapRegionLabelDetails> RegionLabels = new List<MapRegionLabelDetails>();

		/// <summary>Landmarks drawn on the map, baked from <see cref="MapPointOfInterest"/>.</summary>
		public List<MapPointOfInterestDetails> PointsOfInterest = new List<MapPointOfInterestDetails>();

		/// <summary>
		/// The name to show the player for this scene.
		/// </summary>
		public string ResolvedDisplayName =>
			string.IsNullOrWhiteSpace(DisplayName) ? SceneName : DisplayName;

		/// <summary>
		/// Whether usable bounds are present, however they got there.
		/// </summary>
		public bool HasBounds => MapBoundsSize.x > 0.0f && MapBoundsSize.z > 0.0f;

		/// <summary>
		/// Whether the bounds were typed in by a person, as opposed to derived from the scene.
		/// </summary>
		/// <remarks>
		/// The distinction is what stops a derive from becoming permanent. The scene rebuild writes
		/// derived bounds into the same two fields the inspector edits, so without this flag the
		/// very next rebuild would see a non-empty size, conclude somebody had authored it, and
		/// never derive again — freezing every scene's map at whatever its boundaries happened to
		/// be the first time the cache was built.
		/// </remarks>
		public bool HasAuthoredBounds => HasBounds && !BoundsAreDerived;

		/// <summary>
		/// The world-space rectangle the map covers, on the XZ plane.
		/// </summary>
		/// <remarks>
		/// Returns an empty rect when there are no bounds at all, so callers can tell "unknown"
		/// from "genuinely tiny". <see cref="MapBoundsResolver"/> is what fills the gap.
		/// </remarks>
		public Rect MapRect => HasBounds
			? new Rect(MapBoundsCenter.x - (MapBoundsSize.x * 0.5f),
					   MapBoundsCenter.z - (MapBoundsSize.z * 0.5f),
					   MapBoundsSize.x,
					   MapBoundsSize.z)
			: Rect.zero;

		/// <summary>
		/// The region containing a world position, most specific first.
		/// </summary>
		/// <param name="worldPosition">Position to name.</param>
		/// <returns>The containing region, or null when the position is in no named region.</returns>
		/// <remarks>
		/// Smallest containing region wins. Regions are expected to nest — a district inside a city
		/// inside a province — and the player wants the name of the thing they are standing in, not
		/// the name of the continent it is on.
		/// </remarks>
		public MapRegionLabelDetails FindRegion(Vector3 worldPosition)
		{
			MapRegionLabelDetails best = null;
			float bestRadius = float.MaxValue;

			for (int i = 0; i < RegionLabels.Count; ++i)
			{
				MapRegionLabelDetails region = RegionLabels[i];
				if (region == null || !region.Contains(worldPosition))
				{
					continue;
				}
				if (region.Radius < bestRadius)
				{
					best = region;
					bestRadius = region.Radius;
				}
			}

			return best;
		}

		/// <summary>
		/// Clamps the authored zoom range into a usable order on load.
		/// </summary>
		/// <remarks>
		/// A definition whose minimum exceeds its maximum makes every clamp downstream return the
		/// wrong end of the range, and the failure shows up as a minimap stuck at one zoom rather
		/// than as anything that names this asset. Fixed once, here, rather than defended against
		/// at each of the several call sites that read the range.
		/// </remarks>
		private void OnValidate()
		{
			/* Any inspector change to the bounds makes them authored. OnValidate cannot tell which
			 * field changed, so this compares against what was last written by a derive; a derive
			 * records the values it wrote, and anything else means a person edited them. */
			if (BoundsAreDerived &&
				(MapBoundsCenter != derivedBoundsCenter || MapBoundsSize != derivedBoundsSize))
			{
				BoundsAreDerived = false;
			}

			MinimapMinimumRange = Mathf.Max(1.0f, MinimapMinimumRange);
			MinimapMaximumRange = Mathf.Max(MinimapMinimumRange, MinimapMaximumRange);
			MinimapDefaultRange = Mathf.Clamp(MinimapDefaultRange, MinimapMinimumRange, MinimapMaximumRange);
			FogCellSize = Mathf.Max(0.0f, FogCellSize);
		}
	}
}
