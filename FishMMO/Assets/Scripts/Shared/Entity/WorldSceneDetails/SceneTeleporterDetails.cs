using System;
using UnityEngine;

[Serializable]
public class SceneTeleporterDetails
{
	internal string from;
	public string toScene;
	public Vector3 toPosition;
	public Sprite sceneTransitionImage;
}