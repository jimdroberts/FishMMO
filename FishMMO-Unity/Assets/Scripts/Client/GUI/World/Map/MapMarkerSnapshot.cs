using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// One marker as the map should draw it this refresh: where it is, what it looks like, and
	/// what the player is allowed to be told about it.
	/// </summary>
	/// <remarks>
	/// <para>A snapshot rather than a reference to the <see cref="MapMarker"/> itself, and that is
	/// the point of the type. Everything the UI draws comes from here, so the UI has no way to
	/// read a position the filter decided to withhold, coarsen or delay — the exact position of a
	/// throttled marker is never copied into one of these in the first place.</para>
	/// <para>A struct because a busy zone produces a few hundred of them several times a second,
	/// and they are filled, sorted and thrown away inside a single method.</para>
	/// </remarks>
	public struct MapMarkerSnapshot
	{
		/// <summary>The marker this came from. May be null for notes and authored landmarks.</summary>
		public MapMarker Source;

		/// <summary>Where to draw it, in world space. Not necessarily where the object is.</summary>
		/// <remarks>
		/// For a marker with <see cref="TracksSource"/> set this is only the position at the moment
		/// the snapshot was collected; the view re-reads the live transform each frame. For a
		/// throttled one it is the coarsened, deliberately stale position, and it is the only
		/// position the map has.
		/// </remarks>
		public Vector3 Position;

		/// <summary>
		/// Whether the view should re-read <see cref="Source"/>'s live transform every frame
		/// instead of drawing at the collected <see cref="Position"/>.
		/// </summary>
		/// <remarks>
		/// <para>Set only for markers the filter resolved <b>exactly</b>. Collecting markers is
		/// expensive enough to do about ten times a second, but a character crosses real distance in
		/// a tenth of a second — so drawing at the collected position makes every dot stutter along
		/// behind its character while the terrain scrolls smoothly underneath it.</para>
		/// <para>Never set for a throttled marker, and that is the point of the flag rather than
		/// having the view always re-read the source: the whole value of the detection tier is that
		/// the exact position is never published, and a view that helpfully refreshed it from the
		/// transform would undo the filter from the far side.</para>
		/// </remarks>
		public bool TracksSource;

		/// <summary>Which way the object is facing, in degrees clockwise from world north.</summary>
		public float FacingDegrees;

		/// <summary>Whether the facing is meaningful, or the marker should be drawn unrotated.</summary>
		public bool HasFacing;

		/// <summary>What the marker represents.</summary>
		public MapMarkerType Type;

		/// <summary>How the local player stands towards it.</summary>
		public MapRelationship Relationship;

		/// <summary>Icon to draw, or null to fall back to the type's themed dot.</summary>
		public Sprite Icon;

		/// <summary>Tint for the icon.</summary>
		public Color Tint;

		/// <summary>Text drawn beside the icon, or null for none.</summary>
		public string Label;

		/// <summary>Text shown when the pointer rests on the marker, or null for none.</summary>
		public string Tooltip;

		/// <summary>Icon size in UI points.</summary>
		public float Size;

		/// <summary>Whether to pin the marker to the frame edge when it is outside the view.</summary>
		public bool ClampToEdge;

		/// <summary>Draw priority. Higher draws on top.</summary>
		public int Priority;

		/// <summary>Identity of the note this marker came from, or zero when it is not a note.</summary>
		public long NoteID;
	}
}
