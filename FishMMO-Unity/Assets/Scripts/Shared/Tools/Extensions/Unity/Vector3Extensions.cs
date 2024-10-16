﻿using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public static class Vector3Extensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Quaternion GetRandomConicalDirection(this Vector3 forward, Vector3 startPosition, float coneRadius, float distance)
		{
			return Quaternion.Euler(startPosition - ((distance * forward) + (Random.onUnitSphere * coneRadius)));
		}
	}
}