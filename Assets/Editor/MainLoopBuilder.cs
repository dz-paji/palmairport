using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

public static class MainLoopBuilder
{
    public const string ScenePath="Assets/Scenes/MainLoop.unity";
    [MenuItem("Palm Bay/Rebuild MainLoop scene (debug)")]
    public static void CreateScene()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SceneLayoutSync.MarkBakedWorld(AirportWorld.Build());
        var game=new GameObject("Main game loop").AddComponent<MainLoopGame>();
        game.gameObject.AddComponent<SceneLayoutSync>();
        game.Plane=AirportWorld.CreatePlane("Flight",AirportWorld.Hex("4996C8"),Level1Map.PlanePark(0));
        game.Plane.rotation=AircraftMotion.NoseRotation(AircraftMotion.FinalDirection(Level1Map.TaxiInPath(0)));
        game.Players=new Transform[2];game.Vehicles=new Transform[3];game.CargoViews=new Transform[3];
        for(int p=0;p<2;p++)
        {
            game.Players[p]=AirportWorld.CreateCrew("Player "+(p+1),p==0?AirportStyle.Player1:AirportStyle.Player2,Level1Map.CrewSpawn(p));
            game.Players[p].rotation=Quaternion.Euler(0,180,0);
        }
        for(int k=0;k<3;k++)
        {
            var kind=(ServiceKind)k;
            // 停车位须避开站点交互半径（1.5）与车辆获取半径（1.65）的重叠，与试玩脚本的清位停车距离一致；车位在设施路 stub 上。
            game.Vehicles[k]=AirportWorld.CreateCart(kind,MainLoopGame.TaskNames[k]+" vehicle",Level1Map.CartPark(kind));
            GameObject cargo=GameObject.CreatePrimitive(k==2?PrimitiveType.Cylinder:PrimitiveType.Cube);
            Object.DestroyImmediate(cargo.GetComponent<Collider>());cargo.name="Cargo";
            cargo.transform.SetParent(game.Vehicles[k],false);cargo.transform.localPosition=new Vector3(0,1.16f,-.35f);cargo.transform.localScale=new Vector3(.76f,.35f,.66f);
            cargo.GetComponent<Renderer>().sharedMaterial=AirportStyle.SharedMaterial(AirportStyle.ServiceColor(kind),AirportStyle.Finish.Plastic);
            game.CargoViews[k]=cargo.transform;cargo.SetActive(false);
        }
        var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";Level1Map.ConfigureCamera(camera,false);camera.backgroundColor=AirportStyle.Sky;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.gameObject.AddComponent<AudioListener>();game.View=camera;
        AirportWorld.SetupLighting();
        // Save actual mesh/material assets. A scene referencing transient factory objects
        // becomes empty or pink after reopening; compilation cannot detect that failure.
        string folder="Assets/Generated/MainLoop";Directory.CreateDirectory(folder);AssetDatabase.Refresh();int index=0;
        foreach(var root in scene.GetRootGameObjects())
        {
            foreach(var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                if(mesh.sharedMesh && !AssetDatabase.Contains(mesh.sharedMesh))AssetDatabase.CreateAsset(mesh.sharedMesh,AssetDatabase.GenerateUniqueAssetPath(folder+"/Mesh_"+(index++)+".asset"));
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                    if(material && !AssetDatabase.Contains(material))AssetDatabase.CreateAsset(material,AssetDatabase.GenerateUniqueAssetPath(folder+"/Material_"+(index++)+".mat"));
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
        // 唯一产品入口是 PalmBay；MainLoop 仅作为单任务调试场景保留在构建列表中。
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/PalmBay.unity",true),new EditorBuildSettingsScene(ScenePath,true)};
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.defaultIsNativeResolution=false;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;PlayerSettings.colorSpace=ColorSpace.Linear;
        Debug.Log("MAIN_LOOP_SCENE_READY meshes="+Object.FindObjectsOfType<MeshFilter>().Length);
    }
    [MenuItem("Palm Bay/Rebuild and capture MainLoop scene (debug)")]
    public static void CreateAndCapture()
    {
        CreateScene();
        ValidateSavedScene(true);
    }

    /// <summary>
    /// Reloads the scene from disk and checks the references that are most
    /// likely to regress when Unity serializes procedurally-created geometry.
    /// This is intentionally editor-only so it can run from a batch command.
    /// </summary>
    public static void ValidateSavedScene(bool capture)
    {
        var scene=EditorSceneManager.OpenScene(ScenePath);
        if(!scene.IsValid())throw new System.Exception("Could not reopen "+ScenePath);
        int missing=0,meshes=0,materials=0;
        foreach(var root in scene.GetRootGameObjects())
        {
            missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
            foreach(var mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!mesh.sharedMesh)throw new System.Exception("Missing mesh on "+mesh.name);
                meshes++;
                if(!AssetDatabase.Contains(mesh.sharedMesh))throw new System.Exception("Non-persistent mesh on "+mesh.name);
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                {
                    if(!material)throw new System.Exception("Missing material on "+renderer.name);
                    materials++;
                }
        }
        if(missing>0)throw new System.Exception("Missing scripts in MainLoop: "+missing);
        var world=GameObject.Find(SceneLayoutSync.WorldName);
        var marker=world?world.GetComponent<SceneLayoutMarker>():null;
        if(!marker||marker.Version!=Level1Map.LayoutVersion)throw new System.Exception("MainLoop environment layout marker is stale");
        if(meshes<20)throw new System.Exception("MainLoop environment geometry missing; meshes="+meshes);
        var game=Object.FindObjectOfType<MainLoopGame>();
        if(!game||!game.Plane||!game.View||game.Players==null||game.Players.Length!=2||game.Vehicles==null||game.Vehicles.Length!=3||game.CargoViews==null||game.CargoViews.Length!=3)
            throw new System.Exception("MainLoop gameplay references are incomplete after reload");
        if(capture)Capture(game.View,"Evidence/main-loop-scene.png",1600,1000);
        Debug.Log("MAIN_LOOP_SCENE_VALIDATED version="+marker.Version+" meshes="+meshes+" materials="+materials+(capture?" captured=Evidence/main-loop-scene.png":""));
    }

    static void Capture(Camera camera,string path,int width,int height)
    {
        if(!camera)throw new System.Exception("Cannot capture without a camera");
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float oldAspect=camera.aspect;
        var rt=new RenderTexture(width,height,24);camera.targetTexture=rt;camera.aspect=width/(float)height;Level1Map.ConfigureCamera(camera,false);camera.Render();
        RenderTexture.active=rt;var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
        Directory.CreateDirectory("Evidence");File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=oldTarget;camera.aspect=oldAspect;RenderTexture.active=oldActive;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);
    }
}
