using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the overhead nameplate model: what its rows do, how a style resolves a colour, and the
	/// projection fact that made a single anchor necessary in the first place.
	/// </summary>
	/// <remarks>
	/// The last fixture below is the one worth reading. Overhead text used to be several
	/// <see cref="WorldLabel"/>s a few centimetres apart in the world, projected separately and then
	/// pushed apart by a fixed number of screen points. That correction assumed the world gap
	/// projects to a stable screen gap, and it does not: pitch the camera and it collapses. The test
	/// measures the collapse against a real camera, so the reason the rows now live inside one
	/// element cannot be quietly undone by someone who thinks the stack could be reassembled from
	/// separate labels again.
	/// </remarks>
	[TestFixture]
	public class NameplateModelTests
	{
		private GameObject host;
		private Nameplate plate;

		[SetUp]
		public void SetUp()
		{
			host = new GameObject("NameplateHost");
			plate = host.AddComponent<Nameplate>();
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
		}

		// --- Rows ---------------------------------------------------------------------------

		[Test]
		public void TheDefaultRows_StackNameThenGuildThenType()
		{
			// Written out of order on purpose: the stack is the slot's business, not the caller's.
			plate.SetLine(NameplateSlot.InteractableType, "<Banker>");
			plate.SetLine(NameplateSlot.Name, "Bram");
			plate.SetLine(NameplateSlot.GuildName, "[Ironhand]");

			Assert.AreEqual(3, plate.LineCount);
			Assert.AreEqual("Bram", plate.Lines[0].Text);
			Assert.AreEqual("[Ironhand]", plate.Lines[1].Text);
			Assert.AreEqual("<Banker>", plate.Lines[2].Text);
		}

		[Test]
		public void ACustomRow_LandsWhereItsOrderPutsIt()
		{
			plate.SetLine(NameplateSlot.Name, "Bram");
			plate.SetLine(NameplateSlot.GuildName, "[Ironhand]");

			// Between the name and the guild line, which is what an arbitrary order is for.
			plate.SetLine((int)NameplateSlot.Custom, 50, "Level 42");

			Assert.AreEqual("Bram", plate.Lines[0].Text);
			Assert.AreEqual("Level 42", plate.Lines[1].Text);
			Assert.AreEqual("[Ironhand]", plate.Lines[2].Text);
		}

		[Test]
		public void ABlankRow_IsRemovedRatherThanLeftEmpty()
		{
			/* A player leaving a guild clears the row by writing what they have — the empty
			 * string. An empty row that kept its place would leave a line-height gap under the
			 * name of every character in the world who is not in a guild. */
			plate.SetLine(NameplateSlot.Name, "Bram");
			plate.SetLine(NameplateSlot.GuildName, "[Ironhand]");
			Assert.AreEqual(2, plate.LineCount);

			plate.SetLine(NameplateSlot.GuildName, string.Empty);

			Assert.AreEqual(1, plate.LineCount);
			Assert.IsFalse(plate.TryGetLine(NameplateSlot.GuildName, out NameplateLine _));
		}

		[Test]
		public void RewritingARowWithTheSameValue_DoesNotMoveTheRevision()
		{
			/* The renderer rebuilds a plate's rows when this moves. The naming system re-resolves
			 * names, and a revision that moved on every write would re-lay-out every plate on
			 * screen for text that had not changed. */
			plate.SetLine(NameplateSlot.Name, "Bram");
			int revision = plate.Revision;

			plate.SetLine(NameplateSlot.Name, "Bram");
			Assert.AreEqual(revision, plate.Revision, "an identical write must be a no-op");

			plate.SetLine(NameplateSlot.Name, "Bramwell");
			Assert.AreNotEqual(revision, plate.Revision, "a real change must be visible to the renderer");
		}

		[Test]
		public void ClearingAMissingRow_ChangesNothing()
		{
			plate.SetLine(NameplateSlot.Name, "Bram");
			int revision = plate.Revision;

			Assert.IsFalse(plate.ClearLine(NameplateSlot.Status));
			Assert.AreEqual(revision, plate.Revision);
		}

		// --- Style --------------------------------------------------------------------------

		[Test]
		public void TheNameRow_TakesTheStandingColourAndTheRestDoNot()
		{
			plate.SetLine(NameplateSlot.Name, "Bram");
			plate.SetLine(NameplateSlot.GuildName, "[Ironhand]");
			plate.AllianceTint = Color.red;

			NameplateStyle style = plate.Style;
			Assert.AreEqual(Color.red, plate.Lines[0].ResolveColor(in style, plate.AllianceTint),
				"the default plate draws the name in the standing colour");
			Assert.AreEqual(style.SecondaryColor, plate.Lines[1].ResolveColor(in style, plate.AllianceTint),
				"rows under the name stay the plate's secondary colour");
		}

		[Test]
		public void ARowWrittenWithAColour_KeepsItWhateverTheStandingIs()
		{
			// An interactable's title is authored green and must not turn red over a hostile NPC.
			plate.SetLine(NameplateSlot.InteractableType, "<Banker>", Color.green);
			plate.AllianceTint = Color.red;

			NameplateStyle style = plate.Style;
			Assert.AreEqual(Color.green, plate.Lines[0].ResolveColor(in style, plate.AllianceTint));
		}

		[Test]
		public void TheBackground_IsBlendedTowardTheStandingColourAndNeverReplacedByIt()
		{
			/* A background painted in the raw standing colour is a solid red box that swallows the
			 * name in front of it. The blend is what keeps the plate dark and still readable as
			 * hostile at a glance. */
			NameplateStyle style = NameplateStyle.Default;
			Color background = style.ResolveBackgroundColor(Color.red);

			Assert.AreEqual(style.BackgroundOpacity, background.a, 0.0001f,
				"the style's opacity decides the alpha, not the standing colour's");
			Assert.Greater(background.r, style.BackgroundColor.r, "the blend must move toward the tint");
			Assert.Less(background.r, 1.0f, "and must not arrive at it");
		}

		[Test]
		public void AFixedTint_IgnoresTheStandingColourEntirely()
		{
			NameplateStyle style = NameplateStyle.Default;
			style.NameTint = NameplateTint.Fixed;
			style.NameColor = Color.white;

			Assert.AreEqual(Color.white, style.ResolveNameColor(Color.red));
		}

		[Test]
		public void AStyleAsset_OverridesTheDefaultAndAnInlineStyleOverridesTheAsset()
		{
			NameplateStyleAsset asset = ScriptableObject.CreateInstance<NameplateStyleAsset>();
			try
			{
				asset.SetStyle(NameplateStylePresets.Boss);
				plate.SetStyle(asset);
				Assert.IsTrue(plate.Style.ShowBorder, "the referenced asset decides the look");

				NameplateStyle plain = NameplateStylePresets.Plain;
				plate.SetStyle(plain);
				Assert.IsFalse(plate.Style.ShowBorder, "an inline style wins over the reference");
				Assert.IsFalse(plate.Style.ShowBackground);
			}
			finally
			{
				Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void EditingASharedStyle_MovesEveryBorrowingPlatesStyleRevision()
		{
			/* The renderer caches a plate's resolved style and only re-reads it when this moves.
			 * Without the asset's own revision folded in, editing a shared style in play mode
			 * would change nothing on screen. */
			NameplateStyleAsset asset = ScriptableObject.CreateInstance<NameplateStyleAsset>();
			try
			{
				plate.SetStyle(asset);
				int revision = plate.StyleRevision;

				asset.SetStyle(NameplateStylePresets.Boss);

				Assert.AreNotEqual(revision, plate.StyleRevision);
			}
			finally
			{
				Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void TheStandingColour_OnlyMovesTheStyleRevisionWhenItActuallyChanges()
		{
			plate.AllianceTint = Color.red;
			int revision = plate.StyleRevision;

			plate.AllianceTint = Color.red;
			Assert.AreEqual(revision, plate.StyleRevision, "the sweep re-writes this every pass");

			plate.AllianceTint = Color.green;
			Assert.AreNotEqual(revision, plate.StyleRevision);
		}

		// --- Registration -------------------------------------------------------------------

		[Test]
		public void AHiddenPlate_KeepsItsRowsAndItsObjectStaysActive()
		{
			/* Visibility is a flag, not the GameObject's active state. A plate is written into
			 * while it is hidden — a name resolves, a guild arrives — and deactivating the object
			 * to hide it would deregister the plate and drop that state on the floor.
			 *
			 * Registration itself is driven by OnEnable, which the editor does not run outside
			 * play mode, so what is pinned here is the half that can be: hiding a plate is not
			 * something that touches its object or its rows. */
			plate.Visible = false;
			plate.SetLine(NameplateSlot.Name, "Bram");

			Assert.AreEqual(1, plate.LineCount);
			Assert.IsFalse(plate.Visible);
			Assert.IsTrue(host.activeSelf, "hiding a plate must not deactivate what it hangs on");
		}

		[Test]
		public void GetOrCreate_ReturnsAnAuthoredPlateRatherThanBuildingASecond()
		{
			Assert.AreSame(plate, Nameplate.GetOrCreate(host.transform, Vector3.up));
		}

		[Test]
		public void GetOrCreate_BuildsAHiddenPlateForAnObjectWithNone()
		{
			GameObject crate = new GameObject("Crate");
			try
			{
				Nameplate built = Nameplate.GetOrCreate(crate.transform, new Vector3(0.0f, 1.5f, 0.0f));

				Assert.IsNotNull(built);
				Assert.IsFalse(built.Visible, "a plate is shown by the client's rules, never by existing");
				Assert.AreEqual(0, built.LineCount);
				Assert.AreEqual(new Vector3(0.0f, 1.5f, 0.0f), built.transform.localPosition);
			}
			finally
			{
				Object.DestroyImmediate(crate);
			}
		}
	}

	/// <summary>
	/// The projection fact that a nameplate has to be one anchor: a fixed world-space gap between
	/// two points does not project to a fixed screen-space gap.
	/// </summary>
	[TestFixture]
	public class NameplateProjectionGeometryTests
	{
		private GameObject cameraHost;
		private Camera camera;

		[SetUp]
		public void SetUp()
		{
			cameraHost = new GameObject("ProbeCamera");
			camera = cameraHost.AddComponent<Camera>();
			camera.fieldOfView = 60.0f;
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 500.0f;
			camera.aspect = 16.0f / 9.0f;
		}

		[TearDown]
		public void TearDown()
		{
			if (cameraHost != null)
			{
				Object.DestroyImmediate(cameraHost);
			}
		}

		/// <summary>The screen-space vertical gap between two world points, five metres out.</summary>
		private float ProjectedGap(float pitchDegrees)
		{
			Vector3 subject = new Vector3(0.0f, 1.85f, 0.0f);

			/* The camera orbits the subject at a fixed distance and looks straight at it, which is
			 * what the game's own camera does. Only the pitch changes. */
			float radians = pitchDegrees * Mathf.Deg2Rad;
			Vector3 offset = new Vector3(0.0f, Mathf.Sin(radians), -Mathf.Cos(radians)) * 5.0f;
			cameraHost.transform.position = subject + offset;
			cameraHost.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);

			// The two anchors the old nameplate stacked: the name row and the guild row beneath it.
			Vector3 top = camera.WorldToScreenPoint(subject);
			Vector3 bottom = camera.WorldToScreenPoint(subject + new Vector3(0.0f, -0.11f, 0.0f));
			return top.y - bottom.y;
		}

		[Test]
		public void TwoSeparatelyProjectedRows_LoseTheirGapAsTheCameraPitches()
		{
			float level = ProjectedGap(0.0f);
			float steep = ProjectedGap(75.0f);

			Assert.Greater(level, 1.0f, "level with the subject the two anchors are clearly apart");
			Assert.Less(steep, level * 0.5f,
				"looking down on the subject collapses the gap the old stacking assumed was constant — " +
				"which is why a nameplate is now one anchor with its rows laid out in points");
		}

		[Test]
		public void OneAnchorProjectsToOnePoint_WhateverTheCameraIsDoing()
		{
			/* The invariant that replaces it. A plate has a single anchor, so there is no gap to
			 * lose: whatever the camera does, its rows keep the layout the panel gave them. */
			GameObject host = new GameObject("Plate");
			try
			{
				host.transform.position = new Vector3(0.0f, 1.85f, 0.0f);
				Nameplate plate = host.AddComponent<Nameplate>();
				plate.SetLine(NameplateSlot.Name, "Bram");
				plate.SetLine(NameplateSlot.GuildName, "[Ironhand]");

				Assert.AreEqual(2, plate.LineCount, "two rows");
				Assert.AreEqual(host.transform.position, plate.WorldPosition, "one anchor");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}
	}
}
