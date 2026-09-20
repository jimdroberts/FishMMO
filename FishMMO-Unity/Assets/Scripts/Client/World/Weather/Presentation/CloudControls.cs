using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The cloud controls for the sim panels: every band of the sky, and everything the renderer
	/// does with them, on live controls.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The clouds are a 3D noise field cut into bands of altitude, and almost every question about
	/// how a sky looks — how low the deck sits, how deep it is, how much of it there is, how fast
	/// it runs, what colour its shaded side takes — is a number on a band. Putting those numbers on
	/// sliders is the difference between tuning a sky in seconds and tuning it by editing an asset
	/// and pressing play.
	/// </para>
	/// <para>
	/// Everything here writes straight to the render profile's own settings, so what a designer
	/// arrives at is what the game ships with — there is no separate preview state to reconcile.
	/// Bands can be added and removed, and the whole stack reset to the built-in sky.
	/// </para>
	/// </remarks>
	public static class CloudControls
	{
		private static readonly Color Text = new Color(0.9f, 0.92f, 0.95f, 1f);
		private static readonly Color Muted = new Color(0.62f, 0.67f, 0.74f, 1f);
		private static readonly Color Accent = new Color(0.45f, 0.7f, 0.95f, 1f);

		/// <summary>
		/// Builds the whole cloud section into <paramref name="parent"/>.
		/// </summary>
		/// <param name="profile">The render profile the controls write to.</param>
		/// <param name="rebuild">Called when a band is added or removed and the section must be redrawn.</param>
		public static void Build(VisualElement parent, WeatherRenderProfile profile, Action rebuild)
		{
			if (parent == null || profile == null || profile.Clouds == null)
			{
				return;
			}
			VolumetricCloudSettings clouds = profile.Clouds;

			parent.Add(Heading("Clouds"));
			parent.Add(Note("The sky is 3D noise cut into bands of altitude. Every band is the same machinery with different numbers."));

			// ── What the sky is actually doing ──
			// First, because a slider says what was asked for and these say what came of it.
			parent.Add(Subheading("Statistics"));
			parent.Add(Readout(label => label.text = CloudStats.Sky(SkySystem.Instance, clouds), true));

			// ── What the renderer does ──
			parent.Add(Subheading("Rendering"));
			parent.Add(Slider("Density", 0.05f, 4f, clouds.Density, v => clouds.Density = v,
				"How thick and dark the cloud is overall. Multiplies every band."));
			parent.Add(Slider("Cut at clear", 0.2f, 0.9f, clouds.CoverageCutClear, v => clouds.CoverageCutClear = v,
				"Where the noise is cut when the forecast says no cloud. The field runs about 0.33 to 0.76; outside that window the sky is all or nothing."));
			parent.Add(Slider("Cut at overcast", 0.1f, 0.8f, clouds.CoverageCutFull, v => clouds.CoverageCutFull = v,
				"Where the noise is cut under a full overcast."));
			parent.Add(Slider("Cut bend", 0f, 0.4f, clouds.CoverageBend, v => clouds.CoverageBend = v,
				"How much further the cut drops over the last of the range: what closes the final gaps into an overcast."));
			parent.Add(Slider("Edge softness", 0.02f, 0.9f, clouds.EdgeSoftness, v => clouds.EdgeSoftness = v,
				"How far a cloud takes to thin to nothing. Clouds are fog, so this wants to be generous."));
			parent.Add(Slider("Haze distance (m)", 2000f, 120000f, clouds.HazeDistance, v => clouds.HazeDistance = v,
				"Metres over which distance turns a cloud into haze. Smaller greys out the far sky sooner."));
			parent.Add(Slider("Front tilt", 0f, 1f, clouds.CoverageTilt, v => clouds.CoverageTilt = v,
				"How much the sky tilts toward the weather that is coming, so cloud arrives from upwind instead of appearing everywhere at once. Zero gives the whole sky one cover. Only applies where the drifting weather field decides the weather."));
			parent.Add(Slider("Shape warp", 0f, 0.5f, clouds.ShapeWarp, v => clouds.ShapeWarp = v,
				"How far the shape lookup is bent so the noise does not visibly repeat. Too much and the warp field's own structure is printed on the clouds as combed, hairy edges; zero brings the tiling back."));
			parent.Add(Slider("Detail out to (m)", 0f, 20000f, clouds.DetailFadeStart, v => clouds.DetailFadeStart = v,
				"How far the fine detail is used in full. The detail is finer than the march's own steps, so past a point it is undersampled and turns into speckle."));
			parent.Add(Slider("Detail fades over (m)", 100f, 40000f, clouds.DetailFadeRange, v => clouds.DetailFadeRange = v,
				"Metres over which that detail thins away to nothing."));
			parent.Add(Slider("Draw distance (m)", 5000f, 200000f, clouds.MaxDistance, v => clouds.MaxDistance = v,
				"How far the clouds are marched."));

			// ── Light ──
			parent.Add(Subheading("Light"));
			parent.Add(Slider("Sun steps", 1f, 12f, clouds.LightSteps, v => clouds.LightSteps = Mathf.RoundToInt(v),
				"Steps taken toward the sun when lighting a point. More gives deeper, truer shading and costs more."));
			parent.Add(Slider("Powder", 0f, 1f, clouds.Powder, v => clouds.Powder = v,
				"The dark edge a sunlit cloud shows before it brightens."));
			parent.Add(Slider("Forward scatter", 0f, 0.95f, clouds.ForwardScatter, v => clouds.ForwardScatter = v,
				"How much light keeps going forward: the glow around the sun through thin cloud."));
			parent.Add(Slider("Ambient", 0f, 4f, clouds.Ambient, v => clouds.Ambient = v,
				"How much of the sky's own light fills the shaded side."));
			parent.Add(Slider("Shaded tint strength", 0f, 1f, clouds.ShadedTintStrength, v => clouds.ShadedTintStrength = v,
				"How much of a band's shaded colour it takes. 0 leaves undersides grey."));

			// ── Wind ──
			// Speed and direction are separate here because they are separate in the renderer: the
			// speed only carries cloud along, while the direction also sets the axis the noise is
			// drawn out on. Turning the wind up moves the sky faster and changes nothing else.
			parent.Add(Subheading("Wind"));
			parent.Add(Toggle("Set the wind here", SkySystem.CloudWindOverride, v => SkySystem.CloudWindOverride = v,
				"Off, the clouds take the wind from the weather. On, they take it from these two."));
			parent.Add(Slider("Direction (°)", 0f, 360f, SkySystem.CloudWindHeadingOverride,
				v => SkySystem.CloudWindHeadingOverride = v,
				"Which way the wind blows toward, clockwise from north. 0 is north, 90 east."));
			parent.Add(Slider("Speed (m/s)", 0f, 40f, SkySystem.CloudWindSpeedOverride,
				v => SkySystem.CloudWindSpeedOverride = v,
				"How fast the clouds are carried along. Each band scales this by its own wind scale."));

			// ── Shadows and shafts ──
			parent.Add(Subheading("Shadows and shafts"));
			parent.Add(Toggle("Cloud shadows on the world", SkySystem.DrawCloudShadows, v => SkySystem.DrawCloudShadows = v,
				"The shadow the clouds throw on the ground, marched from the same field and put on the sun as a cookie."));
			parent.Add(Slider("Shadow strength", 0f, 1f, clouds.ShadowStrength, v => clouds.ShadowStrength = v,
				"How dark the ground goes under a cloud."));
			parent.Add(Slider("Shadow area (m)", 200f, 12000f, clouds.ShadowAreaMeters, v => clouds.ShadowAreaMeters = v,
				"How much ground the shadow cookie covers. Smaller is sharper and reaches less far."));
			// The shadow as the light is handed it. What is on the ground should be this picture laid
			// out around the camera: if this looks like the clouds and the ground does not, the fault
			// is in how the light reads it; if this is speckle, the fault is in how it is marched.
			// Three rounds of fixes to the shadow were made without ever looking at it.
			var cookieView = new Image { scaleMode = ScaleMode.ScaleToFit };
			cookieView.style.width = 256;
			cookieView.style.height = 256;
			cookieView.style.marginTop = 4;
			cookieView.style.marginBottom = 4;
			cookieView.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
			cookieView.tooltip = "The cloud shadow cookie: white is full sun, dark is under cloud, the camera is at the middle and the square is the shadow area across. Up is the sun light's own up axis.";
			cookieView.schedule.Execute(() =>
			{
				SkySystem sky = SkySystem.Instance;
				Texture cookie = sky != null ? sky.CloudShadowCookie : null;
				if (cookieView.image != cookie)
				{
					cookieView.image = cookie;
				}
				cookieView.MarkDirtyRepaint();
			}).Every(250);
			parent.Add(cookieView);
			parent.Add(Toggle("God rays", SkySystem.DrawGodRays, v => SkySystem.DrawGodRays = v,
				"Shafts of light through broken cloud, and around a body during an eclipse."));

			// ── The bands ──
			parent.Add(Subheading("Bands"));
			List<CloudLayer> bands = clouds.Layers;
			if (bands == null)
			{
				bands = clouds.Layers = CloudLayerDefaults.Sky();
			}
			for (int i = 0; i < bands.Count; i++)
			{
				BuildBand(parent, clouds, bands, i, rebuild);
			}

			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.Add(Button("Add band", () =>
			{
				bands.Add(new CloudLayer { Name = $"Band {bands.Count + 1}", Bottom = 2000f, Top = 2600f });
				rebuild?.Invoke();
			}));
			row.Add(Button("Reset to the built-in sky", () =>
			{
				clouds.Layers = CloudLayerDefaults.Sky();
				rebuild?.Invoke();
			}));
			parent.Add(row);
		}

		/// <summary>One band's controls, in a foldout so a sky of six does not fill the screen.</summary>
		private static void BuildBand(VisualElement parent, VolumetricCloudSettings clouds, List<CloudLayer> bands, int index, Action rebuild)
		{
			CloudLayer band = bands[index];
			if (band == null)
			{
				return;
			}
			var foldout = new Foldout { text = $"{index}  {band.Name}   {band.Bottom:0} – {band.Top:0} m", value = index == 1 };
			foldout.style.marginTop = 4;
			foldout.style.marginBottom = 2;
			Label title = foldout.Q<Label>();
			if (title != null)
			{
				title.style.color = Accent;
			}

			var name = new TextField("Name") { value = band.Name };
			name.labelElement.style.color = Text;
			name.RegisterValueChangedCallback(evt =>
			{
				band.Name = evt.newValue;
				foldout.text = $"{index}  {band.Name}   {band.Bottom:0} – {band.Top:0} m";
			});
			foldout.Add(name);

			// What this band is doing right now, above its own sliders: whether it is drawing at
			// all, what cut its coverage puts on the noise, how deep it is and how fast it runs.
			foldout.Add(Readout(label => label.text = CloudStats.Band(SkySystem.Instance, clouds, index), false));

			foldout.Add(Note("Where the band sits. A band whose floor is near the ground is the weather's own: fog fills it, not the cloud forecast."));
			foldout.Add(Slider("Bottom (m)", 0f, 14000f, band.Bottom, v =>
			{
				band.Bottom = v;
				band.Top = Mathf.Max(band.Top, v + 50f);
				foldout.text = $"{index}  {band.Name}   {band.Bottom:0} – {band.Top:0} m";
			}, "Height of the band's floor. This is the flat base a deck shows."));
			foldout.Add(Slider("Top (m)", 50f, 16000f, band.Top, v =>
			{
				band.Top = Mathf.Max(v, band.Bottom + 50f);
				foldout.text = $"{index}  {band.Name}   {band.Bottom:0} – {band.Top:0} m";
			}, "Height of the band's ceiling. Deeper bands give taller clouds."));

			foldout.Add(Note("How much of it there is."));
			foldout.Add(Slider("Coverage scale", -1f, 2f, band.CoverageScale, v => band.CoverageScale = v,
				"How much of the weather's cloud cover reaches this band. Negative thins it as the sky fills, which is what cirrus does."));
			foldout.Add(Slider("Coverage bias", -1f, 1f, band.CoverageBias, v => band.CoverageBias = v,
				"Added on top. Use it for a band that is always a little there."));
			foldout.Add(Slider("Coverage onset", 0f, 1f, band.CoverageOnset, v => band.CoverageOnset = v,
				"Cover below which this band stays empty. A sheet only arrives once a front has closed the sky over."));
			foldout.Add(Slider("Density", 0f, 4f, band.Density, v => band.Density = v,
				"How thick the cloud in this band is."));

			foldout.Add(Note("What it looks like."));
			foldout.Add(Slider("Noise scale (m)", 100f, 30000f, band.NoiseScale, v => band.NoiseScale = v,
				"Metres one tile of the shape noise covers: how big the cloud masses are."));
			foldout.Add(Slider("Detail scale (m)", 20f, 3000f, band.DetailScale, v => band.DetailScale = v,
				"Metres one tile of the detail noise covers: the size of the wisps eaten out of the edges."));
			foldout.Add(Slider("Detail strength", 0f, 1f, band.DetailStrength, v => band.DetailStrength = v,
				"How hard the detail eats in."));
			foldout.Add(Slider("Stretch along wind", 1f, 20f, band.Stretch, v => band.Stretch = v,
				"Draws the noise out downwind. 1 is round; high gives the streaks of cirrus."));
			foldout.Add(Slider("Base softness", 0.02f, 0.9f, band.BaseSoftness, v => band.BaseSoftness = v,
				"How much of the band's floor the cloud fills before it thins. Low is a flat-based deck."));
			foldout.Add(Slider("Top softness", 0.05f, 1f, band.TopSoftness, v => band.TopSoftness = v,
				"How much of the band's ceiling the cloud thins over. High gives ragged tops."));
			foldout.Add(Slider("Vertical scale", 0.25f, 8f, band.VerticalScale, v => band.VerticalScale = v,
				"How much the shape changes with height, as a multiple of the band's thickness. Low makes flat slabs, because the noise barely turns over between the floor and the ceiling; around 2 puts lumps and hollows in; high breaks the cloud into layers."));
			foldout.Add(Slider("Convection", 0f, 1f, band.Convection, v => band.Convection = v,
				"How much the height the cloud reaches varies from place to place. A base is flat because condensation happens at one height across a region; a top is lumpy because each rising parcel of air runs out of lift somewhere different. 0 is a level sheet, high is cauliflower."));

			foldout.Add(Note("How it moves, and what the weather does to it."));
			foldout.Add(Slider("Wind scale", 0f, 6f, band.WindScale, v => band.WindScale = v,
				"How much faster than the ground wind this band runs. Higher bands run ahead of lower ones."));
			foldout.Add(Toggle("Carries rain", band.CarriesRain, v => band.CarriesRain = v,
				"Whether precipitation thickens and darkens this band. Rain comes out of the low deck, not the cirrus."));
			foldout.Add(Toggle("Grows storms", band.GrowsStorms, v => band.GrowsStorms = v,
				"Whether a storm cell fills this band where it stands."));

			// Three sliders rather than a colour picker: ColorField lives in the editor assembly, and
			// these controls have to work in a player build of the bed as well.
			foldout.Add(Note("The colour a shaded part of this band tends toward. Ground fog is not the grey of a thunderhead."));
			foldout.Add(Slider("Shaded red", 0f, 1f, band.ShadedTint.r, v =>
			{
				Color c = band.ShadedTint; c.r = v; band.ShadedTint = c;
			}, "Red of the shaded colour."));
			foldout.Add(Slider("Shaded green", 0f, 1f, band.ShadedTint.g, v =>
			{
				Color c = band.ShadedTint; c.g = v; band.ShadedTint = c;
			}, "Green of the shaded colour."));
			foldout.Add(Slider("Shaded blue", 0f, 1f, band.ShadedTint.b, v =>
			{
				Color c = band.ShadedTint; c.b = v; band.ShadedTint = c;
			}, "Blue of the shaded colour."));

			var remove = Button("Remove this band", () =>
			{
				bands.RemoveAt(index);
				rebuild?.Invoke();
			});
			remove.style.marginTop = 4;
			foldout.Add(remove);
			parent.Add(foldout);
		}

		// ── Small pieces ───────────────────────────────────────────────

		private static Label Heading(string text)
		{
			var label = new Label(text);
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.color = Accent;
			label.style.marginTop = 10;
			label.style.fontSize = 13;
			return label;
		}

		private static Label Subheading(string text)
		{
			var label = new Label(text);
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.color = Text;
			label.style.marginTop = 6;
			label.style.fontSize = 11;
			return label;
		}

		/// <summary>
		/// A block of live figures, refreshed four times a second.
		/// </summary>
		/// <remarks>
		/// Four times a second rather than every frame: these are numbers to read, and a readout
		/// that changes on every frame is one nobody can. The whole-sky block also drives the
		/// measurement of how much screen the clouds ended up on, which is why only it polls.
		/// </remarks>
		private static Label Readout(Action<Label> refresh, bool measures)
		{
			var label = new Label();
			label.style.color = Text;
			label.style.fontSize = 10;
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.marginTop = 2;
			label.style.marginBottom = 4;
			label.style.paddingLeft = 4;
			label.style.paddingTop = 3;
			label.style.paddingBottom = 3;
			label.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
			refresh(label);
			label.schedule.Execute(() =>
			{
				if (measures)
				{
					CloudStats.Poll();
				}
				refresh(label);
			}).Every(250);
			return label;
		}

		private static Label Note(string text)
		{
			var label = new Label(text);
			label.style.color = Muted;
			label.style.fontSize = 10;
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.marginBottom = 2;
			return label;
		}

		private static Slider Slider(string text, float low, float high, float value, Action<float> changed, string tooltip)
		{
			var slider = new Slider(text, low, high) { value = Mathf.Clamp(value, low, high), showInputField = true, tooltip = tooltip };
			slider.style.marginTop = 1;
			slider.style.marginBottom = 1;
			slider.labelElement.style.minWidth = 118;
			slider.labelElement.style.color = Text;
			slider.labelElement.style.fontSize = 11;
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private static Toggle Toggle(string text, bool value, Action<bool> changed, string tooltip)
		{
			var toggle = new Toggle(text) { value = value, tooltip = tooltip };
			toggle.labelElement.style.color = Text;
			toggle.labelElement.style.fontSize = 11;
			toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return toggle;
		}

		private static Button Button(string text, Action clicked)
		{
			var button = new Button(clicked) { text = text };
			button.style.fontSize = 11;
			button.style.marginRight = 3;
			return button;
		}
	}
}
