using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Puts a static, parameterless editor method on a FishMMO Dashboard page as a button.
	/// </summary>
	/// <remarks>
	/// The dashboard's replacement for a <c>FishMMO/…</c> menu item. The dashboard finds every
	/// tagged method through <see cref="UnityEditor.TypeCache"/>, so a tool in another editor
	/// assembly only needs a reference to <c>FishMMO.Shared.Tools.Editor</c>, not the other way
	/// round. <see cref="Page"/> must name one of the constants below; the dashboard's tests reject
	/// anything else, because a tool on a page that does not exist is a tool nobody can run.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class DashboardToolAttribute : Attribute
	{
		/// <summary>Core → Validate: validators and audits.</summary>
		public const string Validate = "Validate";

		/// <summary>Core → Unit Tests.</summary>
		public const string UnitTests = "Unit Tests";

		/// <summary>Core → UI Tests: UI Toolkit panel validation, renders and probes (client editor only).</summary>
		public const string UITests = "UI Tests";

		/// <summary>Core → Maintenance: one-off wiring, mock content and generators.</summary>
		public const string Maintenance = "Maintenance";

		/// <summary>NPCs → AI Tools: repairs and migrations of AI assets and NPC prefabs.</summary>
		public const string AITools = "AI Tools";

		/// <summary>World → World Scene Details.</summary>
		public const string WorldSceneDetails = "World Scene Details";

		/// <summary>World → Spawn Tables.</summary>
		public const string SpawnTables = "Spawn Tables";

		/// <summary>World → World Map.</summary>
		public const string WorldMap = "World Map";

		/// <summary>Weather → Weather Tools: content generation and texture bakes.</summary>
		public const string Weather = "Weather Tools";

		/// <summary>Every page a tool may name.</summary>
		public static readonly string[] Pages = { Validate, UnitTests, UITests, Maintenance, AITools, WorldSceneDetails, SpawnTables, WorldMap, Weather };

		/// <summary>The dashboard page (sidebar entry) the button appears on.</summary>
		public string Page { get; }

		/// <summary>The button text.</summary>
		public string Label { get; }

		/// <summary>Heading the button is grouped under on its page. Defaults to "Tools".</summary>
		public string Section { get; set; } = "Tools";

		/// <summary>Sort order within the section; lower first, then by label.</summary>
		public int Order { get; set; }

		/// <summary>Hover text; say what the tool changes.</summary>
		public string Tooltip { get; set; }

		/// <summary>When set, a confirmation dialog with this text is shown before the tool runs.</summary>
		public string Confirm { get; set; }

		/// <param name="page">One of the page constants.</param>
		/// <param name="label">The button text.</param>
		public DashboardToolAttribute(string page, string label)
		{
			Page = page;
			Label = label;
		}
	}
}
