using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The one outlined object on screen: what the player is aiming at, when it is something they can
	/// interact with and the "Highlight Interactables" gameplay option is on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>No component on the object.</b> The outline is not something an object carries; it is a
	/// property of the player's aim. This holds one target and the list of its renderers, gathered when
	/// the target changes, and <see cref="TargetOutlineFeature"/> (a URP renderer feature on every
	/// renderer) draws them. Nothing is added to, or allocated per, interactable object, and with no
	/// target the feature adds no pass at all.
	/// </para>
	/// <para>
	/// <b>Who drives it.</b> <see cref="UITKTarget"/>'s hover handling: it already resolves what the
	/// pointer rests on through <see cref="FishMMO.Shared.InteractableResolver"/>, the same rule the
	/// interact key uses, so the highlighted object is always the one the key would act on.
	/// </para>
	/// <para>
	/// <b>What it draws.</b> The <see cref="MeshRenderer"/>s and <see cref="SkinnedMeshRenderer"/>s
	/// under the target, as gathered by <see cref="Show"/>; something that swaps its meshes while
	/// outlined calls <see cref="Refresh"/>. Objects drawn by the GPU-driven systems (terrain, scatter,
	/// batched procedural props) have no renderer and cannot be outlined this way. Only the visible
	/// parts are outlined: the ring follows what can be seen, not what is behind a wall.
	/// </para>
	/// </remarks>
	public static class TargetOutline
	{
		/// <summary>The widest outline, in pixels.</summary>
		public const float MaxWidth = 10f;

		/// <summary>The default outline colour: warm lamplight gold, readable on stone, grass and snow alike.</summary>
		public static readonly Color DefaultColor = new Color(1f, 0.8f, 0.4f, 1f);

		/// <summary>The default outline width, in pixels.</summary>
		public const float DefaultWidth = 2f;

		private static readonly List<Renderer> renderers = new List<Renderer>();
		private static readonly List<Renderer> gather = new List<Renderer>();
		private static Transform target;
		private static float width = DefaultWidth;

		/// <summary>The outline colour (alpha unused). Drawn exactly as chosen, after tonemapping.</summary>
		public static Color Color { get; set; } = DefaultColor;

		/// <summary>The outline width in pixels, 0 to <see cref="MaxWidth"/>. 0 draws nothing.</summary>
		public static float Width
		{
			get => width;
			set => width = Mathf.Clamp(value, 0f, MaxWidth);
		}

		/// <summary>The outlined object, or null. A destroyed target reads as null.</summary>
		public static Transform Target => target != null ? target : null;

		/// <summary>Whether anything is outlined.</summary>
		public static bool HasTarget => target != null && renderers.Count > 0;

		/// <summary>The renderers the outline draws, as gathered by <see cref="Show"/> or <see cref="Refresh"/>.</summary>
		public static IReadOnlyList<Renderer> Renderers => renderers;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			renderers.Clear();
			gather.Clear();
			target = null;
			width = DefaultWidth;
			Color = DefaultColor;
		}

		/// <summary>Outlines <paramref name="newTarget"/> in place of whatever was outlined. Null clears.</summary>
		public static void Show(Transform newTarget)
		{
			if (newTarget == null)
			{
				Clear();
				return;
			}
			if (ReferenceEquals(newTarget, target))
			{
				return;
			}
			target = newTarget;
			Refresh();
		}

		/// <summary>
		/// Stops outlining <paramref name="oldTarget"/> if it is the outlined object. Null (a target that
		/// was destroyed) clears an outline whose own target has been destroyed.
		/// </summary>
		public static void Hide(Transform oldTarget)
		{
			if (ReferenceEquals(oldTarget, target) || (oldTarget == null && target == null))
			{
				Clear();
			}
		}

		/// <summary>Outlines nothing.</summary>
		public static void Clear()
		{
			target = null;
			renderers.Clear();
		}

		/// <summary>Gathers the target's mesh and skinned-mesh renderers again, after it swapped its meshes.</summary>
		public static void Refresh()
		{
			renderers.Clear();
			if (target == null)
			{
				return;
			}
			gather.Clear();
			target.GetComponentsInChildren(true, gather);
			for (int i = 0; i < gather.Count; i++)
			{
				Renderer candidate = gather[i];
				if (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
				{
					renderers.Add(candidate);
				}
			}
			gather.Clear();
		}
	}
}
