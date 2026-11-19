using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TA5 - Parte 1. Arma una calle en el centro plano del terreno:
/// pista, veredas, casas a ambos lados y autos sobre la pista.
/// Todos los objetos usan los materiales de Assets/Colores (criterio 2)
/// y tienen colliders para que el FPSController no los atraviese.
/// Uso: menú TA5 → Generar calle (con Escena_FPS abierta). Se puede repetir: reemplaza la calle anterior.
/// </summary>
public static class GenerarCalle
{
    const string RutaCasa = "Assets/Prefabs/House/House.fbx";
    const string RutaAuto = "Assets/Prefabs/Car/Car FBX.fbx";
    const string CarpetaColores = "Assets/Colores/";

    static readonly Vector3 Centro = new Vector3(100f, 0f, 100f); // centro plano del terreno
    const int CasasPorLado = 4;
    const int CantidadAutos = 5;
    const float AnchoCalle = 10f;
    const float AnchoVereda = 3f;
    const float SeparacionCasas = 5f;
    const float LargoAuto = 4.5f;     // metros: se normaliza la escala del auto a este largo
    const float CasaMin = 6f, CasaMax = 14f; // rango aceptable del ancho de la casa (metros)

    // Si las puertas de las casas no miran hacia la calle, cambiar a 180 (o 90 / 270).
    const float GiroExtraCasas = 180f;

    static readonly string[] ColoresParedes = { "Blanco", "Amarillo", "Naranja", "Verde" };
    static readonly string[] ColoresTechos = { "Rojo", "Marron", "Gris", "Azul" };
    static readonly string[] ColoresCarroceria = { "Rojo", "Azul", "Verde", "Amarillo", "Naranja", "Blanco" };

    static Dictionary<string, Material> mats;

