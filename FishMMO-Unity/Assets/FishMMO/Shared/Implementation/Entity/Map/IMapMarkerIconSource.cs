using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Supplies a marker's icon when the marker has no authored one of its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For a marker whose icon is not a property of the marker at all — it belongs to a template
	/// the marker's object points at, and that template resolves its artwork asynchronously. A
	/// one-shot copy into <see cref="MapMarker.Icon"/> when the object wakes would copy a null and
	/// never recover, because nothing tells the object when the artwork arrives.
	/// </para>
	/// <para>
	/// So the marker asks instead of being told: <see cref="MapMarker.ResolvedIcon"/> falls back to
	/// this property, which is read fresh on every map refresh. An asynchronous load therefore
	/// appears on the map on the next refresh after it completes, with no polling, no subscription
	/// and no event to leak.
	/// </para>
	/// <para>
	/// Implemented beside the marker rather than on it, so the marker stays what it has always
	/// been — authored data plus a registration — and the source keeps its own lifecycle.
	/// </para>
	/// </remarks>
	public interface IMapMarkerIconSource
	{
		/// <summary>
		/// The icon to draw, or null when there is still nothing to draw.
		/// </summary>
		/// <remarks>
		/// Expected to be cheap and to be called on the marker refresh cadence. Returning null is
		/// normal rather than a failure: the marker then draws in its type's own shape.
		/// </remarks>
		Sprite MapIcon { get; }
	}
}
