using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pet panel's attack priority rows on a live panel.
	/// </summary>
	[TestFixture]
	public class PetPriorityPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/FishMMO/Client/GUI/World/Pet/UIPetControl.uxml";

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;
		private UIDocument document;
		private UITKPetControl panel;

		[SetUp]
		public void SetUp()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(ProjectFiles.Path(PanelSettingsPath));
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ProjectFiles.Path(UxmlPath));
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"markup must exist at {UxmlPath}");

			texture = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			host = new GameObject("PetPriorityPanelTests");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			panel = host.AddComponent<UITKPetControl>();
			panel.Document = document;
			panel.StartOpen = true;
			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(panel, null);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null) Object.DestroyImmediate(host);
			if (settings != null) Object.DestroyImmediate(settings);
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private string Row(int slot)
		{
			Label label = document.rootVisualElement.Q<Label>("pet-priority-label-" + slot);
			return label != null ? label.text : "<no label>";
		}

		private static void Press(Button button)
		{
			using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
			{
				submit.target = button;
				button.SendEvent(submit);
			}
		}

		/// <summary>The six commands sit two to a row, not in one column (which made the panel 426 units tall).</summary>
		[UnityTest]
		public IEnumerator TheCommandsAreTwoToARow()
		{
			RenderTexture texture = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			try
			{
				settings.targetTexture = texture;
				panel.Show();
				for (int frame = 0; frame < 10; ++frame)
				{
					yield return null;
				}

				VisualElement commands = document.rootVisualElement.Q("pet-commands");
				Button attack = document.rootVisualElement.Q<Button>("pet-attack");
				Button follow = document.rootVisualElement.Q<Button>("pet-follow");
				LogAssert.IsNotNull(commands, "the command block is in the tree");
				string measured = $"commands content {commands.contentRect.width:0.#} wide; attack {attack.layout.width:0.#} wide " +
					$"(margins {attack.resolvedStyle.marginLeft:0.#}/{attack.resolvedStyle.marginRight:0.#}) at y {attack.layout.y:0.#}; " +
					$"follow at y {follow.layout.y:0.#}, x {follow.layout.x:0.#}";
				LogAssert.IsTrue(Mathf.Abs(attack.layout.y - follow.layout.y) < 0.5f && follow.layout.x > attack.layout.x,
					"Attack and Follow share a row: " + measured);

				Button passive = document.rootVisualElement.Q<Button>("pet-stance-passive");
				Button aggressive = document.rootVisualElement.Q<Button>("pet-stance-aggressive");
				LogAssert.IsTrue(Mathf.Abs(passive.layout.y - aggressive.layout.y) < 0.5f,
					$"the three stances share a row: passive at y {passive.layout.y:0.#}, aggressive at y {aggressive.layout.y:0.#}");
			}
			finally
			{
				settings.targetTexture = texture;
				texture.Release();
				Object.DestroyImmediate(texture);
			}
		}

		[UnityTest]
		public IEnumerator MovingARowUp_SwapsItWithTheOneAbove()
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}
			panel.Show();
			panel.OnTogglePriorityPanel();
			for (int frame = 0; frame < 5; ++frame)
			{
				yield return null;
			}

			string first = Row(0);
			string second = Row(1);
			LogAssert.IsTrue(first.StartsWith("1.") && second.StartsWith("2."), $"the rows are drawn: '{first}', '{second}'");

			Button up = document.rootVisualElement.Q<Button>("pet-priority-up-1");
			LogAssert.IsNotNull(up, "row 2 has a move-up button");
			Press(up);
			for (int frame = 0; frame < 3; ++frame)
			{
				yield return null;
			}

			LogAssert.IsTrue(Row(0).Substring(3) == second.Substring(3) && Row(1).Substring(3) == first.Substring(3),
				$"pressing up on row 2 swaps it with row 1; rows now '{Row(0)}', '{Row(1)}' (were '{first}', '{second}')");
		}
	}
}
