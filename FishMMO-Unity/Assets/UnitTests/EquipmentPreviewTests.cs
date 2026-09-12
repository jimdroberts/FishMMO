using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The equipment panel's character preview frames the character, and the pieces it depends on
	/// are wired.
	/// </summary>
	/// <remarks>
	/// <para>The preview did not work and nothing said so, which is why these tests exist rather
	/// than a note. Three independent things were wrong at once, and each of them fails silently:
	/// the playable prefabs had lost the camera reference (so there was nothing to render with),
	/// the character's own meshes sat on the Default layer while the camera culled to Player (so
	/// there was nothing to see), and the panel never displayed a texture at all. Every one of
	/// those produces the same picture — an empty box — with no warning in the log.</para>
	///
	/// <para>The framing arithmetic is tested directly because it is the one part of this that is
	/// pure: it decides whether the whole character, or just its knees, ends up in the frame, and
	/// it does so from bounds a test can write down.</para>
	/// </remarks>
	[TestFixture]
	public class EquipmentPreviewTests
	{
		private const string PrefabFolder = "Assets/Prefabs/Shared/Entity/PlayableCharacters";

		/// <summary>The playable characters a player can actually be.</summary>
		private static readonly string[] PlayablePrefabs = { "Human", "Orc", "Elf" };

		// ── Framing ───────────────────────────────────────────────────────────

		/// <summary>A 1.8 m tall, 0.5 m wide subject centred on the origin.</summary>
		private static Bounds StandingCharacter()
		{
			return new Bounds(new Vector3(0.0f, 0.9f, 0.0f), new Vector3(0.5f, 1.8f, 0.3f));
		}

		[Test]
		public void AStandingCharacterIsFramedByItsHeight()
		{
			float size = EquipmentPreviewRenderer.ComputeOrthographicSize(StandingCharacter(), 1.55f);

			// Half of 1.8 m, plus the padding. Anything less crops the head or the feet.
			LogAssert.IsTrue(size > 0.9f, $"a 1.8 m subject needs more than half its height; got {size}");
			LogAssert.IsTrue(size < 1.2f, $"and not so much that the character is a speck; got {size}");
		}

		[Test]
		public void AWideSubjectIsFramedByItsWidth()
		{
			/* A broad subject in a viewport narrower than its aspect: fitting by height alone
			 * would put the sides of it outside the frame, which for a character means its arms
			 * and whatever it is holding. */
			Bounds wide = new Bounds(Vector3.zero, new Vector3(4.0f, 1.0f, 0.3f));

			float size = EquipmentPreviewRenderer.ComputeOrthographicSize(wide, 1.0f);

			LogAssert.IsTrue(size >= 2.0f, $"4 m across at aspect 1 needs a half-height of at least 2 m; got {size}");
		}

		[Test]
		public void TheAspectDecidesWhetherWidthOrHeightLimitsTheFrame()
		{
			/* One subject, four times as wide as it is tall, measured in two viewports. In the
			 * square one its width is what has to fit; in the four-times-wide one the width no
			 * longer matters and its height is. Pinning both ends is what catches a rule that only
			 * ever considers one of the two — which is the shape of the bug the framing replaced,
			 * where a fixed orthographic size framed a fixed slice of the world regardless of
			 * either. */
			Bounds broadSubject = new Bounds(Vector3.zero, new Vector3(8.0f, 2.0f, 0.3f));

			float squareViewport = EquipmentPreviewRenderer.ComputeOrthographicSize(broadSubject, 1.0f);
			float wideViewport = EquipmentPreviewRenderer.ComputeOrthographicSize(broadSubject, 4.0f);

			LogAssert.IsTrue(squareViewport > wideViewport * 2.0f,
				$"in a square viewport an 8x2 subject is limited by its width; got {squareViewport} vs {wideViewport}");
			LogAssert.IsTrue(wideViewport < 1.2f,
				$"in a 4:1 viewport only its 2 m height has to fit; got {wideViewport}");
		}

		[Test]
		public void ADegenerateSubjectStillProducesAUsableCamera()
		{
			/* A renderer with no size — an empty mesh, or a bone with nothing skinned to it — must
			 * not produce a zero-size camera: that renders nothing, and since the caller only
			 * re-frames when the frame is missing, it would never recover. */
			float size = EquipmentPreviewRenderer.ComputeOrthographicSize(new Bounds(Vector3.zero, Vector3.zero), 1.55f);

			LogAssert.IsTrue(size > 0.0f, $"a degenerate subject must still give a positive size; got {size}");
			LogAssert.IsFalse(float.IsNaN(size), "and not NaN");
		}

		[Test]
		public void ANonsenseAspectDoesNotPoisonTheSize()
		{
			float size = EquipmentPreviewRenderer.ComputeOrthographicSize(StandingCharacter(), 0.0f);

			LogAssert.IsTrue(size > 0.0f, "an aspect of zero falls back rather than dividing by it");
			LogAssert.IsFalse(float.IsNaN(size) || float.IsInfinity(size), $"got {size}");
		}

		[Test]
		public void TheCameraIsCentredOnTheSubjectNotOnItsFeet()
		{
			/* The bug this replaces: the authored camera sat at the character's local origin with
			 * an orthographic size of 0.85, so a 1.8 m character was framed from the knees down.
			 * The offset that was meant to lift it lived in a RectTransform's anchoredPosition,
			 * which does nothing under a plain Transform parent. */
			Vector3 position = EquipmentPreviewRenderer.ComputeCameraLocalPosition(StandingCharacter(), EquipmentPreviewRenderer.CameraDistance);

			LogAssert.IsTrue(Mathf.Abs(position.y - 0.9f) < 0.001f,
				$"the camera looks at the subject's centre (0.9), not its feet; got {position.y}");
			LogAssert.IsTrue(Mathf.Abs(position.z - EquipmentPreviewRenderer.CameraDistance) < 0.001f,
				$"and sits in front of it ({EquipmentPreviewRenderer.CameraDistance}); got {position.z}");
		}

		[Test]
		public void AnOffCentreSubjectIsStillCentred()
		{
			Bounds offset = new Bounds(new Vector3(0.4f, 1.1f, 0.2f), new Vector3(0.5f, 1.8f, 0.3f));

			Vector3 position = EquipmentPreviewRenderer.ComputeCameraLocalPosition(offset, 3.0f);

			LogAssert.IsTrue(Mathf.Abs(position.x - 0.4f) < 0.001f,
				$"the camera follows the subject sideways; got {position.x}");
			LogAssert.IsTrue(Mathf.Abs(position.y - 1.1f) < 0.001f,
				$"and vertically; got {position.y}");
			LogAssert.IsTrue(Mathf.Abs(position.z - 3.2f) < 0.001f,
				$"distance is measured from the subject, not the origin; got {position.z}");
		}

		// ── Layer rule ────────────────────────────────────────────────────────

		[Test]
		public void VisualHierarchiesMoveToThePlayerLayer()
		{
			int player = Constants.Layers.Index.Player;
			LogAssert.IsTrue(player >= 0, "this project must define a 'Player' layer for the preview to isolate against");

			GameObject root = new GameObject("PreviewLayerRoot");
			try
			{
				GameObject child = new GameObject("Child");
				child.transform.SetParent(root.transform, false);
				GameObject grandchild = new GameObject("Grandchild");
				grandchild.transform.SetParent(child.transform, false);

				// Instantiate copies a prefab's layers verbatim, so an inactive node arrives on
				// Default too and has to be moved for the same reason.
				GameObject inactive = new GameObject("Inactive");
				inactive.transform.SetParent(child.transform, false);
				inactive.SetActive(false);

				BaseCharacter.ApplyVisualLayer(root);

				LogAssert.AreEqual(player, root.layer, "the hierarchy root moves");
				LogAssert.AreEqual(player, child.layer, "so does its child");
				LogAssert.AreEqual(player, grandchild.layer, "and its grandchild");
				LogAssert.AreEqual(player, inactive.layer, "inactive children move too — the preview camera is authored inactive");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void ApplyVisualLayerToleratesNull()
		{
			// Called on the result of a failed addressable load, so it must not throw.
			BaseCharacter.ApplyVisualLayer(null);
		}

		// ── Prefab wiring ─────────────────────────────────────────────────────

		[Test]
		public void EveryPlayableCharacterPrefabCarriesItsPreviewCamera()
		{
			foreach (string name in PlayablePrefabs)
			{
				string path = $"{PrefabFolder}/{name}.prefab";
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				LogAssert.IsNotNull(prefab, $"the {name} prefab must exist at {path}");

				PlayerCharacter character = prefab.GetComponent<PlayerCharacter>();
				LogAssert.IsNotNull(character, $"{name} must carry a PlayerCharacter");

				Camera camera = character.EquipmentViewCamera;
				LogAssert.IsNotNull(camera,
					$"{name} has no equipment view camera. Its meshes are on the Player layer, so a null camera here is an empty preview box.");
				LogAssert.IsTrue(camera.gameObject.name == "EquipmentViewCamera",
					$"{name}'s preview camera is '{camera.gameObject.name}', which is not the camera the prefab authors");

				// The camera is authored on an inactive GameObject and the panel enables it by
				// hand; a camera that is missing from the hierarchy entirely is the failure above.
				LogAssert.IsTrue(camera.transform.IsChildOf(prefab.transform),
					$"{name}'s preview camera must belong to the character it photographs");
			}
		}

		[Test]
		public void ThePreviewCameraCullsToTheLayerTheCharacterIsDrawnOn()
		{
			int player = Constants.Layers.Index.Player;

			foreach (string name in PlayablePrefabs)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
				Camera camera = prefab.GetComponent<PlayerCharacter>().EquipmentViewCamera;

				LogAssert.AreEqual(1 << player, camera.cullingMask,
					$"{name}'s preview camera must cull to the character visual layer and nothing else");
			}
		}

		[Test]
		public void ThePreviewCameraIsAuthoredDisabled()
		{
			/* The panel enables it while the panel is open and hands it back afterwards. A camera
			 * that is live in the prefab renders the character into a texture every frame from
			 * world entry, whether or not anyone has opened the panel. */
			foreach (string name in PlayablePrefabs)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
				Camera camera = prefab.GetComponent<PlayerCharacter>().EquipmentViewCamera;

				LogAssert.IsFalse(camera.gameObject.activeSelf,
					$"{name}'s preview camera object must be authored inactive");
			}
		}

		// ── Camera handover ───────────────────────────────────────────────────

		[Test]
		public void ReleasingTheRendererGivesTheCameraBackDisabled()
		{
			GameObject host = new GameObject("PreviewCameraHost");
			try
			{
				Camera camera = host.AddComponent<Camera>();
				camera.enabled = true;

				EquipmentPreviewRenderer renderer = new EquipmentPreviewRenderer();
				renderer.Configure(camera);
				LogAssert.IsFalse(camera.enabled, "adopting the camera stops it rendering on its own schedule");

				renderer.Configure(null);
				LogAssert.IsFalse(camera.enabled, "and handing it back leaves it disabled");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void HandingOverToASecondCameraReleasesTheFirst()
		{
			/* The panel is handed the current character's camera on every open, so a handover that
			 * does not release what it replaces leaves the previous character's camera enabled and
			 * rendering forever. */
			GameObject firstHost = new GameObject("FirstPreviewCamera");
			GameObject secondHost = new GameObject("SecondPreviewCamera");
			try
			{
				Camera first = firstHost.AddComponent<Camera>();
				Camera second = secondHost.AddComponent<Camera>();
				first.enabled = true;

				EquipmentPreviewRenderer renderer = new EquipmentPreviewRenderer();
				renderer.Configure(first);
				renderer.Configure(second);

				LogAssert.IsFalse(first.enabled, "the camera being replaced is released");
				LogAssert.AreSame(second, renderer.Camera, "and the new one is the one being driven");
			}
			finally
			{
				Object.DestroyImmediate(firstHost);
				Object.DestroyImmediate(secondHost);
			}
		}

		[Test]
		public void ReleasingRestoresTheTargetTextureThePrefabAuthored()
		{
			/* A camera handed back enabled with no target texture draws straight onto the screen.
			 * The prefab authors one, so it goes back the way it was found rather than being
			 * cleared. */
			GameObject host = new GameObject("PreviewCameraHost");
			RenderTexture authored = new RenderTexture(64, 64, 16);
			try
			{
				Camera camera = host.AddComponent<Camera>();
				camera.targetTexture = authored;

				EquipmentPreviewRenderer renderer = new EquipmentPreviewRenderer();
				renderer.Configure(camera);
				renderer.Configure(null);

				LogAssert.AreSame(authored, camera.targetTexture,
					"the camera's own render texture is put back");
			}
			finally
			{
				Object.DestroyImmediate(host);
				Object.DestroyImmediate(authored);
			}
		}

		[Test]
		public void ConfigureWithNoCameraIsHarmless()
		{
			EquipmentPreviewRenderer renderer = new EquipmentPreviewRenderer();

			renderer.Configure(null);
			LogAssert.IsNull(renderer.Camera, "there is no camera to drive");
			LogAssert.IsFalse(renderer.IsReady, "and nothing to render with");
			LogAssert.IsNull(renderer.Texture, "and no texture to show");

			// The panel calls this on every tick until a character has a camera.
			renderer.Dispose();
		}
	}
}
