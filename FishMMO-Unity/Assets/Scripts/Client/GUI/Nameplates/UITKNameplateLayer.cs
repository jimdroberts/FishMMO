using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws every visible <see cref="Nameplate"/> as a single UI Toolkit element, projected from
	/// its world anchor onto this screen-space panel each frame.
	/// </summary>
	/// <remarks>
	/// <para><b>One element per plate is the whole point.</b> Overhead text used to be a set of
	/// sibling <see cref="WorldLabel"/>s projected one at a time and then pushed apart vertically
	/// in screen space. Two world points a few centimetres apart do not project to a constant
	/// screen-space gap: pitch the camera and the gap shrinks, look down the offset and it vanishes,
	/// so the correction that separated the rows at one camera angle overlapped them at another.
	/// A plate projects ONE point and lays its rows out in points inside that element, where
	/// vertical order is the layout engine's business and no camera angle can disturb it.</para>
	///
	/// <para><b>What is shared with <see cref="UITKWorldLabelLayer"/>, and why.</b> Both layers sit
	/// on the same <c>UIDocument</c>, and both take the draw distance and the hide-behind-geometry
	/// toggle from <see cref="ClientWorldLabelSettings"/>. One document means one panel, one
	/// projection matrix and a guaranteed paint order — the nameplate container is the FIRST child
	/// of the root, so a damage number always reads over a plate rather than fighting it for order.
	/// The two shared settings are statements about projected world UI as a whole, and a second
	/// copy of each would let a player set two answers to one question.</para>
	///
	/// <para><b>Everything else is this layer's own</b>, from
	/// <see cref="ClientNameplateSettings"/>: opacity, size, background strength, which optional
	/// rows are drawn, and the visible-plate budget. The budget in particular must not be shared —
	/// a burst of damage numbers can never push the plate the player is aiming at off the screen,
	/// and a crowded hub can never starve the combat feedback.</para>
	///
	/// <para><b>Sizes are in points at a reference size.</b> A plate's font size is computed from
	/// its world-unit size and its distance, exactly as a world label's is, and every other
	/// measurement in <see cref="NameplateStyle"/> — padding, border, radius, row spacing — is
	/// scaled by the same ratio. That is what keeps a plate's proportions identical near and far;
	/// absolute point padding would swallow a distant plate whole.</para>
	/// </remarks>
	[DefaultExecutionOrder(UITKNameplateLayer.ExecutionOrder)]
	[DisallowMultipleComponent]
	public sealed class UITKNameplateLayer : MonoBehaviour
	{
		/// <summary>
		/// Runs after the camera writers, for the reason documented on
		/// <see cref="UITKWorldLabelLayer.ExecutionOrder"/>: projecting against a camera that has
		/// not been placed yet is a one-frame lag that reads as a tick-rate shimmer (issue #227).
		/// </summary>
		public const int ExecutionOrder = UITKWorldLabelLayer.ExecutionOrder;

		/// <summary>The element the plates are parented to.</summary>
		private const string CONTAINER_NAME = "nameplate-container";

		/// <summary>USS class on the per-plate anchor, which carries the projected position.</summary>
		private const string ANCHOR_CLASS = "nameplate-anchor";

		/// <summary>USS class on the plate box itself: background, border, padding, rows.</summary>
		private const string PLATE_CLASS = "nameplate";

		/// <summary>USS class on each row of text.</summary>
		private const string LINE_CLASS = "nameplate-line";

		/// <summary>USS class on the row that carries the plate: the name.</summary>
		private const string PRIMARY_LINE_CLASS = "nameplate-line--primary";

		/// <summary>Name given to every anchor element, for inspection in the UI Toolkit debugger.</summary>
		private const string ANCHOR_NAME = "nameplate-anchor";

		/// <summary>
		/// Font sizes are rounded to this many points before being written.
		/// </summary>
		/// <remarks>
		/// A plate's size changes continuously with distance, and every size change rewrites a font
		/// size per row plus the padding, border and radius of the box. Quantising to a quarter of a
		/// point makes a walking player's plate re-style a few times a second instead of every
		/// frame, and a quarter point is well under what anyone can see.
		/// </remarks>
		private const float FONT_SIZE_QUANTUM = 0.25f;

		/// <summary>The live layer, for callers that need to reach it without a reference.</summary>
		private static UITKNameplateLayer instance;

		/// <summary>The live layer, or null outside a client scene.</summary>
		public static UITKNameplateLayer Instance => instance;

		/// <summary>The document this layer draws into. Shared with <see cref="UITKWorldLabelLayer"/>.</summary>
		private UIDocument document;

		/// <summary>The element every plate is parented to.</summary>
		private VisualElement container;

		/// <summary>
		/// The backing elements for one plate.
		/// </summary>
		private sealed class PlateBinding
		{
			/// <summary>Positioned every frame by writing <c>translate</c>.</summary>
			public VisualElement Anchor;

			/// <summary>The box: background, border, padding, and the rows.</summary>
			public VisualElement Plate;

			/// <summary>One label per row, reused across content changes.</summary>
			public readonly List<Label> Rows = new List<Label>();

			/// <summary>The plate revision last written onto the rows.</summary>
			public int PushedRevision = int.MinValue;

			/// <summary>The style revision last written onto the box and the rows.</summary>
			public int PushedStyleRevision = int.MinValue;

			/// <summary>
			/// The layer's settings epoch last written onto the box and the rows.
			/// </summary>
			/// <remarks>
			/// Tracked beside the plate's own style revision rather than added to it. A player
			/// moving the background slider changes how every plate is painted without changing
			/// anything about any plate, so there has to be a second thing to compare; folding the
			/// two into one number would let an unlucky pair of values cancel out and leave a plate
			/// painted with the previous setting until something else about it changed.
			/// </remarks>
			public int PushedSettingsEpoch = int.MinValue;

			/// <summary>The quantised font size last written.</summary>
			public float PushedFontSize = float.NaN;

			/// <summary>The panel position last written, so an unmoved plate writes nothing.</summary>
			public Vector2 PushedPosition;

			/// <summary>Whether the anchor is currently displayed.</summary>
			public bool Displayed;

			/// <summary>Distance from the camera this frame, for depth ordering and the budget.</summary>
			public float Distance;

			/// <summary>The plate's priority this frame.</summary>
			public int Order;

			/// <summary>Index in the container's child list after the last reorder, or -1.</summary>
			public int RenderIndex;

			/// <summary>The frame this plate was last repositioned, for the distant-plate stride.</summary>
			public int LastMoveFrame;
		}

		/// <summary>Live plates and their elements.</summary>
		private readonly Dictionary<Nameplate, PlateBinding> elements = new Dictionary<Nameplate, PlateBinding>();

		/// <summary>Detached bindings, ready to be handed to the next plate.</summary>
		private readonly Stack<PlateBinding> elementPool = new Stack<PlateBinding>();

		/// <summary>Scratch list for the budget and depth-order pass.</summary>
		private readonly List<PlateBinding> sortScratch = new List<PlateBinding>();

		/// <summary>Scratch list of plates destroyed behind the layer's back.</summary>
		private readonly List<Nameplate> deadScratch = new List<Nameplate>();

		/// <summary>Cached comparisons, so sorting allocates nothing.</summary>
		private static readonly Comparison<PlateBinding> BackToFrontComparison = CompareBackToFront;

		private static readonly Comparison<VisualElement> RenderIndexComparison = CompareRenderIndex;

		/// <summary>The camera plates are projected from. Falls back to <c>Camera.main</c>.</summary>
		public Camera ProjectionCamera;

		/// <summary>Beyond this distance in metres a plate is not drawn. Zero means no limit.</summary>
		public float MaxVisibleDistance = 80.0f;

		/// <summary>
		/// How many plates may be drawn at once. Zero means no limit.
		/// </summary>
		/// <remarks>
		/// Counted separately from the world label budget on purpose; see the class remarks.
		/// </remarks>
		public int MaxVisiblePlates = 64;

		/// <summary>Beyond this distance a plate is repositioned on a stride. Zero disables the stride.</summary>
		public float DistantLodDistance = 30.0f;

		/// <summary>How many frames apart a distant plate is repositioned.</summary>
		public int DistantUpdateInterval = 3;

		/// <summary>How far outside the panel a plate may project before it is dropped.</summary>
		public float OffScreenMargin = 160.0f;

		/// <summary>Whether scene geometry hides a plate. Costs one linecast per visible plate.</summary>
		public bool OccludeBehindGeometry = false;

		/// <summary>The layers a plate can be hidden behind.</summary>
		public LayerMask OcclusionMask = ~0;

		/// <summary>Smallest font size a plate's name row is drawn at, in points.</summary>
		public float MinFontSize = 9.0f;

		/// <summary>Largest font size a plate's name row is drawn at, in points.</summary>
		public float MaxFontSize = 26.0f;

		/// <summary>The player's nameplate scale multiplier.</summary>
		private float plateScale = ClientNameplateSettings.DefaultScale;

		/// <summary>The player's background strength, as a multiplier on each style's own opacity.</summary>
		private float backgroundOpacity = ClientNameplateSettings.DefaultBackgroundOpacity;

		/// <summary>Whether the player wants the guild row drawn.</summary>
		private bool showGuild = ClientNameplateSettings.DefaultShowGuild;

		/// <summary>Whether the player wants the title row drawn.</summary>
		private bool showTitles = ClientNameplateSettings.DefaultShowTitles;

		/// <summary>
		/// Bumped whenever the player's settings are re-read, so every plate restyles once.
		/// </summary>
		private int settingsEpoch;

		/// <summary>True while the cached player settings are stale.</summary>
		private bool settingsDirty = true;

		/// <summary>Camera transform at the previous pass, for the still-camera test.</summary>
		private Vector3 lastCameraPosition;

		private Quaternion lastCameraRotation = Quaternion.identity;

		/// <summary>Squared distance under which the camera counts as still.</summary>
		private const float CAMERA_STILL_SQR_DISTANCE = 1e-8f;

		/// <summary>Angle under which the camera counts as still, in degrees.</summary>
		private const float CAMERA_STILL_DEGREES = 0.001f;

		private void Awake()
		{
			if (instance != null && instance != this)
			{
				Log.Warning("UITKNameplateLayer", "A second nameplate layer was loaded; destroying the duplicate.");
				Destroy(this);
				return;
			}
			instance = this;

			document = GetComponent<UIDocument>();
		}

		private void OnEnable()
		{
			Nameplate.OnNameplateEnabled += HandleNameplateEnabled;
			Nameplate.OnNameplateDisabled += HandleNameplateDisabled;

			ClientWorldLabelSettings.OnChanged -= MarkSettingsDirty;
			ClientWorldLabelSettings.OnChanged += MarkSettingsDirty;
			ClientNameplateSettings.OnChanged -= MarkSettingsDirty;
			ClientNameplateSettings.OnChanged += MarkSettingsDirty;
			settingsDirty = true;

			if (!TryResolveContainer())
			{
				return;
			}

			/* Plates that were already enabled before this layer woke up would otherwise never get
			 * an element: the events above only fire on transitions. Adopting the registry makes
			 * load order between the layer and the characters irrelevant. */
			IReadOnlyList<Nameplate> existing = Nameplate.Active;
			for (int i = 0; i < existing.Count; ++i)
			{
				HandleNameplateEnabled(existing[i]);
			}
		}

		private void OnDisable()
		{
			Nameplate.OnNameplateEnabled -= HandleNameplateEnabled;
			Nameplate.OnNameplateDisabled -= HandleNameplateDisabled;
			ClientWorldLabelSettings.OnChanged -= MarkSettingsDirty;
			ClientNameplateSettings.OnChanged -= MarkSettingsDirty;
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
		/// <remarks>
		/// The created container is inserted as the FIRST child of the root, so plates paint under
		/// the projected world labels and the screen-anchored captions that follow them. A container
		/// resolved against a previous visual tree is worse than none — its elements are never
		/// painted and nothing about it looks wrong from C# — so a tree swap drops every element and
		/// rebuilds.
		/// </remarks>
		private bool TryResolveContainer()
		{
			if (container != null && container.panel != null)
			{
				return true;
			}
			if (document == null)
			{
				document = GetComponent<UIDocument>();
			}
			if (document == null || document.rootVisualElement == null)
			{
				return false;
			}

			if (container != null)
			{
				DiscardAllElements();
			}

			container = document.rootVisualElement.Q<VisualElement>(CONTAINER_NAME);
			if (container == null)
			{
				container = new VisualElement { name = CONTAINER_NAME };
				container.style.position = Position.Absolute;
				container.style.left = 0;
				container.style.top = 0;
				container.style.right = 0;
				container.style.bottom = 0;
				container.style.overflow = Overflow.Hidden;
				document.rootVisualElement.Insert(0, container);
			}

			/* Never pickable. The container covers the whole viewport and sits over the world, so a
			 * pickable plate would swallow every click meant for the thing it is labelling — and a
			 * nameplate is drawn precisely where a player is trying to click. Set here as well as
			 * in USS because picking is an element property, not a style, and a child of an
			 * ignoring parent is still picked itself. */
			container.pickingMode = PickingMode.Ignore;

			// A freshly resolved container carries none of the inline styles the player's settings
			// put on the last one, and the opacity is one of them.
			settingsDirty = true;
			return true;
		}

		/// <summary>
		/// Builds the backing elements for a newly enabled plate.
		/// </summary>
		private void HandleNameplateEnabled(Nameplate plate)
		{
			if (plate == null || !TryResolveContainer() || elements.ContainsKey(plate))
			{
				return;
			}

			PlateBinding binding = RentBinding();
			container.Add(binding.Anchor);
			elements[plate] = binding;
		}

		/// <summary>
		/// Releases the backing elements for a plate that was hidden or destroyed.
		/// </summary>
		private void HandleNameplateDisabled(Nameplate plate)
		{
			if (plate == null)
			{
				return;
			}
			if (elements.TryGetValue(plate, out PlateBinding binding))
			{
				ReleaseBinding(binding);
				elements.Remove(plate);
			}
		}

		/// <summary>Takes a binding from the pool, or builds one when the pool is empty.</summary>
		private PlateBinding RentBinding()
		{
			if (elementPool.Count > 0)
			{
				PlateBinding pooled = elementPool.Pop();
				ResetBinding(pooled);
				return pooled;
			}

			VisualElement anchor = new VisualElement
			{
				name = ANCHOR_NAME,
				pickingMode = PickingMode.Ignore,
			};
			anchor.AddToClassList(ANCHOR_CLASS);

			VisualElement plate = new VisualElement { pickingMode = PickingMode.Ignore };
			plate.AddToClassList(PLATE_CLASS);
			anchor.Add(plate);

			PlateBinding binding = new PlateBinding
			{
				Anchor = anchor,
				Plate = plate,
			};

			// Read by the hierarchy comparison, which is static so that sorting allocates nothing.
			anchor.userData = binding;

			ResetBinding(binding);
			return binding;
		}

		/// <summary>
		/// Clears per-plate state so a reused binding cannot inherit the previous plate's rows.
		/// </summary>
		private static void ResetBinding(PlateBinding binding)
		{
			binding.PushedRevision = int.MinValue;
			binding.PushedStyleRevision = int.MinValue;
			binding.PushedSettingsEpoch = int.MinValue;
			binding.PushedFontSize = float.NaN;
			binding.PushedPosition = new Vector2(float.NaN, float.NaN);
			binding.Distance = 0.0f;
			binding.Order = 0;
			binding.RenderIndex = -1;
			binding.LastMoveFrame = int.MinValue;

			for (int i = 0; i < binding.Rows.Count; ++i)
			{
				binding.Rows[i].text = string.Empty;
				binding.Rows[i].style.display = DisplayStyle.None;
			}

			/* Hidden on release and on rent. A pooled binding that came back displayed would flash
			 * the previous plate's rows at the previous plate's position for one frame. */
			binding.Displayed = false;
			binding.Anchor.style.display = DisplayStyle.None;
		}

		/// <summary>Detaches a binding's elements and returns them to the pool.</summary>
		private void ReleaseBinding(PlateBinding binding)
		{
			binding.Anchor.RemoveFromHierarchy();
			binding.Displayed = false;
			binding.Anchor.style.display = DisplayStyle.None;
			elementPool.Push(binding);
		}

		/// <summary>Drops every backing element into the pool, leaving the plate registry alone.</summary>
		private void ReleaseAll()
		{
			foreach (KeyValuePair<Nameplate, PlateBinding> kvp in elements)
			{
				ReleaseBinding(kvp.Value);
			}
			elements.Clear();
		}

		/// <summary>Throws away every element because the visual tree they belong to is gone.</summary>
		private void DiscardAllElements()
		{
			elements.Clear();
			elementPool.Clear();
			container = null;
		}

		/// <summary>Records that the player's world label settings need re-reading.</summary>
		private void MarkSettingsDirty()
		{
			settingsDirty = true;
		}

		/// <summary>
		/// Copies the player's world label settings onto this layer.
		/// </summary>
		/// <remarks>
		/// Opacity lands on the CONTAINER rather than on each plate: every plate's colours are its
		/// own — standing colour, guild colour, a boss border — and folding a global alpha into each
		/// of them would mean re-blending every colour on every plate whenever the slider moved, and
		/// losing the alpha the style actually asked for.
		/// </remarks>
		private void ApplyPlayerSettings()
		{
			settingsDirty = false;
			++settingsEpoch;

			plateScale = ClientNameplateSettings.Scale;
			MaxVisiblePlates = ClientNameplateSettings.MaxVisible;
			backgroundOpacity = ClientNameplateSettings.BackgroundOpacity;
			showGuild = ClientNameplateSettings.ShowGuild;
			showTitles = ClientNameplateSettings.ShowTitles;

			// Shared with the world labels: both describe projected world UI, not nameplates.
			MaxVisibleDistance = ClientWorldLabelSettings.Distance;
			OccludeBehindGeometry = ClientWorldLabelSettings.Occlude;

			if (container != null)
			{
				container.style.opacity = ClientNameplateSettings.Opacity;
			}
		}

		/// <summary>
		/// Projects, styles and positions every visible plate.
		/// </summary>
		/// <remarks>
		/// LateUpdate, after the camera writers — see <see cref="ExecutionOrder"/>.
		/// </remarks>
		private void LateUpdate()
		{
			if (!TryResolveContainer())
			{
				return;
			}

			if (settingsDirty)
			{
				ApplyPlayerSettings();
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
			Quaternion cameraRotation = camera.transform.rotation;
			int frame = Time.frameCount;

			bool cameraStill = (cameraPosition - lastCameraPosition).sqrMagnitude <= CAMERA_STILL_SQR_DISTANCE &&
				Quaternion.Angle(cameraRotation, lastCameraRotation) <= CAMERA_STILL_DEGREES;
			lastCameraPosition = cameraPosition;
			lastCameraRotation = cameraRotation;

			// Panel points per world unit at one unit of distance, for perspective font scaling.
			float pointsPerUnitAtOne = camera.orthographic
				? panelHeight / Mathf.Max(0.0001f, camera.orthographicSize * 2.0f)
				: panelHeight / (2.0f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));

			sortScratch.Clear();
			deadScratch.Clear();

			foreach (KeyValuePair<Nameplate, PlateBinding> kvp in elements)
			{
				Nameplate plate = kvp.Key;
				PlateBinding binding = kvp.Value;

				if (plate == null)
				{
					deadScratch.Add(plate);
					continue;
				}

				// A hidden plate is not drawn — but it stays registered, because its rows are
				// still being written into while it is down.
				if (!plate.Visible)
				{
					SetDisplayed(binding, false);
					continue;
				}

				/* Counted rather than taken from LineCount, because the player can turn a kind of
				 * row off: a plate carrying nothing but a guild row, with guild rows hidden, would
				 * otherwise draw as an empty box over somebody's head. */
				int visibleRows = CountVisibleRows(plate);
				if (visibleRows == 0)
				{
					SetDisplayed(binding, false);
					continue;
				}

				NameplateStyle style = plate.Style;
				Vector3 worldPosition = plate.WorldPosition;
				if (style.AnchorGap != 0.0f)
				{
					worldPosition.y += style.AnchorGap;
				}

				Vector3 toPlate = worldPosition - cameraPosition;
				float forwardDistance = Vector3.Dot(cameraForward, toPlate);

				// Behind the camera: projection would place it on screen mirrored.
				if (forwardDistance <= 0.01f)
				{
					SetDisplayed(binding, false);
					continue;
				}

				float distance = toPlate.magnitude;
				if (MaxVisibleDistance > 0.0f && distance > MaxVisibleDistance)
				{
					SetDisplayed(binding, false);
					continue;
				}

				binding.Distance = distance;
				binding.Order = plate.Priority;

				/* Level of detail. A distant plate keeps last frame's translate on the frames it is
				 * skipped, which is why the skip happens after the culling tests and before any
				 * projection work. It still joins the sort, or it would hold a stale render index
				 * and make the order check below report a false reorder every frame. */
				bool distant = cameraStill && DistantLodDistance > 0.0f && distance > DistantLodDistance;
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

				if (panelPosition.x < -OffScreenMargin ||
					panelPosition.y < -OffScreenMargin ||
					panelPosition.x > panelWidth + OffScreenMargin ||
					panelPosition.y > panelHeight + OffScreenMargin)
				{
					SetDisplayed(binding, false);
					continue;
				}

				binding.LastMoveFrame = frame;

				/* The name row's size, from the plate's world-unit font size and its distance —
				 * the same arithmetic a world label uses, so a plate shrinks with distance exactly
				 * as text painted into the scene would. Quantised, because every change to it
				 * re-styles the whole box. */
				float fontSize = plateScale * Mathf.Clamp(
					style.FontSize * pointsPerUnitAtOne / (camera.orthographic ? 1.0f : forwardDistance),
					MinFontSize,
					MaxFontSize);
				fontSize = Mathf.Round(fontSize / FONT_SIZE_QUANTUM) * FONT_SIZE_QUANTUM;

				PushPlate(binding, plate, in style, fontSize, visibleRows);
				ApplyPosition(binding, panelPosition);
				sortScratch.Add(binding);
			}

			/* Removed by key rather than through HandleNameplateDisabled: a destroyed Unity object
			 * still works as a dictionary key but compares equal to null, and that method treats
			 * null as "nothing to do" — routing through it would leak the entry forever. */
			for (int i = 0; i < deadScratch.Count; ++i)
			{
				Nameplate dead = deadScratch[i];
				if (elements.TryGetValue(dead, out PlateBinding orphan))
				{
					ReleaseBinding(orphan);
					elements.Remove(dead);
				}
			}

			ApplyBudgetAndDepthOrder();
		}

		/// <summary>
		/// Writes a panel position onto a plate's anchor, skipping the write when nothing moved.
		/// </summary>
		/// <remarks>
		/// Position lands in <c>translate</c>, not <c>left</c>/<c>top</c>: translate is a transform
		/// and never marks layout dirty, while writing left/top every frame re-solves the whole
		/// container once per plate. The plate child carries the constant <c>-50% -100%</c> that
		/// centres it over the anchor and stands it on the anchor point, because one element only
		/// gets one translate and that one is needed for the position.
		/// </remarks>
		private static void ApplyPosition(PlateBinding binding, Vector2 position)
		{
			if (binding.PushedPosition.x != position.x || binding.PushedPosition.y != position.y)
			{
				binding.Anchor.style.translate = new Translate(position.x, position.y);
				binding.PushedPosition = position;
			}
		}

		/// <summary>
		/// Brings a plate's element up to date: its box, then its rows.
		/// </summary>
		/// <remarks>
		/// Three revisions decide what has to be rewritten — the plate's content revision, its style
		/// revision (which folds in the faction tint and any edit to a shared style asset) and the
		/// quantised font size. In the steady state all three match and this is three comparisons
		/// for a plate that is already correct on screen, which is what lets a hub full of players
		/// cost a projection each and nothing more.
		/// </remarks>
		private void PushPlate(PlateBinding binding, Nameplate plate, in NameplateStyle style, float fontSize, int visibleRows)
		{
			bool sizeChanged = !Mathf.Approximately(binding.PushedFontSize, fontSize);
			bool styleChanged = binding.PushedStyleRevision != plate.StyleRevision;
			bool contentChanged = binding.PushedRevision != plate.Revision;
			bool settingsChanged = binding.PushedSettingsEpoch != settingsEpoch;

			if (!sizeChanged && !styleChanged && !contentChanged && !settingsChanged)
			{
				return;
			}

			// Every authored measurement is in points at the style's reference size.
			float scale = fontSize / NameplateStyle.ReferenceFontSize;

			if (sizeChanged || styleChanged || settingsChanged)
			{
				ApplyBox(binding.Plate, in style, plate.AllianceTint, scale, backgroundOpacity);
			}

			if (sizeChanged || styleChanged || contentChanged || settingsChanged)
			{
				ApplyRows(binding, plate, in style, fontSize, scale, visibleRows, showGuild, showTitles);
			}

			binding.PushedFontSize = fontSize;
			binding.PushedStyleRevision = plate.StyleRevision;
			binding.PushedRevision = plate.Revision;
			binding.PushedSettingsEpoch = settingsEpoch;
		}

		/// <summary>
		/// How many of a plate's rows the player has asked to see.
		/// </summary>
		/// <remarks>
		/// Walked every frame for every visible plate, which is why the two answers it needs are
		/// cached fields rather than configuration reads. A plate carries three or four rows, so
		/// this is a handful of comparisons and no allocation.
		/// </remarks>
		private int CountVisibleRows(Nameplate plate)
		{
			IReadOnlyList<NameplateLine> lines = plate.Lines;
			int visible = 0;

			for (int i = 0; i < lines.Count; ++i)
			{
				if (ClientNameplateSettings.IsRowVisible(lines[i].Slot, showGuild, showTitles))
				{
					++visible;
				}
			}
			return visible;
		}

		/// <summary>
		/// Writes the plate's background, border, corners and padding.
		/// </summary>
		/// <param name="plate">The plate's box element.</param>
		/// <param name="style">The style to paint it with.</param>
		/// <param name="allianceTint">The plate's faction-standing colour.</param>
		/// <param name="scale">Resolved font size over the style's reference size.</param>
		/// <param name="backgroundOpacity">
		/// The player's background strength, as a multiplier on the style's own opacity. Zero
		/// leaves the rows over the world with nothing behind them, which is a look people ask for
		/// by name; it is a multiplier rather than a replacement so that a style which deliberately
		/// paints no background, or a fainter one, still means what it says.
		/// </param>
		private static void ApplyBox(VisualElement plate, in NameplateStyle style, Color allianceTint, float scale, float backgroundOpacity)
		{
			IStyle box = plate.style;

			Color background = Color.clear;
			if (style.ShowBackground)
			{
				background = style.ResolveBackgroundColor(allianceTint);
				background.a *= Mathf.Clamp01(backgroundOpacity);
			}
			box.backgroundColor = background;

			float border = style.ShowBorder ? Mathf.Max(0.0f, style.BorderWidth * scale) : 0.0f;
			box.borderTopWidth = border;
			box.borderBottomWidth = border;
			box.borderLeftWidth = border;
			box.borderRightWidth = border;

			if (border > 0.0f)
			{
				Color borderColor = style.ResolveBorderColor(allianceTint);
				box.borderTopColor = borderColor;
				box.borderBottomColor = borderColor;
				box.borderLeftColor = borderColor;
				box.borderRightColor = borderColor;
			}

			float radius = Mathf.Max(0.0f, style.CornerRadius * scale);
			box.borderTopLeftRadius = radius;
			box.borderTopRightRadius = radius;
			box.borderBottomLeftRadius = radius;
			box.borderBottomRightRadius = radius;

			float padH = Mathf.Max(0.0f, style.PaddingHorizontal * scale);
			float padV = Mathf.Max(0.0f, style.PaddingVertical * scale);
			box.paddingLeft = padH;
			box.paddingRight = padH;
			box.paddingTop = padV;
			box.paddingBottom = padV;

			box.minWidth = Mathf.Max(0.0f, style.MinWidth * scale);
		}

		/// <summary>
		/// Writes the plate's rows, reusing the labels already built for it.
		/// </summary>
		/// <remarks>
		/// Surplus labels are hidden rather than removed. A plate's row count changes with events —
		/// a guild joined, a status cleared — and rebuilding the child list each time would churn
		/// the hierarchy for a saving of a few bytes; keeping them means the next row costs a
		/// display flip. Row order is the plate's own, which sorted it on write.
		/// <para>
		/// Each row's height is written rather than left to the text. UI Toolkit gives a label a
		/// line box around 1.3x its font size and exposes no line-height property, so an
		/// auto-sized stack carries paragraph leading between rows that are meant to read as one
		/// plate; the height comes from <see cref="NameplateStyle.LineHeight"/> and the text is
		/// centred in it by the USS.
		/// </para>
		/// </remarks>
		private static void ApplyRows(PlateBinding binding, Nameplate plate, in NameplateStyle style, float fontSize, float scale, int visibleRows, bool showGuild, bool showTitles)
		{
			IReadOnlyList<NameplateLine> lines = plate.Lines;
			Color tint = plate.AllianceTint;
			float spacing = Mathf.Max(0.0f, style.LineSpacing * scale);

			while (binding.Rows.Count < visibleRows)
			{
				Label row = new Label { pickingMode = PickingMode.Ignore };
				row.AddToClassList(LINE_CLASS);
				binding.Plate.Add(row);
				binding.Rows.Add(row);
			}

			/* Two indices, because a row the player has turned off takes no element: `i` walks the
			 * plate's rows and `drawn` walks the elements, so a hidden guild row closes up rather
			 * than leaving a gap where it used to be. */
			int drawn = 0;
			for (int i = 0; i < lines.Count; ++i)
			{
				NameplateLine line = lines[i];
				if (!ClientNameplateSettings.IsRowVisible(line.Slot, showGuild, showTitles))
				{
					continue;
				}

				Label row = binding.Rows[drawn];
				float rowFontSize = Mathf.Max(1.0f, fontSize * (line.Scale > 0.0f ? line.Scale : 1.0f));

				row.style.display = DisplayStyle.Flex;
				row.text = UITKRichText.ToUITK(line.Text);
				row.style.color = line.ResolveColor(in style, tint);
				row.style.fontSize = rowFontSize;
				row.style.height = rowFontSize * Mathf.Max(0.5f, style.LineHeight);
				row.style.marginTop = drawn == 0 ? 0.0f : spacing;

				/* Weight follows the SLOT, not the position. A plate whose name row has been
				 * cleared — an interactable that is only a type, a status-only marker — must not
				 * promote whatever fell to the top into a name it is not. */
				row.EnableInClassList(PRIMARY_LINE_CLASS, NameplateSlots.UsesNameColor(line.Slot));
				++drawn;
			}

			for (int i = drawn; i < binding.Rows.Count; ++i)
			{
				binding.Rows[i].text = string.Empty;
				binding.Rows[i].style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Shows or hides a plate's anchor, writing the style only when the state actually changes.
		/// </summary>
		private static void SetDisplayed(PlateBinding binding, bool displayed)
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
		/// Applies the draw budget, then reorders visible plates back-to-front.
		/// </summary>
		/// <remarks>
		/// UI Toolkit paints in hierarchy order and has no depth buffer, so without this a distant
		/// plate could sit on top of one right in front of the player.
		/// <see cref="Nameplate.Priority"/> outranks distance, so a plate forced to the front is
		/// also the last one the budget drops. The existing order is checked before anything is
		/// moved — reordering dirties the hierarchy, and a stationary camera is already correct.
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

			int budget = MaxVisiblePlates > 0 ? Mathf.Min(MaxVisiblePlates, count) : count;
			int firstDrawn = count - budget;

			for (int i = 0; i < count; ++i)
			{
				PlateBinding binding = sortScratch[i];
				bool visible = i >= firstDrawn;
				SetDisplayed(binding, visible);
				if (visible)
				{
					binding.RenderIndex = i - firstDrawn;
				}
			}

			if (IsHierarchyOutOfOrder())
			{
				container.Sort(RenderIndexComparison);
			}
		}

		/// <summary>
		/// Whether the container's children are already in render-index order.
		/// </summary>
		/// <remarks>
		/// One walk with no allocation. Hidden plates are skipped rather than counted: they hold a
		/// render index of -1 and their position among the children does not matter.
		/// </remarks>
		private bool IsHierarchyOutOfOrder()
		{
			int previous = int.MinValue;
			int childCount = container.childCount;

			for (int i = 0; i < childCount; ++i)
			{
				if (!(container[i].userData is PlateBinding binding) || !binding.Displayed)
				{
					continue;
				}
				if (binding.RenderIndex < previous)
				{
					return true;
				}
				previous = binding.RenderIndex;
			}
			return false;
		}

		/// <summary>Back-to-front: lower priority first, then farther first.</summary>
		private static int CompareBackToFront(PlateBinding a, PlateBinding b)
		{
			if (a.Order != b.Order)
			{
				return a.Order < b.Order ? -1 : 1;
			}
			if (a.Distance > b.Distance)
			{
				return -1;
			}
			if (a.Distance < b.Distance)
			{
				return 1;
			}
			return 0;
		}

		/// <summary>Hierarchy order by the render index assigned in the budget pass.</summary>
		private static int CompareRenderIndex(VisualElement a, VisualElement b)
		{
			int left = a.userData is PlateBinding first ? first.RenderIndex : -1;
			int right = b.userData is PlateBinding second ? second.RenderIndex : -1;
			return left.CompareTo(right);
		}
	}
}
