using System;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	public class WebAssemblyCodeOptimizationTests
	{
		[Test]
		public void RuntimeSpeedSettingCanBeChangedAndRestored()
		{
			string previous = WebAssemblyCodeOptimization.Current;
			if (previous == null) Assert.Ignore("Unity Web build support is not installed on this native build host.");
			try
			{
				WebAssemblyCodeOptimization.Current = "BuildTimes";
				Assert.That(WebAssemblyCodeOptimization.Current, Is.EqualTo("BuildTimes"));
				WebAssemblyCodeOptimization.Current = "RuntimeSpeed";
				Assert.That(WebAssemblyCodeOptimization.Current, Is.EqualTo("RuntimeSpeed"));
				Assert.Throws<ArgumentException>(() => WebAssemblyCodeOptimization.Current = "InvalidOptimization");
				Assert.That(WebAssemblyCodeOptimization.Current, Is.EqualTo("RuntimeSpeed"));
			}
			finally { WebAssemblyCodeOptimization.Current = previous; }
			Assert.That(WebAssemblyCodeOptimization.Current, Is.EqualTo(previous));
		}
	}
}
