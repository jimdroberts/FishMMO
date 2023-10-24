using System;
using UnityEngine;

[Serializable]
public class SceneTeleporterDetails
{
	internal string From;
	public string ToScene;
	public Vector3 ToPosition;
	public Sprite SceneTransitionImage;
}