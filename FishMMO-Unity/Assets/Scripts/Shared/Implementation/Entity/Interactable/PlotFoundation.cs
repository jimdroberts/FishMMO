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
		/// Every foundation currently alive, grouped by the scene it belongs to.
		/// </summary>
		/// <remarks>
		/// Keyed by scene <em>name</em>, not by a scene handle, because several channels of the same
		/// scene may be loaded in one process and they all describe the same land. Registration and
		/// ownership therefore resolve once per scene name and apply to every copy.
		/// </remarks>
		public static class Registry
		{
			private static readonly Dictionary<string, List<PlotFoundation>> foundationsByScene = new Dictionary<string, List<PlotFoundation>>();

			/// <summary>
			/// Raised when a foundation joins a scene that had none, so the housing system knows
			/// there is land here that may not have been registered with the database yet.
			/// </summary>
			public static event Action<string> OnSceneGainedFoundations;

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

				string sceneName = foundation.gameObject.scene.name;
				if (string.IsNullOrWhiteSpace(sceneName))
				{
					return;
				}

				if (!foundationsByScene.TryGetValue(sceneName, out List<PlotFoundation> foundations))
				{
					foundations = new List<PlotFoundation>();
					foundationsByScene.Add(sceneName, foundations);
				}

				if (foundations.Contains(foundation))
				{
					return;
				}
				foundations.Add(foundation);

				OnSceneGainedFoundations?.Invoke(sceneName);
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

				string sceneName = foundation.gameObject.scene.name;
				if (string.IsNullOrWhiteSpace(sceneName) ||
					!foundationsByScene.TryGetValue(sceneName, out List<PlotFoundation> foundations))
				{
					return;
				}

				foundations.Remove(foundation);
				if (foundations.Count < 1)
				{
					foundationsByScene.Remove(sceneName);
				}
			}

			/// <summary>
			/// The foundations authored in a scene, or an empty list when there are none.
			/// </summary>
			public static IReadOnlyList<PlotFoundation> ForScene(string sceneName)
			{
				if (!string.IsNullOrWhiteSpace(sceneName) &&
					foundationsByScene.TryGetValue(sceneName, out List<PlotFoundation> foundations))
				{
					return foundations;
				}
				return Array.Empty<PlotFoundation>();
			}

			/// <summary>
			/// Every scene that currently has at least one foundation.
			/// </summary>
			public static IReadOnlyCollection<string> Scenes => foundationsByScene.Keys;

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
