using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	public class PlanetSurfaceBakeTests
	{
		private string folder, output, entry;
		private WorldBody body;
		private SolarSystemProfile system;
		private StarBody star;
		private bool hadGroup;
		private readonly List<string> extraOutputs = new List<string>(), extraEntries = new List<string>();
		private const int Size = 16;

		[SetUp]
		public void SetUp()
		{
			folder = "Assets/PlanetBakeFixture_" + Guid.NewGuid().ToString("N");
			AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
			body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Cache Test " + Guid.NewGuid().ToString("N");
			body.Kind = WorldBodyKind.GasGiant;
			body.TerrainSeed = 1234;
			body.SkyRadiusKm = 6000;
			AssetDatabase.CreateAsset(body, folder + "/Body.asset");
			star = ScriptableObject.CreateInstance<StarBody>();
			star.name = "Test Star";
			AssetDatabase.CreateAsset(star, folder + "/Star.asset");
			system = ScriptableObject.CreateInstance<SolarSystemProfile>();
			system.name = "Test System";
			system.Bodies.Add(star); system.Bodies.Add(body); system.HomeWorld = body;
			AssetDatabase.CreateAsset(system, folder + "/System.asset");
			AssetDatabase.SaveAssets();
			output = PlanetSurfaceBaker.BakedImagePath(body.name);
			entry = new PlanetSurfaceBakeCache().EntryPath(body, Size);
			hadGroup = AddressableAssetSettingsDefaultObject.Settings.FindGroup(PlanetSurfaceBaker.AddressableGroupName) != null;
		}

		[TearDown]
		public void TearDown()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			foreach (string image in extraOutputs.Concat(new[] { output }))
				if (File.Exists(image))
				{
					settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(image));
					AssetDatabase.DeleteAsset(image);
				}
			var group = settings.FindGroup(PlanetSurfaceBaker.AddressableGroupName);
			if (!hadGroup && group != null && group.entries.Count == 0) settings.RemoveGroup(group);
			AssetDatabase.DeleteAsset(folder);
			foreach (string cacheEntry in extraEntries.Concat(new[] { entry }))
				foreach (string path in new[] { cacheEntry, cacheEntry + ".json" }) if (File.Exists(path)) File.Delete(path);
			extraOutputs.Clear(); extraEntries.Clear();
			AssetDatabase.SaveAssets();
			PlanetSurface.ClearCache();
			PlanetClimateTable.ClearCache();
		}

		[TestCase(WorldBodyKind.GasGiant)]
		[TestCase(WorldBodyKind.Planet)]
		public void RepeatedBakeReusesIdenticalPngWithoutRegeneratingOrTouchingOutput(WorldBodyKind kind)
		{
			body.Kind = kind;
			PlanetSurfaceBaker.Bake(body, Size, (_, __) => false);
			byte[] expected = File.ReadAllBytes(output);
			DateTime write = File.GetLastWriteTimeUtc(output);
			AssetDatabase.SaveAssets();
			PlanetSurfaceBaker.Bake(body, Size, (stage, _) =>
			{
				Assert.That(IsGeneration(stage), Is.False, "unchanged inputs must skip the pixel loops"); return false;
			});
			Assert.That(File.ReadAllBytes(output), Is.EqualTo(expected));
			Assert.That(File.GetLastWriteTimeUtc(output), Is.EqualTo(write));
		}

		[Test]
		public void BatchSettlesClimateTablesBeforeHashingAndRestoresCleanedOutputs()
		{
			var other = ScriptableObject.CreateInstance<WorldBody>();
			other.name = body.name + " Other"; other.Kind = WorldBodyKind.GasGiant; other.TerrainSeed = 5678;
			AssetDatabase.CreateAsset(other, folder + "/Other.asset");
			system.Bodies.Add(other); EditorUtility.SetDirty(system); AssetDatabase.SaveAssets();
			string otherOutput = PlanetSurfaceBaker.BakedImagePath(other.name);
			extraOutputs.Add(otherOutput); extraEntries.Add(new PlanetSurfaceBakeCache().EntryPath(other, Size));
			var bodies = new[] { body, other };
			Assert.That(PlanetSurfaceBaker.BakeAll(bodies, out int skipped, Size, (_, __) => false), Is.EqualTo(2));
			Assert.That(skipped, Is.Zero);
			foreach (string image in new[] { output, otherOutput })
			{
				AddressableAssetSettingsDefaultObject.Settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(image));
				AssetDatabase.DeleteAsset(image);
			}
			Assert.That(PlanetSurfaceBaker.BakeAll(bodies, out skipped, Size, (stage, _) => { Assert.That(IsGeneration(stage), Is.False); return false; }), Is.Zero);
			Assert.That(skipped, Is.EqualTo(2));
			Assert.That(File.Exists(output) && File.Exists(otherOutput), Is.True);
		}

		[Test]
		public void MissingBuildOutputIsRestoredAndRegisteredFromPersistentCache()
		{
			PlanetSurfaceBaker.Bake(body, Size, (_, __) => false);
			byte[] expected = File.ReadAllBytes(output);
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(output));
			AssetDatabase.DeleteAsset(output);
			PlanetSurfaceBaker.Bake(body, Size, (stage, _) => { Assert.That(IsGeneration(stage), Is.False); return false; });
			Assert.That(File.ReadAllBytes(output), Is.EqualTo(expected));
			var registered = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(output));
			Assert.That(registered, Is.Not.Null);
			Assert.That(registered.address, Is.EqualTo(PlanetSurfaceBaker.AddressOf(body.name)));
		}

		[TestCase(WorldBodyKind.GasGiant)]
		[TestCase(WorldBodyKind.Planet)]
		public void CancelInsidePlanetDoesNotPublishAnIncompleteImageOrCache(WorldBodyKind kind)
		{
			body.Kind = kind;
			Assert.Throws<OperationCanceledException>(() => PlanetSurfaceBaker.Bake(body, Size, (stage, fraction) => IsGeneration(stage) && fraction > 0.2f));
			Assert.That(File.Exists(output), Is.False);
			Assert.That(File.Exists(entry), Is.False);
			Assert.That(File.Exists(entry + ".json"), Is.False);
		}

		[Test]
		public void CancelledRebuildKeepsTheLastCompletedCacheAndOutput()
		{
			PlanetSurfaceBaker.Bake(body, Size, (_, __) => false);
			byte[] oldImage = File.ReadAllBytes(output), oldCache = File.ReadAllBytes(entry);
			string oldStamp = File.ReadAllText(entry + ".json");
			body.TerrainSeed++;
			Assert.Throws<OperationCanceledException>(() => PlanetSurfaceBaker.Bake(body, Size, (stage, fraction) => IsGeneration(stage) && fraction > 0.2f));
			Assert.That(File.ReadAllBytes(output), Is.EqualTo(oldImage));
			Assert.That(File.ReadAllBytes(entry), Is.EqualTo(oldCache));
			Assert.That(File.ReadAllText(entry + ".json"), Is.EqualTo(oldStamp));
		}

		[Test]
		public void CorruptCacheIsRegenerated()
		{
			PlanetSurfaceBaker.Bake(body, Size, (_, __) => false);
			File.WriteAllBytes(entry, new byte[] { 1, 2, 3 });
			bool generated = false;
			PlanetSurfaceBaker.Bake(body, Size, (stage, _) => { generated |= IsGeneration(stage); return false; });
			Assert.That(generated, Is.True);
			Assert.That(File.ReadAllBytes(entry), Is.EqualTo(File.ReadAllBytes(output)));
		}

		[Test]
		public void InputHashTracksUnsavedBodyAndStarChangesButIsStableWhenSaved()
		{
			ClimateTableBaker.EnsureBaked(body);
			string shared = PlanetSurfaceBakeCache.SharedInputs();
			string first = PlanetSurfaceBakeCache.InputHash(body, Size, shared);
			AssetDatabase.SaveAssets();
			Assert.That(PlanetSurfaceBakeCache.InputHash(body, Size, shared), Is.EqualTo(first));
			body.TerrainSeed++;
			Assert.That(PlanetSurfaceBakeCache.InputHash(body, Size, shared), Is.Not.EqualTo(first));
			body.TerrainSeed--;
			star.Luminosity *= 2;
			Assert.That(PlanetSurfaceBakeCache.InputHash(body, Size, shared), Is.Not.EqualTo(first));
			star.Luminosity /= 2;
			Assert.That(PlanetSurfaceBakeCache.InputHash(body, Size * 2, shared), Is.Not.EqualTo(first));
		}

		[Test]
		public void BiomeArtEditsInvalidateSharedInputs()
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = "Cache Test Biome";
			AssetDatabase.CreateAsset(biome, folder + "/Biome.asset");
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); texture.Apply();
			AssetDatabase.CreateAsset(texture, folder + "/Art.asset");
			biome.MainTextureLayer.albedoTexture = texture;
			EditorUtility.SetDirty(biome); AssetDatabase.SaveAssets();
			string first = PlanetSurfaceBakeCache.SharedInputs();
			texture.SetPixels(new[] { Color.blue, Color.blue, Color.blue, Color.blue }); texture.Apply();
			EditorUtility.SetDirty(texture); AssetDatabase.SaveAssets();
			Assert.That(PlanetSurfaceBakeCache.SharedInputs(), Is.Not.EqualTo(first));
		}

		[TestCase(WorldBodyKind.GasGiant)]
		[TestCase(WorldBodyKind.Planet)]
		public void ProgressAdvancesThroughRowsToCompletion(WorldBodyKind kind)
		{
			body.Kind = kind;
			float previous = 0;
			int rows = 0;
			PlanetSurfaceBaker.Bake(body, Size, (stage, fraction) =>
			{
				Assert.That(fraction, Is.GreaterThanOrEqualTo(previous)); previous = fraction;
				if (IsGeneration(stage)) rows++;
				return false;
			});
			Assert.That(previous, Is.EqualTo(1));
			Assert.That(rows, Is.GreaterThanOrEqualTo(Size / 2));
		}

		private static bool IsGeneration(string stage) => stage.StartsWith("Gas cloud bands", StringComparison.Ordinal) || stage.StartsWith("Sampling terrain", StringComparison.Ordinal) || stage.StartsWith("Blending biomes", StringComparison.Ordinal);
	}
}
