using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityNativeHull;

#if UNITY_EDITOR
using UnityEditor;
#endif
[ExecuteInEditMode]//在编辑器模式下执行
public class HullTester:MonoBehaviour
{
    public List<Transform> Transforms;
    private Dictionary<int, TestShape> Hulls;// 凸包字典(键:实例ID,值:TestShape测试形状)
    private void Update()
    {
        HandleTransformChanged();//处理凸包变换
    }
    // 处理变换变化
    public void HandleTransformChanged()
    {
        //选择激活状态的变换，并去重
        var transforms = Transforms.Where(t=>t.gameObject.activeSelf).Distinct().ToList();
        var newTransformsFound = false;
        var transformCount = 0;

        if (Hulls!=null)
        {
            for (int i = 0; i < transforms.Count; i++)
            {
                var t = transforms[i];
                if (t==null)  continue;
                transformCount++;
                
                var foundNewHull=!Hulls.ContainsKey(t.GetInstanceID());
                if (foundNewHull)
                {
                    newTransformsFound=true;
                    break;
                }
            }

            if (newTransformsFound && transformCount == Hulls.Count) return;
        }
        
        Debug.Log("重建对象");
        
        //安全的释放资源
        EnsureDestroyed();
        
        //保存下不为空的节点，InstanceID作为key，创建的TestShape作为value记录在字典里，方便后续使用
        Hulls=transforms.Where(t=>t!=null).ToDictionary(t=>t.GetInstanceID(),CreatShape);
    }

    // 创建测试形状
    private TestShape CreatShape(Transform t)
    {
        var hull = CreateHull(t);
        return new TestShape()
        {
            id = t.GetInstanceID(),
            Hull = hull,
        };
    }

    //根据变换创建凸包
    private NativeHull CreateHull(Transform v)
    {
        var collider = v.GetComponent<Collider>();
        if (collider is MeshCollider meshCollider)
        {
            return HullFactory.CreateFromMesh(meshCollider.sharedMesh);
        }
        
        var mf=v.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            return HullFactory.CreateFromMesh(mf.sharedMesh);
        }

        throw new InvalidOperationException($"无法从游戏对象 '{v.name}' 创建凸包");
    }
    
    void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    // 编辑器播放模式状态变化回调
    private void EditorApplication_playModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            case PlayModeStateChange.ExitingPlayMode:
                EnsureDestroyed();
                break;
        }
    }
#endif

    void OnDestroy() => EnsureDestroyed();
    void OnDisable() => EnsureDestroyed();
    // 确保资源被销毁
    private void EnsureDestroyed()
    {
        if (Hulls == null) return;

        foreach (var kvp in Hulls)
        {
            if (kvp.Value.Hull.IsValid)
            {
                kvp.Value.Hull.Dispose();
            }
        }
        Hulls.Clear();
    }
}