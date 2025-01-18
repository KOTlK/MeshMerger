/*
    Konstantin Osintsev (c) 2025

    The software provided "As is" without any warranty. Use at your own risk.

    Usage:
    - Select objects, you need to merge, in the scene
    - Open: Tools->MeshMerger Window
    - Setup settings
    - Press "Fetch selected meshes" button
    - Press "Combine" button

    - If something goes wrong, press "Undo" button to undo changes (note, that all attached components will be lost, except for MeshRenderer, Transform and MeshFilter)

    1.18.2025 (v.1.1) ...
    - Generated meshes with high vertex count (>= 65536) will now use UInt32 index format
    - Added limitation of 128 materials per generated mesh

    1.8.2025 (v.1.0) 
*/

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MeshMerger : EditorWindow {
    public struct MeshElement {
        public GameObject GameObject;
        public Transform  Transform;
        public Mesh       Mesh;
        public Material   Material;
        public Renderer   Renderer;
        public ObjectDump Dump;
    }

    public struct ObjectDump {
        public string                       Name;
        public Transform                    ProbeAnchor;
        public Quaternion                   Rotation;
        public Vector3                      Position;
        public Vector3                      Scale;
        public StaticEditorFlags            Flags;
        public ShadowCastingMode            ShadowCastingMode;
        public ReceiveGI                    ReceiveGI;
        public LightProbeUsage              LightProbeUsage;
        public MotionVectorGenerationMode   MotionVectors;
        public float                        ScaleInLightmap;
        public uint                         RenderingLayerMask;
        public bool                         StaticShadowCaster;
        public bool                         IsStatic;
        public bool                         DynamicOcclusion;
        public bool                         StitchSeams;
    }

    public List<MeshElement> Elements = new();

    public string          Name = "Mesh";
    public bool            DestroyOriginalObjects = false;
    public bool            UndoAvailable = false;
    public bool            SingleMaterial = false;
    public bool            ExtractMaterial = true;
    public Material        ExtractedMaterial;
    public float           ListItemHeight = 30f;
    public float           ListItemWidth  = 150f;
    public Vector2         ScrollPosition = Vector2.zero;

    public readonly char[]  CharBuffer = new char[MaxLength];
    public List<GameObject> SelectedObjects = new List<GameObject>();

    public const int   MaxLength = 128;
    public const string MergerPath = "Assets/MeshMerger";
    public const string MeshesPath = "Assets/MeshMerger/GeneratedMeshes";

    public GameObject LastCreatedObject;

    [MenuItem ("Tools/MeshMerger")]
    public static void ShowWindow () {
        GetWindow(typeof(MeshMerger));
    }

    private void OnGUI () {
        var currentHeight = 20f;

        GUI.Label(new Rect(20f, currentHeight, 80f, 20f), "Mesh name:");

        var input = GUI.TextField(new Rect(100f, currentHeight, 150, 20f), Name);
        var ptr = 0;
        var maxLen = input.Length < MaxLength ? input.Length : MaxLength;
        
        for(var i = 0; i < maxLen; ++i) {
            if(input[i] != '+' &&
               input[i] != '*' &&
               input[i] != '&' &&
               input[i] != '^' &&
               input[i] != '%' &&
               input[i] != '$' &&
               input[i] != '#' &&
               input[i] != '@' &&
               input[i] != '`' &&
               input[i] != '"' &&
               input[i] != '<' &&
               input[i] != '>' &&
               input[i] != ';' &&
               input[i] != ':' &&
               input[i] != '\'' &&
               input[i] != '\\' &&
               input[i] != '|' &&
               input[i] != '/' &&
               input[i] != '?' &&
               input[i] != ',' &&
               input[i] != '[' &&
               input[i] != ']' &&
               input[i] != '{' &&
               input[i] != '}' &&
               input[i] != '=') {
                CharBuffer[ptr++] = input[i];
            }
        }

        Name = new string(CharBuffer, 0, ptr);

        currentHeight += 20f;

        // Destroy Original box
        if(GUI.Toggle(new Rect(20, currentHeight, 150, 20), 
                      DestroyOriginalObjects, 
                      "Destroy original objects")) {
            DestroyOriginalObjects = true;
        } else {
            DestroyOriginalObjects = false;
        }

        // Undo button
        if(UndoAvailable) {
            if(GUI.Button(new Rect(180, currentHeight, 80, 20), "Undo")) {
                Object[] selectedObjects = new Object[Elements.Count];

                for(var i = 0; i < Elements.Count; ++i) {
                    var go = new GameObject(Elements[i].Dump.Name);

                    var renderer   = go.AddComponent<MeshRenderer>();
                    var meshFilter = go.AddComponent<MeshFilter>();

                    GameObjectUtility.SetStaticEditorFlags(go, Elements[i].Dump.Flags);

                    renderer.sharedMaterial             = Elements[i].Material;
                    renderer.shadowCastingMode          = Elements[i].Dump.ShadowCastingMode;
                    renderer.staticShadowCaster         = Elements[i].Dump.StaticShadowCaster;
                    renderer.receiveGI                  = Elements[i].Dump.ReceiveGI;
                    renderer.lightProbeUsage            = Elements[i].Dump.LightProbeUsage;
                    renderer.probeAnchor                = Elements[i].Dump.ProbeAnchor;
                    renderer.motionVectorGenerationMode = Elements[i].Dump.MotionVectors;
                    renderer.allowOcclusionWhenDynamic  = Elements[i].Dump.DynamicOcclusion;
                    renderer.renderingLayerMask         = Elements[i].Dump.RenderingLayerMask;
                    renderer.scaleInLightmap            = Elements[i].Dump.ScaleInLightmap;
                    renderer.stitchLightmapSeams        = Elements[i].Dump.StitchSeams;
                    meshFilter.sharedMesh               = Elements[i].Mesh;

                    go.transform.SetPositionAndRotation(Elements[i].Dump.Position, Elements[i].Dump.Rotation);
                    go.transform.localScale = Elements[i].Dump.Scale;
                    selectedObjects[i] = go;
                }

                if(LastCreatedObject) {
                    DestroyImmediate(LastCreatedObject);
                }

                if(SelectedObjects.Count > 0) {
                    for(var i = 0; i < SelectedObjects.Count; ++i) {
                        DestroyImmediate(SelectedObjects[i]);
                    }

                    SelectedObjects.Clear();
                }

                Selection.objects = selectedObjects;

                UndoAvailable = false;
            }
        }

        currentHeight += 20f;

        // Single material box
        if(GUI.Toggle(new Rect(20, currentHeight, 150, 20), SingleMaterial, "Single material")) {
            SingleMaterial = true;
        } else {
            SingleMaterial = false;
        }

        // Custom material box
        if(SingleMaterial) {
            if(GUI.Toggle(new Rect(180, currentHeight, 150, 20), ExtractMaterial, "Extract material")) {
                ExtractMaterial = true;
            } else {
                ExtractMaterial = false;
            }

            if(!ExtractMaterial) {
                currentHeight += 25f;
                var material = (Material)EditorGUI.ObjectField(new Rect(20, currentHeight, 150, 20),
                                                               ExtractedMaterial, 
                                                               typeof(Material), 
                                                               false);

                ExtractedMaterial = material;
            }
        }

        currentHeight += 25f;

        // Meshes fetching
        if(GUI.Button(new Rect(20, currentHeight, 200, ListItemHeight), "Fetch selected meshes")) {
            Elements.Clear();
            foreach (var transform in Selection.transforms) {
                if(transform.TryGetComponent<MeshFilter>(out var meshFilter)) {
                    MeshElement element   = new();
                    element.GameObject    = transform.gameObject;
                    element.Mesh          = meshFilter.sharedMesh;
                    element.Transform     = transform;
                    element.Dump.Name     = transform.gameObject.name;
                    element.Dump.Position = transform.position;
                    element.Dump.Rotation = transform.rotation;
                    element.Dump.Scale    = transform.localScale;
                    element.Dump.IsStatic = transform.gameObject.isStatic;

                    if(transform.TryGetComponent<MeshRenderer>(out var mr)) {
                        element.Material = mr.sharedMaterial;
                        element.Renderer = mr;
                
                        element.Dump.Flags              = GameObjectUtility.GetStaticEditorFlags(transform.gameObject);
                        element.Dump.ShadowCastingMode  = mr.shadowCastingMode;
                        element.Dump.StaticShadowCaster = mr.staticShadowCaster;
                        element.Dump.ReceiveGI          = mr.receiveGI;
                        element.Dump.LightProbeUsage    = mr.lightProbeUsage;
                        element.Dump.ProbeAnchor        = mr.probeAnchor;
                        element.Dump.MotionVectors      = mr.motionVectorGenerationMode;
                        element.Dump.DynamicOcclusion   = mr.allowOcclusionWhenDynamic;
                        element.Dump.RenderingLayerMask = mr.renderingLayerMask;
                        element.Dump.ScaleInLightmap    = mr.scaleInLightmap;
                        element.Dump.StitchSeams        = mr.stitchLightmapSeams;

                        Elements.Add(element);
                    } else {
                        Debug.LogError("Transform has mesh filter but does not have MeshRenderer");
                    }
                }
            }
        }

        // Combine meshes
        if(GUI.Button(new Rect(230, currentHeight, 80, ListItemHeight), "Combine")) {
            SelectedObjects.Clear();
            var mesh                  = new Mesh();
            var combineInstances      = new List<CombineInstance>();
            var materials             = new List<Material>();
            var vertexCount           = 0;

            for(var i = 0; i < Elements.Count; ++i) {
                var combineInstance = new CombineInstance();

                for(var subMesh = 0; subMesh < Elements[i].Mesh.subMeshCount; ++subMesh) {
                    if(combineInstances.Count == 128) {
                        CombineMesh(mesh, combineInstances.ToArray(), materials.ToArray());
                        
                        mesh = new Mesh();
                        combineInstances.Clear();
                        materials.Clear();
                    }

                    combineInstance.transform    = Elements[i].Transform.localToWorldMatrix;
                    combineInstance.mesh         = Elements[i].Mesh;
                    combineInstance.subMeshIndex = subMesh;

                    combineInstances.Add(combineInstance);
                    materials.Add(Elements[i].Renderer.sharedMaterials[subMesh]);
                    vertexCount += Elements[i].Mesh.vertexCount;

                    if(vertexCount >= 65536) {
                        mesh.indexFormat = IndexFormat.UInt32;
                    }
                }
            }

            CombineMesh(mesh, combineInstances.ToArray(), materials.ToArray());

            Selection.objects = SelectedObjects.ToArray();
        }

        currentHeight += 35f;

        // Draw list of meshes
        var viewRect = new Rect(30f, currentHeight, ListItemWidth, ListItemHeight * Elements.Count);
        var rect = new Rect(30f, currentHeight, ListItemWidth + 20, ListItemHeight * 10);

        ScrollPosition = GUI.BeginScrollView(rect, ScrollPosition, viewRect, false, true);

        for(var i = 0; i < Elements.Count; ++i) {
            var objectChanged = (GameObject)EditorGUI.ObjectField(new Rect(30f, currentHeight, ListItemWidth, ListItemHeight), Elements[i].GameObject, typeof(GameObject), true);

            if(objectChanged && 
               objectChanged != Elements[i].GameObject && 
               objectChanged.TryGetComponent<MeshFilter>(out var meshFilter) &&
               objectChanged.TryGetComponent<MeshRenderer>(out var mr)) {
                var element        = Elements[i];
                var transform      = objectChanged.transform;

                element.Mesh       = meshFilter.sharedMesh;
                element.GameObject = objectChanged;
                element.Transform  = objectChanged.transform;
                element.Material   = mr.sharedMaterial;
                element.Renderer   = mr;

                element.Dump.Name               = objectChanged.name;
                element.Dump.Flags              = GameObjectUtility.GetStaticEditorFlags(objectChanged);
                element.Dump.Position           = transform.position;
                element.Dump.Rotation           = transform.rotation;
                element.Dump.Scale              = transform.localScale;
                element.Dump.IsStatic           = transform.gameObject.isStatic;
                element.Dump.ShadowCastingMode  = mr.shadowCastingMode;
                element.Dump.StaticShadowCaster = mr.staticShadowCaster;
                element.Dump.ReceiveGI          = mr.receiveGI;
                element.Dump.LightProbeUsage    = mr.lightProbeUsage;
                element.Dump.ProbeAnchor        = mr.probeAnchor;
                element.Dump.MotionVectors      = mr.motionVectorGenerationMode;
                element.Dump.DynamicOcclusion   = mr.allowOcclusionWhenDynamic;
                element.Dump.RenderingLayerMask = mr.renderingLayerMask;
                element.Dump.ScaleInLightmap    = mr.scaleInLightmap;
                element.Dump.StitchSeams        = mr.stitchLightmapSeams;

                Elements[i] = element;
            }

            currentHeight += ListItemHeight;
        }

        GUI.EndScrollView();
    }

    private void CombineMesh(Mesh mesh, CombineInstance[] combineInstances, Material[] materials) {
        // mesh.indexFormat = IndexFormat.UInt32;
        mesh.CombineMeshes(combineInstances, SingleMaterial, true);

        var go = new GameObject();
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        if(SingleMaterial) {
            if(!ExtractMaterial && ExtractedMaterial) {
                mr.sharedMaterial = ExtractedMaterial;
            } else {
                mr.sharedMaterial = Elements[0].Material;
            }
        } else {
            mr.sharedMaterials = materials;
        }

        mf.mesh = mesh;

        if(string.IsNullOrEmpty(Name)) {
            Name = "Name";
        }

        if(!AssetDatabase.IsValidFolder(MergerPath)) {
            AssetDatabase.CreateFolder("Assets", "MeshMerger");
        }

        if(AssetDatabase.IsValidFolder(MeshesPath)) {
            var name = $"{MeshesPath}/{Name}.mesh";
            var goName = $"{Name}";
            var obj = AssetDatabase.LoadAssetAtPath(name, typeof(Mesh));
            if(obj) {
                for(var i = 0; i < 999; ++i) {
                    name = $"{MeshesPath}/{Name} ({i}).mesh";
                    goName = $"{Name} ({i})";

                    obj = AssetDatabase.LoadAssetAtPath(name, typeof(Mesh));

                    if(!obj)
                        break;
                }
            }

            go.name = goName;

            AssetDatabase.CreateAsset(mesh, name);
            AssetDatabase.SaveAssets();
        } else {
            AssetDatabase.CreateFolder(MergerPath, "GeneratedMeshes");
        }

        if(DestroyOriginalObjects) {
            for(var i = 0; i < Elements.Count; ++i) {
                DestroyImmediate(Elements[i].GameObject);
            }

            UndoAvailable = true;
        }

        SelectedObjects.Add(go);
        // LastCreatedObject = go;
    }
}