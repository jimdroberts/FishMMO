using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A claimable area of land, authored into a scene by a designer.
	/// </summary>
	/// <remarks>
	/// The object carries the plot's identity and its price; everything else about it — who owns it,
	/// whether it can be claimed at all — is resolved by the server against the database and pushed
	/// back here. Nothing about ownership is decided locally.
	///
	/// <para>Foundations announce themselves through <see cref="Registry"/> rather than being
	/// discovered by a scene sweep, because a sweep would have to know when a scene had finished
	/// loading and would find nothing in an additively-loaded one that arrives later.</para>
	/// </remarks>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class PlotFoundation : Interactable, IPlotFoundation
	{
		/// <summary>
		/// Every foundation currently alive, grouped by the loaded scene it belongs to.
		/// </summary>
		/// <remarks>
		/// Keyed by the scene manager's <em>handle</em>, which identifies one loaded copy of a scene
		/// inside this process. Scene name alone will not do: a scene server may host scenes for
		/// several world servers at once, and each world's copy is its own land with its own owners.
		/// The handle is what the housing system resolves back to a world server.
		///
		/// <para>The handle is a process-local identifier and is never persisted or sent anywhere —
		/// see <c>ISceneInstanceDetails.Handle</c>. It is used here only to group objects that are
		/// alive in this process right now.</para>
		/// </remarks>
		public static class Registry
		{
			private static readonly Dictionary<int, List<PlotFoundation>> foundationsByScene = new Dictionary<int, List<PlotFoundation>>();

			/// <summary>
			/// Raised when a foundation joins a loaded scene that had none, so the housing system
			/// knows there is land here that may not have been registered with the database yet.
			/// </summary>
			public static event Action<int> OnSceneGainedFoundations;

			/// <summary>
			/// Raised when a player asks to claim a foundation.
			/// </summary>
			/// <remarks>
			/// An event rather than a direct call because claiming needs the database, and the
			/// database lives behind a server behaviour that shared code cannot reference. The
			/// housing system subscribes; when it is absent — a client, or a server with housing
			/// off — nothing is listening and the request is simply dropped.
			/// </remarks>
			public static event Action<IPlayerCharacter, IPlotFoundation> OnClaimRequested;

			/// <summary>
			/// Adds a foundation to its scene's set.
			/// </summary>
			public static void Register(PlotFoundation foundation)
			{
				if (foundation == null)
				{
					return;
				}

				int handle = foundation.gameObject.scene.handle;
				if (handle == 0)
				{
					return;
				}

				if (!foundationsByScene.TryGetValue(handle, out List<PlotFoundation> foundations))
				{
					foundations = new List<PlotFoundation>();
					foundationsByScene.Add(handle, foundations);
				}

				if (foundations.Contains(foundation))
				{
					return;
				}
				foundations.Add(foundation);

				OnSceneGainedFoundations?.Invoke(handle);
			}

			/// <summary>
			/// Removes a foundation, and forgets the scene once its last one is gone.
			/// </summary>
			public static void Unregister(PlotFoundation foundation)
			{
				if (foundation == null)
				{
					return;
				}

				int handle = foundation.gameObject.scene.handle;
				if (handle == 0 || !foundationsByScene.TryGetValue(handle, out List<PlotFoundation> foundations))
				{
					return;
				}

				foundations.Remove(foundation);
				if (foundations.Count < 1)
				{
					foundationsByScene.Remove(handle);
				}
			}

			/// <summary>
			/// The foundations in one loaded scene, or an empty list when there are none.
			/// </summary>
			public static IReadOnlyList<PlotFoundation> ForScene(int sceneHandle)
			{
				if (foundationsByScene.TryGetValue(sceneHandle, out List<PlotFoundation> foundations))
				{
					return foundations;
				}
				return Array.Empty<PlotFoundation>();
			}

			/// <summary>
			/// Every loaded scene that currently has at least one foundation.
			/// </summary>
			public static IReadOnlyCollection<int> Scenes => foundationsByScene.Keys;

			/// <summary>
			/// Raises <see cref="OnClaimRequested"/>.
			/// </summary>
			public static void RequestClaim(IPlayerCharacter player, IPlotFoundation foundation)
			{
				OnClaimRequested?.Invoke(player, foundation);
			}
		}

		/// <summary>
		/// The key identifying this foundation within its scene.
		/// </summary>
		[Header("Plot")]
		[Tooltip("Identifies this plot within its scene. Must be unique per scene. Case and surrounding whitespace are ignored.")]
		[SerializeField]
		private string plotKey;

		/// <summary>
		/// What it costs to claim this plot.
		/// </summary>
		[Tooltip("Cost to claim this plot, in the server's currency attribute.")]
		[SerializeField]
		private long price;

		/// <summary>
		/// The plot's footprint in metres, as width (X) by depth (Z).
		/// </summary>
		/// <remarks>
		/// One Unity unit is one metre. Rectangular and axis-relative rather than an arbitrary
		/// volume: everything built on a plot has to be testable against its edge cheaply and
		/// identically on both sides of the wire, and a rectangle is the shape that makes
		/// "is this inside?" a pair of comparisons.
		///
		/// <para>Authored, never stored. The footprint belongs to the scene the same way the
		/// foundation's mesh does — the database records who owns a plot, not how big it is — so
		/// resizing one is a scene edit and needs no migration.</para>
		/// </remarks>
		[Tooltip("Plot footprint in metres: width (X) by depth (Z). 1 unit = 1 metre.")]
		[SerializeField]
		private Vector2 dimensions = new Vector2(DefaultSize, DefaultSize);

		/// <summary>
		/// Height of the plot's claimable volume, in metres.
		/// </summary>
		/// <remarks>
		/// Present so the plot is a box rather than an infinite column. A plot under a bridge should
		/// not own the bridge, and a plot on a cliff should not own the sky above it.
		/// </remarks>
		[Tooltip("Height of the plot volume in metres, measured upward from the foundation.")]
		[SerializeField]
		private float height = DefaultHeight;

		/// <summary>
		/// Footprint used when none is authored, in metres.
		/// </summary>
		/// <remarks>
		/// A placeholder, not a recommendation. The standard sizes a server should offer are a
		/// design decision about how housing districts are laid out, and that has not been made yet.
		/// </remarks>
		public const float DefaultSize = 16f;

		/// <summary>
		/// Height used when none is authored, in metres.
		/// </summary>
		public const float DefaultHeight = 12f;

		/// <summary>
		/// Smallest footprint or height a plot may have, in metres.
		/// </summary>
		/// <remarks>
		/// A plot with a zero or negative edge is a plot nothing can be inside, which would read as
		/// every placement being out of bounds rather than as the authoring mistake it is.
		/// </remarks>
		public const float MinimumExtent = 1f;

		/// <summary>
		/// The canonicalised key, computed once from <see cref="plotKey"/>. Null until first read.
		/// </summary>
		private string canonicalPlotKey;

		/// <inheritdoc />
		/// <remarks>
		/// Computed on demand rather than in <c>Awake</c>, so the key is available to anything that
		/// asks — editor validation tooling, and tests — without depending on the object having been
		/// through its runtime lifecycle. Empty means the authored key was unusable.
		/// </remarks>
		public string PlotKey
		{
			get
			{
				if (canonicalPlotKey == null)
				{
					string candidate = PlotIdentity.Normalize(plotKey);

					/* Over-long keys are rejected outright rather than truncated. A truncated key
					 * would silently collide with any other plot sharing its first characters, and
					 * the two would resolve to one row. */
					canonicalPlotKey = string.IsNullOrEmpty(candidate) || candidate.Length > PlotIdentity.MaxPlotKeyLength
						? string.Empty
						: candidate;
				}
				return canonicalPlotKey;
			}
		}

		/// <inheritdoc />
		public long Price => price < 0 ? 0 : price;

		/// <summary>
		/// The plot's footprint in metres, width (X) by depth (Z), floored at
		/// <see cref="MinimumExtent"/>.
		/// </summary>
		public Vector2 Dimensions => new Vector2(
			Mathf.Max(MinimumExtent, dimensions.x),
			Mathf.Max(MinimumExtent, dimensions.y));

		/// <summary>
		/// The plot's height in metres, floored at <see cref="MinimumExtent"/>.
		/// </summary>
		public float Height => Mathf.Max(MinimumExtent, height);

		/// <summary>
		/// The plot's volume in world space.
		/// </summary>
		/// <remarks>
		/// Centred on the foundation horizontally and resting on it vertically, so the foundation's
		/// transform marks the ground at the middle of the plot — which is where a designer dragging
		/// one into a scene would expect the pivot to be.
		///
		/// <para>Axis-aligned, and deliberately not rotated with the transform. A rotated plot would
		/// make every containment test a matrix operation on a path that runs per placement, and
		/// housing districts laid out on a grid gain nothing from arbitrary angles.</para>
		/// </remarks>
		public Bounds Bounds
		{
			get
			{
				Vector2 size = Dimensions;
				float volumeHeight = Height;
				Vector3 origin = transform.position;

				return new Bounds(
					new Vector3(origin.x, origin.y + (volumeHeight * 0.5f), origin.z),
					new Vector3(size.x, volumeHeight, size.y));
			}
		}

		/// <summary>
		/// True when a world-space point falls inside this plot.
		/// </summary>
		public bool Contains(Vector3 worldPosition)
		{
			return Bounds.Contains(worldPosition);
		}

		/// <inheritdoc />
		public long PlotID { get; private set; }

		/// <summary>
		/// Who owns this plot, as the server last resolved it.
		/// </summary>
		public PlotOwner Owner { get; private set; } = PlotOwner.None;

		/// <summary>
		/// True once the server has matched this foundation to its database row.
		/// </summary>
		public bool IsResolved => PlotID > 0;

		/// <inheritdoc />
		public override string Title => "Plot";

		/// <inheritdoc />
		public override Color TitleColor => TinyColor.ToUnityColor(TinyColor.forestGreen);

		/// <summary>
		/// Canonicalises the authored key and joins the registry.
		/// </summary>
		/// <remarks>
		/// A foundation whose key does not survive <see cref="PlotIdentity"/> never registers. It
		/// would otherwise sit in the world looking claimable while every attempt failed against a
		/// row that does not exist, which is a much harder thing to notice than a warning at load.
		/// </remarks>
		private void Awake()
		{
			/* The key is judged on its own merits before the scene is considered. They are separate
			 * questions — a key is well-formed or not regardless of where it sits — and folding them
			 * together would report a perfectly good key as unusable whenever the scene was the
			 * thing at fault. */
			if (string.IsNullOrEmpty(PlotKey))
			{
				Log.Warning("PlotFoundation",
					$"'{gameObject.name}' has an unusable plot key ('{plotKey}'). " +
					$"Keys must be non-empty and at most {PlotIdentity.MaxPlotKeyLength} characters. This plot cannot be claimed.");
				return;
			}

			string sceneName = gameObject.scene.name;
			if (!PlotIdentity.TryCreate(sceneName, PlotKey, out _))
			{
				Log.Warning("PlotFoundation",
					$"'{gameObject.name}' has a usable key ('{PlotKey}') but its scene name ('{sceneName}') is not, " +
					"so it cannot be registered and this plot cannot be claimed.");
				return;
			}

			Registry.Register(this);
		}

		/// <summary>
		/// Draws the plot's footprint so its extent is visible while authoring.
		/// </summary>
		/// <remarks>
		/// Plots are placed at edit time and never at runtime, so the scene view is the only place
		/// their boundaries are ever seen. Without this a designer would be sizing a volume they
		/// cannot look at.
		/// </remarks>
		private void OnDrawGizmos()
		{
			Bounds bounds = Bounds;

			Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.25f);
			Gizmos.DrawCube(bounds.center, bounds.size);

			Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.9f);
			Gizmos.DrawWireCube(bounds.center, bounds.size);
		}

		/// <summary>
		/// Leaves the registry.
		/// </summary>
		private void OnDestroy()
		{
			Registry.Unregister(this);
		}

		/// <summary>
		/// Records the database identity and ownership the server resolved for this plot.
		/// </summary>
		/// <param name="plotID">The plot's row identity.</param>
		/// <param name="owner">Who owns it.</param>
		public void ApplyResolvedState(long plotID, PlotOwner owner)
		{
			PlotID = plotID;
			Owner = owner;
		}

		/// <summary>
		/// Records a change of ownership without re-resolving the row.
		/// </summary>
		public void ApplyOwner(PlotOwner owner)
		{
			Owner = owner;
		}
	}
}
