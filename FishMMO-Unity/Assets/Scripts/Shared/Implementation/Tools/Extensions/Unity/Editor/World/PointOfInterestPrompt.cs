#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>An answer to the pre-cut prompt: what goes into the scene's <see cref="PointOfInterestSettings"/>.</summary>
	public sealed class PointOfInterestChoice
	{
		public bool Capital;
		public PointOfInterestDensity Density = PointOfInterestDensity.Normal;
		public float Multiplier = 1f;
		public readonly List<PointOfInterestKindOverride> Overrides = new List<PointOfInterestKindOverride>();

		/// <summary>The settings' current values, or the defaults for none; the overrides are copies.</summary>
		public static PointOfInterestChoice From(PointOfInterestSettings settings)
		{
			var choice = new PointOfInterestChoice();
			if (settings == null)
			{
				return choice;
			}
			choice.Capital = settings.Capital;
			choice.Density = settings.Density;
			choice.Multiplier = settings.Multiplier;
			if (settings.Overrides != null)
			{
				foreach (PointOfInterestKindOverride entry in settings.Overrides)
				{
					if (entry != null)
					{
						choice.Overrides.Add(Copy(entry));
					}
				}
			}
			return choice;
		}

		/// <summary>
		/// Writes this answer into a settings object. Overrides that change nothing (enabled, every number
		/// left at the catalogue's) are dropped, so the asset lists only what somebody actually decided.
		/// </summary>
		public void ApplyTo(PointOfInterestSettings settings)
		{
			settings.Capital = Capital;
			settings.Density = Density;
			settings.Multiplier = Mathf.Clamp(Multiplier, 0f, 4f);
			settings.Overrides = new List<PointOfInterestKindOverride>();
			foreach (PointOfInterestKindOverride entry in Overrides)
			{
				if (entry != null && !IsDefault(entry))
				{
					settings.Overrides.Add(Copy(entry));
				}
			}
			// In enum order, so the asset does not churn with the order rows were touched in.
			settings.Overrides.Sort((a, b) => a.Kind.CompareTo(b.Kind));
		}

		/// <summary>The override row for a kind, created (as a no-op) when there is none.</summary>
		public PointOfInterestKindOverride Row(POIType kind)
		{
			foreach (PointOfInterestKindOverride entry in Overrides)
			{
				if (entry != null && entry.Kind == kind)
				{
					return entry;
				}
			}
			var row = new PointOfInterestKindOverride { Kind = kind };
			Overrides.Add(row);
			return row;
		}

		public static bool IsDefault(PointOfInterestKindOverride entry)
			=> entry.Enabled && entry.PerKm2 < 0f && entry.Min < 0 && entry.Max < 0 && entry.SpacingMetres < 0f;

		private static PointOfInterestKindOverride Copy(PointOfInterestKindOverride entry)
			=> new PointOfInterestKindOverride
			{
				Kind = entry.Kind,
				Enabled = entry.Enabled,
				PerKm2 = entry.PerKm2,
				Min = entry.Min,
				Max = entry.Max,
				SpacingMetres = entry.SpacingMetres,
			};
	}

	/// <summary>
	/// The pre-cut prompt (Jim, 2026-10-10): before every cut and re-cut on the World Atlas page, asks
	/// whether the scene holds the capital and how dense its points of interest are.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Asked every time rather than once because a re-cut regenerates every point of interest, and
	/// the moment somebody re-cuts is the moment they are deciding what the scene should hold. It opens
	/// on the scene's current settings, so pressing the button through keeps them.
	/// </para>
	/// <para>
	/// Never shown in batch mode: probes and -executeMethod runs cut scenes too, and a modal there
	/// would hang the process with nobody to answer it. They get the existing settings, or the
	/// defaults.
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestPrompt : EditorWindow
	{
		private string details;
		private string acceptLabel;
		private PointOfInterestChoice choice;
		private bool accepted;
		private bool done;
		private Vector2 scroll;
		private readonly HashSet<PointOfInterestGroup> open = new HashSet<PointOfInterestGroup>();

		/// <summary>
		/// Asks. Returns false when it was cancelled; in batch mode answers at once with the existing
		/// settings (or the defaults) without showing anything.
		/// </summary>
		/// <param name="details">What is about to happen, shown at the top.</param>
		/// <param name="acceptLabel">The accept button's text: "Cut scene" or "Re-cut".</param>
		/// <param name="existing">The scene's current settings, to open on; null for the defaults.</param>
		/// <param name="result">The answer.</param>
		public static bool Ask(string details, string acceptLabel, PointOfInterestSettings existing, out PointOfInterestChoice result)
		{
			result = PointOfInterestChoice.From(existing);
			if (Application.isBatchMode)
			{
				return true;
			}

			var window = CreateInstance<PointOfInterestPrompt>();
			window.titleContent = new GUIContent("Points of interest");
			window.details = details;
			window.acceptLabel = string.IsNullOrEmpty(acceptLabel) ? "Cut scene" : acceptLabel;
			window.choice = result;
			window.minSize = new Vector2(480f, 300f);
			window.maxSize = new Vector2(480f, 640f);
			// Modal, like the name prompt: the rectangle being cut must still be the one that was drawn.
			window.ShowModalUtility();

			bool ok = window.accepted;
			DestroyImmediate(window);
			return ok;
		}

		private void OnGUI()
		{
			if (done || choice == null)
			{
				return;
			}

			EditorGUILayout.Space(6f);
			if (!string.IsNullOrEmpty(details))
			{
				EditorGUILayout.LabelField(details, EditorStyles.wordWrappedLabel);
				EditorGUILayout.Space(6f);
			}

			choice.Capital = EditorGUILayout.ToggleLeft(
				new GUIContent("Capital city scene?",
					"This scene holds its world's capital: a capital is placed, with a larger layout and more " +
					"service NPCs than a city."),
				choice.Capital);

			EditorGUILayout.Space(4f);
			choice.Density = (PointOfInterestDensity)EditorGUILayout.EnumPopup(
				new GUIContent("Density", "How many sites are placed. Natural only places nothing and keeps the features " +
					"found in the ground (falls, lakes, peaks). Custom takes every kind's budget from the rows below."),
				choice.Density);
			choice.Multiplier = EditorGUILayout.Slider(
				new GUIContent("Multiplier", "Scales every placed kind's budget after the density."),
				choice.Multiplier, 0f, 4f);

			EditorGUILayout.HelpBox(Describe(choice.Density), MessageType.None);

			if (choice.Density == PointOfInterestDensity.Custom)
			{
				DrawOverrides();
			}
			else
			{
				GUILayout.FlexibleSpace();
			}

			EditorGUILayout.Space(8f);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
				{
					Close();
				}
				if (GUILayout.Button(acceptLabel, GUILayout.Width(110f)))
				{
					Accept();
				}
			}
			EditorGUILayout.Space(4f);

			if (Event.current.type == EventType.KeyDown)
			{
				if (Event.current.keyCode == KeyCode.Escape)
				{
					Close();
				}
				else if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
				{
					Accept();
				}
			}
		}

		private static string Describe(PointOfInterestDensity density)
		{
			switch (density)
			{
				case PointOfInterestDensity.NaturalOnly: return "Only what the ground already has: waterfalls, lakes, peaks and the like. Nothing is built.";
				case PointOfInterestDensity.Sparse: return "A few sites: wild country with a long walk between places.";
				case PointOfInterestDensity.Dense: return "Many sites: settled, busy country.";
				case PointOfInterestDensity.Custom: return "Each kind below can be switched off or given its own budget; a value of -1 keeps the catalogue's.";
				default: return "The catalogue's budgets.";
			}
		}

		/// <summary>Per-kind overrides, one foldout per group so a hundred kinds stay readable.</summary>
		private void DrawOverrides()
		{
			EditorGUILayout.LabelField("Per-kind overrides", EditorStyles.boldLabel);
			scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
			foreach (PointOfInterestGroup group in (PointOfInterestGroup[])Enum.GetValues(typeof(PointOfInterestGroup)))
			{
				bool any = false;
				foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
				{
					if (info.Group == group)
					{
						any = true;
						break;
					}
				}
				if (!any)
				{
					continue;
				}

				bool expanded = EditorGUILayout.Foldout(open.Contains(group), group.ToString(), true);
				if (expanded) open.Add(group); else open.Remove(group);
				if (!expanded)
				{
					continue;
				}

				EditorGUI.indentLevel++;
				foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
				{
					if (info.Group != group)
					{
						continue;
					}
					PointOfInterestKindOverride row = choice.Row(info.Kind);
					using (new EditorGUILayout.HorizontalScope())
					{
						row.Enabled = EditorGUILayout.ToggleLeft(info.DisplayName, row.Enabled, GUILayout.Width(170f));
						using (new EditorGUI.DisabledScope(!row.Enabled))
						{
							EditorGUIUtility.labelWidth = 44f;
							row.PerKm2 = EditorGUILayout.FloatField(new GUIContent("/km²", "Sites per square kilometre before the multiplier; -1 keeps the catalogue's."), row.PerKm2, GUILayout.Width(100f));
							EditorGUIUtility.labelWidth = 30f;
							row.Max = EditorGUILayout.IntField(new GUIContent("max", "At most this many in the scene; -1 keeps the catalogue's."), row.Max, GUILayout.Width(80f));
							EditorGUIUtility.labelWidth = 0f;
						}
					}
				}
				EditorGUI.indentLevel--;
			}
			EditorGUILayout.EndScrollView();
		}

		private void Accept()
		{
			accepted = true;
			done = true;
			Close();
		}
	}
}
#endif
