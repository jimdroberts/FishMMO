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

		public static Vector3 RandomPositionWithinRadius(Vector3 center, float radius)
		{
			// Generate a random angle between 0 and 2π
			float angle = UnityEngine.Random.Range(0f, 2 * Mathf.PI);

			// Generate a random distance between 0 and radius
			float distance = UnityEngine.Random.Range(0f, radius);

			// Calculate X and Z offsets
			float xOffset = distance * Mathf.Cos(angle);
			float zOffset = distance * Mathf.Sin(angle);

			// Return the random position offset from the center
			return new Vector3(center.x + xOffset, center.y, center.z + zOffset);
		}

		public static Vector3 RandomOnUnitSphere(System.Random random = null)
		{
			if (random == null)
			{
				random = new System.Random();
			}

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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 RandomInBoundingBox(Vector3 boundingBox)
		{
			return new Vector3(UnityEngine.Random.Range(-boundingBox.x, boundingBox.x),
							   UnityEngine.Random.Range(-boundingBox.y, boundingBox.y),
							   UnityEngine.Random.Range(-boundingBox.z, boundingBox.z));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 GetRandomPointInToroid(float R, float radius)
		{
			// Generate random angles
			float theta = UnityEngine.Random.Range(0f, 2f * Mathf.PI); // Angle around the central axis
			float phi = UnityEngine.Random.Range(0f, 2f * Mathf.PI);   // Angle around the tube

			// Convert to Cartesian coordinates
			float x = (R + radius * Mathf.Cos(phi)) * Mathf.Cos(theta);
			float y = (R + radius * Mathf.Cos(phi)) * Mathf.Sin(theta);
			float z = radius * Mathf.Sin(phi);

			return new Vector3(x, y, z);
		}
	}
}