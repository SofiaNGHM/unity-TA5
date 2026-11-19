using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TA5 - Parte 1. Genera el relieve, las texturas y los árboles del objeto "Terreno".
/// Uso: menú TA5 → Generar terreno (con la escena Escena_FPS abierta).
/// Resultado: terreno de 200 x 200 m, centro plano para la calle y cerros alrededor,
/// pasto en lo plano, pasto rocoso en las laderas y árboles SpeedTree en los cerros.
/// </summary>
public static class GenerarTerreno
{
    const string Carpeta = "Assets/Terreno";
    const string TexturasDir = "Assets/Standard Assets/Environment/TerrainAssets/SurfaceTextures/";
    const string ArbolesDir = "Assets/Standard Assets/Environment/SpeedTree/";

    const float Ancho = 200f;       // metros (X y Z)
    const float AlturaMax = 50f;    // metros (Y)
    const float InicioCerros = 0.55f; // 0 = centro, 1 = borde: hasta aquí queda plano
    const int CantidadArboles = 120;

    [MenuItem("TA5/Generar terreno")]
    static void Generar()
    {
        var escena = EditorSceneManager.GetActiveScene();
        Terrain terreno = BuscarTerreno();
        string accion = terreno == null
            ? "No hay terreno en la escena: se va a crear 'Terreno'"
            : "Se va a reemplazar el relieve, las texturas y los árboles de '" + terreno.name + "'";
        if (!EditorUtility.DisplayDialog("TA5",
                "Escena activa: " + escena.name + "\n\n" + accion + " y luego se guardará la escena. ¿Continuar?",
                "Generar", "Cancelar"))
            return;

        if (terreno == null) terreno = CrearTerreno();

        TerrainData datos = terreno.terrainData;
        PrepararCarpeta(datos);

        datos.heightmapResolution = 257;
        datos.size = new Vector3(Ancho, AlturaMax, Ancho);
        terreno.transform.position = Vector3.zero;

        GenerarRelieve(datos);
        PintarTexturas(datos);
        PlantarArboles(datos);

        terreno.Flush();
        EditorUtility.SetDirty(datos);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terreno.gameObject.scene);
        bool guardada = EditorSceneManager.SaveScene(terreno.gameObject.scene);

