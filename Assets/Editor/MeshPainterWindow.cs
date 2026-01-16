using UnityEngine;
using UnityEditor;

public class MeshPainterWindow : EditorWindow
{
	public GameObject meshToPaint;
	public float brushSize = 5f;
	public int density = 5;
	public float minScale = 0.8f;
	public float maxScale = 1.2f;
	public bool alignWithNormal = true;

	// Nueva variable para filtrar por capa
	public LayerMask terrainLayer = -1;

	[MenuItem("Tools/Mesh Painter")]
	public static void ShowWindow() => GetWindow<MeshPainterWindow>("Mesh Painter");

	private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
	private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

	private void OnGUI()
	{
		GUILayout.Label("Configuración de Pintado", EditorStyles.boldLabel);

		meshToPaint = (GameObject)EditorGUILayout.ObjectField("Prefab a Pintar", meshToPaint, typeof(GameObject), false);

		// Campo para seleccionar la capa del terreno
		terrainLayer = LayerMaskField("Capa del Terreno", terrainLayer);

		EditorGUILayout.Space();
		brushSize = EditorGUILayout.Slider("Tamaño del Pincel", brushSize, 0.25f, 20f);
		density = EditorGUILayout.IntSlider("Densidad", density, 1, 300);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Variaciones", EditorStyles.miniBoldLabel);
		minScale = EditorGUILayout.Slider("Escala Mínima", minScale, 0.1f, 1f);
		maxScale = EditorGUILayout.Slider("Escala Máxima", maxScale, 1f, 5f);
		alignWithNormal = EditorGUILayout.Toggle("Alinear con Terreno", alignWithNormal);

		if (GUILayout.Button("Limpiar Todo")) ClearVegetation();

		EditorGUILayout.HelpBox("Asegúrate de que tu suelo tenga la Capa (Layer) seleccionada arriba.", MessageType.Warning);
	}

	private void OnSceneGUI(SceneView sceneView)
	{
		Event e = Event.current;
		Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

		// Solo detecta el rayo si golpea la capa de terreno
		if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer))
		{
			Handles.color = new Color(0f, 1f, 0f, 0.2f);
			Handles.DrawSolidDisc(hit.point, hit.normal, brushSize);

			if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
			{
				PaintMeshes(hit.point);
				e.Use();
			}
		}
		if (e.type == EventType.MouseMove) sceneView.Repaint();
	}

	private void PaintMeshes(Vector3 center)
	{
		if (meshToPaint == null) return;

		GameObject parent = GameObject.Find("Painted_Vegetation") ?? new GameObject("Painted_Vegetation");

		for (int i = 0; i < density; i++)
		{
			Vector2 randomPoint = Random.insideUnitCircle * brushSize;
			Vector3 rayOrigin = center + new Vector3(randomPoint.x, 10f, randomPoint.y);

			// El rayo de spawn también filtra por capa de terreno
			if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, terrainLayer))
			{
				GameObject newObj = (GameObject)PrefabUtility.InstantiatePrefab(meshToPaint);
				newObj.transform.position = hit.point;
				newObj.transform.parent = parent.transform;

				if (alignWithNormal) newObj.transform.up = hit.normal;

				newObj.transform.Rotate(Vector3.up, Random.Range(0f, 360f), Space.Self);
				newObj.transform.Rotate(Vector3.right, Random.Range(-20f, 20f), Space.Self);
				newObj.transform.Rotate(Vector3.forward, Random.Range(-20f, 20f), Space.Self);

				newObj.transform.localScale = Vector3.one * Random.Range(minScale, maxScale);

				Undo.RegisterCreatedObjectUndo(newObj, "Paint Mesh");
			}
		}
	}

	// Método auxiliar para mostrar el selector de capas correctamente
	LayerMask LayerMaskField(string label, LayerMask layerMask)
	{
		var layers = new string[32];
		for (int i = 0; i < 32; i++) layers[i] = LayerMask.LayerToName(i);
		return EditorGUILayout.MaskField(label, layerMask, layers);
	}

	private void ClearVegetation()
	{
		GameObject parent = GameObject.Find("Painted_Vegetation");
		if (parent != null) Undo.DestroyObjectImmediate(parent);
	}
}