    [MenuItem("TA5/Generar calle (casas y autos)")]
    static void Generar()
    {
        GameObject casaFbx = AssetDatabase.LoadAssetAtPath<GameObject>(RutaCasa);
        GameObject autoFbx = AssetDatabase.LoadAssetAtPath<GameObject>(RutaAuto);
        if (casaFbx == null || autoFbx == null)
        {
            EditorUtility.DisplayDialog("TA5", "No se encontró " + (casaFbx == null ? RutaCasa : RutaAuto), "OK");
            return;
        }
        if (!CargarMateriales()) return;

        var escena = EditorSceneManager.GetActiveScene();
        GameObject anterior = GameObject.Find("Calle");
        if (!EditorUtility.DisplayDialog("TA5",
                "Escena activa: " + escena.name + "\n\nSe van a crear " + (CasasPorLado * 2) + " casas, " + CantidadAutos +
                " autos, la pista y las veredas" + (anterior != null ? " (reemplazando la calle actual)" : "") +
                ", y luego se guardará la escena. ¿Continuar?", "Generar", "Cancelar"))
            return;
        if (anterior != null) Undo.DestroyObjectImmediate(anterior);

        Terrain terreno = Terrain.activeTerrain;
        float suelo = terreno != null ? terreno.SampleHeight(Centro) + terreno.transform.position.y : 0f;
        Vector3 centro = new Vector3(Centro.x, suelo, Centro.z);

        GameObject raiz = new GameObject("Calle");
        GameObject grupoCasas = new GameObject("Casas");
        GameObject grupoAutos = new GameObject("Autos");
        grupoCasas.transform.SetParent(raiz.transform);
        grupoAutos.transform.SetParent(raiz.transform);

        // --- Auto de muestra: define la escala y la dirección de la calle (el auto va a 270° en Y) ---
        GameObject muestra = Instanciar(autoFbx, grupoAutos.transform, 270f);
        Debug.Log("TA5: piezas del auto (nombre → tamaño):\n" + DescribirPiezas(muestra));
        List<string> ocultas = OcultarPisos(muestra);
        if (ocultas.Count > 0) Debug.Log("TA5: piezas planas ocultadas en cada auto (piso del modelo de Cinema 4D): " + string.Join(", ", ocultas));
        Bounds bAuto = Limites(muestra);
        float escalaAuto = LargoAuto / Mathf.Max(bAuto.size.x, bAuto.size.z, 0.001f);
        bool calleEnX = bAuto.size.x >= bAuto.size.z;
        Object.DestroyImmediate(muestra);

        Vector3 dir = calleEnX ? Vector3.right : Vector3.forward;   // a lo largo de la calle
        Vector3 lado = calleEnX ? Vector3.forward : Vector3.right;  // perpendicular
        float giroCalle = calleEnX ? 0f : 90f;

        // --- Casa de muestra: define escala y tamaño ---
        GameObject muestraCasa = Instanciar(casaFbx, grupoCasas.transform, giroCalle);
        Bounds bCasa = Limites(muestraCasa);
        float anchoCasa = Mathf.Max(bCasa.size.x, bCasa.size.z);
        float escalaCasa = anchoCasa > CasaMax ? CasaMax / anchoCasa : anchoCasa < CasaMin ? 10f / anchoCasa : 1f;
        Object.DestroyImmediate(muestraCasa);

        // --- Casas ---
        float largoTotal = 0f;
        int n = 1;
        for (int ladoCalle = 0; ladoCalle < 2; ladoCalle++)
        {
            for (int i = 0; i < CasasPorLado; i++)
            {
                float giro = giroCalle + (ladoCalle == 0 ? 0f : 180f) + GiroExtraCasas;
                GameObject casa = Instanciar(casaFbx, grupoCasas.transform, giro);
                casa.name = "Casa_" + n.ToString("00");
                casa.transform.localScale *= escalaCasa;

                Bounds b = Limites(casa);
                float largo = Vector3.Dot(b.size, dir);
                float fondo = Vector3.Dot(b.size, lado);
                float paso = largo + SeparacionCasas;
                largoTotal = Mathf.Max(largoTotal, paso * CasasPorLado);

                float a = (i - (CasasPorLado - 1) / 2f) * paso;
                float s = (ladoCalle == 0 ? 1f : -1f) * (AnchoCalle / 2f + AnchoVereda + 1f + fondo / 2f);
                Colocar(casa, centro + dir * a + lado * s);

                PintarCasa(casa, n - 1);
                AgregarColliders(casa);
                n++;
            }
        }

        // --- Pista y veredas ---
        float largoPista = largoTotal + 10f;
        CrearBloque("Pista", raiz.transform, centro + Vector3.up * 0.02f, dir, lado, largoPista, AnchoCalle, 0.04f, "Gris");
        float sv = AnchoCalle / 2f + AnchoVereda / 2f;
        CrearBloque("Vereda_Izquierda", raiz.transform, centro + lado * sv + Vector3.up * 0.1f, dir, lado, largoPista, AnchoVereda, 0.2f, "Blanco");
        CrearBloque("Vereda_Derecha", raiz.transform, centro - lado * sv + Vector3.up * 0.1f, dir, lado, largoPista, AnchoVereda, 0.2f, "Blanco");

        // --- Autos: dos carriles, en sentidos opuestos ---
        for (int i = 0; i < CantidadAutos; i++)
        {
            bool carrilB = i % 2 == 1;
            GameObject auto = Instanciar(autoFbx, grupoAutos.transform, 270f + (carrilB ? 180f : 0f));
            auto.name = "Auto_" + (i + 1).ToString("00");
            OcultarPisos(auto);
            auto.transform.localScale *= escalaAuto;

            float a = (i - (CantidadAutos - 1) / 2f) * (largoTotal / CantidadAutos);
            float s = (carrilB ? -1f : 1f) * AnchoCalle / 4f;
            Colocar(auto, centro + dir * a + lado * s + Vector3.up * 0.04f);

            PintarAuto(auto, ColoresCarroceria[i % ColoresCarroceria.Length]);
            AgregarColliders(auto);
        }

        Undo.RegisterCreatedObjectUndo(raiz, "Generar calle");
        EditorSceneManager.MarkSceneDirty(escena);
        bool guardada = EditorSceneManager.SaveScene(escena);

        Selection.activeGameObject = raiz;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("TA5: calle generada en '" + escena.name + "'" + (guardada ? " (escena guardada)" : " (NO guardada: usa Ctrl+S)") +
                  ". Calle a lo largo de " + (calleEnX ? "X" : "Z") +
                  ". Escala casa: " + escalaCasa.ToString("0.###") + ", escala auto: " + escalaAuto.ToString("0.###") + ".");
    }

    // ---------- Materiales ----------

