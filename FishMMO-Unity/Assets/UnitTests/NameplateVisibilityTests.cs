using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the three nameplate rules the Gameplay tab exposes: NPC and other-player
	/// nameplates stay up inside their own configurable ranges, and the player's own nameplate
	/// stays up at all times.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three contracts are pinned. The settings contract: the values the panel writes are read
	/// back with the same meaning and kept inside the range the sweep can act on. The rule
	/// itself: <see cref="ClientNameplateDisplay.Decide"/> is pure arithmetic precisely so its
	/// truth table can be written down here. And the authoring contract the sweep depends on:
	/// every playable character prefab actually references its overhead labels — which none of
	/// them did before this feature, so a player's own name had nowhere to be drawn — and every
	/// character prefab authors those labels inactive, since the sweep only ever turns labels on
	/// by transition and a label authored active would be on for everyone from spawn.
	/// </para>
	/// <para>
	/// Every settings test runs against a scratch <see cref="Configuration"/> swapped in as the
	/// global store, so nothing here reads or writes the developer's own Configuration.cfg.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NameplateVisibilityTests
	{
		private const string OptionsUxml = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";
		private const string PlayableCharacterPrefabs = "Assets/Prefabs/Shared/Entity/PlayableCharacters";
		private const string CharacterPrefabs = "Assets/Prefabs/Shared/Entity";

		private Configuration previous;
		private int raised;

		[SetUp]
		public void SetUp()
		{
			previous = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-NameplateVisibilityTests")));

			raised = 0;
			ClientWorldLabelSettings.OnChanged += CountRaise;
		}

		[TearDown]
		public void TearDown()
		{
			ClientWorldLabelSettings.OnChanged -= CountRaise;
			RestoreGlobalSettings(previous);
		}

		private void CountRaise() => ++raised;

		private static void RestoreGlobalSettings(Configuration value)
		{
			FieldInfo field = typeof(Configuration).GetField(
				"globalSettings", BindingFlags.NonPublic | BindingFlags.Static);

			if (field != null)
			{
				field.SetValue(null, value);
			}
			else if (value != null)
			{
				Configuration.SetGlobalSettings(value);
			}
		}

		// --- Settings ----------------------------------------------------------------------------

		[Test]
		public void TheDefaults_MatchTheRequest()
		{
			/* NPC names up inside a range, own name up at all times: both must be what a fresh
			 * install does, or the feature exists only for players who find the options row. */
			LogAssert.IsTrue(ClientWorldLabelSettings.NpcNameRange > 0.0f,
				"a fresh install must show NPC nameplates inside some range");
			LogAssert.IsTrue(ClientWorldLabelSettings.PlayerNameRange > 0.0f,
				"a fresh install must show other players' nameplates inside some range");
			LogAssert.IsTrue(ClientWorldLabelSettings.ShowOwnName,
				"a fresh install must keep the player's own nameplate up");
		}

		[Test]
		public void TheDefaultRange_LiesInsideTheOfferedRangeAndTheDrawDistance()
		{
			LogAssert.IsTrue(
				ClientWorldLabelSettings.DefaultNpcNameRange >= ClientWorldLabelSettings.MinimumNpcNameRange &&
				ClientWorldLabelSettings.DefaultNpcNameRange <= ClientWorldLabelSettings.MaximumNpcNameRange,
				"the default range must be within the slider's range");

			/* A nameplate the range rule turns on is still culled by the layer's draw distance.
			 * A default range beyond the default draw distance would put names up that are
			 * never drawn, which reads as the setting not working. */
			LogAssert.IsTrue(ClientWorldLabelSettings.DefaultNpcNameRange <= ClientWorldLabelSettings.DefaultDistance,
				"the default NPC name range must not exceed the default draw distance");

			LogAssert.IsTrue(
				ClientWorldLabelSettings.DefaultPlayerNameRange >= ClientWorldLabelSettings.MinimumPlayerNameRange &&
				ClientWorldLabelSettings.DefaultPlayerNameRange <= ClientWorldLabelSettings.MaximumPlayerNameRange,
				"the default player range must be within the slider's range");
			LogAssert.IsTrue(ClientWorldLabelSettings.DefaultPlayerNameRange <= ClientWorldLabelSettings.DefaultDistance,
				"the default player name range must not exceed the default draw distance");
		}

		[Test]
		public void SetPlayerNameRange_ClampsAndRoundTripsLikeTheNpcRange()
		{
			ClientWorldLabelSettings.SetPlayerNameRange(10000.0f);
			LogAssert.AreEqual(ClientWorldLabelSettings.MaximumPlayerNameRange, ClientWorldLabelSettings.PlayerNameRange,
				"a range above the ceiling must be stored at the ceiling");

			ClientWorldLabelSettings.SetPlayerNameRange(-5.0f);
			LogAssert.AreEqual(ClientWorldLabelSettings.MinimumPlayerNameRange, ClientWorldLabelSettings.PlayerNameRange,
				"a range below the floor must be stored at the floor");

			ClientWorldLabelSettings.SetPlayerNameRange(0.0f);
			LogAssert.AreEqual(0.0f, ClientWorldLabelSettings.PlayerNameRange, "zero must read back as zero");

			ClientWorldLabelSettings.SetPlayerNameRange(float.NaN);
			LogAssert.AreEqual(ClientWorldLabelSettings.DefaultPlayerNameRange, ClientWorldLabelSettings.PlayerNameRange,
				"NaN through the setter must store the default");
		}

		[Test]
		public void TheTwoRanges_AreStoredIndependently()
		{
			/* One slider per kind of character. A shared key would make the second slider a
			 * confusing alias of the first. */
			ClientWorldLabelSettings.SetNpcNameRange(10.0f);
			ClientWorldLabelSettings.SetPlayerNameRange(50.0f);

			LogAssert.AreEqual(10.0f, ClientWorldLabelSettings.NpcNameRange, "the NPC range must keep its own value");
			LogAssert.AreEqual(50.0f, ClientWorldLabelSettings.PlayerNameRange, "the player range must keep its own value");
		}

		[Test]
		public void SetNpcNameRange_ClampsToTheOfferedRange()
		{
			ClientWorldLabelSettings.SetNpcNameRange(10000.0f);
			LogAssert.AreEqual(ClientWorldLabelSettings.MaximumNpcNameRange, ClientWorldLabelSettings.NpcNameRange,
				"a range above the ceiling must be stored at the ceiling");

			ClientWorldLabelSettings.SetNpcNameRange(-5.0f);
			LogAssert.AreEqual(ClientWorldLabelSettings.MinimumNpcNameRange, ClientWorldLabelSettings.NpcNameRange,
				"a range below the floor must be stored at the floor");
		}

		[Test]
		public void ZeroRange_IsARealSettingThatRoundTrips()
		{
			/* Zero means "target only", the pre-range behaviour. It must survive the round trip
			 * rather than being treated as missing and replaced by the default. */
			ClientWorldLabelSettings.SetNpcNameRange(0.0f);
			LogAssert.AreEqual(0.0f, ClientWorldLabelSettings.NpcNameRange, "zero must read back as zero");
		}

		[Test]
		public void NonFiniteRanges_AreRejectedOnBothPaths()
		{
			ClientWorldLabelSettings.SetNpcNameRange(float.NaN);
			LogAssert.AreEqual(ClientWorldLabelSettings.DefaultNpcNameRange, ClientWorldLabelSettings.NpcNameRange,
				"NaN through the setter must store the default");

			ClientSettings.Set(ClientSettings.WorldLabelNpcNameRangeKey, float.PositiveInfinity);
			LogAssert.AreEqual(ClientWorldLabelSettings.DefaultNpcNameRange, ClientWorldLabelSettings.NpcNameRange,
				"infinity already in the file must read as the default");
		}

		[Test]
		public void SetShowOwnName_RoundTrips()
		{
			ClientWorldLabelSettings.SetShowOwnName(false);
			LogAssert.IsFalse(ClientWorldLabelSettings.ShowOwnName);

			ClientWorldLabelSettings.SetShowOwnName(true);
			LogAssert.IsTrue(ClientWorldLabelSettings.ShowOwnName);
		}

		[Test]
		public void BothSetters_RaiseOnChangedOnce()
		{
			/* The sweep caches both values and re-reads only on this event. A setter that wrote
			 * the key without raising would take effect on the next login. */
			ClientWorldLabelSettings.SetNpcNameRange(12.0f);
			LogAssert.AreEqual(1, raised, "SetNpcNameRange must notify");

			ClientWorldLabelSettings.SetShowOwnName(false);
			LogAssert.AreEqual(2, raised, "SetShowOwnName must notify");

			ClientWorldLabelSettings.SetPlayerNameRange(12.0f);
			LogAssert.AreEqual(3, raised, "SetPlayerNameRange must notify");
		}

		// --- The rule ----------------------------------------------------------------------------

		[Test]
		public void TheOwnCharacter_FollowsTheToggleAndNothingElse()
		{
			LogAssert.IsTrue(Decide(isPlayer: true, isOwner: true, isTargeted: false, wasVisible: false, sqrDistance: 0.0f, range: 0.0f, showOwnName: true),
				"own name must be up with the toggle on, even at zero range");
			LogAssert.IsFalse(Decide(isPlayer: true, isOwner: true, isTargeted: true, wasVisible: true, sqrDistance: 0.0f, range: 30.0f, showOwnName: false),
				"own name must be down with the toggle off, even while self-targeted");
		}

		[Test]
		public void OtherPlayers_FollowThePlayerRangeAndNotTheNpcRange()
		{
			/* Two independent ranges: a player who wants NPC names but not a crowd of stranger
			 * names must be able to have exactly that, and the reverse. */
			LogAssert.IsTrue(Decide(isPlayer: true, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 20.0f * 20.0f, npcRange: 0.0f, playerRange: 30.0f, showOwnName: false),
				"another player inside the player range must show even with NPC names off");
			LogAssert.IsFalse(Decide(isPlayer: true, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 20.0f * 20.0f, npcRange: 200.0f, playerRange: 0.0f, showOwnName: true),
				"another player must not show at zero player range, whatever the NPC range");
			LogAssert.IsFalse(Decide(isPlayer: true, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 40.0f * 40.0f, npcRange: 200.0f, playerRange: 30.0f, showOwnName: true),
				"another player beyond the player range must not show");
			LogAssert.IsTrue(Decide(isPlayer: true, isOwner: false, isTargeted: true, wasVisible: true, sqrDistance: 1e6f, npcRange: 0.0f, playerRange: 0.0f, showOwnName: false),
				"a targeted player is kept by the target frame's rule");
		}

		[Test]
		public void Npcs_FollowTheNpcRangeAndNotThePlayerRange()
		{
			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 20.0f * 20.0f, npcRange: 30.0f, playerRange: 0.0f, showOwnName: false),
				"an NPC inside the NPC range must show even with player names off");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 20.0f * 20.0f, npcRange: 0.0f, playerRange: 200.0f, showOwnName: true),
				"an NPC must not show at zero NPC range, whatever the player range");
		}

		[Test]
		public void TheOwnPet_IsAlwaysKept()
		{
			/* The pet control put the labels up; a sweep that took them down at range would fight it. */
			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: true, isTargeted: false, wasVisible: true, sqrDistance: 1e6f, range: 0.0f, showOwnName: false),
				"an owned pet's nameplate must stay up regardless of range");
		}

		[Test]
		public void TheCurrentTarget_IsAlwaysKept()
		{
			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: false, isTargeted: true, wasVisible: true, sqrDistance: 1e6f, range: 0.0f, showOwnName: false),
				"the target frame owns the target's labels; the rule must agree");
		}

		[Test]
		public void AtZeroRange_NpcNamesAreTargetOnly()
		{
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 0.0f, range: 0.0f, showOwnName: true),
				"an NPC standing on the player must not show at zero range");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: true, sqrDistance: 0.0f, range: 0.0f, showOwnName: true),
				"a nameplate that is up must come down when the range is set to zero");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 0.0f, range: float.NaN, showOwnName: true),
				"a NaN range must read as target only, not as infinite");
		}

		[Test]
		public void Npcs_ShowInsideTheRangeAndNotBeyondIt()
		{
			const float range = 30.0f;

			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 29.0f * 29.0f, range: range, showOwnName: true),
				"inside the range must show");
			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: range * range, range: range, showOwnName: true),
				"exactly on the range must show");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: 40.0f * 40.0f, range: range, showOwnName: true),
				"well beyond the range must not show");
		}

		[Test]
		public void ANameplateThatIsUp_StaysUpSlightlyPastTheRange()
		{
			/* Hysteresis: a character idling on the line must not toggle every sweep. It is only
			 * ever the EXIT distance that is widened; a nameplate that is down still needs the
			 * true range to come up. */
			const float range = 30.0f;
			float justOutside = range * (1.0f + (ClientNameplateDisplay.ExitRangeMultiplier - 1.0f) * 0.5f);
			float pastExit = range * ClientNameplateDisplay.ExitRangeMultiplier * 1.01f;

			LogAssert.IsTrue(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: true, sqrDistance: justOutside * justOutside, range: range, showOwnName: true),
				"a nameplate that is up must survive a step just past the range");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: false, sqrDistance: justOutside * justOutside, range: range, showOwnName: true),
				"a nameplate that is down must not come up just past the range");
			LogAssert.IsFalse(Decide(isPlayer: false, isOwner: false, isTargeted: false, wasVisible: true, sqrDistance: pastExit * pastExit, range: range, showOwnName: true),
				"a nameplate that is up must come down past the exit distance");
		}

		[Test]
		public void WithoutALiveDisplay_TheTargetFrameKeepsOnlyOwnedLabels()
		{
			/* ShouldStayVisible is what UITKTarget.ClearTarget asks. With no display running —
			 * the target frame outliving the client, or a test — the pre-range behaviour holds:
			 * nothing that is not owned stays up, and a null character never throws. */
			LogAssert.IsFalse(ClientNameplateDisplay.ShouldStayVisible(null),
				"a null character must simply not be kept");
		}

		/// <summary>Calls the rule with one range for both kinds of character, for the tests that do not care which.</summary>
		private static bool Decide(bool isPlayer, bool isOwner, bool isTargeted, bool wasVisible, float sqrDistance, float range, bool showOwnName)
		{
			return ClientNameplateDisplay.Decide(isPlayer, isOwner, isTargeted, wasVisible, sqrDistance, range, range, showOwnName);
		}

		private static bool Decide(bool isPlayer, bool isOwner, bool isTargeted, bool wasVisible, float sqrDistance, float npcRange, float playerRange, bool showOwnName)
		{
			return ClientNameplateDisplay.Decide(isPlayer, isOwner, isTargeted, wasVisible, sqrDistance, npcRange, playerRange, showOwnName);
		}

		// --- The options panel offers both --------------------------------------------------------

		[Test]
		public void TheOptionsPanel_DeclaresAllThreeControls()
		{
			string uxml = ReadSource(OptionsUxml);

			foreach (string name in new[]
			{
				"worldlabel-npc-range-slider",
				"worldlabel-player-range-slider",
				"worldlabel-show-own-toggle",
			})
			{
				LogAssert.IsTrue(uxml.Contains($"name=\"{name}\""),
					$"UIOptions.uxml must declare {name}");
			}
		}

		[Test]
		public void TheAuthoredSliderBounds_MatchTheSettings()
		{
			string uxml = ReadSource(OptionsUxml);
			AssertSliderBounds(uxml, "worldlabel-npc-range-slider",
				ClientWorldLabelSettings.MinimumNpcNameRange, ClientWorldLabelSettings.MaximumNpcNameRange);
			AssertSliderBounds(uxml, "worldlabel-player-range-slider",
				ClientWorldLabelSettings.MinimumPlayerNameRange, ClientWorldLabelSettings.MaximumPlayerNameRange);
		}

		private static void AssertSliderBounds(string uxml, string name, float low, float high)
		{
			Match slider = Regex.Match(uxml,
				$"<ui:Slider name=\"{name}\"[^>]*low-value=\"([^\"]+)\"[^>]*high-value=\"([^\"]+)\"");

			LogAssert.IsTrue(slider.Success, $"{name} must author low-value and high-value");
			LogAssert.AreEqual(low, float.Parse(slider.Groups[1].Value, CultureInfo.InvariantCulture),
				$"{name} low-value must equal the minimum");
			LogAssert.AreEqual(high, float.Parse(slider.Groups[2].Value, CultureInfo.InvariantCulture),
				$"{name} high-value must equal the maximum");
		}

		// --- The prefabs are authored the way the sweep assumes ----------------------------------

		[Test]
		public void EveryPlayableCharacterPrefab_ReferencesItsNameAndGuildLabels()
		{
			/* The player prefabs carried both label objects under NameLabels but referenced
			 * neither, so CharacterNameLabel was null on every player and nothing — not the
			 * naming system, not the target frame, not this feature — could show a player's
			 * name. The sweep skips characters with no name label, so a regression here is
			 * silent: the option does nothing and no error says why. */
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PlayableCharacterPrefabs });
			LogAssert.IsTrue(guids.Length > 0, $"{PlayableCharacterPrefabs} must contain playable character prefabs");

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				PlayerCharacter character = prefab != null ? prefab.GetComponent<PlayerCharacter>() : null;
				if (character == null)
				{
					continue;
				}

				LogAssert.IsNotNull(character.CharacterNameLabel, $"{path} must reference its name label");
				LogAssert.IsNotNull(character.CharacterGuildLabel, $"{path} must reference its guild label");
				LogAssert.IsTrue(character.CharacterNameLabel.transform.IsChildOf(prefab.transform),
					$"{path}'s name label must be one of its own children");
				LogAssert.IsTrue(character.CharacterGuildLabel.transform.IsChildOf(prefab.transform),
					$"{path}'s guild label must be one of its own children");
				LogAssert.IsTrue(character.CharacterNameLabel.SortOrder < character.CharacterGuildLabel.SortOrder,
					$"{path}'s guild label must stack above its name label");
			}
		}

		[Test]
		public void EveryCharacterPrefab_AuthorsItsLabelsInactive()
		{
			/* The sweep and the target frame both work by transition: a label is turned on when
			 * a rule starts holding and off when it stops. A label authored ACTIVE is on from
			 * spawn for every client, and stays on until something targets and untargets it —
			 * which for a player's own labels, with the toggle off, is never. */
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { CharacterPrefabs });
			int checkedPrefabs = 0;

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				ICharacter character = prefab != null ? prefab.GetComponent<ICharacter>() : null;
				if (character == null)
				{
					continue;
				}

				WorldLabel[] labels = { character.CharacterNameLabel, character.CharacterGuildLabel };
				foreach (WorldLabel label in labels)
				{
					if (label == null)
					{
						continue;
					}
					++checkedPrefabs;
					LogAssert.IsFalse(label.gameObject.activeSelf,
						$"{path} authors {label.gameObject.name} active; nameplates must start hidden");
				}
			}

			LogAssert.IsTrue(checkedPrefabs > 0, "at least one character prefab must carry a nameplate to check");
		}

		private static string ReadSource(string projectRelativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), projectRelativePath);
			LogAssert.IsTrue(File.Exists(path), $"{projectRelativePath} must exist");
			return File.ReadAllText(path);
		}
	}
}
