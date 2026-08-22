using System;
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
	///
	/// <para><b>Why this class is written the way it is.</b> It runs once per label per frame in
	/// the middle of combat, which makes it the client's hottest UI code, and the obvious
	/// implementation of every one of its jobs is the expensive one:</para>
	///
	/// • <b>Position goes through <c>translate</c>, never <c>left</c>/<c>top</c>.</b> Writing
	///   <c>left</c>/<c>top</c> marks the element's layout dirty, and Yoga then re-solves the
	///   whole container — every frame, for every label. <c>translate</c> is a transform: it moves
	///   the painted result and touches no layout at all. Centring on the anchor still needs a
	///   percentage of the label's own size, and one element gets one translate, so each label is
	///   an <em>anchor</em> element carrying the per-frame position and a <em>text</em> child
	///   carrying the static <c>-50% / -100%</c> centring. The extra element buys back a full
	///   relayout per frame.
	///
	/// • <b>Elements are pooled.</b> The GameObject side was already pooled; the elements were not,
	///   so every damage number allocated a <c>Label</c>, an anchor and (previously) an
	///   interpolated element name. That was the combat GC spike. Released elements go back to
	///   <see cref="elementPool"/> with their per-label state cleared.
	///
	/// • <b>Style writes are diffed.</b> Colour, font size and text are compared against what was
	///   last pushed and only written when they actually differ, so a nameplate that is not
	///   changing costs one translate write per frame and nothing else.
	///
	/// • <b>Depth order uses <c>VisualElement.Sort</c>.</b> Re-adding every child to reorder is
	///   O(N²) inside the hierarchy list and dirties the tree N times; <c>Sort</c> is one
	///   O(N log N) pass and one dirty. It is also skipped entirely unless the existing order is
	///   actually wrong, which is checked in a single allocation-free walk.
	///
	/// • <b>There is a draw budget.</b> <see cref="MaxVisibleLabels"/> caps how many labels are
	///   drawn at once; over budget, the nearest survive and the rest are hidden. Combined with
	///   the spawn budget in <see cref="UITKLabelMaker"/> this bounds the cost of a large AoE —
	///   or of a server that sends more labels than a client can reasonably draw.
	/// </remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UIDocument))]
	public sealed class UITKWorldLabelLayer : MonoBehaviour
	{
		/// <summary>USS class applied to the positioning element of every projected label.</summary>
		private const string ANCHOR_CLASS = "world-label-anchor";

		/// <summary>USS class applied to the text element of every projected label.</summary>
		private const string LABEL_CLASS = "world-label";

		/// <summary>Name of the container element the labels are parented to.</summary>
		private const string CONTAINER_NAME = "world-label-container";

		/// <summary>Name of the container element for screen-anchored labels.</summary>
		private const string SCREEN_CONTAINER_NAME = "screen-label-container";

		/// <summary>
		/// Shared name for every pooled anchor element.
		/// </summary>
		/// <remarks>
		/// Deliberately a constant rather than <c>$"world-label-{id}"</c>. Nothing queries a label
		/// by name — the layer holds direct references — and the interpolation allocated a string
		/// per label per spawn, which in a burst of damage numbers is the allocation that shows up
		/// as a frame-time spike.
		/// </remarks>
		private const string ANCHOR_NAME = "world-label-anchor";

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

		/// <summary>
		/// Everything the layer keeps for one live label: the two elements that draw it, the last
		/// values pushed onto them, and this frame's projection result.
		/// </summary>
		/// <remarks>
		/// One object per label rather than several parallel dictionaries. The per-frame loop
		/// touches every field, and a dictionary lookup per field per label per frame was a
		/// measurable share of the old cost on its own.
		/// </remarks>
		private sealed class LabelBinding
		{
			/// <summary>Absolutely positioned element that carries the per-frame translate.</summary>
			public VisualElement Anchor;

			/// <summary>Text element, centred on the anchor by a static translate from USS.</summary>
			public Label Text;

			/// <summary>Source text last converted and pushed onto <see cref="Text"/>.</summary>
			public string PushedSourceText;

			/// <summary>Colour last written to <see cref="Text"/>.</summary>
			public Color PushedColor;

			/// <summary>Font size last written to <see cref="Text"/>.</summary>
			public float PushedFontSize;

			/// <summary>Whether the anchor is currently displayed.</summary>
			public bool Displayed;

			/// <summary>Panel-space position last written as a translate.</summary>
			public Vector2 PushedPosition;

			/// <summary>Distance from the camera this frame, for depth ordering and budgeting.</summary>
			public float Distance;

			/// <summary>The label's explicit sort bias this frame.</summary>
			public int Order;

			/// <summary>
			/// Position of this label in the back-to-front draw order, or -1 when not drawn.
			/// </summary>
			public int RenderIndex;

			/// <summary>Frame index this label was last repositioned on, for distant-label stagger.</summary>
			public int LastMoveFrame;
		}

		/// <summary>Backing state for each live label.</summary>
		private readonly Dictionary<WorldLabel, LabelBinding> elements = new Dictionary<WorldLabel, LabelBinding>();

		/// <summary>Anchor/text element pairs released by dead labels, ready to be reused.</summary>
		private readonly Stack<LabelBinding> elementPool = new Stack<LabelBinding>();

		/// <summary>Scratch list used to order labels back-to-front without allocating each frame.</summary>
		private readonly List<LabelBinding> sortScratch = new List<LabelBinding>();

		/// <summary>Labels found to have been destroyed during a frame, collected for removal.</summary>
		private readonly List<WorldLabel> deadScratch = new List<WorldLabel>();

		/// <summary>
		/// Back-to-front comparison, cached so the per-frame sort allocates no closure.
		/// </summary>
		/// <remarks>
		/// The old code passed a lambda that captured the camera position, so a delegate and a
		/// display class were allocated every frame the sort ran. The distance is resolved into
		/// the binding during the projection pass instead, which leaves the comparison static.
		/// </remarks>
		private static readonly Comparison<LabelBinding> BackToFrontComparison = CompareBackToFront;

		/// <summary>
		/// Hierarchy comparison applied to the container's children, cached for the same reason.
		/// </summary>
		private static readonly Comparison<VisualElement> RenderIndexComparison = CompareRenderIndex;

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
		/// Hard cap on how many labels are drawn at once. Zero disables the cap.
		/// </summary>
		/// <remarks>
		/// The draw half of the label budget. When more labels than this want to be on screen the
		/// nearest ones win and the rest are hidden for the frame — a player standing in a large
		/// AoE sees the numbers that matter rather than paying for hundreds of overlapping ones.
		/// The spawn half lives in <see cref="UITKLabelMaker"/>; both are needed, because this cap
		/// alone still leaves the GameObjects allocated.
		/// </remarks>
		[Tooltip("Maximum labels drawn per frame; the nearest win. 0 = no limit.")]
		[Min(0)]
		public int MaxVisibleLabels = 64;

		/// <summary>
		/// Beyond this distance a label is repositioned every <see cref="DistantUpdateInterval"/>
		/// frames instead of every frame. Zero disables the level of detail.
		/// </summary>
		/// <remarks>
		/// A label 60 metres away moves a fraction of a panel point per frame, so updating it at a
		/// third of the rate is invisible and removes two thirds of its per-frame cost. Nearby
		/// labels — the ones a player is actually reading — are never staggered.
		/// </remarks>
		[Tooltip("Distance past which labels reposition every Nth frame instead of every frame. 0 = off.")]
		[Min(0.0f)]
		public float DistantLodDistance = 30.0f;

		/// <summary>Frame stride used for labels past <see cref="DistantLodDistance"/>.</summary>
		[Tooltip("Frame stride for distant labels. 1 = every frame.")]
		[Min(1)]
		public int DistantUpdateInterval = 3;

		/// <summary>
		/// Panel-point margin outside the panel within which a label is still drawn.
		/// </summary>
		/// <remarks>
		/// A label whose anchor is just off the edge can still have visible text, because the text
		/// is centred on the anchor and extends past it. The margin keeps that case correct while
		/// still culling the labels that are genuinely nowhere near the screen — which, with a
		/// wide draw distance, is most of them.
		/// </remarks>
		[Tooltip("Panel-point margin outside the viewport within which labels are still drawn.")]
		[Min(0.0f)]
		public float OffScreenMargin = 128.0f;

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
			if (container != null && container.panel != null)
			{
				return true;
			}
			if (document == null || document.rootVisualElement == null)
			{
				return false;
			}

			/* A container resolved against a previous tree is worse than no container: the
			 * elements parented to it are never painted, and nothing about it looks wrong from
			 * C#. Both handles are dropped so the lookup below rebuilds against the live tree. */
			if (container != null)
			{
				DiscardAllElements();
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
		/// Creates the backing elements for a newly enabled label, reusing a pooled pair if one is
		/// available.
		/// </summary>
		/// <param name="label">The label that became visible.</param>
		private void HandleLabelEnabled(WorldLabel label)
		{
			if (label == null || !TryResolveContainer() || elements.ContainsKey(label))
			{
				return;
			}

			LabelBinding binding = RentBinding();
			container.Add(binding.Anchor);
			elements[label] = binding;
		}

		/// <summary>
		/// Takes an anchor/text pair from the pool, or builds one when the pool is empty.
		/// </summary>
		private LabelBinding RentBinding()
		{
			if (elementPool.Count > 0)
			{
				LabelBinding pooled = elementPool.Pop();
				ResetBinding(pooled);
				return pooled;
			}

			VisualElement anchor = new VisualElement
			{
				name = ANCHOR_NAME,
				pickingMode = PickingMode.Ignore,
			};
			anchor.AddToClassList(ANCHOR_CLASS);

			Label text = new Label { pickingMode = PickingMode.Ignore };
			text.AddToClassList(LABEL_CLASS);
			anchor.Add(text);

			LabelBinding binding = new LabelBinding
			{
				Anchor = anchor,
				Text = text,
			};

			/* Read by the hierarchy comparison, which is static so that sorting allocates nothing.
			 * Set once here rather than per frame; the anchor and its binding never separate. */
			anchor.userData = binding;

			ResetBinding(binding);
			return binding;
		}

		/// <summary>
		/// Clears the per-label state on a binding so a reused pair cannot inherit the previous
		/// label's text, colour or position.
		/// </summary>
		private static void ResetBinding(LabelBinding binding)
		{
			binding.PushedSourceText = null;
			binding.PushedColor = default;
			binding.PushedFontSize = float.NaN;
			binding.PushedPosition = new Vector2(float.NaN, float.NaN);
			binding.Distance = 0.0f;
			binding.Order = 0;
			binding.RenderIndex = -1;
			binding.LastMoveFrame = int.MinValue;

			binding.Text.text = string.Empty;

			/* Hidden on release and on rent. A pooled pair that comes back displayed would flash
			 * the previous label's position for the frame between being parented and being
			 * projected. */
			binding.Displayed = false;
			binding.Anchor.style.display = DisplayStyle.None;
		}

		/// <summary>
		/// Releases the backing elements for a label that was hidden or destroyed.
		/// </summary>
		/// <param name="label">The label that went away.</param>
		private void HandleLabelDisabled(WorldLabel label)
		{
			if (label == null)
			{
				return;
			}
			if (elements.TryGetValue(label, out LabelBinding binding))
			{
				ReleaseBinding(binding);
				elements.Remove(label);
			}
		}

		/// <summary>
		/// Detaches a binding's elements and returns them to the pool.
		/// </summary>
		private void ReleaseBinding(LabelBinding binding)
		{
			binding.Anchor.RemoveFromHierarchy();
			binding.Displayed = false;
			binding.Anchor.style.display = DisplayStyle.None;
			elementPool.Push(binding);
		}

		/// <summary>
		/// Drops every backing element into the pool, leaving the label registry untouched.
		/// </summary>
		private void ReleaseAll()
		{
			foreach (KeyValuePair<WorldLabel, LabelBinding> kvp in elements)
			{
				ReleaseBinding(kvp.Value);
			}
			elements.Clear();
		}

		/// <summary>
		/// Throws away every element, pooled or live, because the visual tree they belong to is
		/// gone.
		/// </summary>
		private void DiscardAllElements()
		{
			elements.Clear();
			elementPool.Clear();
			container = null;
			screenContainer = null;
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
			if (!TryResolveContainer())
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
			float panelWidth = document.rootVisualElement.resolvedStyle.width;
			if (float.IsNaN(panelHeight) || panelHeight <= 0.0f)
			{
				return;
			}

			Vector3 cameraPosition = camera.transform.position;
			Vector3 cameraForward = camera.transform.forward;
			int frame = Time.frameCount;

			// Panel points per world unit at one unit of distance, for perspective font scaling.
			float pointsPerUnitAtOne = camera.orthographic
				? panelHeight / Mathf.Max(0.0001f, camera.orthographicSize * 2.0f)
				: panelHeight / (2.0f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));

			sortScratch.Clear();
			deadScratch.Clear();

			foreach (KeyValuePair<WorldLabel, LabelBinding> kvp in elements)
			{
				WorldLabel label = kvp.Key;
				LabelBinding binding = kvp.Value;

				if (label == null)
				{
					deadScratch.Add(label);
					continue;
				}

				Vector3 worldPosition = label.WorldPosition;
				Vector3 toLabel = worldPosition - cameraPosition;
				float forwardDistance = Vector3.Dot(cameraForward, toLabel);

				// Behind the camera: projection would place it on screen mirrored.
				if (forwardDistance <= 0.01f)
				{
					SetDisplayed(binding, false);
					continue;
				}

				float distance = toLabel.magnitude;
				if (MaxVisibleDistance > 0.0f && distance > MaxVisibleDistance)
				{
					SetDisplayed(binding, false);
					continue;
				}

				binding.Distance = distance;
				binding.Order = label.SortOrder;

				/* Level of detail. A distant label is repositioned on a frame stride, so on the
				 * frames it is skipped it keeps last frame's translate — which is why the skip
				 * happens here, after the culling tests but before any projection work. It still
				 * joins the sort, because dropping it out would leave it holding a stale render
				 * index and make the order check below report a false reorder every frame. */
				bool distant = DistantLodDistance > 0.0f && distance > DistantLodDistance;
				if (distant && DistantUpdateInterval > 1 && binding.Displayed &&
					frame - binding.LastMoveFrame < DistantUpdateInterval)
				{
					sortScratch.Add(binding);
					continue;
				}

				if (OccludeBehindGeometry &&
					Physics.Linecast(cameraPosition, worldPosition, OcclusionMask, QueryTriggerInteraction.Ignore))
				{
					SetDisplayed(binding, false);
					continue;
				}

				Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, camera);
				if (float.IsNaN(panelPosition.x) || float.IsNaN(panelPosition.y))
				{
					SetDisplayed(binding, false);
					continue;
				}

				/* Off-screen cull. Projection succeeds for anything in front of the camera, so
				 * without this a player facing away from a crowded area still pays the full
				 * per-label cost for labels nobody can see. */
				if (panelPosition.x < -OffScreenMargin ||
					panelPosition.y < -OffScreenMargin ||
					panelPosition.x > panelWidth + OffScreenMargin ||
					panelPosition.y > panelHeight + OffScreenMargin)
				{
					SetDisplayed(binding, false);
					continue;
				}

				binding.LastMoveFrame = frame;

				float fontSize = Mathf.Clamp(
					label.fontSize * pointsPerUnitAtOne / (camera.orthographic ? 1.0f : forwardDistance),
					MinFontSize,
					MaxFontSize);

				PushContent(binding, label, fontSize);

				/* Position lands in translate, not left/top. See the class remarks: left/top is a
				 * layout property and rewriting it every frame re-solves the whole container. */
				if (binding.PushedPosition.x != panelPosition.x || binding.PushedPosition.y != panelPosition.y)
				{
					binding.Anchor.style.translate = new Translate(panelPosition.x, panelPosition.y);
					binding.PushedPosition = panelPosition;
				}

				sortScratch.Add(binding);
			}

			/* Removed by key rather than through HandleLabelDisabled: a destroyed Unity object
			 * still works as a dictionary key but compares equal to null, and that method treats
			 * null as "nothing to do" — routing through it would leak the entry forever. */
			for (int i = 0; i < deadScratch.Count; ++i)
			{
				WorldLabel dead = deadScratch[i];
				if (elements.TryGetValue(dead, out LabelBinding orphan))
				{
					ReleaseBinding(orphan);
					elements.Remove(dead);
				}
			}

			ApplyBudgetAndDepthOrder();
		}

		/// <summary>
		/// Writes text, colour and font size onto a label's element, skipping anything unchanged.
		/// </summary>
		/// <remarks>
		/// <see cref="WorldLabel.Revision"/> is bumped by colour and font-size changes as well as
		/// text, so keying the text write off it re-converted and re-assigned the string on every
		/// frame of any animated label — which is every damage number, because the fade and scale
		/// effects rewrite colour and size continuously. The three values are diffed separately
		/// here so an animating label pays for the two cheap struct writes and not the string.
		/// </remarks>
		private static void PushContent(LabelBinding binding, WorldLabel label, float fontSize)
		{
			string source = label.text;
			if (!ReferenceEquals(binding.PushedSourceText, source) &&
				!string.Equals(binding.PushedSourceText, source, StringComparison.Ordinal))
			{
				binding.Text.text = UITKRichText.ToUITK(source);
				binding.PushedSourceText = source;
			}

			Color color = label.color;
			if (binding.PushedColor != color)
			{
				binding.Text.style.color = color;
				binding.PushedColor = color;
			}

			if (!Mathf.Approximately(binding.PushedFontSize, fontSize))
			{
				binding.Text.style.fontSize = fontSize;
				binding.PushedFontSize = fontSize;
			}
		}

		/// <summary>
		/// Shows or hides a label's anchor, writing the style only when the state actually changes.
		/// </summary>
		private static void SetDisplayed(LabelBinding binding, bool displayed)
		{
			if (binding.Displayed == displayed)
			{
				return;
			}
			binding.Displayed = displayed;
			binding.Anchor.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
			if (!displayed)
			{
				binding.RenderIndex = -1;
			}
		}

		/// <summary>
		/// Applies the draw budget, then reorders visible label elements back-to-front so nearer
		/// labels paint over farther ones.
		/// </summary>
		/// <remarks>
		/// <see cref="WorldLabel.SortOrder"/> takes precedence over distance so a caller can force
		/// a label to the front regardless of where it sits in the world; the budget honours the
		/// same ranking, so a label pushed to the front is also the last one to be dropped.
		/// <para>
		/// Reordering dirties the hierarchy, so the existing order is checked first and left alone
		/// when it is already correct — the common case for a stationary camera. The check is a
		/// single walk over the children with no allocation, unlike the old <c>IndexOf</c> probe,
		/// which was a linear search per label and therefore quadratic overall.
		/// </para>
		/// </remarks>
		private void ApplyBudgetAndDepthOrder()
		{
			int count = sortScratch.Count;
			if (count == 0)
			{
				return;
			}

			if (count > 1)
			{
				sortScratch.Sort(BackToFrontComparison);
			}

			/* Back-to-front, so the entries the budget drops are the ones at the FRONT of the
			 * list: farthest away and lowest priority. */
			int budget = MaxVisibleLabels > 0 ? Mathf.Min(MaxVisibleLabels, count) : count;
			int firstDrawn = count - budget;

			for (int i = 0; i < firstDrawn; ++i)
			{
				SetDisplayed(sortScratch[i], false);
			}
			for (int i = firstDrawn; i < count; ++i)
			{
				LabelBinding binding = sortScratch[i];
				binding.RenderIndex = i;
				SetDisplayed(binding, true);
			}

			if (!IsHierarchyOutOfOrder())
			{
				return;
			}

			container.Sort(RenderIndexComparison);
		}

		/// <summary>
		/// Walks the container's children once and reports whether their render indices are out of
		/// ascending order.
		/// </summary>
		private bool IsHierarchyOutOfOrder()
		{
			int childCount = container.childCount;
			int previous = int.MinValue;
			for (int i = 0; i < childCount; ++i)
			{
				int current = container.ElementAt(i).userData is LabelBinding binding ? binding.RenderIndex : -1;
				if (current < previous)
				{
					return true;
				}
				previous = current;
			}
			return false;
		}

		/// <summary>
		/// Orders two labels farthest-first, so the nearer one is painted last and ends up on top.
		/// </summary>
		private static int CompareBackToFront(LabelBinding a, LabelBinding b)
		{
			int byOrder = a.Order.CompareTo(b.Order);
			if (byOrder != 0)
			{
				return byOrder;
			}
			// Farthest first.
			return b.Distance.CompareTo(a.Distance);
		}

		/// <summary>
		/// Orders the container's children by the render index assigned this frame.
		/// </summary>
		private static int CompareRenderIndex(VisualElement a, VisualElement b)
		{
			int ia = a.userData is LabelBinding ba ? ba.RenderIndex : -1;
			int ib = b.userData is LabelBinding bb ? bb.RenderIndex : -1;
			return ia.CompareTo(ib);
		}
	}
}