    static bool CargarMateriales()
    {
        mats = new Dictionary<string, Material>();
        foreach (string c in new[] { "Rojo", "Azul", "Verde", "Amarillo", "Blanco", "Negro", "Gris", "Marron", "Naranja" })
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(CarpetaColores + "Mat_" + c + ".mat");
            if (m == null)
            {
                EditorUtility.DisplayDialog("TA5", "Falta el material " + CarpetaColores + "Mat_" + c + ".mat", "OK");
                return false;
            }
            mats[c] = m;
        }
        return true;
    }

    static string NombreBase(Material m)
    {
        return m == null ? "" : m.name.Replace(" (Instance)", "").Trim();
    }

    static void PintarCasa(GameObject casa, int indice)
    {
        string pared = ColoresParedes[indice % ColoresParedes.Length];
        string techo = ColoresTechos[(indice / 2 + indice) % ColoresTechos.Length];
        string marcos = pared == "Blanco" ? "Marron" : "Blanco";

        foreach (Renderer r in casa.GetComponentsInChildren<Renderer>())
        {
            Material[] actuales = r.sharedMaterials;
            Material[] nuevos = new Material[actuales.Length];
            for (int i = 0; i < actuales.Length; i++)
            {
                string color;
                switch (NombreBase(actuales[i]))
                {
                    case "Walls": color = pared; break;
                    case "Roof": color = techo; break;
                    case "Doors": color = "Marron"; break;
                    case "Window Frames": color = marcos; break;
                    case "Glass":
                    case "Sliding Glass Door": color = "Azul"; break;
                    case "Floor": color = "Marron"; break;
                    case "Ceilings": color = "Blanco"; break;
                    case "Light": color = "Amarillo"; break;
                    case "Edges": color = "Gris"; break;
                    case "Door Handles":
                    case "Metal":
                    case "Bar":
                    case "Light Switch": color = "Gris"; break;
                    default: color = MasParecido(actuales[i]); break;
                }
                nuevos[i] = mats[color];
            }
            Undo.RecordObject(r, "Pintar casa");
            r.sharedMaterials = nuevos;
        }
    }

    static void PintarAuto(GameObject auto, string carroceria)
    {
        // La carrocería es el material que cubre más volumen del auto.
        var volumen = new Dictionary<string, float>();
        foreach (Renderer r in auto.GetComponentsInChildren<Renderer>())
        {
            float v = r.bounds.size.x * r.bounds.size.y * r.bounds.size.z;
            foreach (Material m in r.sharedMaterials)
            {
                string k = NombreBase(m);
                volumen[k] = (volumen.TryGetValue(k, out float prev) ? prev : 0f) + v;
            }
        }
        string matCarroceria = "";
        float max = -1f;
        foreach (var kv in volumen) if (kv.Value > max) { max = kv.Value; matCarroceria = kv.Key; }

        foreach (Renderer r in auto.GetComponentsInChildren<Renderer>())
        {
            Material[] actuales = r.sharedMaterials;
            Material[] nuevos = new Material[actuales.Length];
            for (int i = 0; i < actuales.Length; i++)
                nuevos[i] = mats[NombreBase(actuales[i]) == matCarroceria ? carroceria : MasParecido(actuales[i])];
            Undo.RecordObject(r, "Pintar auto");
            r.sharedMaterials = nuevos;
        }
    }

    // Devuelve el color de Assets/Colores más parecido al color original del material importado.
    static string MasParecido(Material original)
    {
        Color c = original != null && original.HasProperty("_Color") ? original.color : Color.gray;
        string mejor = "Gris";
        float dist = float.MaxValue;
        foreach (var kv in mats)
        {
            Color p = kv.Value.color;
            float d = (c.r - p.r) * (c.r - p.r) + (c.g - p.g) * (c.g - p.g) + (c.b - p.b) * (c.b - p.b);
            if (d < dist) { dist = d; mejor = kv.Key; }
        }
        return mejor;
    }

    // ---------- Utilidades ----------

    // El FBX del auto trae, además del auto, piezas planas y grandes (piso de la escena de Cinema 4D).
    // Se desactivan las piezas casi planas cuyo ancho supera 1,5 veces el de la pieza no plana más grande.
    static List<string> OcultarPisos(GameObject auto)
    {
        Renderer[] rs = auto.GetComponentsInChildren<Renderer>();
        float mayorNoPlana = 0f;
        foreach (Renderer r in rs)
        {
            float h = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
            if (!EsPlana(r)) mayorNoPlana = Mathf.Max(mayorNoPlana, h);
        }

        List<string> ocultas = new List<string>();
        foreach (Renderer r in rs)
        {
            float h = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
            if (EsPlana(r) && h > mayorNoPlana * 1.5f)
            {
                r.gameObject.SetActive(false);
                ocultas.Add(r.name);
            }
        }
        return ocultas;
    }

    static bool EsPlana(Renderer r)
    {
        Vector3 s = r.bounds.size;
        return s.y < 0.02f * Mathf.Max(s.x, s.z, 0.0001f);
    }

    static string DescribirPiezas(GameObject go)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            sb.Append("  ").Append(r.name).Append(" → ").Append(r.bounds.size.ToString("0.00")).Append(" [");
            foreach (Material m in r.sharedMaterials) sb.Append(NombreBase(m)).Append(' ');
            sb.Append("]\n");
        }
        return sb.ToString();
    }

    static GameObject Instanciar(GameObject prefab, Transform padre, float giroY)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        go.transform.rotation = Quaternion.Euler(0f, giroY, 0f) * go.transform.rotation;
        return go;
    }

    static Bounds Limites(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Mueve el objeto para que su base quede centrada en "destino".
    static void Colocar(GameObject go, Vector3 destino)
    {
        Bounds b = Limites(go);
        go.transform.position += new Vector3(destino.x - b.center.x, destino.y - b.min.y, destino.z - b.center.z);
    }

    static void AgregarColliders(GameObject go)
    {
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>();
    }

    static void CrearBloque(string nombre, Transform padre, Vector3 centro, Vector3 dir, Vector3 lado,
                            float largo, float ancho, float alto, string color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = nombre;
        g.transform.SetParent(padre);
        g.transform.position = centro;
        g.transform.localScale = dir * largo + lado * ancho + Vector3.up * alto;
        g.GetComponent<Renderer>().sharedMaterial = mats[color];
    }
}
