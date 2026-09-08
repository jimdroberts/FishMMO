using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The overhead plate for one thing in the world: a stack of rows, a style, and a point to
	/// hang them over. Rendered by whatever UI layer is present rather than by a renderer of its
	/// own.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this exists instead of a group of <see cref="WorldLabel"/>s.</b> A nameplate
	/// used to be several labels parented to the same overhead anchor, each projected onto the
	/// screen independently and then pushed apart vertically by the renderer. Two labels a few
	/// centimetres apart in the world do not stay a fixed number of pixels apart on screen: as the
	/// camera pitches, that world-space gap projects to a different screen-space gap — and to none
	/// at all when the camera looks straight down the offset — so the correction that separated
	/// them at one camera angle overlapped them at another. Rotating the camera made names collide,
	/// and no amount of tuning the correction could fix it, because the premise (a fixed screen
	/// offset between two separately projected points) is what was wrong.</para>
	///
	/// <para>A plate is ONE anchor. It projects once, and its rows are laid out by the UI's own
	/// vertical layout inside a single element, in points, where a gap authored as two points is
	/// two points at every camera angle in the game. The stack cannot come apart because it is not
	/// assembled from independently positioned pieces.</para>
	///
	/// <para><b>What stays with <see cref="WorldLabel"/>.</b> Damage numbers, healing numbers,
	/// region captions and anything else that is one free-floating piece of text with a lifetime
	/// of its own. Those never wanted to be grouped, and they keep the pooled label path with its
	/// effect timeline.</para>
	///
	/// <para><b>This is data, not a renderer.</b> Like <see cref="WorldLabel"/> it has no visual of
	/// its own, is never instantiated by the server, and is inert in a headless build. It is also
	/// deliberately not client-only code: the rows are written from Shared — a name resolving, a
	/// guild changing, an interactable declaring what it is — and only the drawing is the client's
	/// business.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class Nameplate : MonoBehaviour
	{
		/// <summary>The name given to plates this class builds at runtime.</summary>
		public const string RuntimeObjectName = "Nameplate";

		/// <summary>
		/// Every enabled plate in the scene, in registration order.
		/// </summary>
		/// <remarks>
		/// A static registry rather than a scene sweep, for the same reason
		/// <see cref="WorldLabel.Active"/> is one: the renderer walks it every frame, and plates
		/// come and go with spawning.
		/// </remarks>
		private static readonly List<Nameplate> active = new List<Nameplate>();

		/// <summary>Read-only view of the currently enabled plates, for a renderer to walk.</summary>
		public static IReadOnlyList<Nameplate> Active => active;

		/// <summary>Raised when a plate is enabled, so a renderer can build backing UI for it.</summary>
		public static event Action<Nameplate> OnNameplateEnabled;

		/// <summary>Raised when a plate is disabled or destroyed, so a renderer can release it.</summary>
		public static event Action<Nameplate> OnNameplateDisabled;

		[SerializeField]
		[Tooltip("World-space offset from this transform. The plate's bottom edge sits at this point.")]
		private Vector3 worldOffset = Vector3.zero;

		[SerializeField]
		[Tooltip("Whether the plate is drawn. Authored off: the client decides who is worth a nameplate.")]
		private bool visible = false;

		[SerializeField]
		[Tooltip("Draw-order bias. A higher plate paints over a nearer one and survives the visible-plate budget longer.")]
		private int priority = 0;

		[SerializeField]
		[Tooltip("Optional shared style. Ignored when Use Custom Style is set; the default plate is used when both are empty.")]
		private NameplateStyleAsset styleAsset;

		[SerializeField]
		[Tooltip("Author a style inline on this prefab instead of referencing a shared asset.")]
		private bool useCustomStyle = false;

		[SerializeField]
		private NameplateStyle customStyle = NameplateStyle.Default;

		/// <summary>The rows, kept sorted by <see cref="NameplateLine.Order"/> then key.</summary>
		/// <remarks>
		/// Sorted on write rather than on read. Rows are written a handful of times in a plate's
		/// life and read every frame it is on screen, and a renderer that had to sort would need a
		/// scratch buffer per plate to do it without allocating.
		/// </remarks>
		private readonly List<NameplateLine> lines = new List<NameplateLine>();

		/// <summary>The faction-standing colour the client last resolved for this plate.</summary>
		private Color allianceTint = Color.white;

		/// <summary>Bumped by every change to the rows.</summary>
		public int Revision { get; private set; }

		/// <summary>Bumped by every change to the style or the tint, including the asset's own edits.</summary>
		/// <remarks>
		/// Folds the referenced asset's revision in, so editing a shared style while the client is
		/// running restyles every plate using it without the plates having to subscribe to anything.
		/// </remarks>
		public int StyleRevision => styleRevision + (styleAsset != null ? styleAsset.Revision : 0);

		private int styleRevision;

		/// <summary>The rows, in the order they stack from the top down.</summary>
		public IReadOnlyList<NameplateLine> Lines => lines;

		/// <summary>How many rows the plate carries.</summary>
		public int LineCount => lines.Count;

		/// <summary>
		/// Whether the plate is drawn.
		/// </summary>
		/// <remarks>
		/// A flag rather than the GameObject's active state, which is what the old label stack
		/// used. A plate carries the rows other systems write into it — a name resolved on spawn,
		/// a guild that arrived while the plate was hidden — and deactivating the object to hide it
		/// would deregister the plate and drop that state on the floor. Hiding is a rendering
		/// decision and this is where it belongs.
		/// </remarks>
		public bool Visible
		{
			get => visible;
			set => visible = value;
		}

		/// <summary>Draw-order bias; higher paints in front and is dropped last by the budget.</summary>
		public int Priority
		{
			get => priority;
			set => priority = value;
		}

		/// <summary>World-space offset from this transform to the plate's anchor point.</summary>
		public Vector3 WorldOffset
		{
			get => worldOffset;
			set => worldOffset = value;
		}

		/// <summary>The world point the plate's bottom edge sits at.</summary>
		public Vector3 WorldPosition => transform.position + worldOffset;

		/// <summary>
		/// The faction-standing colour used by every part of the style tinted
		/// <see cref="NameplateTint.Alliance"/>.
		/// </summary>
		/// <remarks>
		/// Written by the client, because standing is the viewer's question and not the plate's:
		/// the same orc is an enemy to one player and a neutral to another, and a plate that
		/// resolved its own colour would be answering for the wrong client.
		/// </remarks>
		public Color AllianceTint
		{
			get => allianceTint;
			set
			{
				if (allianceTint == value)
				{
					return;
				}
				allianceTint = value;
				++styleRevision;
			}
		}

		/// <summary>The shared style this plate references, if any.</summary>
		public NameplateStyleAsset StyleAsset => styleAsset;

		/// <summary>
		/// The style this plate is drawn with: its own if it authors one, its asset's if it
		/// references one, otherwise the default plate.
		/// </summary>
		public NameplateStyle Style
		{
			get
			{
				if (useCustomStyle)
				{
					return customStyle;
				}
				return styleAsset != null ? styleAsset.Style : NameplateStyle.Default;
			}
		}

		/// <summary>
		/// Points this plate at a shared style, or at none.
		/// </summary>
		/// <param name="value">The style asset, or null for the default plate.</param>
		/// <remarks>
		/// Clears the inline style flag: a caller asking for a shared style at runtime — a boss
		/// entering its final phase, a quest giver becoming relevant — means it, and leaving an
		/// inline style in place to silently win would make the call do nothing.
		/// </remarks>
		public void SetStyle(NameplateStyleAsset value)
		{
			if (ReferenceEquals(styleAsset, value) && !useCustomStyle)
			{
				return;
			}
			styleAsset = value;
			useCustomStyle = false;
			++styleRevision;
		}

		/// <summary>
		/// Gives this plate a style of its own, overriding any referenced asset.
		/// </summary>
		/// <param name="value">The style to draw with.</param>
		public void SetStyle(NameplateStyle value)
		{
			customStyle = value;
			useCustomStyle = true;
			++styleRevision;
		}

		// ── Rows ────────────────────────────────────────────────────

		/// <summary>
		/// Writes a standard row, coloured by the plate's style.
		/// </summary>
		/// <param name="slot">Which row.</param>
		/// <param name="text">The text; null or blank removes the row.</param>
		public void SetLine(NameplateSlot slot, string text)
		{
			WriteLine((int)slot, (int)slot, text, default, false, NameplateSlots.DefaultScale(slot));
		}

		/// <summary>
		/// Writes a standard row in a colour of its own.
		/// </summary>
		/// <param name="slot">Which row.</param>
		/// <param name="text">The text; null or blank removes the row.</param>
		/// <param name="color">The colour to draw it in, overriding the style.</param>
		public void SetLine(NameplateSlot slot, string text, Color color)
		{
			WriteLine((int)slot, (int)slot, text, color, true, NameplateSlots.DefaultScale(slot));
		}

		/// <summary>
		/// Writes a row of the caller's own, at the caller's own place in the stack.
		/// </summary>
		/// <param name="key">The row's identity. Use <see cref="NameplateSlot.Custom"/> or above.</param>
		/// <param name="order">Where it stacks; ascending from the top of the plate.</param>
		/// <param name="text">The text; null or blank removes the row.</param>
		/// <param name="scale">Font scale relative to the plate's name row.</param>
		public void SetLine(int key, int order, string text, float scale = 0.85f)
		{
			WriteLine(key, order, text, default, false, scale);
		}

		/// <summary>
		/// Writes a row of the caller's own, in a colour of its own.
		/// </summary>
		/// <param name="key">The row's identity. Use <see cref="NameplateSlot.Custom"/> or above.</param>
		/// <param name="order">Where it stacks; ascending from the top of the plate.</param>
		/// <param name="text">The text; null or blank removes the row.</param>
		/// <param name="color">The colour to draw it in, overriding the style.</param>
		/// <param name="scale">Font scale relative to the plate's name row.</param>
		public void SetLine(int key, int order, string text, Color color, float scale = 0.85f)
		{
			WriteLine(key, order, text, color, true, scale);
		}

		/// <summary>Removes a standard row.</summary>
		/// <param name="slot">Which row.</param>
		/// <returns>True when a row was actually removed.</returns>
		public bool ClearLine(NameplateSlot slot)
		{
			return ClearLine((int)slot);
		}

		/// <summary>Removes a row by key.</summary>
		/// <param name="key">The row's identity.</param>
		/// <returns>True when a row was actually removed.</returns>
		public bool ClearLine(int key)
		{
			for (int i = 0; i < lines.Count; ++i)
			{
				if (lines[i].Key != key)
				{
					continue;
				}
				lines.RemoveAt(i);
				++Revision;
				return true;
			}
			return false;
		}

		/// <summary>Removes every row.</summary>
		/// <remarks>
		/// For a plate being handed to something else — a pooled interactable coming back out of
		/// the pool as a different object — where the previous occupant's rows would otherwise
		/// show through.
		/// </remarks>
		public void ClearLines()
		{
			if (lines.Count == 0)
			{
				return;
			}
			lines.Clear();
			++Revision;
		}

		/// <summary>Reads a row back.</summary>
		/// <param name="key">The row's identity.</param>
		/// <param name="line">The row, when one is present.</param>
		/// <returns>True when the plate carries that row.</returns>
		public bool TryGetLine(int key, out NameplateLine line)
		{
			for (int i = 0; i < lines.Count; ++i)
			{
				if (lines[i].Key != key)
				{
					continue;
				}
				line = lines[i];
				return true;
			}
			line = default;
			return false;
		}

		/// <summary>Reads a standard row back.</summary>
		/// <param name="slot">Which row.</param>
		/// <param name="line">The row, when one is present.</param>
		/// <returns>True when the plate carries that row.</returns>
		public bool TryGetLine(NameplateSlot slot, out NameplateLine line)
		{
			return TryGetLine((int)slot, out line);
		}

		/// <summary>
		/// Inserts, replaces or removes a row, and bumps the revision only if something changed.
		/// </summary>
		/// <remarks>
		/// <para>Blank text removes the row rather than leaving an empty one. Callers clear a row by
		/// writing what they have — a player leaving a guild sets the guild name to the empty string
		/// — and an empty row that still took a line of height would leave a visible gap in the
		/// stack for every character in the world who is not in a guild.</para>
		/// <para>The revision is what the renderer diffs against, so it must move if and only if the
		/// rows actually differ: moving it on every write would re-lay-out every plate on screen
		/// every time a name was re-resolved, and not moving it would leave stale text on screen.</para>
		/// </remarks>
		private void WriteLine(int key, int order, string text, Color color, bool hasColor, float scale)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				ClearLine(key);
				return;
			}

			NameplateLine line = new NameplateLine
			{
				Key = key,
				Order = order,
				Text = text,
				Color = color,
				HasColor = hasColor,
				Scale = scale,
			};

			for (int i = 0; i < lines.Count; ++i)
			{
				if (lines[i].Key != key)
				{
					continue;
				}

				NameplateLine existing = lines[i];
				if (existing.Order == line.Order &&
					string.Equals(existing.Text, line.Text, StringComparison.Ordinal) &&
					existing.HasColor == line.HasColor &&
					existing.Color == line.Color &&
					Mathf.Approximately(existing.Scale, line.Scale))
				{
					return;
				}

				lines[i] = line;
				if (existing.Order != line.Order)
				{
					Sort();
				}
				++Revision;
				return;
			}

			lines.Add(line);
			Sort();
			++Revision;
		}

		/// <summary>
		/// Restores the row order after an insert or a reorder.
		/// </summary>
		/// <remarks>
		/// An insertion sort over a list that is almost always three rows long and already sorted:
		/// <c>List.Sort</c> would allocate a comparer and is slower at this size.
		/// </remarks>
		private void Sort()
		{
			for (int i = 1; i < lines.Count; ++i)
			{
				NameplateLine line = lines[i];
				int j = i - 1;
				while (j >= 0 && IsAfter(lines[j], line))
				{
					lines[j + 1] = lines[j];
					--j;
				}
				lines[j + 1] = line;
			}
		}

		/// <summary>Whether <paramref name="a"/> stacks below <paramref name="b"/>.</summary>
		private static bool IsAfter(in NameplateLine a, in NameplateLine b)
		{
			return a.Order > b.Order || (a.Order == b.Order && a.Key > b.Key);
		}

		// ── Lifetime ────────────────────────────────────────────────

		private void OnEnable()
		{
			active.Add(this);
			OnNameplateEnabled?.Invoke(this);
		}

		private void OnDisable()
		{
			active.Remove(this);

			/* Back to hidden, the state a plate is authored in. A character that despawns goes
			 * back into the object pool with its GameObject deactivated and comes out again as
			 * something else somewhere else; a plate that kept the previous life's visibility
			 * would be up from the first frame of the new one, until the range sweep next ran and
			 * took it down. The sweep re-decides on its own cadence, so nothing is lost by
			 * starting from off. */
			visible = false;

			OnNameplateDisabled?.Invoke(this);
		}

		/// <summary>
		/// Finds the plate on a hierarchy, building one if it has none.
		/// </summary>
		/// <param name="root">The object the plate belongs to.</param>
		/// <param name="localOffset">Where the plate hangs, in the root's local space, when one is built.</param>
		/// <returns>The plate, or null when <paramref name="root"/> is null.</returns>
		/// <remarks>
		/// For the things that are worth a plate but were never authored with one — a chest, a
		/// harvest node, a dropped item. An authored plate always wins, so adding one to a prefab
		/// later takes over from this without any code changing. The built plate starts hidden and
		/// carries no rows, exactly like an authored one.
		/// </remarks>
		public static Nameplate GetOrCreate(Transform root, Vector3 localOffset)
		{
			if (root == null)
			{
				return null;
			}

			Nameplate existing = root.GetComponentInChildren<Nameplate>(true);
			if (existing != null)
			{
				return existing;
			}

			GameObject holder = new GameObject(RuntimeObjectName);
			holder.transform.SetParent(root, false);
			holder.transform.localPosition = localOffset;
			holder.layer = root.gameObject.layer;
			return holder.AddComponent<Nameplate>();
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			// Keeps a renderer in step with inspector edits made while playing.
			++styleRevision;
			++Revision;
		}
#endif
	}
}
