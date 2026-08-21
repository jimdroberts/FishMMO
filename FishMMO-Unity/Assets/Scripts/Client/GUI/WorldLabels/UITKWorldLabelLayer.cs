using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws every active <see cref="WorldLabel"/> as a UI Toolkit element, projecting each one
	/// from its world position onto this screen-space panel each frame.
	/// </summary>
	/// <remarks>
	/// UI Toolkit has no world-space render mode, so the labels that used to be TextMeshPro
	/// components sitting in the scene are now plain data (<see cref="WorldLabel"/>) that this
	/// layer positions. <c>RuntimePanelUtils.CameraTransformWorldToPanel</c> does the projection,
	/// which is the supported path and accounts for the panel's own scaling — computing screen
	/// coordinates by hand and dividing by a scale factor drifts as soon as the reference
	/// resolution or DPI changes.
	///
	/// Two behaviours of true 3D text are reproduced deliberately rather than dropped:
	///
	/// • <b>Perspective scaling.</b> A world-unit font size is converted to panel points using the
	///   camera's vertical FOV and the label's distance, so distant labels shrink exactly as they
	///   did when they were geometry. Callers keep passing the same world-unit sizes they always
	///   did (1 for nameplates, 2 for damage, 4 for heals) and get the same apparent result.
	///
	/// • <b>Depth ordering.</b> Elements are reordered back-to-front by distance so a near label
	///   overlaps a far one. UI Toolkit paints in hierarchy order and has no depth buffer, so
	///   without this the draw order would be creation order, and a distant nameplate could sit on
	///   top of one right in front of the player.
	///
	/// What is <em>not</em> reproduced is occlusion by scene geometry: 3D text was hidden behind
	/// walls by the depth buffer, and a screen-space panel has none. <see cref="OccludeBehindGeometry"/>
	/// restores it with a physics raycast per label, off by default because it costs a raycast per
	/// label per frame and most labels are on targets the player can already see.
	/// </remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UIDocument))]
	public sealed class UITKWorldLabelLayer : MonoBehaviour
	{
		/// <summary>USS class applied to every projected label element.</summary>
		private const string LABEL_CLASS = "world-label";

		/// <summary>Name of the container element the labels are parented to.</summary>
		private const string CONTAINER_NAME = "world-label-container";

		/// <summary>Name of the container element for screen-anchored labels.</summary>
		private const string SCREEN_CONTAINER_NAME = "screen-label-container";

		/// <summary>
		/// The active layer, so the label pool can reach it without a scene search.
		/// </summary>
		private static UITKWorldLabelLayer instance;

		/// <summary>
		/// The active layer, or null when no client scene is loaded.
		/// </summary>
		public static UITKWorldLabelLayer Instance => instance;

		/// <summary>The document this layer renders into.</summary>
		private UIDocument document;

		/// <summary>Element the label elements are parented to.</summary>
		private VisualElement container;

		/// <summary>Element screen-anchored labels are parented to.</summary>
		private VisualElement screenContainer;

		/// <summary>
		/// Container for labels positioned in screen space rather than projected from the world.
		/// </summary>
		/// <remarks>
		/// Shared with <see cref="UITKAdvancedLabel"/> so a transient on-screen caption — a region
		/// name, a zone banner — does not need a UIDocument and PanelSettings of its own. It draws
		/// above the projected labels because it is declared after them in the UXML.
		/// </remarks>
		public VisualElement ScreenContainer
		{
			get
			{
				TryResolveContainer();
				return screenContainer;
			}
		}

		/// <summary>Backing element for each live label.</summary>
		private readonly Dictionary<WorldLabel, Label> elements = new Dictionary<WorldLabel, Label>();

		/// <summary>Revision last pushed to each backing element, so text is only written on change.</summary>
		private readonly Dictionary<WorldLabel, int> pushedRevisions = new Dictionary<WorldLabel, int>();

		/// <summary>Scratch list used to sort labels back-to-front without allocating each frame.</summary>
		private readonly List<WorldLabel> sortScratch = new List<WorldLabel>();

		/// <summary>Labels found to have been destroyed during a frame, collected for removal.</summary>
		private readonly List<WorldLabel> deadScratch = new List<WorldLabel>();

		/// <summary>
		/// Camera used for projection. Falls back to <see cref="Camera.main"/> when unset.
		/// </summary>
		[Tooltip("Camera used to project world positions. Defaults to Camera.main.")]
		public Camera ProjectionCamera;

		/// <summary>
		/// Labels further than this from the camera are hidden. Zero disables the cutoff.
		/// </summary>
		[Tooltip("Hide labels beyond this distance from the camera. 0 = no limit.")]
		[Min(0.0f)]
		public float MaxVisibleDistance = 80.0f;

		/// <summary>
		/// When true, a label with scene geometry between it and the camera is hidden.
		/// </summary>
		/// <remarks>
		/// Costs one raycast per visible label per frame. Off by default; see the class remarks.
		/// </remarks>
		[Tooltip("Hide labels blocked by scene geometry. Costs a raycast per label per frame.")]
		public bool OccludeBehindGeometry = false;

		/// <summary>Layers treated as occluders when <see cref="OccludeBehindGeometry"/> is on.</summary>
		[Tooltip("Layers that block labels when occlusion is enabled.")]
		public LayerMask OcclusionMask = ~0;

		/// <summary>Smallest font size, in panel points, a label is allowed to shrink to.</summary>
		[Tooltip("Lower clamp for projected font size, in panel points.")]
		[Min(1.0f)]
		public float MinFontSize = 8.0f;

		/// <summary>Largest font size, in panel points, a label is allowed to grow to.</summary>
		[Tooltip("Upper clamp for projected font size, in panel points.")]
		[Min(1.0f)]
		public float MaxFontSize = 96.0f;

		private void Awake()
		{
			if (instance != null && instance != this)
			{
				Log.Warning("UITKWorldLabelLayer", "A second world label layer was loaded; destroying the duplicate.");
				Destroy(this);
				return;
			}
			instance = this;

			document = GetComponent<UIDocument>();

			/* Pushed under every panel. This layer is not a UITKControl — it owns its document
			 * directly — so it applies its own tier rather than inheriting one. Projected
			 * nameplates and damage numbers belong to the world, and a nameplate showing through
			 * an open inventory reads as a bug. See UITKPanelLayer. */
			if (document != null)
			{
				document.sortingOrder = (float)UITKPanelLayer.WorldOverlay;
			}
		}

		private void OnEnable()
		{
			/* Labels that were already enabled before this layer woke up would otherwise never get
			 * an element: the events below only fire on transitions. Adopting the current registry
			 * makes load order between the layer and the characters irrelevant. */
			WorldLabel.OnLabelEnabled += HandleLabelEnabled;
			WorldLabel.OnLabelDisabled += HandleLabelDisabled;

			if (!TryResolveContainer())
			{
				return;
			}

			IReadOnlyList<WorldLabel> existing = WorldLabel.Active;
			for (int i = 0; i < existing.Count; ++i)
			{
				HandleLabelEnabled(existing[i]);
			}
		}

		private void OnDisable()
		{
			WorldLabel.OnLabelEnabled -= HandleLabelEnabled;
			WorldLabel.OnLabelDisabled -= HandleLabelDisabled;
			ReleaseAll();
		}

		private void OnDestroy()
		{
			if (instance == this)
			{
				instance = null;
			}
		}

		/// <summary>
		/// Resolves the container element, creating one if the document has no dedicated element.
		/// </summary>
		/// <returns>True when a container is available.</returns>
		private bool TryResolveContainer()
		{
			if (container != null)
			{
				return true;
			}
			if (document == null || document.rootVisualElement == null)
			{
				return false;
			}

			container = document.rootVisualElement.Q<VisualElement>(CONTAINER_NAME);
			if (container == null)
			{
				/* The layer is useful without a UXML of its own — a bare UIDocument with a panel
				 * settings asset is enough — so build the container rather than failing. */
				container = new VisualElement { name = CONTAINER_NAME };
				container.style.position = Position.Absolute;
				container.style.left = 0;
				container.style.top = 0;
				container.style.right = 0;
				container.style.bottom = 0;
				container.pickingMode = PickingMode.Ignore;
				document.rootVisualElement.Add(container);
			}
			container.pickingMode = PickingMode.Ignore;

			screenContainer = document.rootVisualElement.Q<VisualElement>(SCREEN_CONTAINER_NAME);
			if (screenContainer == null)
			{
				screenContainer = new VisualElement { name = SCREEN_CONTAINER_NAME };
				screenContainer.style.position = Position.Absolute;
				screenContainer.style.left = 0;
				screenContainer.style.top = 0;
				screenContainer.style.right = 0;
				screenContainer.style.bottom = 0;
				document.rootVisualElement.Add(screenContainer);
			}
			screenContainer.pickingMode = PickingMode.Ignore;
			return true;
		}

		/// <summary>
		/// Creates the backing element for a newly enabled label.
		/// </summary>
		/// <param name="label">The label that became visible.</param>
		private void HandleLabelEnabled(WorldLabel label)
		{
			if (label == null || !TryResolveContainer() || elements.ContainsKey(label))
			{
				return;
			}

			Label element = new Label
			{
				name = $"world-label-{label.GetInstanceID()}",
				pickingMode = PickingMode.Ignore,
			};
			element.AddToClassList(LABEL_CLASS);
			element.style.position = Position.Absolute;

			container.Add(element);
			elements[label] = element;
			pushedRevisions[label] = int.MinValue;
		}

		/// <summary>
		/// Removes the backing element for a label that was hidden or destroyed.
		/// </summary>
		/// <param name="label">The label that went away.</param>
		private void HandleLabelDisabled(WorldLabel label)
		{
			if (label == null)
			{
				return;
			}
			if (elements.TryGetValue(label, out Label element))
			{
				element.RemoveFromHierarchy();
				elements.Remove(label);
			}
			pushedRevisions.Remove(label);
		}

		/// <summary>
		/// Drops every backing element, leaving the registry untouched.
		/// </summary>
		private void ReleaseAll()
		{
			foreach (KeyValuePair<WorldLabel, Label> kvp in elements)
			{
				kvp.Value.RemoveFromHierarchy();
			}
			elements.Clear();
			pushedRevisions.Clear();
		}

		/// <summary>
		/// Projects and positions every live label.
		/// </summary>
		/// <remarks>
		/// LateUpdate rather than Update so the camera has already moved this frame — projecting
		/// against last frame's camera transform makes labels visibly lag the world they are
		/// pinned to whenever the player turns.
		/// </remarks>
		private void LateUpdate()
		{
			if (container == null && !TryResolveContainer())
			{
				return;
			}

			Camera camera = ProjectionCamera != null ? ProjectionCamera : Camera.main;
			if (camera == null || document == null || document.rootVisualElement == null)
			{
				return;
			}

			IPanel panel = document.rootVisualElement.panel;
			if (panel == null)
			{
				return;
			}

			float panelHeight = document.rootVisualElement.resolvedStyle.height;
			if (float.IsNaN(panelHeight) || panelHeight <= 0.0f)
			{
				return;
			}

			Vector3 cameraPosition = camera.transform.position;
			Vector3 cameraForward = camera.transform.forward;

			// Panel points per world unit at one unit of distance, for perspective font scaling.
			float pointsPerUnitAtOne = camera.orthographic
				? panelHeight / Mathf.Max(0.0001f, camera.orthographicSize * 2.0f)
				: panelHeight / (2.0f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));

			sortScratch.Clear();
			deadScratch.Clear();

			foreach (KeyValuePair<WorldLabel, Label> kvp in elements)
			{
				WorldLabel label = kvp.Key;
				if (label == null)
				{
					deadScratch.Add(label);
					continue;
				}

				Label element = kvp.Value;
				Vector3 worldPosition = label.WorldPosition;
				Vector3 toLabel = worldPosition - cameraPosition;
				float forwardDistance = Vector3.Dot(cameraForward, toLabel);

				// Behind the camera: projection would place it on screen mirrored.
				if (forwardDistance <= 0.01f)
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				float distance = toLabel.magnitude;
				if (MaxVisibleDistance > 0.0f && distance > MaxVisibleDistance)
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				if (OccludeBehindGeometry &&
					Physics.Linecast(cameraPosition, worldPosition, OcclusionMask, QueryTriggerInteraction.Ignore))
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, camera);
				if (float.IsNaN(panelPosition.x) || float.IsNaN(panelPosition.y))
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				float fontSize = Mathf.Clamp(
					label.fontSize * pointsPerUnitAtOne / (camera.orthographic ? 1.0f : forwardDistance),
					MinFontSize,
					MaxFontSize);

				if (pushedRevisions.TryGetValue(label, out int pushed) && pushed != label.Revision)
				{
					element.text = UITKRichText.ToUITK(label.text);
					pushedRevisions[label] = label.Revision;
				}

				element.style.display = DisplayStyle.Flex;
				element.style.color = label.color;
				element.style.fontSize = fontSize;

				/* Centre on the anchor. The element's own resolved width is only known after
				 * layout, so translate by -50% instead of subtracting half the width — that also
				 * keeps the label centred as its text changes width mid-frame. */
				element.style.left = panelPosition.x;
				element.style.top = panelPosition.y;
				element.style.translate = new Translate(Length.Percent(-50.0f), Length.Percent(-100.0f));

				sortScratch.Add(label);
			}

			/* Removed by key rather than through HandleLabelDisabled: a destroyed Unity object
			 * still works as a dictionary key but compares equal to null, and that method treats
			 * null as "nothing to do" — routing through it would leak the entry forever. */
			for (int i = 0; i < deadScratch.Count; ++i)
			{
				WorldLabel dead = deadScratch[i];
				if (elements.TryGetValue(dead, out Label orphan))
				{
					orphan.RemoveFromHierarchy();
					elements.Remove(dead);
				}
				pushedRevisions.Remove(dead);
			}

			ApplyDepthOrder(cameraPosition);
		}

		/// <summary>
		/// Reorders visible label elements back-to-front so nearer labels paint over farther ones.
		/// </summary>
		/// <param name="cameraPosition">World position to measure distance from.</param>
		/// <remarks>
		/// Reparenting a VisualElement dirties layout, so the order is compared before it is
		/// applied and untouched when nothing moved — which is the common case for a stationary
		/// camera. <see cref="WorldLabel.SortOrder"/> takes precedence over distance so a caller
		/// can force a label to the front regardless of where it sits in the world.
		/// </remarks>
		private void ApplyDepthOrder(Vector3 cameraPosition)
		{
			if (sortScratch.Count < 2)
			{
				return;
			}

			sortScratch.Sort((a, b) =>
			{
				int byOrder = a.SortOrder.CompareTo(b.SortOrder);
				if (byOrder != 0)
				{
					return byOrder;
				}
				float da = (b.WorldPosition - cameraPosition).sqrMagnitude;
				float db = (a.WorldPosition - cameraPosition).sqrMagnitude;
				// Farthest first, so it is painted first and ends up behind.
				return da.CompareTo(db);
			});

			bool orderChanged = false;
			int expectedIndex = 0;
			for (int i = 0; i < sortScratch.Count; ++i)
			{
				if (!elements.TryGetValue(sortScratch[i], out Label element))
				{
					continue;
				}
				if (container.IndexOf(element) != expectedIndex)
				{
					orderChanged = true;
					break;
				}
				++expectedIndex;
			}

			if (!orderChanged)
			{
				return;
			}

			for (int i = 0; i < sortScratch.Count; ++i)
			{
				if (elements.TryGetValue(sortScratch[i], out Label element))
				{
					container.Add(element);
				}
			}
		}
	}
}
