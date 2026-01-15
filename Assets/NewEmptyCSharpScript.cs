using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public class ProBuilderAutoInstaller
{
	// Este constructor se ejecuta automáticamente al cargar Unity o compilar
	static ProBuilderAutoInstaller()
	{
		EditorApplication.hierarchyChanged += OnHierarchyChanged;
	}

	static void OnHierarchyChanged()
	{
		// Buscamos todos los objetos ProBuilder en la escena
		ProBuilderMesh[] allMeshes = Object.FindObjectsByType<ProBuilderMesh>(FindObjectsSortMode.None);

		foreach (var mesh in allMeshes)
		{
			// Si el objeto NO tiene el script de deformación, se lo ponemos
			if (mesh.gameObject.GetComponent<AutoSoftDeformer>() == null)
			{
				mesh.gameObject.AddComponent<AutoSoftDeformer>();
				Debug.Log($"<color=green>AutoSoftDeformer añadido a: </color> {mesh.gameObject.name}");
			}
		}
	}
}