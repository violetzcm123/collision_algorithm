using System;
using System.Collections.Generic;
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
    }
}