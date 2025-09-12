using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Common;

namespace UnityNativeHull
{
    // 凸包工厂类，用于创建各种类型的凸包
    public class HullFactory
    {
        // 详细面定义结构体
        public struct DetailedFaceDef
        {
            public Vector3 Center; // 面中心点
            public Vector3 Normal; // 面法线
            public List<float3> Verts; // 面的顶点列表
            public List<int> Indices; // 顶点索引列表
        }

        // 原生面定义结构体(不安全代码)
        public unsafe struct NativeFaceDef
        {
            public int VertexCount; // 顶点数量
            public int* Vertices; // 顶点指针
            public int HighestIndex; // 最高顶点索引(用于优化)
        };

        // 原生凸包定义结构体(不安全代码)
        public unsafe struct NativeHullDef
        {
            public int FaceCount; // 面数量
            public int VertexCount; // 顶点数量
            public NativeArray<float3> VerticesNative; // 顶点原生数组
            public NativeArray<NativeFaceDef> FacesNative; // 面原生数组
        };

        // 从网格（Mesh）数据创建一个凸包（NativeHull）
        //从 Mesh 顶点和三角形索引数据，计算每个三角形的法线和中心。
        //去除重复顶点，合并具有相同法线且共享顶点的三角形，形成大面。
        //计算每个合并面边界的顶点序列，标记“孤立顶点”并将其剔除。
        //构造最终的顶点和面数据，传入底层方法生成 NativeHull 凸包数据结构
        public static unsafe NativeHull CreateFromMesh(Mesh mesh)
        {
            // 存储详细面的列表（带有顶点、法线等信息）
            var faces = new List<DetailedFaceDef>();
            // 对网格顶点进行舍入处理，防止因浮点精度导致重复顶点识别失败
            var verts = mesh.vertices.Select(RoundVertex).ToArray();
            // 去除重复顶点，只保留唯一顶点
            var uniqueVerts = verts.Distinct().ToList();
            // 获取网格的三角形索引数组（三个一组）
            var indices = mesh.triangles;

            // 遍历所有三角形，每3个索引为一个三角形，主要为了收集面信息
            for (int i = 0; i < mesh.triangles.Length; i = i + 3)
            {
                // 三角形顶点索引
                var idx1 = i;
                var idx2 = i + 1;
                var idx3 = i + 2;

                // 根据索引获取三角形顶点的坐标
                Vector3 p1 = verts[indices[idx1]];
                Vector3 p2 = verts[indices[idx2]];
                Vector3 p3 = verts[indices[idx3]];

                // 计算三角形法线（通过叉乘两个边向量，并归一化）
                var normal = math.normalize(math.cross(p3 - p2, p1 - p2));
                // 对法线进行舍入，防止法线因为浮点误差微小不同而无法正确归类
                var roundedNormal = RoundVertex(normal);

                // 创建一个详细面定义，包含中心点、法线、顶点列表和顶点索引
                faces.Add(new DetailedFaceDef
                {
                    Center = ((p1 + p2 + p3) / 3), // 面中心点（3顶点坐标均值）
                    Normal = roundedNormal, // 舍入后的法线
                    Verts = new List<float3> { p1, p2, p3 }, // 三角形顶点
                    Indices = new List<int>
                    {
                        uniqueVerts.IndexOf(p1), // 顶点在唯一顶点列表中的索引
                        uniqueVerts.IndexOf(p2),
                        uniqueVerts.IndexOf(p3)
                    }
                });
            }

            // 创建一个存储最终合并面定义的列表
            var faceDefs = new List<NativeFaceDef>();
            // 用来记录“孤立”顶点的索引，这些顶点没有被任何边界连接
            var orphanIndices = new HashSet<int>();
            // 先根据法线分组，再根据共享顶点分组，合并具有相同法线和共享顶点的所有面
            var mergedFaces = GroupBySharedVertex(GroupByNormal(faces));

            // 遍历合并后的每组面
            foreach (var faceGroup in mergedFaces)
            {
                // 收集该组所有面中所有顶点的索引，SelectMany 会将所有 face.Indices 扁平化成一个单一的集合（flat list），而不是一个集合的集合
                var indicesFromMergedFaces = faceGroup.SelectMany(face => face.Indices).ToArray();
                // 计算这些顶点形成的多边形的边界轮廓（边界顶点序列）
                var border = PolygonPerimeter.CalculatePerimeter(indicesFromMergedFaces);
                // 获取边界顶点的索引列表，提取EndIndex组成新数组
                var borderIndices = border.Select(b => b.EndIndex).ToArray();
                // 找出在组内顶点索引中不在边界上的顶点，认为它们是“孤立顶点”
                //Except(borderIndices) 会返回一个新的集合，包含 indicesFromMergedFaces 中有、但 borderIndices 中没有的元素（即差集）
                foreach (var idx in indicesFromMergedFaces.Except(borderIndices))
                {
                    orphanIndices.Add(idx);
                }

                // 使用栈内存分配存储边界顶点索引的数组（为了性能）,不然在托管堆上
                var v = stackalloc int[borderIndices.Length];
                int max = 0;

                // 遍历边界顶点索引，记录最大索引值（后续删除孤立顶点时需要）
                for (int i = 0; i < borderIndices.Length; i++)
                {
                    var idx = borderIndices[i];
                    if (idx > max)
                        max = idx;
                    v[i] = idx;
                }

                // 创建一个新的面定义，包含最大顶点索引、顶点数量、顶点索引数组
                faceDefs.Add(new NativeFaceDef
                {
                    HighestIndex = max,
                    VertexCount = borderIndices.Length,
                    Vertices = v,
                });
            }

            foreach (var orphanIdx in orphanIndices.OrderByDescending(i=>i))
            {
                // 删除孤立顶点
                uniqueVerts.RemoveAt(orphanIdx);
                
                // 修正面中顶点索引，只修正那些 使用了索引大于或等于 orphanIdx 的面，因为小于它的索引不受影响，
                foreach (var face in faceDefs.Where(f => f.HighestIndex >= orphanIdx))
                {
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        var faceVertIdx = face.Vertices[i];
                        if (faceVertIdx >= orphanIdx)
                        {
                            // 顶点索引减1，保持索引正确
                            face.Vertices[i] = --faceVertIdx;
                        }
                    }
                }
            }
            // 创建一个空的 NativeHull 结构体，用于存放结果
            var result = new NativeHull();
            
