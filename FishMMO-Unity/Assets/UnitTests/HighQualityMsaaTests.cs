using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The High Fidelity quality level ships native scale with 4× MSAA (the owner's default for High, 2026-11-05); players
	/// choose otherwise in the options (ClientDisplaySettings.MsaaOption), and WebGPU offers only off or 4×.
	/// </summary>
	public class HighQualityMsaaTests
	{
		[Test]
		public void HighFidelity_Defaults_To4xMsaa_AtNativeScale()
		{
			string asset = System.IO.File.ReadAllText("Assets/Settings/URP-HighFidelity.asset");
			LogAssert.IsTrue(asset.Contains("  m_MSAA: 4\n") || asset.Contains("  m_MSAA: 4\r\n"), "High Fidelity authors 4× MSAA");
			LogAssert.IsTrue(asset.Contains("  m_RenderScale: 1\n") || asset.Contains("  m_RenderScale: 1\r\n"),
				"at native scale (STP below native would make ClientDisplaySettings.ApplyMsaa switch MSAA off)");
		}

		[Test]
		public void TheOptions_OfferOnlyWhatTheDeviceDraws()
		{
			string settings = System.IO.File.ReadAllText("Assets/Scripts/Client/Settings/ClientDisplaySettings.cs");
			LogAssert.IsTrue(settings.Contains("? new[] { MsaaOption.Off, MsaaOption.Four }"), "WebGPU: off or 4× (its spec allows 1 or 4 samples)");
			string options = System.IO.File.ReadAllText("Assets/Scripts/Client/GUI/World/Options/UITKOptions.cs");
			LogAssert.IsTrue(options.Contains("ClientDisplaySettings.AvailableMsaa()"), "the dropdown lists the device's choices");
		}
	}
}
