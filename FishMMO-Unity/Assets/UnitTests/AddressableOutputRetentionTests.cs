using System;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Addressables;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.Build;

namespace FishMMO.UnitTests
{
	public class AddressableOutputRetentionTests
	{
		private string root, output;

		[SetUp]
		public void SetUp()
		{
			root = Path.Combine(Path.GetTempPath(), "fishmmo-output-retention-" + Guid.NewGuid().ToString("N"));
			output = Path.Combine(root, "content");
			Directory.CreateDirectory(output);
		}

		[TearDown]
		public void TearDown() => Directory.Delete(root, true);

		[Test]
		public void RetainsCurrentContentAndCaches_AndRemovesOnlyUnreferencedOutputBundles()
		{
			string current = Write(output, "current.bundle", "current content");
			string stale = Write(output, "nested/old-client.bundle", "excluded group");
			string catalog = Write(output, "catalog.bin", "catalog");
			string cache = Write(root, "BuildCache/cached.bundle", "cached build data");
			string shader = Write(root, "ShaderCache/program.bin", "compiled shader");
			DateTime modified = File.GetLastWriteTimeUtc(current);
			var result = Successful(current);
			Assert.That(AddressableManager.PruneStaleBundleOutputs(result, new[] { output, output }), Is.EqualTo(1));
			Assert.That(File.Exists(stale), Is.False);
			Assert.That(File.ReadAllText(current), Is.EqualTo("current content"));
			Assert.That(File.GetLastWriteTimeUtc(current), Is.EqualTo(modified));
			Assert.That(File.ReadAllText(catalog), Is.EqualTo("catalog"));
			Assert.That(File.ReadAllText(cache), Is.EqualTo("cached build data"));
			Assert.That(File.ReadAllText(shader), Is.EqualTo("compiled shader"));
		}

		[Test]
		public void SwitchingClientAndServerKeepsOnlyTheSuccessfulBuildsBundles()
		{
			string client = Write(output, "client.bundle", "client");
			string server = Write(output, "server.bundle", "server");
			AddressableManager.PruneStaleBundleOutputs(Successful(server), new[] { output });
			Assert.That(File.Exists(client), Is.False);
			Assert.That(File.Exists(server), Is.True);
			client = Write(output, "client.bundle", "client again");
			AddressableManager.PruneStaleBundleOutputs(Successful(client), new[] { output });
			Assert.That(File.Exists(server), Is.False);
			Assert.That(File.ReadAllText(client), Is.EqualTo("client again"));
		}

		[TestCase(true)]
		[TestCase(false)]
		public void FailedOrUnverifiableBuildLeavesPreviousOutputsAlone(bool failed)
		{
			string previous = Write(output, "previous.bundle", "previous working content");
			var result = failed ? new AddressablesPlayerBuildResult { Error = "content build failed", FileRegistry = new FileRegistry() }
				: new AddressablesPlayerBuildResult();
			Assert.Throws<BuildFailedException>(() => AddressableManager.PruneStaleBundleOutputs(result, new[] { output }));
			Assert.That(File.ReadAllText(previous), Is.EqualTo("previous working content"));
		}

		private static AddressablesPlayerBuildResult Successful(params string[] paths)
		{
			var result = new AddressablesPlayerBuildResult { FileRegistry = new FileRegistry() };
			foreach (string path in paths) result.FileRegistry.AddFile(path);
			return result;
		}

		private static string Write(string root, string relative, string content)
		{
			string path = Path.Combine(root, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, content);
			return path;
		}
	}
}
