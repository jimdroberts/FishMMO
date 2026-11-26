using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>The web page's loader line while the first scene downloads (<see cref="WebBootProgress"/>).</summary>
	public class WebBootProgressTests
	{
		[Test]
		public void Describe_SaysHowMuchOfTheDownloadIsDone_InMegabytes()
		{
			const long mb = 1024 * 1024;
			Assert.AreEqual("Downloading game data: 45.0 of 163.5 MB", WebBootProgress.Describe(45 * mb, 163 * mb + mb / 2));
			Assert.AreEqual("Downloading game data: 0.0 of 2.0 MB", WebBootProgress.Describe(-5, 2 * mb), "never below nothing");
			Assert.AreEqual("Downloading game data: 2.0 of 2.0 MB", WebBootProgress.Describe(3 * mb, 2 * mb), "never past the total");
		}

		[Test]
		public void Describe_WithNothingToDownload_SaysStarting()
		{
			Assert.AreEqual("Starting", WebBootProgress.Describe(0, 0), "everything was already in the browser's cache");
		}
	}
}
