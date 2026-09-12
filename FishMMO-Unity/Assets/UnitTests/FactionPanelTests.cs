using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The reputation panel, mounted on a real <see cref="UIDocument"/>: the section grouping issue
	/// #289 asks for, and the expandable row it asks for beside it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Issue #289: "Faction UI should be categorized by alliance level" and "Clicking on a Faction
	/// row should allow it to expand". The first is about where a row ends up in the tree and in
	/// what order; the second is about what a click does to a row that is already there. Neither
	/// is visible without a tree, and neither can be checked by reading the panel: the grouping
	/// depends on the UXML's element names agreeing with the enum's spelling, and the expansion
	/// depends on a USS class actually being toggled on the label a player is looking at.
	/// </para>
	/// <para>
	/// Rows are driven through <c>ApplyFaction</c> rather than through the update event. That method
	/// is deliberately character-free — the identity test on the event answers "whose standing is
	/// this", which is a question about the event and not about rendering one row — so no
	/// networked <c>IPlayerCharacter</c> is needed to build a row. The event's own half is pinned
	/// by the source scan at the end of this fixture.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class FactionPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Faction/UIFactions.uxml";

		private const string RowClass = "faction-row";
		private const string NameClass = "faction-row__name";
		private const string CaretClass = "faction-row__caret";
		private const string DescClass = "faction-row__desc";
		private const string DescCollapsedClass = "faction-row__desc--collapsed";
		private const string SectionEmptyClass = "faction-section--empty";

		private const string CaretExpanded = "▼";
		private const string CaretCollapsed = "▶";

		/// <summary>Section suffixes, in display order — the enum's own spelling, lower-cased.</summary>
		private static readonly string[] SectionSuffixes = { "ally", "neutral", "enemy" };

		private readonly List<Object> temporaries = new List<Object>();

		private GameObject host;
		private UITKFactions panel;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the faction UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UIFactions");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;

			// A start-hidden panel: the document is off until something shows it.
			document.enabled = false;

			panel = host.AddComponent<UITKFactions>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnQuitToMenu = true;
			panel.ReleasesCursor = false;
			panel.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(panel, null);

			panel.Show();
			LogAssert.IsTrue(panel.Visible, "the panel must be up before anything is asserted about its tree");
		}

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < temporaries.Count; ++i)
			{
				if (temporaries[i] is ScriptableObject so && so is ICachedObject cached)
				{
					cached.RemoveFromCache();
				}
				if (temporaries[i] != null)
				{
					Object.DestroyImmediate(temporaries[i]);
				}
			}
			temporaries.Clear();

			if (host != null)
			{
				Object.DestroyImmediate(host);
			}

			if (sharedSettings != null)
			{
				Object.DestroyImmediate(sharedSettings);
			}
		}

		// ── Fixtures ─────────────────────────────────────────────────────────────

		private VisualElement Live => document.rootVisualElement;

		private static void Invoke(object target, string method, params object[] args)
		{
			MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKFactions must still declare {method}");
			info.Invoke(target, args);
		}

		private T Field<T>(string name) where T : class
		{
			FieldInfo info = typeof(UITKFactions).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKFactions must still declare {name}");
			return info.GetValue(panel) as T;
		}

		private FactionTemplate NewFaction(string name)
		{
			FactionTemplate template = ScriptableObject.CreateInstance<FactionTemplate>();
			template.name = name;
			template.Description = $"What {name} is.";
			template.AddToCache(name);
			temporaries.Add(template);
			return template;
		}

		/// <summary>Builds a row through the panel's own render path, as an update would.</summary>
		private void Apply(FactionTemplate template, int value)
		{
			Invoke(panel, "ApplyFaction", new Faction(template.ID, value));
		}

		/// <summary>The container a section's rows are filed into.</summary>
		private VisualElement RowsIn(string suffix)
		{
			VisualElement rows = Live.Q($"faction-section-rows-{suffix}");
			LogAssert.IsNotNull(rows, $"the {suffix} section's row container must exist in the cloned tree");
			return rows;
		}

		/// <summary>The row element rendering a named faction, or null.</summary>
		private VisualElement RowFor(string name)
		{
			foreach (VisualElement row in Live.Query<VisualElement>(className: RowClass).Build())
			{
				Label label = row.Q<Label>(className: NameClass);
				if (label != null && label.text == name)
				{
					return row;
				}
			}
			return null;
		}

		/// <summary>The names rendered in a section, in the order they appear.</summary>
		private List<string> NamesIn(string suffix)
		{
			List<string> names = new List<string>();
			VisualElement rows = RowsIn(suffix);
			for (int i = 0; i < rows.childCount; ++i)
			{
				names.Add(rows[i].Q<Label>(className: NameClass).text);
			}
			return names;
		}

		/// <summary>Relayouts a container the way adding, moving or removing rows does.</summary>
		private static void Relayout(VisualElement element)
		{
			using (GeometryChangedEvent evt = GeometryChangedEvent.GetPooled(new Rect(0, 0, 100, 10), new Rect(0, 0, 100, 40)))
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		/// <summary>Refreshes the header chrome the way a relayout would.</summary>
		private void RefreshChrome()
		{
			Relayout(Field<VisualElement>("list"));
		}

		private static bool IsCollapsed(VisualElement row)
		{
			Label description = row.Q<Label>(className: DescClass);
			LogAssert.IsNotNull(description, "every row must carry a description label");
			return description.ClassListContains(DescCollapsedClass);
		}

		// ── The grouping ─────────────────────────────────────────────────────────

		[Test]
		public void TheListDeclaresThreeSectionsInAllianceOrder()
		{
			/* The requirement in as many words: allies first, neutrals in the middle, enemies last.
			 * Asserted on the ELEMENT NAMES rather than on the headers' text, because the panel
			 * resolves its sections by name at open and the header text is decoration — a cap on
			 * the section that did not match the enum's spelling would leave the panel inert, and
			 * three headings would still read correctly. */
			VisualElement list = Live.Q("faction-list");
			LogAssert.IsNotNull(list, "the list container must exist");
			LogAssert.AreEqual(3, list.childCount, "the list must hold exactly the three sections");

			for (int i = 0; i < SectionSuffixes.Length; ++i)
			{
				LogAssert.AreEqual($"faction-section-{SectionSuffixes[i]}", list[i].name,
					$"section {i} must be the {SectionSuffixes[i]} one, in display order");
			}

			// And every element the panel resolves at open is present, or it renders nothing at all.
			LogAssert.IsNotNull(Field<VisualElement>("list"),
				"the panel must have resolved the list; a null one makes it inert");
			for (int i = 0; i < SectionSuffixes.Length; ++i)
			{
				LogAssert.IsNotNull(Live.Q($"faction-section-{SectionSuffixes[i]}"),
					$"the {SectionSuffixes[i]} section element must exist");
				LogAssert.IsNotNull(Live.Q($"faction-section-rows-{SectionSuffixes[i]}"),
					$"the {SectionSuffixes[i]} row container must exist");
			}
		}

		[Test]
		public void EachFactionLandsInTheSectionForTheSignOfItsStanding()
		{
			FactionTemplate ally = NewFaction("FactionPanel_Ally");
			FactionTemplate neutral = NewFaction("FactionPanel_Neutral");
			FactionTemplate enemy = NewFaction("FactionPanel_Enemy");

			Apply(ally, 5000);
			Apply(neutral, 0);
			Apply(enemy, -5000);

			LogAssert.AreEqual(1, RowsIn("ally").childCount, "the positive standing belongs under Allies");
			LogAssert.AreEqual(1, RowsIn("neutral").childCount, "a zero standing belongs under Neutral");
			LogAssert.AreEqual(1, RowsIn("enemy").childCount, "a negative standing belongs under Enemies");

			LogAssert.AreSame(RowsIn("ally")[0], RowFor("FactionPanel_Ally"), "the ally row is the ally faction's");
			LogAssert.AreSame(RowsIn("neutral")[0], RowFor("FactionPanel_Neutral"), "and the neutral one's");
			LogAssert.AreSame(RowsIn("enemy")[0], RowFor("FactionPanel_Enemy"), "and the enemy one's");
		}

		[Test]
		public void RowsAreFiledInNameOrderWithinASection()
		{
			/* Delivered out of order, which is what the spawn payload does — it walks a dictionary. */
			FactionTemplate zeta = NewFaction("FactionPanel_Zeta");
			FactionTemplate alpha = NewFaction("FactionPanel_Alpha");
			FactionTemplate mid = NewFaction("FactionPanel_Mid");

			Apply(zeta, 100);
			Apply(alpha, 200);
			Apply(mid, 300);

			List<string> names = NamesIn("ally");
			LogAssert.AreEqual(3, names.Count, "all three allies must be filed");
			LogAssert.AreEqual("FactionPanel_Alpha", names[0], "rows are filed alphabetically");
			LogAssert.AreEqual("FactionPanel_Mid", names[1], "so the middle name sits in the middle");
			LogAssert.AreEqual("FactionPanel_Zeta", names[2], "and the last one last, whatever order they arrived in");
		}

		[Test]
		public void AnUpdateThatDoesNotCrossZeroLeavesItsRowAlone()
		{
			/* A reputation tick is not a move. Re-sorting the section on every update would shuffle
			 * rows the player is reading while standings stream in. */
			FactionTemplate first = NewFaction("FactionPanel_First");
			FactionTemplate second = NewFaction("FactionPanel_Second");

			Apply(first, 100);
			Apply(second, 200);

			VisualElement before = RowsIn("ally");
			LogAssert.AreSame(before[0], RowFor("FactionPanel_First"), "the first row is filed first");

			// A large change that stays inside the same band.
			Apply(first, 9000);

			LogAssert.AreSame(before, RowFor("FactionPanel_First").parent, "an in-band update must not re-parent the row");
			LogAssert.AreEqual(0, before.IndexOf(RowFor("FactionPanel_First")), "nor move it within its section");
			LogAssert.AreEqual("9000", RowFor("FactionPanel_First").Q<Label>(className: "faction-row__value").text,
				"but the standing it shows must be the new one");
		}

		[Test]
		public void ARowThatCrossesZeroMigratesToItsNewSection()
		{
			FactionTemplate faction = NewFaction("FactionPanel_Crosser");

			Apply(faction, 500);
			LogAssert.AreEqual(1, RowsIn("ally").childCount, "it starts as an ally");

			Apply(faction, -500);

			LogAssert.AreEqual(0, RowsIn("ally").childCount, "crossing zero must empty the section it left");
			LogAssert.AreEqual(1, RowsIn("enemy").childCount, "and fill the one it belongs in now");

			/* One row, not two. Read through the non-generic IDictionary because the field is a
			 * Dictionary<int, FactionRow> and its nested row type is private — generics are
			 * invariant, so a Dictionary<int, object> cast would quietly return null. */
			System.Collections.IDictionary rows = Field<System.Collections.IDictionary>("factions");
			LogAssert.IsNotNull(rows, "the panel must still key its rows by faction template ID");
			LogAssert.AreEqual(1, rows.Count, "the move must reuse the row, not create a second one for the same faction");
		}

		[Test]
		public void ASectionIsHiddenExactlyWhenItHoldsNoRows()
		{
			/* The invariant the header count rests on: the badge sums the rows containers, which is
			 * only the number of factions if a section's visibility and its emptiness agree. */
			FactionTemplate ally = NewFaction("FactionPanel_SectionAlly");
			FactionTemplate enemy = NewFaction("FactionPanel_SectionEnemy");

			VisualElement allySection = Live.Q("faction-section-ally");
			VisualElement neutralSection = Live.Q("faction-section-neutral");
			VisualElement enemySection = Live.Q("faction-section-enemy");

			// Nothing but the authored state: a freshly opened panel shows no headings at all.
			LogAssert.IsTrue(allySection.ClassListContains(SectionEmptyClass), "an empty section is hidden");
			LogAssert.IsTrue(neutralSection.ClassListContains(SectionEmptyClass), "and so is the neutral one");
			LogAssert.IsTrue(enemySection.ClassListContains(SectionEmptyClass), "and the enemy one");

			Apply(ally, 500);
			LogAssert.IsFalse(allySection.ClassListContains(SectionEmptyClass), "a section with a row is shown");
			LogAssert.IsTrue(neutralSection.ClassListContains(SectionEmptyClass), "while its neighbours stay hidden");

			// Emptied by a crossing — the section the row left has to hide itself again.
			Apply(ally, -500);
			LogAssert.IsTrue(allySection.ClassListContains(SectionEmptyClass),
				"a section emptied by a migration must hide again");
			LogAssert.IsFalse(enemySection.ClassListContains(SectionEmptyClass), "and the one that gained a row must show");

			// And the invariant itself, over every section, in both states.
			for (int i = 0; i < SectionSuffixes.Length; ++i)
			{
				bool empty = RowsIn(SectionSuffixes[i]).childCount == 0;
				bool hidden = Live.Q($"faction-section-{SectionSuffixes[i]}").ClassListContains(SectionEmptyClass);
				LogAssert.AreEqual(empty, hidden,
					$"the {SectionSuffixes[i]} section is hidden if and only if it holds no rows");
			}
		}

		// ── The header chrome ────────────────────────────────────────────────────

		/// <summary>
		/// The badge has to count factions, not sections — the regression test for the row counter
		/// <see cref="UITKControl.BindListChrome"/> grew.
		/// </summary>
		/// <remarks>
		/// Its default counts the bound container's direct children, which with the sections in
		/// place is three no matter how many factions the player knows. The failure is quiet: the
		/// panel still renders every row, and only the header lies.
		/// </remarks>
		[Test]
		public void TheHeaderCountsFactionsRatherThanSections()
		{
			FactionTemplate ally = NewFaction("FactionPanel_CountAlly");
			FactionTemplate neutral = NewFaction("FactionPanel_CountNeutral");
			FactionTemplate enemy = NewFaction("FactionPanel_CountEnemy");

			Label count = Live.Q<Label>("faction-count");
			Label subtitle = Live.Q<Label>("faction-subtitle");
			Label empty = Live.Q<Label>("faction-empty");

			Apply(ally, 100);
			Apply(neutral, 0);
			Apply(enemy, -100);
			RefreshChrome();

			LogAssert.AreEqual("3", count.text, "three factions, across three sections, is three");
			LogAssert.AreEqual("3 factions", subtitle.text, "and the subtitle agrees");
			LogAssert.AreEqual(DisplayStyle.None, empty.style.display.value, "there is nothing empty about this list");

			// A move between sections changes the distribution, not the total.
			Apply(ally, -100);
			RefreshChrome();
			LogAssert.AreEqual("3", count.text, "a migration must not change the count");

			panel.ClearAll();
			RefreshChrome();

			LogAssert.AreEqual("0", count.text, "an emptied list reports zero");
			LogAssert.AreEqual("0 factions", subtitle.text, "and says so");
			LogAssert.AreEqual(DisplayStyle.Flex, empty.style.display.value, "and shows the placeholder it had hidden");
			for (int i = 0; i < SectionSuffixes.Length; ++i)
			{
				LogAssert.IsTrue(Live.Q($"faction-section-{SectionSuffixes[i]}").ClassListContains(SectionEmptyClass),
					$"with no rows, the {SectionSuffixes[i]} section must be hidden — three headings over an empty " +
					"placeholder is what the authored class exists to prevent");
			}
		}

		[Test]
		public void ASingleFactionIsCountedInTheSingular()
		{
			Label subtitle = Live.Q<Label>("faction-subtitle");

			Apply(NewFaction("FactionPanel_OnlyOne"), 100);
			RefreshChrome();

			LogAssert.AreEqual("1 faction", subtitle.text, "one faction is one faction, not one factions");
		}

		// ── The expansion ────────────────────────────────────────────────────────

		[Test]
		public void RowsStartCollapsedAndClickingOneOpensIt()
		{
			FactionTemplate faction = NewFaction("FactionPanel_Opener");
			Apply(faction, 100);

			VisualElement row = RowFor("FactionPanel_Opener");
			LogAssert.IsTrue(IsCollapsed(row), "a row opens shut; the whole point is to shorten the list");
			LogAssert.AreEqual(CaretCollapsed, row.Q<Label>(className: CaretClass).text, "and says so with its caret");

			Invoke(panel, "ToggleExpansion", faction.ID);

			LogAssert.IsFalse(IsCollapsed(row), "clicking it must reveal the description");
			LogAssert.AreEqual(CaretExpanded, row.Q<Label>(className: CaretClass).text, "and turn the caret");
		}

		[Test]
		public void OnlyOneRowIsOpenAtATimeAndClickingTheOpenOneShutsIt()
		{
			FactionTemplate first = NewFaction("FactionPanel_OneAtATimeA");
			FactionTemplate second = NewFaction("FactionPanel_OneAtATimeB");
			Apply(first, 100);
			Apply(second, 200);

			Invoke(panel, "ToggleExpansion", first.ID);
			LogAssert.IsFalse(IsCollapsed(RowFor("FactionPanel_OneAtATimeA")), "the first row opens");

			Invoke(panel, "ToggleExpansion", second.ID);
			LogAssert.IsFalse(IsCollapsed(RowFor("FactionPanel_OneAtATimeB")), "the second row opens");
			LogAssert.IsTrue(IsCollapsed(RowFor("FactionPanel_OneAtATimeA")),
				"and the first shuts — several open rows is most of the way back to a wall of text");

			Invoke(panel, "ToggleExpansion", second.ID);
			LogAssert.IsTrue(IsCollapsed(RowFor("FactionPanel_OneAtATimeB")), "clicking the open row shuts it");
		}

		[Test]
		public void TheDescriptionIsCarriedByTheRowAndOnlyHiddenWhileShut()
		{
			/* A description that is not in the tree cannot be revealed by a click, and one that was
			 * emptied at creation would make the expansion look like it did nothing. */
			FactionTemplate faction = NewFaction("FactionPanel_Desc");
			Apply(faction, 100);

			VisualElement row = RowFor("FactionPanel_Desc");
			Label description = row.Q<Label>(className: DescClass);

			LogAssert.AreEqual($"What {faction.name} is.", description.text,
				"the row must carry the template's description, even while folded");
			LogAssert.IsFalse(description.enableRichText,
				"a description is authored text, so an angle bracket in one must render rather than be parsed");

			Invoke(panel, "ToggleExpansion", faction.ID);
			LogAssert.IsFalse(description.ClassListContains(DescCollapsedClass),
				"opening the row is what removes the class that was hiding it");
		}

		[Test]
		public void ARowThatMigratesCarriesItsExpansionWithIt()
		{
			/* The row is the same object after a move — PlaceRow re-parents it rather than rebuilding
			 * it — so an open row that crosses zero must arrive open, not silently snap shut. */
			FactionTemplate faction = NewFaction("FactionPanel_Migrant");
			Apply(faction, 500);
			Invoke(panel, "ToggleExpansion", faction.ID);

			VisualElement row = RowFor("FactionPanel_Migrant");
			LogAssert.IsFalse(IsCollapsed(row), "it is open before the crossing");

			Apply(faction, -500);

			LogAssert.AreSame(row, RowFor("FactionPanel_Migrant"), "the move must keep the row it had");
			LogAssert.IsFalse(IsCollapsed(row), "and keep it open");
			LogAssert.AreEqual(CaretExpanded, row.Q<Label>(className: CaretClass).text, "caret included");
			LogAssert.AreEqual(0, RowsIn("ally").childCount, "and it has left the Allies section");
			LogAssert.AreEqual(1, RowsIn("enemy").childCount, "for the Enemies one");
		}

		[Test]
		public void ClearingTheListResetsTheRowsAndTheExpansion()
		{
			/* ClearAll is reached from the quit-to-login path and from the local client stopping,
			 * neither of which rebuilds afterwards. Rows left behind, or an expansion remembered
			 * across the clear, would both surface on the way back to the login screen. */
			FactionTemplate faction = NewFaction("FactionPanel_Clear");
			Apply(faction, 100);
			Invoke(panel, "ToggleExpansion", faction.ID);
			RefreshChrome();
			LogAssert.AreEqual("1", Live.Q<Label>("faction-count").text, "the row is counted before the clear");

			panel.ClearAll();

			for (int i = 0; i < SectionSuffixes.Length; ++i)
			{
				LogAssert.AreEqual(0, RowsIn(SectionSuffixes[i]).childCount,
					$"the {SectionSuffixes[i]} section's rows must be gone");
				LogAssert.IsTrue(Live.Q($"faction-section-{SectionSuffixes[i]}").ClassListContains(SectionEmptyClass),
					$"and the {SectionSuffixes[i]} section hidden");
			}
			LogAssert.IsNull(RowFor("FactionPanel_Clear"), "no row may survive the clear");
			LogAssert.IsNull(Field<object>("expandedRow"), "and no expansion may be remembered across it");

			// Re-applying the same faction builds a new row, folded shut.
			Apply(faction, 100);
			VisualElement rebuilt = RowFor("FactionPanel_Clear");
			LogAssert.IsNotNull(rebuilt, "the faction must be renderable again");
			LogAssert.IsTrue(IsCollapsed(rebuilt), "and the rebuilt row must start shut");

			/* Called twice in a row, which is what actually happens: OnDestroying reaches it
			 * directly and again through the character teardown. It must not throw or double-count. */
			panel.ClearAll();
			panel.ClearAll();
			RefreshChrome();
			LogAssert.AreEqual("0", Live.Q<Label>("faction-count").text, "the count follows the rows out");
		}

		[Test]
		public void ClosingThePanelAndReopeningItRebuildsTheRowsIntoTheNewTree()
		{
			/* Hiding the panel disables its UIDocument, which discards the tree; showing it clones a
			 * fresh one, so every element the panel cached belongs to a tree nobody can see. */
			FactionTemplate faction = NewFaction("FactionPanel_Reopen");
			Apply(faction, 100);
			LogAssert.IsNotNull(RowFor("FactionPanel_Reopen"), "the row exists in the first tree");

			panel.Hide();

			/* The Pre half of the rebuild. OnAfterStarting runs this and then OnPostSetCharacter
			 * whenever the tree is rebuilt and a character is set; this fixture has no character, so
			 * it is called directly here. It is the half that drops the rows belonging to the tree
			 * that has just been discarded — and dropping them is load-bearing, not tidiness:
			 * without it the panel holds a row element nobody can see, ApplyFaction then finds the
			 * faction already in its dictionary, decides the row does not need moving, and renders
			 * nothing into the tree that is actually on screen. */
			panel.OnPreSetCharacter();
			panel.Show();

			LogAssert.IsNull(RowFor("FactionPanel_Reopen"),
				"the rebuilt tree starts empty; the rows went with the tree that was discarded");
			LogAssert.AreSame(Live.Q("faction-list"), Field<VisualElement>("list"),
				"and the panel must have re-resolved the list from the tree it can actually draw");

			// Rendering into the new tree works, and files into the NEW tree's sections.
			Apply(faction, 100);
			LogAssert.IsNotNull(RowFor("FactionPanel_Reopen"), "a faction must render into the rebuilt tree");
			LogAssert.AreEqual(1, RowsIn("ally").childCount, "in the section it belongs to");
			LogAssert.IsTrue(IsCollapsed(RowFor("FactionPanel_Reopen")),
				"and folded shut, since the expansion went with the rows");
		}

		// ── What cannot be reached from a tree ──────────────────────────────────

		/// <summary>
		/// The click handler must ignore everything but the left button.
		/// </summary>
		/// <remarks>
		/// A right-click on a row is a request for whatever context menu a player expects; folding
		/// the row as a side effect is not that. Pinned at source because a pointer event cannot be
		/// fabricated into the row's lambda from here — the guard is on the event's own button, and
		/// what this asserts is that it is still there and still first in the handler.
		/// </remarks>
		[Test]
		public void TheRowClickHandlerIgnoresNonLeftButtons()
		{
			string source = ReadPanelSource();
			string path = FactionSourcePath();

			int handler = source.IndexOf("RegisterCallback<PointerDownEvent>", System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(handler, 0, $"the rows must still register a pointer handler — {path}");

			int guard = source.IndexOf("evt.button != 0", handler, System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(guard, 0,
				"and it must test the button before acting on the click");

			int toggle = source.IndexOf("ToggleExpansion(templateID)", handler, System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(toggle, 0, "the handler must route through the toggle seam");
			Assert.Less(guard, toggle, "and the guard must come before it, or it guards nothing");
		}

		/// <summary>
		/// No <c>cursor:</c> keyword may appear in this panel's stylesheet.
		/// </summary>
		/// <remarks>
		/// A runtime panel cannot resolve the cursor property and logs a warning every frame the
		/// element is under the pointer, which is a log flood a player never sees and a developer
		/// never stops seeing. The rule is stated in FishMMO-Theme.uss and already broken in
		/// UIGuild.uss; this keeps it from spreading to the panel being changed beside it.
		/// </remarks>
		[Test]
		public void TheStylesheetObeysTheNoCursorRule()
		{
			string uss = System.IO.File.ReadAllText(System.IO.Path.Combine(
				System.IO.Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Faction/UIFactions.uss"));

			Assert.IsFalse(uss.Contains("cursor:"),
				"UIFactions.uss must not declare a cursor — a runtime panel logs a warning every frame for one");
		}

		/// <summary>
		/// Hover has to be written in this sheet, not borrowed from the theme's row classes.
		/// </summary>
		/// <remarks>
		/// <c>.faction-row</c> sets its own <c>background-color</c>, the theme sheet loads first, and
		/// equal-specificity rules resolve by order — so a hover rule living in the theme loses
		/// silently and the row simply never lights up. Pinned because "it looks like every other
		/// row, so reuse its class" is exactly the change somebody would make.
		/// </remarks>
		[Test]
		public void TheRowHoverRuleIsDeclaredInThisSheet()
		{
			string uss = System.IO.File.ReadAllText(System.IO.Path.Combine(
				System.IO.Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Faction/UIFactions.uss"));

			Assert.GreaterOrEqual(uss.IndexOf(".faction-row:hover", System.StringComparison.Ordinal), 0,
				"the faction row must declare its own hover state; the theme's is out-ordered by .faction-row's own background");

			Assert.GreaterOrEqual(uss.IndexOf("transition: background-color", System.StringComparison.Ordinal), 0,
				"and the row must transition, or the hover snaps rather than fades");
		}

		/// <summary>
		/// A <c>FactionAllianceLevel</c> maps to a section that the UXML actually declares.
		/// </summary>
		/// <remarks>
		/// The panel builds section element names by lower-casing each enum member, so the enum's
		/// spelling and the UXML's are one string apart. Renaming either half leaves the panel
		/// inert rather than wrong-looking, which is the failure this catches at the moment it is
		/// introduced rather than at the next launch.
		/// </remarks>
		[Test]
		public void EveryAllianceLevelHasASectionInTheTree()
		{
			string uxml = System.IO.File.ReadAllText(System.IO.Path.Combine(
				System.IO.Directory.GetCurrentDirectory(), UxmlPath));

			foreach (FactionAllianceLevel level in System.Enum.GetValues(typeof(FactionAllianceLevel)))
			{
				string suffix = level.ToString().ToLowerInvariant();
				Assert.IsTrue(uxml.Contains($"name=\"faction-section-{suffix}\""),
					$"the UXML must declare a section for FactionAllianceLevel.{level} as 'faction-section-{suffix}'");
				Assert.IsTrue(uxml.Contains($"name=\"faction-section-rows-{suffix}\""),
					$"and a row container for it as 'faction-section-rows-{suffix}'");
			}
		}

		private static string FactionSourcePath()
		{
			return System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Faction/UITKFactions.cs");
		}

		private static string ReadPanelSource()
		{
			string path = FactionSourcePath();
			Assert.IsTrue(System.IO.File.Exists(path), $"UITKFactions.cs not found at {path}.");
			return System.IO.File.ReadAllText(path);
		}
	}
}