        Selection.activeGameObject = terreno.gameObject;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("TA5: terreno generado en la escena '" + terreno.gameObject.scene.name + "'"
                  + (guardada ? " (escena guardada)" : " (NO se pudo guardar: usa Ctrl+S)")
                  + ". Centro plano en (100, 0, 100) para ubicar casas y autos.");
    }

    // Crea el GameObject "Terreno", reutilizando un TerrainData existente si lo hay.
    static Terrain CrearTerreno()
    {
        TerrainData datos = AssetDatabase.LoadAssetAtPath<TerrainData>(Carpeta + "/Terreno.asset")
                            ?? AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Terreno.asset");
        if (datos == null)
        {
            if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets", "Terreno");
            datos = new TerrainData();
            AssetDatabase.CreateAsset(datos, Carpeta + "/Terreno.asset");
        }

        GameObject go = Terrain.CreateTerrainGameObject(datos);
        go.name = "Terreno";
        Undo.RegisterCreatedObjectUndo(go, "Crear Terreno");
        return go.GetComponent<Terrain>();
    }

    static Terrain BuscarTerreno()
    {
        GameObject go = GameObject.Find("Terreno");
        if (go != null && go.GetComponent<Terrain>() != null) return go.GetComponent<Terrain>();
        return Terrain.activeTerrain;
    }

    // Guarda el TerrainData y las capas en Assets/Terreno/ (orden de carpetas, criterio 9).
    static void PrepararCarpeta(TerrainData datos)
    {
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets", "Terreno");

        string ruta = AssetDatabase.GetAssetPath(datos);
        string destino = Carpeta + "/Terreno.asset";
        if (!string.IsNullOrEmpty(ruta) && ruta != destino && AssetDatabase.LoadAssetAtPath<Object>(destino) == null)
            AssetDatabase.MoveAsset(ruta, destino);
    }

    // Distancia "cuadrada" al centro: 0 en el centro, 1 en el borde.
    static float DistanciaAlCentro(float u, float v)
    {
        return Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f;
    }

    static void GenerarRelieve(TerrainData datos)
    {
        int res = datos.heightmapResolution;
        float[,] alturas = new float[res, res];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1);
                float v = z / (float)(res - 1);

                float borde = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InicioCerros, 0.95f, DistanciaAlCentro(u, v)));
                float forma = Mathf.PerlinNoise(u * 4f + 7.3f, v * 4f + 2.1f);     // cerros grandes
                float detalle = Mathf.PerlinNoise(u * 14f + 1.7f, v * 14f + 9.4f); // irregularidad

                alturas[z, x] = Mathf.Clamp01(borde * (0.40f + 0.50f * forma + 0.10f * detalle));
            }
        }
        datos.SetHeights(0, 0, alturas);
    }

    static void PintarTexturas(TerrainData datos)
    {
        TerrainLayer pasto = CrearCapa("Capa_Pasto", "GrassHillAlbedo.psd");
        TerrainLayer roca = CrearCapa("Capa_Roca", "GrassRockyAlbedo.psd");
        datos.terrainLayers = new[] { pasto, roca };

        datos.alphamapResolution = 256;
        int res = datos.alphamapResolution;
        float[,,] mapa = new float[res, res, 2];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1);
                float v = z / (float)(res - 1);

                float porPendiente = Mathf.InverseLerp(18f, 32f, datos.GetSteepness(u, v));
                float porAltura = Mathf.InverseLerp(0.35f, 0.55f, datos.GetInterpolatedHeight(u, v) / datos.size.y);
                float r = Mathf.Clamp01(Mathf.Max(porPendiente, porAltura));

                mapa[z, x, 0] = 1f - r;
                mapa[z, x, 1] = r;
            }
        }
        datos.SetAlphamaps(0, 0, mapa);
    }

    static TerrainLayer CrearCapa(string nombre, string archivoTextura)
    {
        string ruta = Carpeta + "/" + nombre + ".terrainlayer";
        TerrainLayer capa = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ruta);
        if (capa == null)
        {
            capa = new TerrainLayer();
            AssetDatabase.CreateAsset(capa, ruta);
        }

        Texture2D textura = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturasDir + archivoTextura);
        if (textura == null) Debug.LogWarning("TA5: no se encontró la textura " + TexturasDir + archivoTextura);
        capa.diffuseTexture = textura;
        capa.tileSize = new Vector2(15f, 15f);
        EditorUtility.SetDirty(capa);
        return capa;
    }

    static void PlantarArboles(TerrainData datos)
    {
        string[] modelos = { "Broadleaf/Broadleaf_Desktop.spm", "Conifer/Conifer_Desktop.spm" };
        List<TreePrototype> prototipos = new List<TreePrototype>();
        foreach (string m in modelos)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArbolesDir + m);
            if (prefab != null) prototipos.Add(new TreePrototype { prefab = prefab });
            else Debug.LogWarning("TA5: no se encontró el árbol " + ArbolesDir + m);
        }
        datos.treePrototypes = prototipos.ToArray();
        datos.RefreshPrototypes();

        List<TreeInstance> arboles = new List<TreeInstance>();
        if (prototipos.Count > 0)
        {
            System.Random azar = new System.Random(5); // semilla fija: mismo resultado cada vez
            for (int intento = 0; intento < 5000 && arboles.Count < CantidadArboles; intento++)
            {
                float u = (float)azar.NextDouble();
                float v = (float)azar.NextDouble();
                float d = DistanciaAlCentro(u, v);
                if (d < InicioCerros + 0.05f || d > 0.97f) continue; // solo en los cerros
                if (datos.GetSteepness(u, v) > 30f) continue;        // no en pendientes muy fuertes

                float escala = 0.8f + (float)azar.NextDouble() * 0.5f;
                arboles.Add(new TreeInstance
                {
                    position = new Vector3(u, 0f, v),
                    prototypeIndex = azar.Next(prototipos.Count),
                    widthScale = escala,
                    heightScale = escala,
                    rotation = (float)(azar.NextDouble() * Mathf.PI * 2.0),
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }
        }
        datos.SetTreeInstances(arboles.ToArray(), true);
    }
}
