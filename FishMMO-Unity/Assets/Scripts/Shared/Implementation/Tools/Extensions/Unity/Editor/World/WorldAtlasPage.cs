#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// FishMMO Dashboard → World → World Atlas: every scene laid out on a real 3D globe of the
	/// planet or moon it is on, with layers, time zones, daylight, routed teleporter links, a
	/// library of unplaced scenes, problems and the selected scene's settings.
	/// </summary>
	public class WorldAtlasPage : VisualElement
	{
		private const double SpinSecondsPerDay = 20.0;

		/// <summary>The globe, for render probes.</summary>
		public GlobeView Globe => globe;

		/// <summary>A scene to select when the page next opens (from the scene settings inspector).</summary>
		public static string PendingSelection;

		private enum Mode
		{
			Select,
			Connect,
		}

		private readonly AtlasModel model = new AtlasModel();
		private WorldBody body;
		private WorldAtlasLayer layer;
		private WorldAtlasScene selected;
		private Mode mode = Mode.Select;
		private SceneTeleporterCacheEntry pendingTeleporter;
		private double hours;
		private bool spinning;
		private double lastTick;
		private int dirtyStamp = -1;
		private bool dragging;
		private List<AtlasProblem> problems = new List<AtlasProblem>();
		private readonly List<AtlasProblem> routeProblems = new List<AtlasProblem>();

		private readonly GlobeView globe;
		private readonly VisualElement bodyChips;
		private readonly VisualElement layerTabs;
		private readonly VisualElement problemList;
		private readonly VisualElement library;
		private readonly VisualElement inspectorHost;
		private readonly Label status;
		private readonly Label modeLabel;
		private InspectorElement inspector;
		private IVisualElementScheduledItem ticker;

		public WorldAtlasPage()
		{
			style.flexGrow = 1f;
			style.flexDirection = FlexDirection.Column;
			focusable = true;

			// ── Toolbars ──
			var bar = new Toolbar();
			bodyChips = new VisualElement();
			bodyChips.style.flexDirection = FlexDirection.Row;
			bar.Add(bodyChips);
			bar.Add(new ToolbarSpacer());
			layerTabs = new VisualElement();
			layerTabs.style.flexDirection = FlexDirection.Row;
			bar.Add(layerTabs);
			bar.Add(new ToolbarButton(AddLayer) { text = "+ Layer", tooltip = "Adds a layer to this body (or to the atlas's default layers)." });
			bar.Add(new ToolbarSpacer { flex = true });
			bar.Add(new ToolbarButton(() => { model.Reload(); SyncScenes(); }) { text = "Sync scenes", tooltip = "Adds an atlas entry for every world scene that has none and refreshes scene sizes from the world scene details cache." });
			var more = new ToolbarMenu { text = "More" };
			more.menu.AppendAction("Render preview of the selected scene", _ => RenderPreviews(true), _ => selected != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
			more.menu.AppendAction("Render previews of every scene", _ => RenderPreviews(false));
			more.menu.AppendSeparator();
			more.menu.AppendAction("Copy climate, biome map and client cap from the scenes", _ => CopySceneSettings());
			more.menu.AppendAction("Grow the body until nothing overlaps…", _ => GrowUntilClear(), _ => body != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
			more.menu.AppendSeparator();
			more.menu.AppendAction("Select the World Atlas asset", _ => Selection.activeObject = model.Atlas);
			more.menu.AppendAction("Validate (log every problem)", _ => AtlasModel.ValidateFromDashboard());
			bar.Add(more);
			Add(bar);

			var tools = new Toolbar();
			var selectButton = new ToolbarButton(() => SetMode(Mode.Select)) { text = "Select (V)" };
			var connectButton = new ToolbarButton(() => SetMode(Mode.Connect)) { text = "Connect (C)", tooltip = "Click a scene and pick its teleporter, then click the destination scene and pick where it arrives." };
			tools.Add(selectButton);
			tools.Add(connectButton);
			modeLabel = new Label();
			modeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
			modeLabel.style.marginLeft = 4f;
			modeLabel.style.marginRight = 8f;
			tools.Add(modeLabel);
			Toggle(tools, "Lock scenes", false, v => { globe.LockScenes = v; });
			Toggle(tools, "Snap", true, v => { globe.Snap = v; });
			Toggle(tools, "Grid", true, v => { globe.ShowGrid = v; globe.Refresh(); });
			Toggle(tools, "Time zones", true, v => { globe.ShowTimeZones = v; globe.Refresh(); });
			Toggle(tools, "Reference", true, v => { globe.ShowReference = v; globe.Refresh(); });
			Toggle(tools, "Daylight", true, v => { globe.ShowDaylight = v; globe.Refresh(); });
			Toggle(tools, "Spin", false, v => { spinning = v; });
			tools.Add(new ToolbarSpacer { flex = true });
			tools.Add(new ToolbarButton(() => { hours = NowHours(); RefreshGlobe(); }) { text = "Now", tooltip = "World time right now, from this machine's clock." });
			Toggle(tools, "Cut scene", false, v =>
			{
				globe.CutMode = v;
				globe.tooltip = v
					? "Drag a rectangle on the globe to cut a new scene from it."
					: string.Empty;
			});
			tools.Add(new ToolbarButton(RemoveOrphans)
			{
				text = "Clear orphans",
				tooltip = "Deletes atlas entries whose scene file no longer exists. They still draw on the globe and still block a rectangle from being cut there.",
			});
			tools.Add(new ToolbarButton(BakeSurface)
			{
				text = "Bake surface",
				tooltip = "Generates this body's terrain from its seed and draws it on the globe. Build output: the folder is gitignored and a client build removes it again.",
			});
			tools.Add(new ToolbarButton(() => globe.Frame()) { text = "Frame scenes (F)" });
			tools.Add(new ToolbarButton(() => globe.ResetView()) { text = "Reset view (Home)" });
			Add(tools);

			var columns = new VisualElement();
			columns.style.flexDirection = FlexDirection.Row;
			columns.style.flexGrow = 1f;
			Add(columns);

			// ── Left: problems, library, legend ──
			var left = new ScrollView();
			left.style.width = 260f;
			left.style.minWidth = 200f;
			left.style.paddingLeft = 4f;
			left.style.borderRightWidth = 1f;
			left.style.borderRightColor = new Color(0f, 0f, 0f, 0.3f);
			problemList = Section(left, "Problems");
			library = Section(left, "Unplaced scenes");
			VisualElement legend = Section(left, "Legend");
			foreach (string line in new[]
			{
				"gold ◂┈▸ teleporters both ways, gate to gate",
				"gold ●┈▸ one way: teleporter to where it lands",
				"violet ┈ the same, to another layer",
				"◉  links to another body or an unplaced scene",
				"⚠  teleporters without a valid destination",
				"red outline: overlap or too big",
				"▲  the scene's north (+Z)",
				"Drag the globe to turn it; Alt- or middle-drag turns it over a scene.",
				"Drag a scene to move it. R turns it 90°, Shift+R 15°.",
				"Scroll to zoom, double-click to centre, arrows to turn.",
			})
			{
				Line(legend, line, 0.8f);
			}
			columns.Add(left);

			// ── Centre: the globe ──
			var centre = new VisualElement();
			centre.style.flexGrow = 1f;
			centre.style.flexBasis = 0f;
			globe = new GlobeView();
			globe.TimeLabel = TimeLabelOf;
			globe.SceneClicked += OnSceneClicked;
			globe.RectangleDrawn += OnRectangleDrawn;
			globe.EmptyClicked += OnEmptyClicked;
			globe.SceneMoveStarted += e => { dragging = true; Undo.RecordObject(e, "Move scene"); globe.Routes.Clear(); };
			globe.SceneMoving += OnSceneMoving;
			globe.SceneMoveEnded += OnSceneMoveEnded;
			globe.ContextRequested += OnContext;
			centre.Add(globe);
			status = new Label();
			status.style.paddingLeft = 6f;
			status.style.paddingTop = 2f;
			status.style.paddingBottom = 2f;
			status.style.whiteSpace = WhiteSpace.Normal;
			status.RegisterCallback<ClickEvent>(_ => { if (problems.Count > 0) JumpTo(problems[0]); });
			centre.Add(status);
			columns.Add(centre);

			// ── Right: inspector ──
			var right = new ScrollView();
			right.style.width = 360f;
			right.style.minWidth = 260f;
			right.style.paddingLeft = 6f;
			right.style.borderLeftWidth = 1f;
			right.style.borderLeftColor = new Color(0f, 0f, 0f, 0.3f);
			inspectorHost = new VisualElement();
			right.Add(inspectorHost);
			columns.Add(right);

			RegisterCallback<KeyDownEvent>(OnKeyDown);
			RegisterCallback<AttachToPanelEvent>(_ => { lastTick = EditorApplication.timeSinceStartup; ticker = schedule.Execute(Tick).Every(50); });
			RegisterCallback<DetachFromPanelEvent>(_ => { ticker?.Pause(); ticker = null; inspector?.RemoveFromHierarchy(); inspector = null; });

			hours = NowHours();
			model.Reload();
			body = model.System != null ? model.System.HomeWorld : null;
			if (model.Atlas != null)
			{
				SyncScenes(false);
			}
			if (!string.IsNullOrEmpty(PendingSelection) && model.BySceneName.TryGetValue(PendingSelection, out WorldAtlasScene pending))
			{
				selected = pending;
				body = model.BodyOf(pending) ?? body;
			}
			PendingSelection = null;
			Rebuild();
			if (selected != null && selected.Placed)
			{
				globe.TurnTo(selected.Latitude, selected.Longitude, false);
				globe.Zoom = 3f;
			}
			else
			{
				globe.TurnTo(20.0, 0.0, false);
			}
			SetMode(Mode.Select);
		}

		// ── Small UI helpers ──

		private static ToolbarToggle Toggle(VisualElement parent, string text, bool value, Action<bool> changed)
		{
			var toggle = new ToolbarToggle { text = text, value = value };
			toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
			parent.Add(toggle);
			return toggle;
		}

		private static VisualElement Section(VisualElement parent, string title)
		{
			var header = new Label(title);
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginTop = 8f;
			header.style.marginBottom = 2f;
			parent.Add(header);
			var content = new VisualElement();
			parent.Add(content);
			return content;
		}

		private static Label Line(VisualElement parent, string text, float opacity = 1f, Color? colour = null)
		{
			var label = new Label(text);
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.opacity = opacity;
			if (colour.HasValue)
			{
				label.style.color = colour.Value;
			}
			parent.Add(label);
			return label;
		}

		private static void Pair(VisualElement parent, string key, string value)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			var k = new Label(key);
			k.style.width = 130f;
			k.style.opacity = 0.75f;
			var v = new Label(value);
			v.style.flexGrow = 1f;
			v.style.whiteSpace = WhiteSpace.Normal;
			row.Add(k);
			row.Add(v);
			parent.Add(row);
		}

		private double NowHours()
		{
			long epoch = model.System != null ? model.System.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
			return WorldClock.WorldSecondsFromUtc(DateTime.UtcNow, epoch) / 3600.0;
		}

		// ── Ticking and change detection ──

		private void Tick()
		{
			double now = EditorApplication.timeSinceStartup;
			double dt = Math.Min(0.25, now - lastTick);
			lastTick = now;
			if (spinning && model.System != null && body != null)
			{
				double day = CelestialMath.SolarDayHours(model.System, body);
				if (!double.IsInfinity(day))
				{
					double before = CelestialMath.RotationAngle(model.System, body, hours);
					hours += dt * day / SpinSecondsPerDay;
					double after = CelestialMath.RotationAngle(model.System, body, hours);
					globe.Orientation = globe.Orientation * Quaternion.AngleAxis((float)((after - before) * CelestialMath.Rad2Deg), Vector3.up);
					RefreshGlobe(false);
				}
			}
			if (!dragging)
			{
				int stamp = DirtyStamp();
				if (stamp != dirtyStamp)
				{
					dirtyStamp = stamp;
					model.Reload();
					Rebuild();
				}
			}
		}

		private int DirtyStamp()
		{
			unchecked
			{
				int stamp = model.Entries.Count;
				foreach (WorldAtlasScene entry in model.Entries)
				{
					stamp = stamp * 31 + (entry != null ? EditorUtility.GetDirtyCount(entry) : 3);
				}
				foreach (WorldBody b in model.Bodies())
				{
					stamp = stamp * 31 + EditorUtility.GetDirtyCount(b);
				}
				if (model.Atlas != null)
				{
					stamp = stamp * 31 + EditorUtility.GetDirtyCount(model.Atlas);
				}
				if (model.System != null)
				{
					stamp = stamp * 31 + EditorUtility.GetDirtyCount(model.System);
				}
				if (model.Teleporters != null)
				{
					stamp = stamp * 31 + EditorUtility.GetDirtyCount(model.Teleporters);
				}
				return stamp;
			}
		}

		// ── Rebuilding ──

		private void Rebuild()
		{
			List<WorldBody> bodies = model.Bodies();
			if (body == null || !bodies.Contains(body))
			{
				body = model.System != null && model.System.HomeWorld != null ? model.System.HomeWorld : bodies.Count > 0 ? bodies[0] : null;
			}
			List<WorldAtlasLayer> layers = model.LayersOf(body);
			if (layer != null && !layers.Contains(layer))
			{
				layer = null;
			}
			if (selected != null && !model.Entries.Contains(selected))
			{
				selected = null;
			}
			foreach (WorldBody b in bodies)
			{
				model.SettleRadius(b);
			}
			RebuildChips(bodies, layers);
			RefreshGlobe();
			RebuildRoutes();
			problems = model.Problems();
			problems.AddRange(routeProblems);
			RebuildProblems();
			RebuildLibrary();
			RebuildInspector();
			dirtyStamp = DirtyStamp();
		}

		private void RebuildChips(List<WorldBody> bodies, List<WorldAtlasLayer> layers)
		{
			bodyChips.Clear();
			if (bodies.Count == 0)
			{
				bodyChips.Add(new Label("No planets yet — press New on the Solar System page.") { style = { unityTextAlign = TextAnchor.MiddleLeft } });
			}
			foreach (WorldBody b in bodies)
			{
				WorldBody captured = b;
				var chip = new ToolbarToggle { text = (b.Kind == WorldBodyKind.Moon ? "◐ " : "● ") + b.ResolvedName, value = b == body };
				/* The selected scene is cleared with the body, so the inspector shows the body that
				 * was just clicked. Without this the right-hand panel kept showing the scene
				 * selected on the PREVIOUS world — a scene that is not on the globe being looked
				 * at, and whose latitude and layer belong to somewhere else entirely. */
				chip.RegisterValueChangedCallback(_ =>
				{
					body = captured;
					layer = null;
					selected = null;
					Rebuild();
					globe.Frame();
				});
				bodyChips.Add(chip);
			}
			layerTabs.Clear();
			var all = new ToolbarToggle { text = "All layers", value = layer == null };
			all.RegisterValueChangedCallback(_ => { layer = null; Rebuild(); });
			layerTabs.Add(all);
			foreach (WorldAtlasLayer l in layers)
			{
				WorldAtlasLayer captured = l;
				var tab = new ToolbarToggle { text = l.ResolvedName, value = l == layer };
				tab.RegisterValueChangedCallback(_ => { layer = captured; Rebuild(); });
				layerTabs.Add(tab);
			}
		}

		/// <summary>The scene's boundaries in metres: the rectangle its atlas footprint was sized from.</summary>
		private Rect SceneWorldRect(string sceneName)
		{
			WorldSceneDetails details = null;
			model.Details?.Scenes?.TryGetValue(sceneName, out details);
			return details != null ? MapBoundsResolver.FromSceneBoundaries(details) : Rect.zero;
		}

		private bool Visible(WorldAtlasScene entry) => layer == null || entry.Layer == layer;

		/// <summary>
		/// A rectangle was drawn on the globe: name it, check it, and cut a scene out of it.
		/// </summary>
		/// <remarks>
		/// Everything that can refuse does so before anything is written. A scene is a folder, a
		/// scene file, a terrain asset per tile and an atlas entry; half of that on disk because a
		/// name was rejected afterwards is worse than not starting.
		/// </remarks>
		private void OnRectangleDrawn(double latitude, double longitude, Vector2 sizeKm)
		{
			if (body == null)
			{
				EditorUtility.DisplayDialog("Cut scene", "Choose a body to cut the scene from first.", "OK");
				return;
			}

			WorldAtlasLayer layer = model.Atlas != null ? model.Atlas.SurfaceLayerOf(body) : null;
			var request = new SceneGenerationRequest
			{
				Body = body,
				Layer = layer,
				Latitude = latitude,
				Longitude = longitude,
				SizeKm = sizeKm,
			};

			List<WorldAtlasScene> hits = SceneGeneration.Collisions(request, model.Entries, AtlasModel.RadiusOf(body));
			if (hits.Count > 0)
			{
				var names = new List<string>();
				foreach (WorldAtlasScene hit in hits)
				{
					names.Add(hit.SceneName);
				}
				EditorUtility.DisplayDialog("Cut scene",
					$"That rectangle lands on {(hits.Count == 1 ? "an existing scene" : "existing scenes")}: {string.Join(", ", names)}.\n\n" +
					"Scenes in the same layer cannot overlap. Draw somewhere else, or put this one in another layer.",
					"OK");
				return;
			}

			TerrainTilePlan plan = SceneGeneration.PlanTiles(sizeKm);
			string sceneName = SceneNamePrompt.Ask(
				$"{sizeKm.x:0.##} x {sizeKm.y:0.##} km on {body.ResolvedName}, at {latitude:0.##}°, {longitude:0.##}°.\n{plan}",
				SuggestSceneName(), out bool fineDetail,
				SceneNamePrompt.NamerFor(body, layer, latitude, longitude));
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			request.SceneName = sceneName;
			request.FineDetail = fineDetail;

			SceneGenerationResult result;
			try
			{
				EditorUtility.DisplayProgressBar("Cut scene", $"Generating {sceneName} ({plan.TotalTiles} tile(s))...", 0.5f);
				result = SceneGenerator.Generate(request);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (!result.Success)
			{
				EditorUtility.DisplayDialog("Cut scene", result.Problem, "OK");
				return;
			}

			Debug.Log($"[World atlas] Generated '{sceneName}': {plan}, {result.ReliefMetres:0} m of relief, " +
				$"standing at {result.BaseAltitudeMetres:0} m above sea level (its ground floor is {result.GroundAltitudeMetres:0} m).\n  "
				+ string.Join("\n  ", result.Wrote));
			string sea = result.HasWater
				? $"\nThe sea is at y = {result.SeaLevelY:0} m, which is where this body's water line falls here."
				: "\nNo sea: this scene's lowest ground is above the body's water line.";
			EditorUtility.DisplayDialog("Cut scene",
				$"\"{sceneName}\" is ready.\n\n{plan}\n" +
				$"{result.ReliefMetres:0} m of relief, standing at {result.BaseAltitudeMetres:0} m above sea level.{sea}\n\n" +
				$"Wrote {result.Wrote.Count} file(s) under {SceneGenerator.WorldFolder(body)}.\n\n" +
				"Rebuild the world scene details cache to bring it into the game.",
				"OK");

			model.Reload();
			RefreshGlobe();
		}

		/// <summary>A name nothing is using yet, so the prompt opens on something workable.</summary>
		private string SuggestSceneName()
		{
			List<string> existing = SceneGenerator.ExistingSceneNames();
			string stem = body != null ? body.ResolvedName : "New Scene";
			for (int n = 1; n < 1000; n++)
			{
				string candidate = $"{stem} {n}";
				if (SceneGeneration.NameProblem(candidate, existing) == null)
				{
					return candidate;
				}
			}
			return string.Empty;
		}

		/// <summary>
		/// Deletes a scene and everything that belongs only to it, after asking.
		/// </summary>
		/// <remarks>
		/// Lists exactly what will go before it goes. The terrain data lives in a folder beside the
		/// scene and is useless without it; the atlas entry is what puts the rectangle on the globe
		/// and blocks anything else being cut there. Leaving either behind is the state that had to
		/// be cleaned up by hand.
		/// </remarks>
		private void DeleteScene(WorldAtlasScene entry)
		{
			if (entry == null)
			{
				return;
			}
			string sceneName = entry.SceneName;
			model.ScenePaths.TryGetValue(sceneName ?? string.Empty, out string scenePath);
			WorldBody owner = model.BodyOf(entry);
			string terrainFolder = owner != null ? SceneGenerator.TerrainFolder(owner, sceneName) : null;
			string entryPath = AssetDatabase.GetAssetPath(entry);

			var doomed = new List<string>();
			if (!string.IsNullOrEmpty(scenePath))
			{
				doomed.Add(scenePath);
			}
			if (!string.IsNullOrEmpty(terrainFolder) && AssetDatabase.IsValidFolder(terrainFolder))
			{
				doomed.Add(terrainFolder + " (terrain data)");
			}
			if (!string.IsNullOrEmpty(entryPath))
			{
				doomed.Add(entryPath + " (atlas entry)");
			}
			if (doomed.Count == 0)
			{
				EditorUtility.DisplayDialog("Delete scene", $"Nothing left to delete for \"{sceneName}\".", "OK");
				return;
			}

			if (!EditorUtility.DisplayDialog("Delete scene",
				$"Delete \"{sceneName}\" and everything that belongs to it?\n\n" +
				string.Join("\n", doomed) +
				"\n\nThis cannot be undone.",
				"Delete", "Cancel"))
			{
				return;
			}

			// Closed first: the asset database will not delete a scene that is open, and reports
			// that by returning false rather than by failing loudly.
			if (!string.IsNullOrEmpty(scenePath))
			{
				UnityEngine.SceneManagement.Scene open = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
				if (open.IsValid() && open.isLoaded)
				{
					// Closed without saving: it is about to be deleted, so offering to save it first
					// would only ask somebody to write a file and then watch it go.
					UnityEditor.SceneManagement.EditorSceneManager.CloseScene(open, true);
				}
			}

			foreach (string path in new[] { scenePath, terrainFolder, entryPath })
			{
				if (!string.IsNullOrEmpty(path) && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
				{
					AssetDatabase.DeleteAsset(path);
				}
			}

			// The world folder, if this was the last scene in it.
			if (owner != null)
			{
				string worldFolder = SceneGenerator.WorldFolder(owner);
				if (System.IO.Directory.Exists(worldFolder) && System.IO.Directory.GetFileSystemEntries(worldFolder).Length == 0)
				{
					AssetDatabase.DeleteAsset(worldFolder);
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			WorldAtlasScene.EditorLookup.Invalidate();

			Debug.Log($"[World atlas] Deleted '{sceneName}':\n  " + string.Join("\n  ", doomed) +
				"\n  Rebuild the world scene details cache so the game stops expecting it.");

			selected = null;
			model.Reload();
			Rebuild();
		}

		/// <summary>Deletes atlas entries whose scene is gone, after asking.</summary>
		private void RemoveOrphans()
		{
			List<string> orphans = WorldEditorAssets.OrphanedAtlasScenes();
			if (orphans.Count == 0)
			{
				EditorUtility.DisplayDialog("Clear orphans", "Every atlas entry has a scene.", "OK");
				return;
			}
			if (!EditorUtility.DisplayDialog("Clear orphans",
				$"Delete {orphans.Count} atlas entr{(orphans.Count == 1 ? "y" : "ies")} whose scene no longer exists?\n\n" +
				string.Join("\n", orphans) + "\n\nThis cannot be undone.",
				"Delete", "Cancel"))
			{
				return;
			}

			List<string> removed = WorldEditorAssets.RemoveOrphanedAtlasScenes();
			Debug.Log($"[World atlas] Removed {removed.Count} orphaned atlas entr{(removed.Count == 1 ? "y" : "ies")}: {string.Join(", ", removed)}.");
			selected = null;
			model.Reload();
			Rebuild();
		}

		/// <summary>
		/// Bakes the body being looked at and puts it straight on the globe.
		/// </summary>
		/// <remarks>
		/// One body, not all of them: this is the button somebody presses while deciding where a
		/// scene goes, and waiting for nineteen worlds to answer a question about one is the kind
		/// of tool people stop using. Core → Maintenance bakes the whole system for a build.
		/// </remarks>
		private void BakeSurface()
		{
			if (body == null)
			{
				EditorUtility.DisplayDialog("Bake surface", "There is no body selected to bake.", "OK");
				return;
			}
			try
			{
				EditorUtility.DisplayProgressBar("Bake surface", $"Generating {body.ResolvedName}…", 0.5f);
				PlanetSurfaceBaker.Bake(body);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
			RefreshGlobe();
		}

		private void RefreshGlobe(bool full = true)
		{
			globe.RadiusKm = AtlasModel.RadiusOf(body);
			globe.BodyColour = body != null ? Color.Lerp(body.Tint, new Color(0.2f, 0.3f, 0.35f, 1f), 0.45f) : new Color(0.3f, 0.45f, 0.6f, 1f);
			/* The baked surface if this body has one, and a plain ball if not. Null is the normal
			 * state in a fresh clone — the bake is build output — so this must never be a fault.
			 * With it, scene rectangles sit on real coastlines, which is what makes choosing where
			 * a scene goes a decision rather than a guess. */
			globe.Surface = PlanetSurfaceBaker.Baked(body);
			// The body's own tilt, so the tropics and polar circles are this world's, not Earth's.
			globe.AxialTiltDegrees = body != null ? body.AxialTiltDegrees : 23.4f;
			globe.Selected = selected;
			globe.SnapKm = 0.25;
			globe.SunDirection = SunDirection();
			if (full)
			{
				globe.Scenes.Clear();
				HashSet<string> external = ScenesWithExternalLinks();
				foreach (WorldAtlasScene entry in model.On(body, null))
				{
					Color tint = entry.Layer != null ? entry.Layer.Tint : new Color(0.55f, 0.75f, 0.95f, 1f);
					AtlasImage image = AtlasPreviews.Find(entry.SceneName, model.Details);
					globe.Scenes.Add(new GlobeScene
					{
						Entry = entry,
						Footprint = AtlasFootprint.Of(entry),
						Preview = image.Texture,
						Image = image,
						WorldRect = SceneWorldRect(entry.SceneName),
						Tint = tint,
						Ghost = !Visible(entry),
						Warnings = model.BrokenTeleporters(entry.SceneName),
						HasExternalLinks = external.Contains(entry.SceneName),
						Caption = entry.SceneName,
					});
				}
				MarkProblems();
			}
			globe.Refresh();
		}

		private void MarkProblems()
		{
			var bad = new HashSet<WorldAtlasScene>();
			foreach ((WorldAtlasScene a, WorldAtlasScene b) in model.Overlaps(body, null))
			{
				bad.Add(a);
				bad.Add(b);
			}
			// An entry whose scene file is gone: it still draws, still blocks a cut, and is still
			// counted as somewhere a character can be. Red, like any other invalid rectangle.
			var orphans = new HashSet<string>(WorldEditorAssets.OrphanedAtlasScenes(), StringComparer.Ordinal);

			double radius = AtlasModel.RadiusOf(body);
			foreach (GlobeScene scene in globe.Scenes)
			{
				scene.Problem = bad.Contains(scene.Entry)
					|| !AtlasGeometry.Fits(scene.Footprint, radius)
					|| scene.Entry != null && orphans.Contains(scene.Entry.SceneName ?? string.Empty);
			}
		}

		private Vector3d? SunDirection()
		{
			if (model.System == null || body == null || model.System.PrimaryStar == null)
			{
				return null;
			}
			CelestialMath.SunEquatorial(model.System, body, hours, out double ra, out double dec);
			double lon = CelestialMath.WrapPi(ra - CelestialMath.RotationAngle(model.System, body, hours)) * CelestialMath.Rad2Deg;
			return AtlasGeometry.ToUnit(dec * CelestialMath.Rad2Deg, lon);
		}

		private string TimeLabelOf(WorldAtlasScene entry)
		{
			if (entry.TimeMode == SceneTimeMode.Fixed)
			{
				return $"{SceneTime.Format(entry.FixedTimeOfDay01)} (fixed) · {entry.TimeZone:+0;-0} h";
			}
			if (model.System == null || body == null)
			{
				return $"{entry.TimeZone:+0;-0} h";
			}
			double t = CelestialMath.LocalTime01(model.System, body, hours, entry.TimeLongitude);
			bool day = CelestialMath.IsDaylight(model.System, body, hours, entry.EffectiveSunLatitude, entry.TimeLongitude);
			return $"{SceneTime.Format(t)} {(day ? "☀" : "☾")} · {entry.TimeZone:+0;-0} h";
		}

		/// <summary>Scenes with a link the globe cannot draw: to an unplaced scene or another body.</summary>
		private HashSet<string> ScenesWithExternalLinks()
		{
			var result = new HashSet<string>(StringComparer.Ordinal);
			foreach (AtlasScenePair pair in model.Pairs())
			{
				if (!pair.A.Placed || !pair.B.Placed || model.BodyOf(pair.A) != model.BodyOf(pair.B))
				{
					result.Add(pair.A.SceneName);
					result.Add(pair.B.SceneName);
				}
			}
			return result;
		}

		/// <summary>One connection between two scenes: where it starts and ends, and whether it goes both ways.</summary>
		private sealed class LinkLine
		{
			public WorldAtlasScene From;
			public WorldAtlasScene To;
			public Vector3d Start;
			public Vector3d End;
			public bool TwoWay;
			public bool Highlight;
		}

		/// <summary>
		/// Draws every connection on this body as an arc lifted off the globe: one per pair of
		/// scenes, from teleporter to teleporter when it goes both ways (arrowheads at both ends),
		/// or from the teleporter to where it lands when it only goes one way. Links within a
		/// layer are gold, links between layers violet.
		/// </summary>
		private void RebuildRoutes()
		{
			globe.Routes.Clear();
			routeProblems.Clear();
			if (body == null)
			{
				return;
			}
			double radius = AtlasModel.RadiusOf(body);
			WorldAtlasLayer surface = model.Atlas != null ? model.Atlas.SurfaceLayerOf(body) : null;
			WorldAtlasLayer LayerOf(WorldAtlasScene e) => e.Layer != null ? e.Layer : surface;

			foreach (AtlasScenePair pair in model.Pairs())
			{
				if (!pair.A.Placed || !pair.B.Placed || model.BodyOf(pair.A) != body || model.BodyOf(pair.B) != body)
				{
					continue;
				}
				AtlasLink there = pair.AToB.Count > 0 ? pair.AToB[0] : null;
				AtlasLink back = pair.BToA.Count > 0 ? pair.BToA[0] : null;
				var line = new LinkLine { TwoWay = there != null && back != null, Highlight = selected != null && (pair.A == selected || pair.B == selected) };
				if (line.TwoWay)
				{
					// Gate to gate.
					line.From = pair.A;
					line.To = pair.B;
					line.Start = ScenePointToUnit(pair.A, there.Teleporter.Position, radius);
					line.End = ScenePointToUnit(pair.B, back.Teleporter.Position, radius);
				}
				else
				{
					AtlasLink only = there ?? back;
					line.From = there != null ? pair.A : pair.B;
					line.To = there != null ? pair.B : pair.A;
					line.Start = ScenePointToUnit(line.From, only.Teleporter.Position, radius);
					line.End = ScenePointToUnit(line.To, only.Destination.Position, radius);
				}
				WorldAtlasLayer fromLayer = LayerOf(line.From), toLayer = LayerOf(line.To);
				bool shown = layer == null || fromLayer == layer || toLayer == layer;
				globe.Routes.Add(Arc(line, fromLayer, toLayer, !shown));
			}
			globe.Refresh();
		}

		/// <summary>A teleporter's position (world metres) as a point on the globe, kept inside its scene.</summary>
		private Vector3d ScenePointToUnit(WorldAtlasScene entry, Vector3 position, double radius)
		{
			Rect rect = SceneWorldRect(entry.SceneName);
			AtlasFootprint footprint = AtlasFootprint.Of(entry);
			double x = 0.0, z = 0.0;
			if (rect.width > 0f && rect.height > 0f)
			{
				x = Mathf.Clamp((position.x - rect.center.x) / 1000f, -footprint.SizeKm.x * 0.49f, footprint.SizeKm.x * 0.49f);
				z = Mathf.Clamp((position.z - rect.center.y) / 1000f, -footprint.SizeKm.y * 0.49f, footprint.SizeKm.y * 0.49f);
			}
			return AtlasGeometry.SceneToUnit(footprint, x, z, radius);
		}

		/// <summary>A connection drawn as a dotted arc hopping over the surface.</summary>
		private GlobeRoute Arc(LinkLine line, WorldAtlasLayer fromLayer, WorldAtlasLayer toLayer, bool ghost)
		{
			bool crossLayer = fromLayer != toLayer;
			Color a = fromLayer != null ? fromLayer.Tint : new Color(0.55f, 0.75f, 0.95f, 1f);
			Color b = toLayer != null ? toLayer.Tint : a;
			Color colour = crossLayer
				? Color.Lerp(Color.Lerp(a, b, 0.5f), new Color(0.85f, 0.6f, 1f, 1f), 0.5f)
				: Color.Lerp(a, new Color(1f, 0.82f, 0.35f, 1f), 0.7f);
			colour.a = 1f;
			var route = new GlobeRoute
			{
				TwoWay = line.TwoWay,
				Clean = true,
				CrossLayer = true,
				Ghost = ghost,
				Highlight = line.Highlight,
				MarkStart = !line.TwoWay,
				Colour = colour,
			};
			Vector3d p0 = line.Start.Normalized, p1 = line.End.Normalized;
			double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(p0, p1))));
			double sin = Math.Sin(angle);
			// Lift the arc off the ground in proportion to its length so it reads as a hop, not a road.
			double lift = Math.Min(0.25, Math.Max(angle * 0.35, 0.004));
			const int steps = 64;
			for (int i = 0; i <= steps; i++)
			{
				double t = i / (double)steps;
				Vector3d p = sin < 1e-6 ? p0 : (p0 * (Math.Sin((1 - t) * angle) / sin)) + (p1 * (Math.Sin(t * angle) / sin));
				route.Points.Add(p.Normalized * (1.0 + lift * Math.Sin(Math.PI * t)));
			}
			return route;
		}

		private void RebuildProblems()
		{
			problemList.Clear();
			int errors = 0, warnings = 0;
			foreach (AtlasProblem problem in problems)
			{
				if (problem.Severity == AtlasProblemSeverity.Error) errors++;
				else if (problem.Severity == AtlasProblemSeverity.Warning) warnings++;
			}
			Line(problemList, problems.Count == 0 ? "None." : $"{errors} error(s), {warnings} warning(s), {problems.Count - errors - warnings} note(s). Click one to go to it.", 0.8f);
			int shown = 0;
			foreach (AtlasProblem problem in problems)
			{
				if (shown++ >= 200)
				{
					Line(problemList, $"…and {problems.Count - 200} more (Validate logs them all).", 0.7f);
					break;
				}
				string icon = problem.Severity == AtlasProblemSeverity.Error ? "⛔" : problem.Severity == AtlasProblemSeverity.Warning ? "⚠" : "ℹ";
				Color colour = problem.Severity == AtlasProblemSeverity.Error ? new Color(1f, 0.55f, 0.5f, 1f) : problem.Severity == AtlasProblemSeverity.Warning ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.75f, 0.82f, 0.95f, 1f);
				Label label = Line(problemList, $"{icon} {problem.Message}", 1f, colour);
				label.style.marginBottom = 2f;
				AtlasProblem captured = problem;
				label.RegisterCallback<ClickEvent>(_ => JumpTo(captured));
			}
			status.text = problems.Count == 0
				? "No problems."
				: $"{errors} error(s), {warnings} warning(s). First: {problems[0].Message}  (click to go there)";
		}

		private void RebuildLibrary()
		{
			library.Clear();
			List<WorldAtlasScene> unplaced = model.Unplaced();
			if (model.Atlas == null)
			{
				Line(library, "No atlas yet. Press New on the Solar System page.", 0.8f);
				return;
			}
			if (unplaced.Count == 0)
			{
				Line(library, "Every scene is placed.", 0.8f);
				return;
			}
			Line(library, layer != null
				? $"Place drops a scene into {layer.ResolvedName} where you are looking, at the nearest free spot. Pick All layers to keep each scene's own layer."
				: "Place drops a scene where you are looking, at the nearest free spot, in its own layer. Pick a layer tab to place into that layer.", 0.7f);
			foreach (WorldAtlasScene entry in unplaced)
			{
				var row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				row.style.alignItems = Align.Center;
				var name = new Label($"{entry.SceneName}  ({entry.SizeKm.x:0.#}×{entry.SizeKm.y:0.#} km{(entry.Layer != null ? ", " + entry.Layer.ResolvedName : string.Empty)})");
				name.style.flexGrow = 1f;
				name.style.whiteSpace = WhiteSpace.Normal;
				WorldAtlasScene captured = entry;
				row.Add(name);
				row.Add(new Button(() => Place(captured, null)) { text = "Place" });
				library.Add(row);
			}
		}

		// ── Inspector ──

		private void RebuildInspector()
		{
			inspector?.RemoveFromHierarchy();
			inspector = null;
			inspectorHost.Clear();
			if (selected != null)
			{
				BuildSceneInspector(selected);
			}
			else
			{
				BuildBodyInspector();
			}
		}

		private void BuildSceneInspector(WorldAtlasScene entry)
		{
			var title = new Label(entry.SceneName);
			title.style.fontSize = 15;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginTop = 6f;
			inspectorHost.Add(title);

			var actions = new VisualElement();
			actions.style.flexDirection = FlexDirection.Row;
			actions.style.flexWrap = Wrap.Wrap;
			model.ScenePaths.TryGetValue(entry.SceneName, out string path);
			actions.Add(new Button(() => AtlasTeleporters.OpenAndSelect(path, null, false)) { text = "Open scene" });
			actions.Add(new Button(() => AtlasTeleporters.OpenAndSelect(path, null, true)) { text = "Open additively" });
			actions.Add(new Button(() => { if (path != null) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)); }) { text = "Ping scene" });
			actions.Add(new Button(() => Turn(90f)) { text = "Turn 90° (R)" });
			actions.Add(new Button(() => { if (entry.Placed) RemoveFromMap(entry); else Place(entry, null); }) { text = entry.Placed ? "Remove from map" : "Place" });

			/* Deleting a scene is three separate things — the scene file, the terrain data beside
			 * it and the atlas entry — and doing one without the others is what leaves a rectangle
			 * on the globe for ground that no longer exists. There was no way to do it from here at
			 * all, so it had to be done by hand in the Project window, where the terrain folder and
			 * the entry are easy to miss. */
			var delete = new Button(() => DeleteScene(entry)) { text = "Delete scene…" };
			delete.style.marginLeft = 8f;
			actions.Add(delete);
			inspectorHost.Add(actions);

			WorldBody b = model.BodyOf(entry);
			List<WorldAtlasLayer> bodyLayers = model.LayersOf(b);
			if (bodyLayers.Count > 0)
			{
				var choices = new List<WorldAtlasLayer>(bodyLayers);
				WorldAtlasLayer current = entry.Layer != null && choices.Contains(entry.Layer) ? entry.Layer : null;
				if (current == null)
				{
					// Not one of this body's layers (or none): show it so the picker can fix it.
					choices.Insert(0, entry.Layer);
					current = entry.Layer;
				}
				var picker = new PopupField<WorldAtlasLayer>("Layer", choices, current,
					l => l != null ? l.ResolvedName : "(none)",
					l => l != null ? (bodyLayers.Contains(l) ? l.ResolvedName : l.ResolvedName + " (not on this body)") : "(none)");
				picker.tooltip = "The layer this scene sits in. Scenes only overlap-check and route against their own layer.";
				picker.RegisterValueChangedCallback(evt =>
				{
					if (evt.newValue != null && evt.newValue != entry.Layer)
					{
						// Rebuilding tears this picker down; do it after the event.
						WorldAtlasLayer target = evt.newValue;
						picker.schedule.Execute(() => MoveToLayer(entry, target));
					}
				});
				inspectorHost.Add(picker);
			}
			var info = new VisualElement();
			Pair(info, "Local time", TimeLabelOf(entry));
			Pair(info, "Position", $"{entry.Latitude:0.###}°, {entry.Longitude:0.###}°, heading {entry.HeadingDegrees:0.#}°");
			Pair(info, "Size", $"{entry.SizeKm.x:0.##} × {entry.SizeKm.y:0.##} km");
			Pair(info, "Body", b != null ? $"{b.ResolvedName}, radius {AtlasModel.RadiusOf(b):0.#} km" : "(none)");
			Pair(info, "Weather", $"{entry.EffectiveWeather}{(b != null && !b.HasWeather ? " — no air here, so none" : string.Empty)}");
			inspectorHost.Add(info);

			// Teleporters leaving this scene.
			var header = new Label("Teleporters");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginTop = 8f;
			inspectorHost.Add(header);
			List<AtlasLink> links = model.LinksFrom(entry.SceneName);
			if (links.Count == 0)
			{
				Line(inspectorHost, model.Teleporters == null ? "The teleporter cache is missing." : "None. Add SceneTeleporter objects in the scene, then rebuild the teleporter cache.", 0.8f);
			}
			foreach (AtlasLink link in links)
			{
				var row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				row.style.alignItems = Align.Center;
				string target = link.Unassigned ? "⚠ no destination" : link.Missing ? "⚠ destination missing" : $"→ {link.Destination.SceneName} / {link.Destination.DisplayName}";
				var label = new Label($"{link.Teleporter.TeleporterName}  {target}");
				label.style.flexGrow = 1f;
				label.style.whiteSpace = WhiteSpace.Normal;
				if (link.Unassigned || link.Missing)
				{
					label.style.color = new Color(1f, 0.6f, 0.5f, 1f);
				}
				row.Add(label);
				AtlasLink captured = link;
				row.Add(new Button(() => ShowDestinationMenu(captured.Teleporter, null)) { text = "Connect…" });
				if (!link.Unassigned)
				{
					row.Add(new Button(() => DoConnect(captured.Teleporter, null)) { text = "Clear" });
				}
				row.Add(new Button(() => AtlasTeleporters.OpenAndSelect(captured.Teleporter.ScenePath, new[] { captured.Teleporter.TeleporterName }, false)) { text = "Open" });
				inspectorHost.Add(row);
			}

			var settings = new Label("Settings");
			settings.style.unityFontStyleAndWeight = FontStyle.Bold;
			settings.style.marginTop = 8f;
			inspectorHost.Add(settings);
			inspector = new InspectorElement(entry);
			inspectorHost.Add(inspector);
		}

		private void BuildBodyInspector()
		{
			if (body == null)
			{
				Line(inspectorHost, "Pick a body. Planets and moons come from the Solar System page.", 0.8f);
				return;
			}
			var title = new Label(body.ResolvedName);
			title.style.fontSize = 15;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginTop = 6f;
			inspectorHost.Add(title);
			Line(inspectorHost, "Click a scene to edit it. With nothing selected, these are the body's globe settings.", 0.75f);
			var info = new VisualElement();
			Pair(info, "Radius now", $"{AtlasModel.RadiusOf(body):0.##} km ({body.RadiusMode})");
			Pair(info, "Auto would use", $"{Mathf.Max(body.MinimumRadiusKm, model.RequiredRadius(body)):0.##} km (minimum {body.MinimumRadiusKm:0.#} km, at most {body.MaxCoveragePercent:0}% of each layer covered, no scene wider than {AtlasGeometry.MaxSceneArcDegrees:0}°)");
			Pair(info, "Scenes placed", model.On(body, null).Count.ToString());
			if (model.System != null)
			{
				Pair(info, "Solar day", $"{CelestialMath.SolarDayHours(model.System, body):0.###} h");
			}
			inspectorHost.Add(info);
			var grow = new Button(GrowUntilClear) { text = "Grow until nothing overlaps…" };
			grow.style.alignSelf = Align.FlexStart;
			inspectorHost.Add(grow);
			inspector = new InspectorElement(body);
			inspectorHost.Add(inspector);
		}

		// ── Editing ──

		private void SyncScenes(bool rebuild = true)
		{
			if (model.Atlas == null)
			{
				model.Atlas = WorldEditorAssets.EnsureAtlas(model.System);
			}
			int created = WorldEditorAssets.SyncAtlasScenes(model.Atlas);
			if (created > 0 || rebuild)
			{
				model.Reload();
				if (rebuild)
				{
					Rebuild();
				}
			}
		}

		private void Place(WorldAtlasScene entry, Vector3d? at)
		{
			if (body == null)
			{
				EditorUtility.DisplayDialog("No body", "There is no planet to place scenes on. Press New on the Solar System page first.", "OK");
				return;
			}
			Vector3d wanted = at ?? (globe.Pick(globe.contentRect.center, out Vector3d middle) ? middle : new Vector3d(0, 0, 1));
			AtlasGeometry.FromUnit(wanted, out double lat, out double lon);
			List<WorldAtlasLayer> layers = model.LayersOf(body);
			// The layer tab being viewed is where the scene goes; with All layers, it keeps its own.
			WorldAtlasLayer targetLayer = layer != null && layers.Contains(layer) ? layer
				: entry.Layer != null && layers.Contains(entry.Layer) ? entry.Layer
				: model.Atlas != null ? model.Atlas.SurfaceLayerOf(body) : null;

			Undo.RecordObject(entry, "Place scene");
			entry.Body = body;
			entry.Layer = targetLayer;

			var others = new List<AtlasFootprint>();
			foreach (WorldAtlasScene other in model.On(body, targetLayer))
			{
				if (other != entry)
				{
					others.Add(AtlasFootprint.Of(other));
				}
			}
			var footprint = new AtlasFootprint(lat, lon, entry.HeadingDegrees, entry.SizeKm);
			double radius = Math.Max(AtlasModel.RadiusOf(body), model.RequiredRadius(body));
			if (!AtlasGeometry.FindFreeSpot(footprint, others, radius, out lat, out lon))
			{
				Debug.LogWarning($"[World Atlas] No free spot for {entry.SceneName} on {body.ResolvedName}; placed where you are looking. Grow the body to make room.");
			}
			entry.Latitude = Math.Max(-85.0, Math.Min(85.0, lat));
			entry.Longitude = AtlasGeometry.WrapLongitude(lon);
			entry.Placed = true;
			EditorUtility.SetDirty(entry);
			selected = entry;
			model.SettleRadius(body);
			AssetDatabase.SaveAssets();
			Rebuild();
		}

		private void RemoveFromMap(WorldAtlasScene entry)
		{
			Undo.RecordObject(entry, "Remove scene from map");
			entry.Placed = false;
			EditorUtility.SetDirty(entry);
			model.SettleRadius(model.BodyOf(entry));
			AssetDatabase.SaveAssets();
			Rebuild();
		}

		/// <summary>Moves a scene to another layer of its body, finding it a free spot there if it would overlap.</summary>
		private void MoveToLayer(WorldAtlasScene entry, WorldAtlasLayer target)
		{
			if (entry == null || target == null || entry.Layer == target)
			{
				return;
			}
			WorldBody b = model.BodyOf(entry);
			Undo.RecordObject(entry, "Move scene to layer");
			entry.Layer = target;
			if (entry.Placed && b != null)
			{
				var others = new List<AtlasFootprint>();
				foreach (WorldAtlasScene other in model.On(b, target))
				{
					if (other != entry)
					{
						others.Add(AtlasFootprint.Of(other));
					}
				}
				AtlasFootprint footprint = AtlasFootprint.Of(entry);
				double radius = Math.Max(AtlasModel.RadiusOf(b), model.RequiredRadius(b));
				bool clear = true;
				foreach (AtlasFootprint other in others)
				{
					if (AtlasGeometry.Overlaps(footprint, other, radius))
					{
						clear = false;
						break;
					}
				}
				if (!clear)
				{
					if (AtlasGeometry.FindFreeSpot(footprint, others, radius, out double lat, out double lon))
					{
						entry.Latitude = Math.Max(-85.0, Math.Min(85.0, lat));
						entry.Longitude = AtlasGeometry.WrapLongitude(lon);
					}
					else
					{
						Debug.LogWarning($"[World Atlas] {entry.SceneName} overlaps scenes in {target.ResolvedName} and there is no free spot; grow {b.ResolvedName} to make room.");
					}
				}
				model.SettleRadius(b);
			}
			EditorUtility.SetDirty(entry);
			AssetDatabase.SaveAssets();
			if (layer != null)
			{
				// Follow the scene, so it does not vanish into a ghost.
				layer = target;
			}
			selected = entry;
			Rebuild();
		}

		private void Turn(float degrees)
		{
			if (selected == null)
			{
				return;
			}
			Undo.RecordObject(selected, "Turn scene");
			selected.HeadingDegrees = Mathf.Repeat(Mathf.Round((selected.HeadingDegrees + degrees) * 1000f) / 1000f, 360f);
			EditorUtility.SetDirty(selected);
			Rebuild();
		}

		private void OnSceneMoving(WorldAtlasScene entry, double lat, double lon)
		{
			entry.Latitude = lat;
			entry.Longitude = lon;
			foreach (GlobeScene scene in globe.Scenes)
			{
				if (scene.Entry == entry)
				{
					scene.Footprint = AtlasFootprint.Of(entry);
				}
			}
			MarkProblems();
			globe.Refresh();
		}

		private void OnSceneMoveEnded(WorldAtlasScene entry)
		{
			dragging = false;
			EditorUtility.SetDirty(entry);
			model.SettleRadius(model.BodyOf(entry));
			AssetDatabase.SaveAssets();
			Rebuild();
		}

		private void GrowUntilClear()
		{
			if (body == null)
			{
				return;
			}
			if (model.Overlaps(body, null).Count == 0)
			{
				EditorUtility.DisplayDialog("Nothing overlaps", $"No scenes overlap on {body.ResolvedName}.", "OK");
				return;
			}
			if (!EditorUtility.DisplayDialog("Grow the body", $"Raise {body.ResolvedName}'s minimum radius until no scenes overlap? Scenes keep their latitude and longitude, so they spread apart.", "Grow", "Cancel"))
			{
				return;
			}
			Undo.RecordObject(body, "Grow body");
			bool manual = body.RadiusMode == AtlasRadiusMode.Manual;
			for (int i = 0; i < 400 && model.Overlaps(body, null).Count > 0; i++)
			{
				if (manual)
				{
					body.ManualRadiusKm *= 1.03f;
				}
				else
				{
					body.MinimumRadiusKm = Mathf.Max(body.MinimumRadiusKm, body.CurrentRadiusKm) * 1.03f;
					body.CurrentRadiusKm = Mathf.Max(body.CurrentRadiusKm, body.MinimumRadiusKm);
				}
			}
			EditorUtility.SetDirty(body);
			AssetDatabase.SaveAssets();
			Rebuild();
		}

		private void AddLayer()
		{
			if (model.Atlas == null)
			{
				return;
			}
			WorldAtlasLayer created = WorldEditorAssets.Create<WorldAtlasLayer>(WorldEditorAssets.LayersFolder, "Layer " + (model.LayersOf(body).Count + 1), l =>
			{
				l.DisplayName = l.name;
				l.SortOrder = model.LayersOf(body).Count;
			});
			if (body != null && body.Layers != null && body.Layers.Count > 0)
			{
				Undo.RecordObject(body, "Add layer");
				body.Layers.Add(created);
				EditorUtility.SetDirty(body);
			}
			else if (body != null && EditorUtility.DisplayDialog("New layer", $"Add {created.name} to every body (the atlas's default layers), or only to {body.ResolvedName}?", "Every body", $"Only {body.ResolvedName}"))
			{
				Undo.RecordObject(model.Atlas, "Add layer");
				model.Atlas.DefaultLayers.Add(created);
				EditorUtility.SetDirty(model.Atlas);
			}
			else if (body != null)
			{
				Undo.RecordObject(body, "Add layer");
				body.Layers.AddRange(model.Atlas.DefaultLayers);
				body.Layers.Add(created);
				EditorUtility.SetDirty(body);
			}
			AssetDatabase.SaveAssets();
			layer = created;
			model.Reload();
			Rebuild();
			Selection.activeObject = created;
		}

		private void RenderPreviews(bool onlySelected)
		{
			var paths = new List<string>();
			if (onlySelected)
			{
				if (selected != null && model.ScenePaths.TryGetValue(selected.SceneName, out string path))
				{
					paths.Add(path);
				}
			}
			else
			{
				paths.AddRange(model.ScenePaths.Values);
			}
			if (paths.Count == 0)
			{
				return;
			}
			if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			int written = AtlasPreviews.Render(paths, model.Details);
			Debug.Log($"[World Atlas] Rendered {written} preview(s) into {AtlasPreviews.Folder}.");
			RefreshGlobe();
		}

		private void CopySceneSettings()
		{
			if (!EditorUtility.DisplayDialog("Copy scene settings", "Copy each scene's climate, biome map and client cap into its atlas entry, where the entry has none? Scenes are only read.", "Copy", "Cancel"))
			{
				return;
			}
			int changed = WorldEditorAssets.CopySceneSettingsIntoAtlas();
			Debug.Log($"[World Atlas] Copied scene settings into {changed} atlas entr{(changed == 1 ? "y" : "ies")}.");
			model.Reload();
			Rebuild();
		}

		// ── Connections ──

		private void SetMode(Mode value)
		{
			mode = value;
			pendingTeleporter = null;
			UpdateModeLabel();
		}

		private void UpdateModeLabel()
		{
			modeLabel.text = mode == Mode.Select
				? "Select: click a scene to edit it."
				: pendingTeleporter == null
					? "Connect: click the scene the teleporter is in."
					: $"Connect '{pendingTeleporter.TeleporterName}' ({pendingTeleporter.SceneName}): click the destination scene. Esc cancels.";
		}

		private void OnSceneClicked(WorldAtlasScene entry)
		{
			if (mode == Mode.Connect)
			{
				if (pendingTeleporter == null)
				{
					ShowTeleporterMenu(entry);
				}
				else
				{
					ShowDestinationMenu(pendingTeleporter, entry.SceneName);
				}
				return;
			}
			if (selected != entry)
			{
				selected = entry;
				RefreshGlobe(false);
				RebuildRoutes();
				RebuildInspector();
			}
		}

		private void OnEmptyClicked()
		{
			if (mode == Mode.Connect)
			{
				return;
			}
			if (selected != null)
			{
				selected = null;
				RefreshGlobe(false);
				RebuildRoutes();
				RebuildInspector();
			}
		}

		private void ShowTeleporterMenu(WorldAtlasScene entry)
		{
			List<AtlasLink> links = model.LinksFrom(entry.SceneName);
			var menu = new GenericMenu();
			if (links.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent($"{entry.SceneName} has no teleporters"));
			}
			foreach (AtlasLink link in links)
			{
				AtlasLink captured = link;
				string state = link.Unassigned ? " (no destination)" : link.Missing ? " (destination missing)" : $" → {link.Destination.SceneName}";
				menu.AddItem(new GUIContent(link.Teleporter.TeleporterName + state), false, () =>
				{
					pendingTeleporter = captured.Teleporter;
					UpdateModeLabel();
				});
			}
			menu.ShowAsContext();
		}

		/// <summary>A menu of destinations: in one scene, or in every scene grouped by scene.</summary>
		private void ShowDestinationMenu(SceneTeleporterCacheEntry teleporter, string sceneName)
		{
			var menu = new GenericMenu();
			List<string> scenes = new List<string>();
			if (sceneName != null)
			{
				scenes.Add(sceneName);
			}
			else if (model.Teleporters != null)
			{
				foreach (TeleporterCacheEntry destination in model.Teleporters.Destinations.Values)
				{
					if (destination != null && !scenes.Contains(destination.SceneName))
					{
						scenes.Add(destination.SceneName);
					}
				}
				scenes.Sort(StringComparer.Ordinal);
			}
			int count = 0;
			foreach (string scene in scenes)
			{
				foreach (TeleporterCacheEntry destination in AtlasTeleporters.DestinationsIn(model.Teleporters, scene))
				{
					TeleporterCacheEntry captured = destination;
					string label = sceneName != null ? destination.DisplayName : $"{scene}/{destination.DisplayName}";
					menu.AddItem(new GUIContent(label), destination.DestinationID == teleporter.DestinationID, () => DoConnect(teleporter, captured));
					count++;
				}
			}
			if (count == 0)
			{
				menu.AddDisabledItem(new GUIContent(sceneName != null ? $"{sceneName} has no teleporter destinations" : "No destinations in the teleporter cache"));
			}
			menu.AddSeparator(string.Empty);
			menu.AddItem(new GUIContent("Cancel"), false, () => SetMode(mode));
			menu.ShowAsContext();
		}

		private void DoConnect(SceneTeleporterCacheEntry teleporter, TeleporterCacheEntry destination)
		{
			if (!AtlasTeleporters.Connect(teleporter, destination, out string error))
			{
				if (error != "Cancelled.")
				{
					EditorUtility.DisplayDialog("Could not connect", error, "OK");
				}
			}
			pendingTeleporter = null;
			UpdateModeLabel();
			model.Reload();
			Rebuild();
		}

		// ── Context menu and keys ──

		private void OnContext(WorldAtlasScene entry, Vector2 position)
		{
			var menu = new GenericMenu();
			if (entry != null)
			{
				model.ScenePaths.TryGetValue(entry.SceneName, out string path);
				WorldAtlasScene captured = entry;
				menu.AddItem(new GUIContent("Open scene in editor"), false, () => AtlasTeleporters.OpenAndSelect(path, null, false));
				menu.AddItem(new GUIContent("Open scene additively"), false, () => AtlasTeleporters.OpenAndSelect(path, null, true));
				menu.AddItem(new GUIContent("Ping scene asset"), false, () => { if (path != null) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)); });
				menu.AddItem(new GUIContent("Select atlas entry"), false, () => Selection.activeObject = captured);
				menu.AddItem(new GUIContent("Render preview"), false, () => { selected = captured; RenderPreviews(true); });
				menu.AddSeparator(string.Empty);
				menu.AddItem(new GUIContent("Turn 90°"), false, () => { selected = captured; Turn(90f); });
				menu.AddItem(new GUIContent("Turn 15°"), false, () => { selected = captured; Turn(15f); });
				menu.AddItem(new GUIContent("Remove from map"), false, () => RemoveFromMap(captured));
				foreach (WorldAtlasLayer l in model.LayersOf(model.BodyOf(captured)))
				{
					WorldAtlasLayer target = l;
					if (target == captured.Layer)
					{
						menu.AddDisabledItem(new GUIContent($"Move to layer/{target.ResolvedName}"), true);
					}
					else
					{
						menu.AddItem(new GUIContent($"Move to layer/{target.ResolvedName}"), false, () => MoveToLayer(captured, target));
					}
				}
				List<AtlasLink> links = model.LinksFrom(entry.SceneName);
				var broken = new List<string>();
				foreach (AtlasLink link in links)
				{
					AtlasLink l = link;
					string name = link.Teleporter.TeleporterName.Replace("/", "∕");
					menu.AddItem(new GUIContent($"Teleporters/{name}/Connect…"), false, () => ShowDestinationMenu(l.Teleporter, null));
					if (!link.Unassigned)
					{
						menu.AddItem(new GUIContent($"Teleporters/{name}/Disconnect"), false, () => DoConnect(l.Teleporter, null));
					}
					menu.AddItem(new GUIContent($"Teleporters/{name}/Open in scene"), false, () => AtlasTeleporters.OpenAndSelect(l.Teleporter.ScenePath, new[] { l.Teleporter.TeleporterName }, false));
					if (link.Unassigned || link.Missing)
					{
						broken.Add(link.Teleporter.TeleporterName);
					}
				}
				if (broken.Count > 0)
				{
					menu.AddItem(new GUIContent($"Open scene with the {broken.Count} broken teleporter(s) selected"), false, () => AtlasTeleporters.OpenAndSelect(path, broken, false));
				}
			}
			else
			{
				globe.Pick(position, out Vector3d point);
				bool onGlobe = globe.Pick(position, out _);
				foreach (WorldAtlasScene unplaced in model.Unplaced())
				{
					WorldAtlasScene captured = unplaced;
					if (onGlobe)
					{
						menu.AddItem(new GUIContent($"Place here/{unplaced.SceneName}"), false, () => Place(captured, point));
					}
				}
				menu.AddItem(new GUIContent("Frame scenes"), false, () => globe.Frame());
				menu.AddItem(new GUIContent("Reset view"), false, () => globe.ResetView());
			}
			menu.ShowAsContext();
		}

		private void OnKeyDown(KeyDownEvent evt)
		{
			switch (evt.keyCode)
			{
				case KeyCode.R:
					Turn(evt.shiftKey ? 15f : 90f);
					break;
				case KeyCode.F:
					globe.Frame();
					break;
				case KeyCode.V:
					SetMode(Mode.Select);
					break;
				case KeyCode.C:
					SetMode(Mode.Connect);
					break;
				case KeyCode.Escape:
					SetMode(mode);
					break;
				case KeyCode.Delete:
				case KeyCode.Backspace:
					if (selected != null && selected.Placed)
					{
						RemoveFromMap(selected);
					}
					break;
				default:
					return;
			}
			evt.StopPropagation();
		}

		/// <summary>Goes to a problem: its body and layer, turns the globe to it, and blinks it.</summary>
		private void JumpTo(AtlasProblem problem)
		{
			if (problem.Scene == null)
			{
				if (!string.IsNullOrEmpty(problem.ScenePath))
				{
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(problem.ScenePath));
				}
				return;
			}
			WorldAtlasScene entry = problem.Scene;
			selected = entry;
			WorldBody target = model.BodyOf(entry);
			if (target != null)
			{
				body = target;
			}
			if (layer != null && entry.Layer != layer)
			{
				layer = entry.Layer;
			}
			Rebuild();
			if (entry.Placed)
			{
				globe.TurnTo(entry.Latitude, entry.Longitude);
				Blink(entry, 6);
			}
			else
			{
				Selection.activeObject = entry;
			}
		}

		private void Blink(WorldAtlasScene entry, int times)
		{
			int remaining = times;
			schedule.Execute(() =>
			{
				foreach (GlobeScene scene in globe.Scenes)
				{
					if (scene.Entry == entry)
					{
						scene.Problem = remaining % 2 == 0;
					}
				}
				globe.Refresh();
				remaining--;
				if (remaining == 0)
				{
					MarkProblems();
					globe.Refresh();
				}
			}).Every(180).Until(() => remaining <= 0);
		}
	}
}
#endif
