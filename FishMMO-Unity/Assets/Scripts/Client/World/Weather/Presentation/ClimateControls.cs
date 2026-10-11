using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The climate controls for the sim panels: what is added to the air, and what the air then does.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every slider is an ADDITION to the physical environment, centred on zero — never a setting of
	/// it. The air is whatever the world, the season, the hour, the place and the drifting weather
	/// make it; these add a few kelvin of cold, a little damp, a lower pressure, less stable air, more
	/// wind, more pull — and the clouds, the rain, the fog and the storms are then worked out from
	/// that air exactly as they are anywhere else. There is nothing here that says "clouds" or
	/// "rain": asking for weather is asking the air to be different.
	/// </para>
	/// <para>
	/// What the sliders add is runtime, like an admin's <c>/admin weather air</c>. Where the scene
	/// has an atlas entry, what they add can be written into it as the scene's own authored offsets:
	/// a scene authored colder is colder always, and everything that happens in it happens in colder
	/// air.
	/// </para>
	/// </remarks>
	public static class ClimateControls
	{
		/// <summary>
		/// Builds the whole climate section into <paramref name="parent"/>.
		/// </summary>
		/// <param name="get">What is added at runtime now.</param>
		/// <param name="set">Adds these instead.</param>
		/// <param name="settings">The scene's settings, for authoring into its atlas entry. May be null.</param>
		/// <param name="rebuild">Redraws the section, after a reset.</param>
		public static void Build(VisualElement parent, Func<AirOffsets> get, Action<AirOffsets> set, WorldSceneSettings settings, Action rebuild)
		{
			if (parent == null || get == null || set == null)
			{
				return;
			}
			parent.Add(Heading("Climate"));
			parent.Add(Note("Everything here is ADDED to the air, never set: zero is the world as it is. The clouds, the rain, the fog and the storms all follow from the air."));

			// ── What is added ──
			parent.Add(Subheading("Added to the air"));
			AirOffsets now = get();
			parent.Add(Offset("Temperature (K)", -40f, 40f, now.Temperature, v => { AirOffsets o = get(); o.Temperature = v; set(o); },
				"Kelvin added to the air everywhere. Colder air holds less water, freezes lower, and what falls turns to snow; warmer air lifts the freezing level and the tropopause and holds more water."));
			parent.Add(Offset("Humidity", -0.5f, 0.5f, now.Humidity, v => { AirOffsets o = get(); o.Humidity = v; set(o); },
				"Added to the humidity (0 parched … 1 saturated). Damper air condenses lower — the cloud base comes down — clouds over more and rains sooner; drier air lifts the base until, above the lid, there is no low cloud at all."));
			parent.Add(Offset("Pressure", -1f, 1f, now.Pressure, v => { AirOffsets o = get(); o.Pressure = v; set(o); },
				"Added to the pressure pattern (−1 the heart of a low … +1 a settled high). Down is rising air: cloud, sheets of middle cloud, rain. Up is sinking air: a lid on the sky, flat fair-weather cloud or none."));
			parent.Add(Offset("Instability", -0.6f, 0.6f, now.Instability, v => { AirOffsets o = get(); o.Instability = v; set(o); },
				"Added to how ready the air is to overturn. Up heaps the clouds, then breaks them through to the tropopause as towers; down flattens the sky into decks and sheets."));
			parent.Add(Offset("Wind (m/s)", -20f, 20f, now.Wind, v => { AirOffsets o = get(); o.Wind = v; set(o); },
				"Metres per second added to the wind along the way it already blows. More wind lifts loose ground, carries storms faster, and pushes air over hills it would otherwise flow round."));
			parent.Add(Offset("Gravity (m/s²)", -5f, 5f, now.Gravity, v => { AirOffsets o = get(); o.Gravity = v; set(o); },
				"Added to the pull the air feels. More pull thins the air faster with height: the cloud base comes down, the freezing level and the tropopause with it, and towers are squat. Less lets the whole sky stand taller."));

			var row = new VisualElement();
			row.AddToClassList("ws-button-row");
			row.AddToClassList("ws-button-row--wide");
			row.Add(Button("Back to the air as it is", () =>
			{
				set(default);
				rebuild?.Invoke();
			}));
			WorldAtlasScene entry = settings != null ? settings.AtlasEntry : null;
			if (entry != null)
			{
				row.Add(Button("Author into this scene", () =>
				{
					entry.Air = entry.Air + get();
					set(default);
#if UNITY_EDITOR
					UnityEditor.EditorUtility.SetDirty(entry);
#endif
					rebuild?.Invoke();
				}));
			}
			parent.Add(row);
			if (entry != null)
			{
				parent.Add(Note($"Authored into {entry.name}: {(entry.Air.IsZero ? "nothing" : entry.Air.ToString())}."));
			}
			else
			{
				parent.Add(Note("This scene has no atlas entry, so there is nowhere to author these into. A scene's own additions live on its World Atlas entry."));
			}

			// ── What the air is doing ──
			// First after the sliders, because a slider says what was added and these say what came of it.
			parent.Add(Subheading("The air, and its clouds"));
			parent.Add(Figures(into => CloudStats.Sky(SkySystem.Instance, into), true, false));
			SkySystem sky = SkySystem.Instance;
			for (int i = 0; i < CloudClimate.BandCount; i++)
			{
				int index = i;
				var foldout = new Foldout { text = ((CloudRegime)index).ToString(), value = false };
				foldout.style.marginTop = 4;
				foldout.style.marginBottom = 2;
				Label title = foldout.Q<Label>();
				if (title != null)
				{
					title.AddToClassList("fish-label--accent");
				}
				foldout.Add(Figures(into => CloudStats.Band(SkySystem.Instance, index, into), false, true));
				parent.Add(foldout);
			}

			// ── Drawing ──
			parent.Add(Subheading("Drawing"));
			parent.Add(Toggle("Cloud shadows on the world", SkySystem.DrawCloudShadows, v => SkySystem.DrawCloudShadows = v,
				"The shadow the clouds throw on the ground, marched from the same field and put on the sun as a cookie."));
			// The shadow as the light is handed it. What is on the ground should be this picture laid
			// out around the camera: if this looks like the clouds and the ground does not, the fault
			// is in how the light reads it; if this is speckle, the fault is in how it is marched.
			var cookieView = new Image { scaleMode = ScaleMode.ScaleToFit };
			cookieView.style.width = 256;
			cookieView.style.height = 256;
			cookieView.style.marginTop = 4;
			cookieView.style.marginBottom = 4;
			cookieView.AddToClassList("fish-well");
			cookieView.tooltip = "The cloud shadow cookie: white is full sun, dark is under cloud, the camera is at the middle and the square is the shadow area across. Up is the sun light's own up axis.";
			cookieView.schedule.Execute(() =>
			{
				SkySystem current = SkySystem.Instance;
				Texture cookie = current != null ? current.CloudShadowCookie : null;
				if (cookieView.image != cookie)
				{
					cookieView.image = cookie;
				}
				cookieView.MarkDirtyRepaint();
			}).Every(250);
			parent.Add(cookieView);
			Toggle godRays = Toggle("God rays", SkySystem.DrawGodRays, v => SkySystem.DrawGodRays = v,
				"Shafts of light through broken cloud, and around a body during an eclipse.");
			// The host's panel may switch the same flag; this follows it rather than showing a stale state.
			godRays.schedule.Execute(() => godRays.SetValueWithoutNotify(SkySystem.DrawGodRays)).Every(250);
			parent.Add(godRays);
		}

		// ── Small pieces ───────────────────────────────────────────────

		private static Label Heading(string text)
		{
			var label = new Label(text.ToUpperInvariant());
			label.AddToClassList("fish-section");
			label.AddToClassList("ws-section");
			return label;
		}

		private static Label Subheading(string text)
		{
			var label = new Label(text);
			label.AddToClassList("ws-subsection");
			return label;
		}

		/// <summary>
		/// Live figures as cards of aligned rows, one card to a group, refreshed four times a second.
		/// </summary>
		/// <remarks>
		/// The rows are built once and then only their text is set; they are rebuilt only when the set
		/// of figures itself changes, so nothing flickers and a hovered tooltip stays up.
		/// </remarks>
		/// <param name="wide">One full-width card with its rows two abreast, for a single group.</param>
		private static VisualElement Figures(Action<List<CloudStats.Figure>> gather, bool measures, bool wide)
		{
			var host = new VisualElement();
			host.AddToClassList("ws-stats");
			var figures = new List<CloudStats.Figure>();
			var values = new List<Label>();
			string shape = null;

			void Refresh()
			{
				gather(figures);
				var key = new System.Text.StringBuilder();
				foreach (CloudStats.Figure figure in figures)
				{
					key.Append(figure.Group).Append('|').Append(figure.Label).Append(';');
				}
				string current = key.ToString();
				if (current != shape)
				{
					shape = current;
					Rebuild();
				}
				for (int i = 0; i < figures.Count && i < values.Count; i++)
				{
					CloudStats.Figure figure = figures[i];
					Label value = values[i];
					value.text = figure.Value;
					value.parent.tooltip = figure.Note;
					value.EnableInClassList("ws-stat__value--good", figure.Tone == CloudStats.Tone.Good);
					value.EnableInClassList("ws-stat__value--warn", figure.Tone == CloudStats.Tone.Warn);
					value.EnableInClassList("ws-stat__value--dim", figure.Tone == CloudStats.Tone.Dim);
				}
			}

			void Rebuild()
			{
				host.Clear();
				values.Clear();
				VisualElement grid = null;
				string group = null;
				foreach (CloudStats.Figure figure in figures)
				{
					if (figure.Group != group)
					{
						group = figure.Group;
						var card = new VisualElement();
						card.AddToClassList("ws-card");
						if (wide)
						{
							card.AddToClassList("ws-card--wide");
						}
						var body = new VisualElement();
						body.AddToClassList("fish-well");
						body.AddToClassList("ws-card__body");
						card.Add(body);
						var title = new Label(group.ToUpperInvariant());
						title.AddToClassList("fish-label--caption");
						title.AddToClassList("ws-card__title");
						body.Add(title);
						grid = new VisualElement();
						grid.AddToClassList("ws-card__grid");
						body.Add(grid);
						host.Add(card);
					}
					var row = new VisualElement();
					row.AddToClassList("ws-stat");
					var name = new Label(figure.Label);
					name.AddToClassList("ws-stat__key");
					var value = new Label();
					value.AddToClassList("ws-stat__value");
					row.Add(name);
					row.Add(value);
					grid.Add(row);
					values.Add(value);
				}
			}

			Refresh();
			host.schedule.Execute(() =>
			{
				if (measures)
				{
					CloudStats.Poll();
				}
				Refresh();
			}).Every(250);
			return host;
		}

		private static Label Note(string text)
		{
			var label = new Label(text);
			label.AddToClassList("ws-note");
			return label;
		}

		/// <summary>A slider that adds to the air: zero in the middle, less to the left and more to the right.</summary>
		private static Slider Offset(string text, float low, float high, float value, Action<float> changed, string tooltip)
		{
			var slider = new Slider(text, low, high) { value = Mathf.Clamp(value, low, high), showInputField = true, tooltip = tooltip + " Zero adds nothing." };
			slider.AddToClassList("fish-slider");
			slider.AddToClassList("ws-slider");
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private static Toggle Toggle(string text, bool value, Action<bool> changed, string tooltip)
		{
			var toggle = new Toggle(text) { value = value, tooltip = tooltip };
			toggle.AddToClassList("fish-toggle");
			toggle.AddToClassList("ws-toggle");
			toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return toggle;
		}

		private static Button Button(string text, Action clicked)
		{
			var button = new Button(clicked) { text = text };
			button.AddToClassList("fish-button");
			button.AddToClassList("fish-button--ghost");
			button.AddToClassList("ws-button");
			return button;
		}
	}
}
