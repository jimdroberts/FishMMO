using System;
using System.Collections.Generic;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
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
			/// Every live copy of one plot, across every loaded channel.
			/// </summary>
			/// <remarks>
			/// Deliberately plural. Channels are several loaded copies of the same scene, and a plot
			/// is one row shared between all of them — so a grant, a revocation or a change of state
			/// arrives naming a plot and has to reach every copy. Applying it to the first match
			/// would leave the same house locked in one channel and open in the next.
			///
			/// <para>A linear walk, because the count is small: this is the foundations authored
			/// into the scenes one server happens to be hosting, not the world's land. Indexing them
			/// by plot ID would mean an index that has to be maintained through resolution,
			/// re-resolution and scene unload for a lookup that runs when somebody changes a
			/// permission.</para>
			/// </remarks>
			public static List<PlotFoundation> ForPlot(long plotID)
			{
				List<PlotFoundation> matches = new List<PlotFoundation>();
				if (plotID <= 0)
				{
					return matches;
				}

				foreach (KeyValuePair<int, List<PlotFoundation>> pair in foundationsByScene)
				{
					foreach (PlotFoundation foundation in pair.Value)
					{
						if (foundation != null && foundation.PlotID == plotID)
						{
							matches.Add(foundation);
						}
					}
				}

				return matches;
			}

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
		/// Where this plot is in its lifecycle, replicated to everyone who can see it.
		/// </summary>
		/// <remarks>
		/// Replicated, unlike <see cref="Owner"/>, because it decides what the foundation looks
		/// like. A client has to draw an empty lot, a building site, a finished house and an
		/// abandoned one differently, and it cannot ask the database — channels hold no world state
		/// and a client holds less. Ownership stays server-side: nothing a client renders depends on
		/// <em>who</em> owns a plot, only on whether it is held.
		///
		/// <para>Server-authoritative and persisted on the plot row, so it survives the scene being
		/// unloaded, the channel being torn down, and the owner logging out.</para>
		/// </remarks>
		private readonly SyncVar<int> plotState = new SyncVar<int>((int)PlotState.Empty, new SyncTypeSettings()
		{
			SendRate = 0.0f,
			Channel = Channel.Reliable,
			ReadPermission = ReadPermission.Observers,
			WritePermission = WritePermission.ServerOnly,
		});

		/// <inheritdoc />
		public PlotState State => (PlotState)plotState.Value;

		/// <summary>
		/// Who the owner has let in, and what they may do. Server-side only.
		/// </summary>
		/// <remarks>
		/// Never replicated. A plot's guest list is the owner's business, and pushing it to every
		/// observer would tell the whole street who has keys to which house — including to a client
		/// that only had to stand nearby to collect it. Access questions are answered on the server,
		/// which is the only place their answer is binding anyway.
		///
		/// <para>Null until the server resolves the plot, so "no grants loaded yet" is
		/// distinguishable from "nobody has been let in". The two must not be confused: treating an
		/// unloaded list as empty would evict every friend in the house for the seconds between a
		/// scene loading and its grants arriving.</para>
		/// </remarks>
		private Dictionary<long, PlotPermission> accessGrants;

		/// <summary>
		/// True once the server has loaded this plot's access list.
		/// </summary>
		public bool HasResolvedAccess => accessGrants != null;

		/// <summary>
		/// What one character has been granted on this plot, or <see cref="PlotPermission.None"/>.
		/// </summary>
		public PlotPermission GrantFor(long characterID)
		{
			if (accessGrants == null ||
				characterID <= 0 ||
				!accessGrants.TryGetValue(characterID, out PlotPermission granted))
			{
				return PlotPermission.None;
			}
			return granted;
		}

		/// <summary>
		/// Everyone currently granted access, for the owner's guest list. Never null.
		/// </summary>
		public IReadOnlyDictionary<long, PlotPermission> AccessGrants =>
			accessGrants ?? EmptyGrants;

		/// <summary>
		/// Shared empty grant list, so reading one before it loads allocates nothing.
		/// </summary>
		private static readonly Dictionary<long, PlotPermission> EmptyGrants = new Dictionary<long, PlotPermission>();

		/// <summary>
		/// The character currently editing this plot, or zero when nobody is.
		/// </summary>
		/// <remarks>
		/// Replicated so every client can see that a plot is closed, not just the owner. Building
		/// changes the shape of the world underneath people; a plot that is being edited has to look
		/// shut from the outside or players will walk into geometry that is appearing and vanishing
		/// around them.
		///
		/// <para>Server-authoritative, and deliberately not persisted: a session is a person standing
		/// there with the editor open. If the server dies, nobody is standing there any more, and a
		/// stored session would leave the plot locked with no way to unlock it.</para>
		/// </remarks>
		private readonly SyncVar<long> builderCharacterID = new SyncVar<long>(0, new SyncTypeSettings()
		{
			SendRate = 0.0f,
			Channel = Channel.Reliable,
			ReadPermission = ReadPermission.Observers,
			WritePermission = WritePermission.ServerOnly,
		});

		/// <summary>
		/// The character currently editing this plot, or zero.
		/// </summary>
		public long BuilderCharacterID => builderCharacterID.Value;

		/// <summary>
		/// True while somebody is editing this plot.
		/// </summary>
		public bool IsBeingBuilt => builderCharacterID.Value != 0;

		/// <summary>
		/// Everything a character may do here right now.
		/// </summary>
		/// <param name="characterID">The character asking.</param>
		/// <param name="characterGuildID">Their guild, or zero when they are in none.</param>
		/// <remarks>
		/// Delegated to <see cref="PlotAccess"/> rather than decided here, so the server enforcing
		/// the rule and the client greying out a button reach the answer through the same
		/// arithmetic. A house whose door refuses somebody the UI told they could enter reads as the
		/// game being broken rather than as the house being locked.
		/// </remarks>
		public PlotPermission PermissionsFor(long characterID, long characterGuildID)
		{
			return PlotAccess.Resolve(State, Owner, characterID, characterGuildID, GrantFor(characterID));
		}

		/// <summary>
		/// True when this character may be inside the plot right now.
		/// </summary>
		/// <param name="characterID">The character asking.</param>
		/// <param name="characterGuildID">Their guild, or zero when they are in none.</param>
		/// <remarks>
		/// Two gates, and both have to pass. The first is the standing rule — the state of the plot
		/// and whatever the owner has granted. The second is the live build session: a plot being
		/// edited is closed even to people who normally hold a key, because the ground is moving
		/// under them and a visitor standing in it is a visitor inside a wall that was not there a
		/// moment ago.
		///
		/// <para>The session gate is deliberately narrower than the state one. An owner may end a
		/// session without leaving <see cref="PlotState.Building"/>, and their friends should not be
		/// let in the instant they stop placing things — the house is still a building site.</para>
		///
		/// <para>Access that has not loaded yet admits nobody but the owner. The alternative is
		/// admitting everybody for the seconds between a scene loading and its grants arriving, and
		/// an access rule that is off during startup is an access rule a player can wait out.</para>
		/// </remarks>
		public bool AllowsEntry(long characterID, long characterGuildID)
		{
			if (IsBeingBuilt && builderCharacterID.Value != characterID)
			{
				return false;
			}

			/* Unresolved grants are treated as no grants, which closes the plot to guests rather
			 * than opening it to strangers. The owner is unaffected: ownership is read from the plot
			 * row, which resolves in the same pass, not from the access list. */
			return PlotAccess.AllowsEntry(State, Owner, characterID, characterGuildID, GrantFor(characterID));
		}

		/// <summary>
		/// Opens or closes a build session. Server only.
		/// </summary>
		/// <param name="characterID">The editing character, or zero to close the session.</param>
		public void SetBuilder(long characterID)
		{
			builderCharacterID.Value = characterID;
		}

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
		/// <param name="state">Where it is in its lifecycle.</param>
		/// <param name="grants">Who has been let in, or null when that is not known yet.</param>
		public void ApplyResolvedState(long plotID, PlotOwner owner, PlotState state, Dictionary<long, PlotPermission> grants = null)
		{
			PlotID = plotID;
			Owner = owner;
			plotState.Value = (int)state;

			/* Only overwritten when the caller actually read the access list. Passing null here
			 * means "I did not load this", not "there is nobody", and clobbering a resolved list
			 * with an empty one would silently evict every guest in the house. */
			if (grants != null)
			{
				accessGrants = grants;
			}
		}

		/// <summary>
		/// Records a change of ownership without re-resolving the row.
		/// </summary>
		public void ApplyOwner(PlotOwner owner)
		{
			Owner = owner;
		}

		/// <summary>
		/// Records the plot's lifecycle state. Server only.
		/// </summary>
		public void ApplyState(PlotState state)
		{
			plotState.Value = (int)state;
		}

		/// <summary>
		/// Replaces the plot's access list with what the database holds. Server only.
		/// </summary>
		/// <remarks>
		/// Replaces wholesale rather than merging. The list read back is the complete answer, and a
		/// merge could only ever add — so a grant revoked while this server was not looking would
		/// survive every refresh, and the friend the owner thought they had locked out would keep
		/// walking in.
		/// </remarks>
		public void ApplyAccessGrants(Dictionary<long, PlotPermission> grants)
		{
			accessGrants = grants ?? new Dictionary<long, PlotPermission>();
		}

		/// <summary>
		/// Records one character's grant, or removes it. Server only.
		/// </summary>
		/// <param name="characterID">The character granted or revoked.</param>
		/// <param name="permissions">What they now hold; <see cref="PlotPermission.None"/> revokes.</param>
		/// <remarks>
		/// A no-op before the list has loaded. Seeding a single entry into an unresolved list would
		/// turn "not loaded" into "loaded, containing exactly one person", and the resolve that
		/// followed would look like a mass revocation of everybody else.
		/// </remarks>
		public void ApplyAccessGrant(long characterID, PlotPermission permissions)
		{
			if (accessGrants == null || characterID <= 0)
			{
				return;
			}

			PlotPermission sanitized = PlotAccess.Sanitize(permissions);
			if (sanitized == PlotPermission.None)
			{
				accessGrants.Remove(characterID);
				return;
			}

			accessGrants[characterID] = sanitized;
		}

		/// <summary>
		/// Forgets this plot's resolved state, putting it back to unclaimable. Server only.
		/// </summary>
		/// <remarks>
		/// Used when a scene's registration has to be redone. The access list goes back to null
		/// rather than empty, so the gap reads as "not loaded" — which closes the plot — instead of
		/// as "nobody has a key", which is the same thing until somebody is let in and then quietly
		/// is not.
		/// </remarks>
		public void ClearResolvedState()
		{
			PlotID = 0;
			Owner = PlotOwner.None;
			accessGrants = null;
		}
	}
}
