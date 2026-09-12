using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for issue #278 — clicking a buff off the HUD strip, and what its tooltip says.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three reports, and the first two are the same sentence read twice: "Unable to left click to
	/// remove buffs" and "Debuffs should not be clickable to remove". Neither was ever implemented —
	/// the strip registered pointer enter and leave on each icon and nothing else, and there was no
	/// client-to-server message for a click to send even if one had been registered. The tooltip
	/// said "Left Mouse Button to remove" anyway, so the strip has always advertised a feature it
	/// did not have.
	/// </para>
	/// <para>
	/// The third is a tooltip that under-reports and mis-reports: no duration, no tick cadence, no
	/// stack ceiling, and — on the reported movement-speed buff — a bonus attribute printed as a
	/// bare "30" when the buff grants 30 PERCENT. The rule, the tooltip and the request are pinned
	/// here; the panel is mounted on a real <see cref="UIDocument"/> for the parts that live in a
	/// tree.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BuffDismissTooltipTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Buff/UIBuff.uxml";
		private const string DismissBroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Character/DismissBuffBroadcast.cs";
		private const string HandlerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Connection.cs";

		private GameObject host;
		private UITKBuff buffPanel;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the buff UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UIBuff");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;

			// A start-hidden HUD panel: the document is off until something shows it.
			document.enabled = false;

			buffPanel = host.AddComponent<UITKBuff>();
			buffPanel.Document = document;
			buffPanel.StartOpen = false;
			buffPanel.IsAlwaysOpen = false;
			buffPanel.CloseOnQuitToMenu = true;
			buffPanel.ReleasesCursor = false;
			buffPanel.CloseOnEscape = false;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(buffPanel, null);

			buffPanel.Show();
			LogAssert.IsTrue(buffPanel.Visible, "the strip must be up before anything is asserted about its tree");
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}

			Resources.UnloadUnusedAssets();
		}

		/// <summary>
		/// A buff template with nothing but the fields under test filled in.
		/// </summary>
		private sealed class TestBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		/// <summary>
		/// A buff that counts charges, so the live-state rows have one to report.
		/// </summary>
		private sealed class ChargedBuffTemplate : BaseBuffTemplate
		{
			public override int InitialCharges => 5;
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		private static TestBuffTemplate NewTemplate(string name, bool isDebuff = false, float duration = 0.0f)
		{
			TestBuffTemplate template = ScriptableObject.CreateInstance<TestBuffTemplate>();
			template.name = name;
			template.IsDebuff = isDebuff;
			template.Duration = duration;
			template.AddToCache(name);
			return template;
		}

		/// <summary>The value of a stat row, or null when no row carries this label.</summary>
		private static string StatValue(TooltipContent content, string label)
		{
			for (int i = 0; i < content.Rows.Count; ++i)
			{
				if (content.Rows[i].Label == label)
				{
					return content.Rows[i].Value;
				}
			}
			return null;
		}

		private static bool HasRow(TooltipContent content, TooltipRowKind kind)
		{
			for (int i = 0; i < content.Rows.Count; ++i)
			{
				if (content.Rows[i].Kind == kind)
				{
					return true;
				}
			}
			return false;
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  The rule — one expression, read by the hint, the click and the server
		// ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void OnlyABuffMayBeDismissed()
		{
			/* The polarity the server depends on. Report point 2 is "debuffs should not be able to
			 * be removed", and the whole answer is this one property — so if it ever inverts, every
			 * test below this one is asserting the wrong world. */
			LogAssert.IsTrue(NewTemplate("A Buff").CanBeDismissedByPlayer,
				"a buff is what the player may click off");
			LogAssert.IsFalse(NewTemplate("A Debuff", isDebuff: true).CanBeDismissedByPlayer,
				"a debuff is on you until it expires or something dispels it");
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  The tooltip — the reported movement-speed buff, read back row by row
		// ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheReportedMoveSpeedBuffSaysPlusThirtyPercent()
		{
			/* The exact defect: "increased movement speed" hovered and read `Move Speed 30`. Move
			 * Speed is a percentage attribute whose value is in percentage POINTS — KCCController
			 * multiplies RunSpeed by moveSpeedModifier.FinalValueAsPct — so 30 means +30%, and a
			 * bare 30 is wrong by two orders of magnitude in the direction that matters. */
			CharacterAttributeTemplate moveSpeed = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			moveSpeed.name = "Move Speed";
			moveSpeed.InitialValue = 100;
			moveSpeed.IsPercentage = true;

			AttributeBuffTemplate template = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			template.name = "Minor Increase Move Speed";
			template.Duration = 360.0f;
			template.AddToCache(template.name);
			template.BonusAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = moveSpeed, Value = 30 },
			};

			TooltipContent content = template.BuildContent();

			LogAssert.AreEqual("+30%", StatValue(content, "Move Speed"),
				"a percentage attribute's modifier must carry its unit, or the buff reads as 30x");

			// The authored facts the report said were missing entirely.
			LogAssert.AreEqual("360s", StatValue(content, "Duration"),
				"the buff's duration must be on the tooltip");
			LogAssert.IsTrue(HasRow(content, TooltipRowKind.Subtitle),
				"the tooltip must say what kind of thing this is");
		}

		[Test]
		public void APlainNumericAttributeIsStillSignedWithoutAUnit()
		{
			// The other half of the formatter: a flat value gains a sign and nothing else.
			CharacterAttributeTemplate armour = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			armour.name = "Armour";
			armour.IsPercentage = false;

			CharacterAttributeTemplate speed = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			speed.name = "Move Speed";
			speed.IsPercentage = true;

			LogAssert.AreEqual("+5", armour.FormatValue(5), "a flat bonus is signed but has no unit");
			LogAssert.AreEqual("-5", armour.FormatValue(-5), "a penalty keeps its own sign");
			LogAssert.AreEqual("+5%", speed.FormatValue(5),
				"a percentage attribute is reported in percent — its value is in percentage points");
		}

		[Test]
		public void ADebuffIsLabelledADebuffAndAPermanentBuffSaysSo()
		{
			TestBuffTemplate debuff = NewTemplate("A Debuff", isDebuff: true, duration: 12.0f);
			TooltipContent debuffContent = debuff.BuildContent();
			LogAssert.AreEqual("Debuff", debuffContent.Rows[FindRow(debuffContent, TooltipRowKind.Subtitle)].Label,
				"a debuff must be labelled as one");
			LogAssert.AreEqual("12s", StatValue(debuffContent, "Duration"), "a timed debuff shows its duration");

			TestBuffTemplate permanent = NewTemplate("A Blessing");
			permanent.IsPermanent = true;
			TooltipContent permanentContent = permanent.BuildContent();
			LogAssert.AreEqual("Permanent", StatValue(permanentContent, "Duration"),
				"a permanent buff has no duration to count down, and saying 0s would be a lie");
		}

		[Test]
		public void TickRateAndMaxStacksAppearOnlyWhenTheBuffHasThem()
		{
			TestBuffTemplate plain = NewTemplate("Plain", duration: 10.0f);
			TooltipContent plainContent = plain.BuildContent();
			LogAssert.IsNull(StatValue(plainContent, "Tick Rate"),
				"a buff with no tick cadence must not print one");
			LogAssert.IsNull(StatValue(plainContent, "Max Stacks"),
				"a buff that does not stack must not print a stack ceiling");

			TestBuffTemplate ticking = NewTemplate("Ticking", duration: 10.0f);
			ticking.TickRate = 2.0f;
			ticking.MaxStacks = 3;
			TooltipContent tickingContent = ticking.BuildContent();
			LogAssert.AreEqual("2s", StatValue(tickingContent, "Tick Rate"), "the tick cadence must be shown");
			LogAssert.AreEqual("3", StatValue(tickingContent, "Max Stacks"), "the stack ceiling must be shown");
		}

		private static int FindRow(TooltipContent content, TooltipRowKind kind)
		{
			for (int i = 0; i < content.Rows.Count; ++i)
			{
				if (content.Rows[i].Kind == kind)
				{
					return i;
				}
			}
			LogAssert.Fail($"no {kind} row in the content");
			return -1;
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  Live state — the values only the owner's own strip can know
		// ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheOwnersStripReportsTimeLeftStacksAndCharges()
		{
			const float tickDelta = 1.0f / 30.0f;
			TestBuffTemplate template = NewTemplate("Stacking Hot", duration: 30.0f);
			template.TickRate = 0.0f;

			// Applied at tick 100 with a 30s duration at 30 tps: expiry is tick 1000.
			Buff buff = new Buff(template.ID, 100u, tickDelta, stacks: 2);

			TooltipContent content = template.BuildContent();
			BuffTooltip.AppendLiveState(content, buff, 400u);

			LogAssert.AreEqual("20s", StatValue(content, "Time Left"),
				"600 ticks at 30 tps is 20 seconds");
			LogAssert.AreEqual("3", StatValue(content, "Stacks"),
				"Stacks counts applications above the first, the same convention the icon label uses");
			LogAssert.IsNull(StatValue(content, "Charges"),
				"a buff that spends nothing has no charge pool to report");
		}

		[Test]
		public void APermanentBuffReportsNoTimeLeft()
		{
			const float tickDelta = 1.0f / 30.0f;
			TestBuffTemplate template = NewTemplate("A Blessing");
			template.IsPermanent = true;

			Buff buff = new Buff(template.ID, 100u, tickDelta);

			TooltipContent content = template.BuildContent();
			BuffTooltip.AppendLiveState(content, buff, 400u);

			/* RemainingSeconds answers a permanent buff with its authored duration, which would
			 * render as a timer that is not running. */
			LogAssert.IsNull(StatValue(content, "Time Left"),
				"a permanent buff must not show a countdown");
		}

		[Test]
		public void AChargedBuffReportsWhatIsLeftOfItsPool()
		{
			const float tickDelta = 1.0f / 30.0f;
			ChargedBuffTemplate template = ScriptableObject.CreateInstance<ChargedBuffTemplate>();
			template.name = "Absorb Shield";
			template.Duration = 10.0f;
			template.AddToCache(template.name);

			Buff buff = new Buff(template.ID, 100u, tickDelta, stacks: 1);

			TooltipContent content = template.BuildContent();
			BuffTooltip.AppendLiveState(content, buff, 100u);

			// RefreshCharges scales the pool by 1 + Stacks, so two applications hold ten.
			LogAssert.AreEqual("10", StatValue(content, "Charges"),
				"a spent pool is the most useful number on the tooltip of a shield");
		}

		[Test]
		public void LiveStateIsSkippedRatherThanGuessedAt()
		{
			TestBuffTemplate template = NewTemplate("Plain", duration: 10.0f);
			TooltipContent content = template.BuildContent();
			int before = content.Rows.Count;

			BuffTooltip.AppendLiveState(content, null, 400u);
			BuffTooltip.AppendLiveState(null, new Buff(template.ID, 100u, 1.0f / 30.0f), 400u);

			LogAssert.AreEqual(before, content.Rows.Count, "nothing to report must add nothing");
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  The strip — mounted on the UXML the player sees
		// ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheStripMountsItsIconList()
		{
			VisualElement list = document.rootVisualElement.Q("buff-list");
			LogAssert.IsNotNull(list, "the strip's icon container must exist in the cloned tree");
			LogAssert.IsFalse(CanDismiss(1234),
				"a strip showing nothing has nothing to dismiss");
		}

		[Test]
		public void TheStripRefusesADebuffEvenWhenItIsShowingOne()
		{
			/* Report point 2, tested where it could actually go wrong. Debuffs live on their own
			 * panel, so today a debuff entry cannot reach this strip — but relying on that is
			 * relying on a layout decision, and the strip's answer is supposed to come from the
			 * template. Seed the model directly and ask. */
			TestBuffTemplate debuff = NewTemplate("A Debuff", isDebuff: true, duration: 12.0f);
			TestBuffTemplate buff = NewTemplate("A Buff", duration: 12.0f);

			SeedEntry(debuff.ID, debuff);
			SeedEntry(buff.ID, buff);

			LogAssert.IsFalse(CanDismiss(debuff.ID),
				"a debuff on the buff strip is still not dismissable");
			LogAssert.IsTrue(CanDismiss(buff.ID),
				"a buff on the strip is dismissable");
		}

		[Test]
		public void NoClientMeansNoRequest()
		{
			// Nothing is sent through a client that is not there, whatever is on the strip.
			TestBuffTemplate buff = NewTemplate("A Buff", duration: 12.0f);
			SeedEntry(buff.ID, buff);

			LogAssert.IsNull(buffPanel.Client, "the mounted strip has no client");
			LogAssert.IsFalse(TryRequestDismiss(buff.ID),
				"a dismissal that cannot be sent must report that it was not");
			LogAssert.IsFalse(TryRequestDismiss(buff.ID + 999),
				"and an id the strip is not showing is refused outright");
		}

		/// <summary>
		/// Calls the strip's dismissal decision. Panel-internal, so reached by reflection — the
		/// suite's own convention for a client panel seam, and the lookup is asserted so a rename
		/// fails loudly here rather than quietly making the tests above vacuous.
		/// </summary>
		private bool CanDismiss(int templateID)
		{
			return (bool)PanelMethod("CanDismiss").Invoke(buffPanel, new object[] { templateID });
		}

		/// <summary>Calls the strip's dismissal request path.</summary>
		private bool TryRequestDismiss(int templateID)
		{
			return (bool)PanelMethod("TryRequestDismiss").Invoke(buffPanel, new object[] { templateID });
		}

		private static MethodInfo PanelMethod(string name)
		{
			MethodInfo info = typeof(UITKBuffContainer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKBuffContainer must still declare {name}");
			return info;
		}

		/// <summary>
		/// Puts one entry into the strip's model without standing up a networked character.
		/// </summary>
		/// <remarks>
		/// The model is a dictionary of a private nested struct, and the only path into it —
		/// <c>AddBuffGroup</c> — filters on the event's owning controller, which needs an
		/// <see cref="IPlayerCharacter"/>. This suite reflects into private state elsewhere for
		/// exactly this reason; the field lookups are asserted so a rename fails loudly here rather
		/// than silently making the two tests above vacuous.
		/// </remarks>
		private void SeedEntry(int templateID, BaseBuffTemplate template)
		{
			FieldInfo entriesField = typeof(UITKBuffContainer).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(entriesField, "UITKBuffContainer must still hold its entry model in `entries`");

			var entries = entriesField.GetValue(buffPanel) as System.Collections.IDictionary;
			LogAssert.IsNotNull(entries, "`entries` must be a dictionary keyed by template ID");

			System.Type entryType = entriesField.FieldType.GetGenericArguments()[1];
			object entry = System.Activator.CreateInstance(entryType);

			FieldInfo templateField = entryType.GetField("Template");
			LogAssert.IsNotNull(templateField, "the entry struct must still carry its Template");
			templateField.SetValue(entry, template);

			entries[templateID] = entry;
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  The server — the half that cannot be driven without a live connection
		// ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheServerRefusesADebuffAndReadsTheSameRule()
		{
			/* The client is a request, not a decision. What makes report point 2 true for real is
			 * that the server tests the rule itself before removing anything, on ITS OWN copy of
			 * the buff — so a modified client that sends a dismissal for a debuff changes nothing. */
			string handler = MethodBody(HandlerPath,
				"private void OnClientDismissBuffBroadcastReceived",
				"private void OnClientRespawnAtBindPointBroadcastReceived");

			LogAssert.IsTrue(handler.Contains("CanBeDismissedByPlayer"),
				"the handler must read the same rule the tooltip and the strip read");
			LogAssert.IsTrue(handler.Contains("ConnectionCharacters.TryGetValue(conn"),
				"the buff must be looked for on the SENDER's own character, never a named one");
			LogAssert.IsTrue(handler.Contains("buffController.Buffs.TryGetValue"),
				"an id the sender does not hold must remove nothing");
			LogAssert.IsTrue(handler.Contains("buffController.Remove(msg.TemplateID)"),
				"the removal must go through the controller, so the owner is reconciled");
		}

		[Test]
		public void TheDismissalRequestCarriesTheTemplateID()
		{
			/* The key the server looks up with. Both ends have to agree on it, and the container is
			 * keyed by template ID already — an index into the strip's own dictionary would have to
			 * survive a round trip it cannot. */
			string source = ReadSource(DismissBroadcastPath);
			LogAssert.IsTrue(source.Contains("struct DismissBuffBroadcast : IBroadcast"),
				"the request must be a broadcast the server can register");
			LogAssert.IsTrue(source.Contains("public int TemplateID;"),
				"the payload is the buff's template ID");
		}

		private static string ReadSource(string relativePath)
		{
			string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(System.IO.File.Exists(path), $"{relativePath} not found at {path}.");
			return System.IO.File.ReadAllText(path);
		}

		/// <summary>The body of a named method in a source file.</summary>
		private static string MethodBody(string relativePath, string signature, string nextSymbol)
		{
			string source = ReadSource(relativePath);

			int start = source.IndexOf(signature, System.StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"{relativePath} must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, System.StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"{relativePath}: the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}
	}
}
