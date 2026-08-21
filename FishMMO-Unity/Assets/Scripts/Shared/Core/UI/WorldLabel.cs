using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A piece of text anchored to a position in the world, rendered by whatever UI layer is
	/// present rather than by a renderer of its own.
	/// </summary>
	/// <remarks>
	/// This replaces the <c>TextMeshPro</c> components that used to hang off characters. UI
	/// Toolkit has no world-space rendering, so a label can no longer *be* a renderer sitting in
	/// the scene — it is a position plus some text, and the client projects it onto a screen-space
	/// panel every frame (see <c>UITKWorldLabelLayer</c>).
	///
	/// The property names are deliberately <c>text</c>, <c>color</c> and <c>fontSize</c> rather
	/// than the PascalCase this codebase otherwise uses. Roughly thirty call sites across Shared
	/// and Client assign through them, and matching the old TextMeshPro surface exactly is what
	/// lets those sites carry over untouched instead of being rewritten for a change that does not
	/// concern them.
	///
	/// Being a MonoBehaviour also keeps <c>label.gameObject.SetActive(false)</c> working as the
	/// visibility switch it always was: a disabled label deregisters, and the renderer never sees
	/// it. The server never instantiates these — nothing outside a client scene creates one — so
	/// the type is inert in headless builds.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class WorldLabel : MonoBehaviour
	{
		/// <summary>
		/// Every enabled label in the scene, in registration order.
		/// </summary>
		/// <remarks>
		/// A static registry rather than a <c>FindObjectsOfType</c> sweep: the renderer walks this
		/// once per frame, and labels are created and destroyed constantly by the damage-number
		/// pool. Registration is driven by OnEnable/OnDisable so the list only ever holds labels
		/// that should actually be drawn.
		/// </remarks>
		private static readonly List<WorldLabel> active = new List<WorldLabel>();

		/// <summary>
		/// Read-only view of the currently enabled labels, for the renderer to walk.
		/// </summary>
		public static IReadOnlyList<WorldLabel> Active => active;

		/// <summary>
		/// Raised when a label is enabled, so a renderer can create backing UI for it.
		/// </summary>
		public static event Action<WorldLabel> OnLabelEnabled;

		/// <summary>
		/// Raised when a label is disabled or destroyed, so a renderer can release its backing UI.
		/// </summary>
		public static event Action<WorldLabel> OnLabelDisabled;

		[SerializeField]
		private string labelText = string.Empty;

		[SerializeField]
		private Color labelColor = Color.white;

		[SerializeField]
		[Tooltip("Font size in UI panel points at the reference distance.")]
		private float labelFontSize = 14.0f;

		[SerializeField]
		[Tooltip("World-space offset from this transform, applied before projection.")]
		private Vector3 worldOffset = Vector3.zero;

		/// <summary>
		/// Incremented whenever text, colour or font size changes.
		/// </summary>
		/// <remarks>
		/// The renderer pushes text and colour onto its backing element only when this moves,
		/// rather than every frame for every label. Position still updates every frame because the
		/// camera moves regardless of whether the label changed.
		/// </remarks>
		public int Revision { get; private set; }

		/// <summary>
		/// The displayed text. Rich-text markup is permitted.
		/// </summary>
		public string text
		{
			get => labelText;
			set
			{
				if (labelText == value)
				{
					return;
				}
				labelText = value;
				++Revision;
			}
		}

		/// <summary>
		/// The text colour.
		/// </summary>
		public Color color
		{
			get => labelColor;
			set
			{
				if (labelColor == value)
				{
					return;
				}
				labelColor = value;
				++Revision;
			}
		}

		/// <summary>
		/// Font size in panel points.
		/// </summary>
		public float fontSize
		{
			get => labelFontSize;
			set
			{
				if (Mathf.Approximately(labelFontSize, value))
				{
					return;
				}
				labelFontSize = value;
				++Revision;
			}
		}

		/// <summary>
		/// World-space offset applied to this transform's position before projection.
		/// </summary>
		public Vector3 WorldOffset
		{
			get => worldOffset;
			set => worldOffset = value;
		}

		/// <summary>
		/// Optional sort bias. Higher values draw in front of lower ones within the label layer.
		/// </summary>
		public int SortOrder { get; set; }

		/// <summary>
		/// The world position this label should be drawn at.
		/// </summary>
		public Vector3 WorldPosition => transform.position + worldOffset;

		private void OnEnable()
		{
			active.Add(this);
			OnLabelEnabled?.Invoke(this);
		}

		private void OnDisable()
		{
			active.Remove(this);
			OnLabelDisabled?.Invoke(this);
		}

		/// <summary>
		/// Sets text and colour together without raising two revisions.
		/// </summary>
		/// <param name="value">The text to display.</param>
		/// <param name="tint">The colour to display it in.</param>
		public void Set(string value, Color tint)
		{
			bool changed = labelText != value || labelColor != tint;
			labelText = value;
			labelColor = tint;
			if (changed)
			{
				++Revision;
			}
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			// Keeps the renderer in step with edits made through the inspector while playing.
			++Revision;
		}
#endif
	}
}
