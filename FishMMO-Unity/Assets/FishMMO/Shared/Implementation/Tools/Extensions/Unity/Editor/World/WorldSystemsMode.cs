using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What the world scene details rebuild does about a scene that is not set up.</summary>
	public enum WorldSystemsMode
	{
		/// <summary>
		/// Do not check at all: rebuild the cache and nothing else.
		/// </summary>
		/// <remarks>
		/// What every caller but the dashboard gets, and deliberately. An audit finding is not a
		/// fault in the rebuild, but it is logged at its own severity — so a scene that lost its
		/// settings component would turn every test that rebuilds the cache red, and blame the
		/// rebuild for it. The check is a thing you ask for.
		/// </remarks>
		Off = 0,

		/// <summary>Look and say so in the console. Nothing is opened, nothing is written.</summary>
		Report = 1,
		/// <summary>
		/// Put missing components in, and ask about everything else scene by scene.
		/// </summary>
		/// <remarks>
		/// A scene that is missing its World Scene Settings or its World Day Night Cycle is not
		/// missing an opinion, it is missing a part — there is nothing to ask, so those go in. What
		/// still asks is anything that would overrule a choice somebody made: a cycle switched off,
		/// a directional light they placed.
		/// </remarks>
		Prompt = 2,
		/// <summary>Configure everything fixable without asking.</summary>
		FixAll = 3,
	}

	/// <summary>
	/// The scene details cache builder's world systems pass: the World
	/// Simulation module's audit of each scene's atlas entry, climate and sky. Installed by the module; without it the cache is
	/// rebuilt and nothing is checked.
	/// </summary>
	public static class WorldSystemsHooks
	{
		/// <summary>Starts gathering what each scanned scene says of its world systems.</summary>
		public static Action BeginCollecting;

		/// <summary>Stops gathering.</summary>
		public static Action EndCollecting;

		/// <summary>Resolves the gathered problems and applies them in the mode asked; true when it changed a scene.</summary>
		public static Func<WorldSystemsMode, bool> ResolveAndApply;
	}
}