            // 使用临时原生数组（NativeArray）存放面和顶点数据，方便后续调用本地方法构建凸包
            // Allocator.Temp就是告诉Unity，我要分配一段临时使用的原生内存，在这一帧内使用完就释放
            using (var faceNative = new NativeArray<NativeFaceDef>(faceDefs.ToArray(), Allocator.Temp))
            using (var vertsNative = new NativeArray<float3>(uniqueVerts.ToArray(), Allocator.Temp))
            {
                NativeHullDef hullDef;
                hullDef.VertexCount = vertsNative.Length; // 顶点数量
                hullDef.VerticesNative = vertsNative; // 顶点数组
                hullDef.FaceCount = faceNative.Length; // 面数量
                hullDef.FacesNative = faceNative; // 面数组

                // 调用本地函数根据面定义构建 NativeHull
                SetFromFaces(ref result, hullDef);
            }

            // 标记结果为已创建状态
            result.IsCreated = true;

            // 返回最终生成的凸包
            return result;
        }

        // 从面定义设置凸包（NativeHull）结构体
        public unsafe static void SetFromFaces(ref NativeHull hull, NativeHullDef def)
        {
            //面数和顶点数必须大于0，确保数据合法
            Debug.Assert(def.FaceCount > 0);
            Debug.Assert(def.VertexCount > 0);
            
            //设置顶点数量
            hull.VertexCount=def.VertexCount;

            // 将原生顶点数组转换为普通托管数组
            var arr = def.VerticesNative.ToArray();
            
            // 将顶点数据复制到 Persistent 分配的 NativeArray 中（长期保留）
            hull.VerticesNative=new NativeArrayNoLeakDetection<float3>(arr, Allocator.Temp);
            
            //获取指针
            hull.Vertices=(float3*)hull.VerticesNative.GetUnsafePtr();
            
        }
        // 按法线分组
        public static Dictionary<float3, List<DetailedFaceDef>> GroupByNormal(IList<DetailedFaceDef> data)
        {
            var map = new Dictionary<float3, List<DetailedFaceDef>>();
            for (var i = 0; i < data.Count; i++)
            {
                var item = data[i];
                if (!map.TryGetValue(item.Normal, out List<DetailedFaceDef> value))
                {
                    map[item.Normal] = new List<DetailedFaceDef> { item };
                    continue;
                }

                value.Add(item);
            }

            return map;
        }

        // 按共享顶点分组
        // 输入参数 groupedFaces 是一个字典，键是法线方向（float3），值是所有具有该法线的面（DetailedFaceDef 列表）。
        // 这些面之前已经按法线归类，这里要进一步把法线相同且有“顶点连接”的面归并在一起。
        public static List<List<DetailedFaceDef>> GroupBySharedVertex(
            Dictionary<float3, List<DetailedFaceDef>> groupedFaces)
        {
            // 最终结果：每组是若干个法线相同、且共享顶点的面
            var result = new List<List<DetailedFaceDef>>();

            // 遍历每个法线分组（即法线方向相同的一组面）
            foreach (var facesSharingNormal in groupedFaces)
            {
                // 临时 map，每个元素包含：
                // - 一个 HashSet<int>：用于记录当前面组中所有顶点的索引（用于判断是否与其它面共享）
                // - 一个面列表 List<DetailedFaceDef>：存储当前组的所有面
                var map = new List<(HashSet<int> Key, List<DetailedFaceDef> Value)>();

                // 遍历当前法线下的所有面
                foreach (var face in facesSharingNormal.Value)
                {
                    // 尝试查找当前面是否与已有组共享顶点
                    var group = map.FirstOrDefault(pair => face.Indices.Any(pair.Key.Contains));
                    if (group.Key != null)
                    {
                        // 如果找到了共享顶点的组：将当前面的所有顶点加入该组的顶点集合
                        foreach (var idx in face.Indices)
                        {
                            group.Key.Add(idx);
                        }

                        // 把当前面加入该组中
                        group.Value.Add(face);
                    }
                    else
                    {
                        // 没有共享顶点的组，就创建一个新组，把当前面作为第一项
                        map.Add((new HashSet<int>(face.Indices), new List<DetailedFaceDef> { face }));
                    }
                }

                // 把该法线方向下的所有合并组加入最终结果
                result.AddRange(map.Select(group => group.Value));
            }

            return result;
        }

