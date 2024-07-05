using System.Runtime.CompilerServices;
using UnityEngine;
using System;

namespace FishMMO.Shared
{
	public static class Vector3Extensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Quaternion GetRandomConicalDirection(this Vector3 forward, Vector3 startPosition, float coneRadius, float distance, System.Random random)
		{
			return Quaternion.Euler(startPosition - ((distance * forward) + (RandomOnUnitSphere(random) * coneRadius)));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 RandomOnUnitSphere(System.Random random)
		{
			// Generate random spherical coordinates
			double theta = random.NextDouble() * 2 * Math.PI;  // azimuthal angle (0 to 2pi)
			double phi = Math.Acos(2 * random.NextDouble() - 1);  // polar angle (0 to pi)

			// Convert spherical coordinates to Cartesian coordinates
			double x = Math.Sin(phi) * Math.Cos(theta);
			double y = Math.Sin(phi) * Math.Sin(theta);
			double z = Math.Cos(phi);

			return new Vector3(Unsafe.As<double, float>(ref x),
							   Unsafe.As<double, float>(ref y),
							   Unsafe.As<double, float>(ref z));
		}
	}
}