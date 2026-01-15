using UnityEngine;
using UnityEngine.ProBuilder;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class AutoSoftDeformer : MonoBehaviour
{
	public float radius = 3f;
	public AnimationCurve curve = AnimationCurve.EaseInOut(0, 1, 1, 0);

	private Vector3[] basePositions;
	private ProBuilderMesh pbMesh;
	private Dictionary<int, Vector3> lastPositions = new Dictionary<int, Vector3>();
	private bool isDeforming = false;

	void Update()
	{
		if (Application.isPlaying) return;
		if (pbMesh == null) pbMesh = GetComponent<ProBuilderMesh>();

		var selection = pbMesh.selectedVertices.ToArray();

		if (selection == null || selection.Length == 0)
		{
			if (isDeforming)
			{
				isDeforming = false;
				UpdateBase();
			}
			return;
		}

		bool moved = false;
		foreach (int i in selection)
		{
			if (!lastPositions.ContainsKey(i) || pbMesh.positions[i] != lastPositions[i])
			{
				moved = true;
				break;
			}
		}

		if (moved)
		{
			// Solo registramos el Undo al empezar a mover, para no saturar la memoria
			if (!isDeforming)
			{
				RegisterUndo("Deformar Malla");
				isDeforming = true;
			}

			ApplyMultiSoftSelection(selection);

			lastPositions.Clear();
			foreach (int i in selection) lastPositions[i] = pbMesh.positions[i];
		}
	}

	void RegisterUndo(string actionName)
	{
#if UNITY_EDITOR
		Undo.RecordObject(this, actionName);
		Undo.RecordObject(pbMesh, actionName);
#endif
	}

	// BOTÓN DE UNDO EN EL INSPECTOR
	[ContextMenu("Deshacer último cambio (Undo)")]
	public void ManualUndo()
	{
#if UNITY_EDITOR
		Undo.PerformUndo();
		UpdateBase();
		Debug.Log("Deshacer ejecutado.");
#endif
	}

	void ApplyMultiSoftSelection(int[] selectedIndices)
	{
		if (basePositions == null || basePositions.Length != pbMesh.positions.Count)
			UpdateBase();

		Vector3[] nextPositions = basePositions.ToArray();
		Dictionary<int, Vector3> offsets = new Dictionary<int, Vector3>();

		foreach (int sIdx in selectedIndices)
		{
			offsets[sIdx] = pbMesh.positions[sIdx] - basePositions[sIdx];
			nextPositions[sIdx] = pbMesh.positions[sIdx];
		}

		for (int i = 0; i < nextPositions.Length; i++)
		{
			if (selectedIndices.Contains(i)) continue;

			float maxInfluence = 0f;
			Vector3 targetOffset = Vector3.zero;

			foreach (int sIdx in selectedIndices)
			{
				float dist = Vector3.Distance(basePositions[sIdx], basePositions[i]);
				if (dist < radius)
				{
					float influence = curve.Evaluate(dist / radius);
					if (influence > maxInfluence)
					{
						maxInfluence = influence;
						targetOffset = offsets[sIdx] * influence;
					}
				}
			}

			if (maxInfluence > 0)
			{
				nextPositions[i] = basePositions[i] + targetOffset;
			}
		}

		pbMesh.positions = nextPositions;
		pbMesh.ToMesh();
		pbMesh.Refresh();
	}

	void UpdateBase()
	{
		if (pbMesh == null) pbMesh = GetComponent<ProBuilderMesh>();
		pbMesh.ToMesh();
		pbMesh.Refresh();
		basePositions = pbMesh.positions.ToArray();
		lastPositions.Clear();
	}
}