        // 顶点坐标舍入，保留小数点后3位就行了
        public static float3 RoundVertex(Vector3 v)
        {
            return new float3(
                (float)System.Math.Round(v.x, 3),
                (float)System.Math.Round(v.y, 3),
                (float)System.Math.Round(v.z, 3));
        }

        // 多边形周长计算结构
        public struct PolygonPerimeter
        {
            public struct Edge
            {
                public int StartIndex;
                public int EndIndex;
            }

            private static readonly List<Edge> OutsideEdges = new List<Edge>();

            // 计算多边形的边界周长（即外部边缘的有序列表）
            // 参数：
            //   indices：由三角面组成的索引数组（每 3 个为一组三角形）
            // 返回：
            //   外部边缘（即构成多边形轮廓的边）列表，已按顺序排列形成闭环
            public static List<Edge> CalculatePerimeter(int[] indices)
            {
                // 清空全局临时边列表 OutsideEdges（存储的是最终的“边界边”，不是三角形内部边）
                OutsideEdges.Clear();

                //遍历所有三角形（每 3 个索引构成一个三角形）
                for (var i = 0; i < indices.Length - 1; i += 3)
                {
                    int v1 = indices[i];
                    int v2 = indices[i + 1];
                    int v3 = indices[i + 2];

                    // 将三角形的三条边尝试加入外部边集合
                    AddOutsideEdge(v1, v2); // 边 v1->v2
                    AddOutsideEdge(v2, v3); // 边 v2->v3
                    AddOutsideEdge(v3, v1); // 边 v3->v1
                }

                for (int i = 0; i < OutsideEdges.Count; i++)
                {
                    var edge = OutsideEdges[i];
                    var nextIdx=i+1>OutsideEdges.Count-1?0:i+1;// 最后一个边指向第一个，闭环
                    var next=OutsideEdges[nextIdx];
                    
                    // 如果当前边的终点不是下一个边的起点，说明边界不连续，需重构
                    if (edge.EndIndex != next.StartIndex)
                    {
                        return Rebuild(); // 尝试按连通方式重新排序边
                    }
                }
                
                // 所有边已经按顺序闭合，直接返回
                return OutsideEdges;
            }
            
            // 添加一个边到外部边集合中（用于构建多边形的边界）
            // 如果该边的反向边已经存在，则说明这是一个内部边，将其从集合中移除。
            // 否则将其作为边界边添加进去。
            public static void AddOutsideEdge(int v1, int v2)
            {
                for (var i = 0; i < OutsideEdges.Count; i++)
                {
                    if ((OutsideEdges[i].StartIndex == v1 && OutsideEdges[i].EndIndex == v2) ||
                        OutsideEdges[i].StartIndex == v2 && OutsideEdges[i].EndIndex == v1)
                    {
                        // 已经存在这条边或其反向边，说明它是两个三角形共享的“内部边”
                        // 将它从外部边集合中移除（最终我们只保留非共享的“轮廓边”）
                        OutsideEdges.Remove(OutsideEdges[i]);
                        return;
                    }
                }
                OutsideEdges.Add(new Edge { StartIndex = v1, EndIndex = v2 });
            }
            
            // 重建边的顺序，使它们按连续顺序连接成一个闭合的轮廓边链
            private static List<Edge> Rebuild()
            {
                var result = new List<Edge>();
                
                // 构建一个从起点索引到终点索引的映射字典
                // 用于快速查找给定起点对应的终点（边的连接关系）
                var map=OutsideEdges.ToDictionary(k=>k.StartIndex, v=>v.EndIndex);
                
                var cur=OutsideEdges.First().StartIndex;
                
                // 依次构造每一条边，使它们首尾相接
                for (int i = 0; i < OutsideEdges.Count; i++)
                {
                    var edge = new Edge 
                        { StartIndex = cur, // 当前边的起点
                            EndIndex = map[cur]// 当前边的终点，从映射中查到
                        };
                    
                    // 添加到结果集合中
                    result.Add(edge);
                    
                    // 将当前终点设置为下一条边的起点，继续连边
                    cur=edge.EndIndex;
                }
                // 返回重建后的、有序的边集合
                return result;
            }
        }
    }
}