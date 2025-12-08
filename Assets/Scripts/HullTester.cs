using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityNativeHull;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode] //在编辑器模式下执行
public class HullTester : MonoBehaviour
{
    public List<Transform> Transforms;

    // 凸包绘制选项
    public DebugHullFlags HullDrawingOptions = DebugHullFlags.Outline;
    [Header("可视化选项")] public bool DrawIsCollided; // 绘制碰撞状态
    public bool DrawIntersection; // 绘制相交区域

    [Header("控制台日志")] public bool LogContact; // 记录接触日志

    // 凸包字典(键:实例ID,值:TestShape测试形状)
    private Dictionary<int, TestShape> Hulls;

    private void Update()
    {
        HandleTransformChanged(); //处理凸包变换
        HandleHullCollisions(); // 处理凸包碰撞
    }

    // 处理变换变化
    public void HandleTransformChanged()
    {
        //选择激活状态的变换，并去重
        var transforms = Transforms.Where(t => t.gameObject.activeSelf).Distinct().ToList();
        var newTransformsFound = false;
        var transformCount = 0;

        if (Hulls != null)
        {
            for (int i = 0; i < transforms.Count; i++)
            {
                var t = transforms[i];
                if (t == null) continue;
                transformCount++;

                var foundNewHull = !Hulls.ContainsKey(t.GetInstanceID());
                if (foundNewHull)
                {
                    newTransformsFound = true;
                    break;
                }
            }

            if (newTransformsFound && transformCount == Hulls.Count) return;
        }

        Debug.Log("重建对象");

        //安全的释放资源
        EnsureDestroyed();

        //保存下不为空的节点，InstanceID作为key，创建的TestShape作为value记录在字典里，方便后续使用
        Hulls = transforms.Where(t => t != null).ToDictionary(t => t.GetInstanceID(), CreatShape);
    }

    // 处理凸包碰撞
    void HandleHullCollisions()
    {
        for (int i = 0; i < Transforms.Count; i++)
        {
            var t1 = Transforms[i];
            if (1 == null)
                continue;

            //获取凸包信息
            var hull1 = Hulls[t1.GetInstanceID()].Hull;
            //RigidTransform是Unity.Mathematics下的高性能结构体，就是为了之后的变换速度快，只关心位置角度这些信息
            var transform1 = new RigidTransform(t1.rotation, t1.position);

            HullDrawingUtility.DrawDebugHull(hull1, transform1, HullDrawingOptions);
            for (int j = i + 1; j < Transforms.Count; j++)
            {
                var t2 = Transforms[j];
                if (t2 == null)
                    continue;
                if (!(t1.hasChanged && t2.hasChanged))
                    continue;

                var hull2 = Hulls[t2.GetInstanceID()].Hull;
                var transform2 = new RigidTransform(t2.rotation, t2.position);
                //绘制碰撞信息
                DrawHullCollision(t1.gameObject, hull1, transform1, t2.gameObject, hull2, transform2);
            }
        }
    }

    // 绘制凸包碰撞信息
    public void DrawHullCollision(GameObject a, NativeHull h1, RigidTransform t1, GameObject b,
        NativeHull h2, RigidTransform t2)
    {
        var collision = HullCollision.GetDebugCollisionInfo(t1, h1, t2, h2);
        if (collision.IsCollision)
        {
            // 绘制相交区域
            if (DrawIntersection)
            {
                HullIntersection.DrawNativeHullHullIntersection(t1, h1, t2, h2);
            }

            // 绘制接触信息
            if (LogContact)
            {
                var sw1 = Stopwatch.StartNew();
                var tmp = new NativeManifold(Allocator.Persistent);
                var normalResult = HullIntersection.NativeHullHullContact(ref tmp, t1, h1, t2, h2);
                sw1.Stop();
                tmp.Dispose();

                var sw2 = Stopwatch.StartNew();
                //var burstResult = HullOperations.TryGetContact.Invoke(out NativeManifold manifold, t1, h1, t2, h2);
                sw2.Stop();

                if (LogContact)
                {
                    Debug.Log($"'{a.name}'与'{b.name}'的接触计算耗时: {sw1.Elapsed.TotalMilliseconds:N4}ms (普通), {sw2.Elapsed.TotalMilliseconds:N4}ms (Burst)");
                }
            }
        }
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

        var mf = v.GetComponent<MeshFilter>();
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