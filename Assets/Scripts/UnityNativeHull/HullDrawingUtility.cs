using System;
using System.Collections.Generic;
using System.Linq;
using Common;
using Unity.Mathematics;
using UnityEngine;

namespace UnityNativeHull
{
    [Flags]

    public enum DebugHullFlags
    {
        None = 0,           //什么都不画
        PlaneNormals = 2,   //绘制平面法线 
        Indices = 4,        //绘制索引
        Outline = 8,        //绘制轮廓
        All = ~0,           //全部绘制
    }
    /// <summary>
    /// Hull 绘制工具类。
    /// </summary>
    public class HullDrawingUtility
    {
        public static void DrawDebugHull(NativeHull hull,RigidTransform t,DebugHullFlags options=DebugHullFlags.All,Color BaseColor=default)
        {
            if (!hull.IsValid)
            {
                throw new ArgumentException("Hull is not valid", nameof(hull));
            }

            if (options==DebugHullFlags.None)
            {
                return;
            }

            if (BaseColor==default)
            {
                BaseColor = Color.yellow;
            }

            for (int i = 0; i < hull.EdgeCount-1; i+=2)
            {
                var edge = hull.GetHalfEdge(i);
                var twin = hull.GetHalfEdge(i+1);
                
                //hull.GetVertex获取局部空间位置，进而用math.transform根据t来转化为世界空间
                var edgeVertex1 = math.transform(t, hull.GetVertex(edge.Origin));
                var twinVertex1 = math.transform(t, hull.GetVertex(twin.Origin));
                
                if ((options & DebugHullFlags.Outline) != 0)
                {
                    Debug.DrawLine(edgeVertex1 , twinVertex1 , BaseColor);
                }
            }
            
            //绘制平面法线
            if ((options & DebugHullFlags.PlaneNormals) != 0)
            {
                for (int i = 0; i < hull.FaceCount; i++)
                {
                    var center=math.transform(t,hull.CalculateFaceCentroid(hull.GetFace(i)));
                    var normal = math.rotate(t, hull.GetPlane(i).Normal);
                    DebugDrawer.DrawArrow(center, normal*0.2f, BaseColor);
                }
            }

            if ((options & DebugHullFlags.Indices) != 0)
            {
                var dupeCheck=new HashSet<Vector3>();
                for (int i = 0; i < hull.VertexCount; i++)
                {
                    var v= math.transform(t, hull.GetVertex(i));
                    var offset=dupeCheck.Contains(v) ? (float3)Vector3.forward * 0.05f : 0;
                    
                    DebugDrawer.DrawLabel(v+offset, i.ToString());
                    dupeCheck.Add(v);
                    
                }
            }
        }
        // 调试绘制一个碰撞接触点集，方便看哪里碰撞相交了
        public static void DebugDrawManifold(NativeManifold manifold, Color color = default)
        {
            // 如果碰撞接触点集没有创建或者长度为0，直接返回，不绘制
            if (!manifold.IsCreated || manifold.Length == 0)
                return;

            // 如果没有传入颜色参数，则默认使用蓝色，并且设置透明度为0.3
            if (color == default)
                color = UnityColors.Blue.ToOpacity(0.3f);

            // 遍历每一个接触点
            for (int i = 0; i < manifold.Length; i++)
            {
                var start = manifold[i];// 当前接触点
                if (manifold.Length >= 2)
                {
                    // 如果有两个及以上点，绘制线段
                    // end点为前一个接触点，i==0时，连接最后一个点形成闭环
                    var end = i > 0 ? manifold[i - 1] : manifold[manifold.Length - 1];
                    Debug.DrawLine(start.Position, end.Position, color);                    
                }
                // 以球体的形式绘制当前接触点，半径0.02，颜色透明度0.8
                DebugDrawer.DrawSphere(start.Position, 0.02f, color.ToOpacity(0.8f));
            }
            // 将所有接触点的位置提取成Vector3数组，绘制凸多边形的轮廓，颜色为前面定义的color
            DebugDrawer.DrawAAConvexPolygon(manifold.ToArray().Select(cp => (Vector3)cp.Position).ToArray(), color);
        }
    }
}