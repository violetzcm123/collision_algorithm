using System.Numerics;
using Common;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityNativeHull
{
    public struct ClipVertex
    {
        public float3 position; // 裁剪后的顶点位置
        public FeaturePair featurePair; // 顶点的特征对信息
        public NativePlane plane; // 所属面
        public float3 hull2local; // 原始 local 位置（调试/反变换用）
    };

    public struct ClipPlane
    {
        public Vector3 position;
        public NativePlane plane;
        public int edgeId;
    };

    public class HullIntersection
    {
        #region 绘制（用于调试）

        public static bool DrawNativeHullHullIntersection(RigidTransform t1, NativeHull hull1, RigidTransform t2,
            NativeHull hull2)
        {
            // 对 hull2 的每一个面，与 hull1 做裁剪
            for (int i = 0; i < hull2.FaceCount; i++)
            {
                // 创建一个临时的接触面区域（manifold）用于存储裁剪结果
                var tmp = new NativeManifold(Allocator.Temp);

                // 将 hull2 的第 i 个面投影/裁剪到 hull1 上
                ClipFace(ref tmp, i, t2, hull2, t1, hull1);

                // 将裁剪出的接触区域绘制出来（调试用）
                HullDrawingUtility.DebugDrawManifold(tmp);

                // 释放临时资源
                tmp.Dispose();
            }

            // 对 hull1 的每一个面，与 hull2 做裁剪（同样的操作反向再做一次）
            for (int i = 0; i < hull1.FaceCount; i++)
            {
                var tmp = new NativeManifold(Allocator.Temp);

                // 将 hull1 的第 i 个面投影/裁剪到 hull2 上
                ClipFace(ref tmp, i, t1, hull1, t2, hull2);

                // 绘制结果
                HullDrawingUtility.DebugDrawManifold(tmp);

                tmp.Dispose();
            }

            // 返回值目前无意义，始终返回 true，仅作为调试函数
            return true;
        }

        private static void ClipFace(ref NativeManifold tmp, int faceIndex, RigidTransform t1, NativeHull hull1,
            RigidTransform t2, NativeHull hull2)
        {
            // 将 hull1 的第 i 个局部平面变换到世界空间中
            var plane = t1 * hull1.GetPlane(faceIndex);
            // 找出 hull2 中与该平面相对的面索引，
            var incidentFaceIndex = ComputeIncidentFaceIndex(plane, hull2, t2);

            // 执行实际裁剪操作
            ClipFaceAgainstAnother(ref tmp, faceIndex, t1, hull1, incidentFaceIndex, t2, hull2);
        }

        private static int ComputeIncidentFaceIndex(NativePlane plane, NativeHull hull, RigidTransform t)
        {
            var normal = plane.Normal;
            int minIndex = 0;
            //用第一个面作为候选面
            float minDot = math.dot(normal, (t * hull.GetPlane(minIndex)).Normal);
            for (int i = 1; i < hull.FaceCount; i++)
            {
                float dot = math.dot(normal, (t * hull.GetPlane(i)).Normal);
                if (dot < minDot)
                {
                    minDot = dot;
                    minIndex = i;
                }
            }

            // 返回该面索引，它将作为“incident face”
            return minIndex;
        }

        // 将 hull2 的某个面（incidentFaceIndex）裁剪到 hull1 的 referenceFaceIndex 面所在的平面上
        private static int ClipFaceAgainstAnother(ref NativeManifold tmp,
            int referenceFaceIndex, RigidTransform t1, NativeHull hull1,
            int incidentFaceIndex, RigidTransform t2, NativeHull hull2)
        {
            // 获取世界空间下的reference 面的平面
            NativePlane referencePlane = t1 * hull1.GetPlane(referenceFaceIndex);

            NativeBuffer<ClipPlane> clipPlanes = new NativeBuffer<ClipPlane>(hull1.FaceCount, Allocator.Temp);

            // 从 referenceFace 构建裁剪平面组，用于将 incident polygon 裁剪成相交区域
            GetClippingPlanes(ref clipPlanes, t1, hull1);

            NativeBuffer<ClipVertex> incidentPolygon = new NativeBuffer<ClipVertex>(hull2.VertexCount, Allocator.Temp);
            // 计算 incident 面的初始多边形顶点
            ComputeFaceClippingPolygon(ref incidentPolygon, incidentFaceIndex, t2, hull2);

            for (int i = 0; i < clipPlanes.Length; i++)
            {
                //临时缓冲区，用于存储裁剪结果
                NativeBuffer<ClipVertex> outputPolygon =
                    new NativeBuffer<ClipVertex>(math.max(hull1.VertexCount, hull2.VertexCount), Allocator.Temp);

                // 对 incidentPolygon 进行裁剪
                Clip(clipPlanes[i], ref incidentPolygon, ref outputPolygon);

                if (outputPolygon.Length == 0)
                {
                    return -1;
                }

                // 释放旧的多边形缓冲区
                incidentPolygon.Dispose();
                // 使用当前裁剪结果作为下一轮裁剪的输入
                incidentPolygon = outputPolygon;
            }

            for (int i = 0; i < incidentPolygon.Length; i++)
            {
                ClipVertex vertex = incidentPolygon[i];
                float distance = referencePlane.Distance(vertex.position);

                tmp.Add(vertex.position, distance, new ContactID
                {
                    FeaturePair = vertex.featurePair
                });
            }

            clipPlanes.Dispose();
            incidentPolygon.Dispose();

            // 返回被裁剪的面索引（通常用于记录或调试）
            return incidentFaceIndex;
        }

        /// <summary>
        /// 执行 Sutherland-Hodgman 多边形裁剪算法。
        /// 所有边界平面的法线都指向外侧，因此保留面内侧的顶点。
        /// </summary>
        private static void Clip(ClipPlane clipPlane, ref NativeBuffer<ClipVertex> input,
            ref NativeBuffer<ClipVertex> output)
        {
            ClipVertex vertex1 = input[input.Length - 1];
            float distance1 = clipPlane.plane.Distance(vertex1.position);

            for (int i = 0; i < input.Length; i++)
            {
                ClipVertex vertex2 = input[i];
                float distance2 = clipPlane.plane.Distance(vertex2.position);

                // 顶点1和顶点2都在平面后面或正好在平面上 → 保留顶点2
                if (distance1 <= 0 && distance2 <= 0)
                {
                    output.Add(vertex2);
                }
                // 顶点1在内侧，顶点2在平面外侧
                else if (distance1 <= 0 && distance2 > 0)
                {
                    float fraction = distance1 / (distance1 - distance2); // 计算插值比例
                    // 计算交点并添加到输出
                    float3 pos = vertex1.position + fraction * (vertex2.position - vertex1.position);
                    ClipVertex clipVertex;
                    clipVertex.position = pos;
                    clipVertex.plane = clipPlane.plane;
                    clipVertex.hull2local = pos;
                    clipVertex.featurePair.InEdge1 = -1;
                    clipVertex.featurePair.OutEdge1 = (sbyte)clipPlane.edgeId; // 交点由当前平面裁剪边产生
                    clipVertex.featurePair.InEdge2 = vertex1.featurePair.OutEdge2;
                    clipVertex.featurePair.OutEdge2 = -1;
                    output.Add(clipVertex); // 添加交点
                }
                // 顶点1在平面外侧，顶点2在内侧
                else if (distance1 > 0 && distance2 <= 0)
                {
                    float fraction = distance1 / (distance1 - distance2); // 计算插值比例
                    // 计算交点并添加到输出
                    float3 pos = vertex1.position + fraction * (vertex2.position - vertex1.position);
                    ClipVertex clipVertex;
                    clipVertex.position = pos;
                    clipVertex.plane = clipPlane.plane;
                    clipVertex.hull2local = pos;
                    clipVertex.featurePair.InEdge1 = (sbyte)clipPlane.edgeId; // 当前平面是交点进入边;
                    clipVertex.featurePair.OutEdge1 = -1;
                    clipVertex.featurePair.InEdge2 = -1;
                    clipVertex.featurePair.OutEdge2 = vertex1.featurePair.OutEdge2;
                    output.Add(clipVertex); // 添加交点

                    output.Add(vertex2); // 添加顶点2
                }

                // 移动到下一条边
                vertex1 = vertex2;
                distance1 = distance2;
            }
        }

        // <summary>
        /// 获取所有变换后的裁剪面（clipping planes），用于将 incident 面裁剪到 reference 面的边界内。
        /// </summary>
        private static unsafe void GetClippingPlanes(ref NativeBuffer<ClipPlane> output, RigidTransform transform,
            NativeHull hull)
        {
            for (int i = 0; i < hull.FaceCount; i++)
            {
                var p = hull.GetPlane(i); // 获取第 i 个面的局部平面信息

                // 将平面从局部空间转换到世界空间或参考空间
                output.Add(new ClipPlane
                {
                    plane = transform * p,
                });
            }
        }

        private static unsafe void ComputeFaceClippingPolygon(ref NativeBuffer<ClipVertex> output, int faceIndex,
            RigidTransform t, NativeHull hull)
        {
            // 获取面数据和该面所在的平面
            NativeFace* face = hull.GetFaces(faceIndex);
            NativePlane plane = hull.GetPlane(faceIndex);

            // 获取该面首条边
            NativeHalfEdge* startEdge = hull.GetHalfEdgePtr(face->Edge);
            NativeHalfEdge* currentEdge = startEdge;

            do
            {
                //获取对边
                NativeHalfEdge* twinEdge = hull.GetHalfEdgePtr(currentEdge->Twin);

                // 获取当前边的起点（局部空间）并转换到目标空间（例如世界空间）
                float3 vertex = hull.GetVertex(currentEdge->Origin);
                float3 P = math.transform(t, vertex);

                ClipVertex clipVertex;
                clipVertex.position = P;
                clipVertex.plane = plane;
                clipVertex.hull2local = vertex;
                clipVertex.featurePair.InEdge1 = -1;
                clipVertex.featurePair.OutEdge1 = -1;
                clipVertex.featurePair.InEdge2 = (sbyte)currentEdge->Next; // 属于 incident hull
                clipVertex.featurePair.OutEdge2 = (sbyte)twinEdge->Twin; // 属于 reference hull

                // 添加裁剪顶点到输出列表
                output.Add(clipVertex);
                // 移动到下一条边
                currentEdge = hull.GetHalfEdgePtr(currentEdge->Next);
            } while (currentEdge != startEdge);
        }

        #endregion

        public static bool NativeHullHullContact(ref NativeManifold res, RigidTransform t1, NativeHull h1,
            RigidTransform t2, NativeHull h2)
        {
            FaceQueryResult faceQueryResult1;
            HullCollision.QueryFaceDistance(out faceQueryResult1, t1, h1, t2, h2);

            // 如果第一个面的距离大于0，表示两凸包在该面方向上未接触（分离）
            if (faceQueryResult1.Distance > 0)
            {
                return false; // 无碰撞
            }

            // 查询第二个凸包 hull2 在 hull1 上的面距离
            FaceQueryResult faceQueryResult2;
            HullCollision.QueryFaceDistance(out faceQueryResult2, t2, h2, t1, h1);

            // 若第二个面的距离大于0，也表示分离无碰撞
            if (faceQueryResult2.Distance > 0)
            {
                return false; // 无碰撞
            }

            // 查询两个凸包之间的边距离（边与边的最近距离），边检测有助于发现边缘接触情况
            HullCollision.QueryEdgeDistance(out EdgeQueryResult edgeQuery, t1, h1, t2, h2);

            // 如果边距离大于0，说明边之间也没有接触，返回无碰撞
            if (edgeQuery.Distance > 0)
            {
                return false;
            }

            float kRelEdgeTolerance = 0.9f; // 边检测相对容忍度90%
            float kRelFaceTolerance = 0.95f; // 面检测相对容忍度95%
            float kAbsTolerance = 0.5f * 0.005f; // 绝对容忍度，控制最小容忍范围

            float maxEdgeDistance = math.max(faceQueryResult2.Distance, faceQueryResult1.Distance);

            if (edgeQuery.Distance > maxEdgeDistance*kRelFaceTolerance+kAbsTolerance)
            {
                // 生成边接触信息，添加到碰撞结果中
                CreateEdgeContact(ref res, edgeQuery, t1, h1, t2, h2);
            }
            else
            {
                // 生成面接触信息，添加到碰撞结果中
                if (faceQueryResult2.Distance>kRelFaceTolerance*faceQueryResult1.Distance+kAbsTolerance)
                {
                    CreateFaceContact(ref res, faceQueryResult2, t2, h2, t1, h1,false);
                }
                else
                {
                    CreateFaceContact(ref res, faceQueryResult1, t1, h1, t2, h2,false);
                }
            }
            return true; // 碰撞检测通过，已生成接触信息
        }
        
        private unsafe static void CreateFaceContact(ref NativeManifold res, FaceQueryResult input,
            RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2, bool flipNormal)
        {
            //h1在h2中最大深度面
            var refPlane = h1.GetPlane(input.FaceIndex);
            // 将参考平面转换到世界空间
            var referencePlane=t1*refPlane;
            
            // 在栈上分配一个存储裁剪平面的缓冲区，大小为 hull1 的面数量
            var clippingPlaneStackPtr = stackalloc ClipPlane[h1.FaceCount];
            var clippingPlanes = new NativeBuffer<ClipPlane>(clippingPlaneStackPtr,h1.FaceCount);
            
            // 获取参考面的侧边平面，这些平面用于裁剪入射面多边形
            GetFaceSidePlanes(ref clippingPlanes, referencePlane, input.FaceIndex, t1, h1);
            
            // 在栈上分配一个缓冲区，用来存储入射多边形顶点，大小为 hull1 的顶点数
            var incidentPolygonStackPtr = stackalloc ClipVertex[h1.FaceCount];
            var incidentPolygon = new NativeBuffer<ClipVertex>(incidentPolygonStackPtr, h1.VertexCount);

            // 计算入射面索引（即与参考面法线最不平行的 hull2 的面）
            var incidentFaceIndex = ComputeIncidentFaceIndex(referencePlane, h2, t2);
            // 计算入射面多边形的顶点（裁剪多边形的初始顶点）
            ComputeFaceClippingPolygon(ref incidentPolygon, incidentFaceIndex, t2, h2);

            // 以下是用于多边形裁剪的临时输出缓冲区，大小同样为 hull1.FaceCount
            var outputPolygonStackPtr = stackalloc ClipVertex[h1.FaceCount];

            // 使用参考面的侧边平面逐个裁剪入射多边形
            for (int i = 0; i < clippingPlanes.Length; ++i)
            {
                // 每次裁剪产生新的输出多边形缓冲区
                var outputPolygon = new NativeBuffer<ClipVertex>(outputPolygonStackPtr, h1.FaceCount);

                // 将入射多边形按当前裁剪平面裁剪，结果存到 outputPolygon
                Clip(clippingPlanes[i], ref incidentPolygon, ref outputPolygon);

                // 如果裁剪后多边形顶点数为0，说明无交集，提前返回
                if (outputPolygon.Length == 0)
                {
                    return;
                }
                
                // 准备下一次裁剪，incidentPolygon 指向最新的裁剪结果
                incidentPolygon = outputPolygon;
            }

            // 遍历裁剪后的入射多边形顶点，生成接触点
            for (int i = 0; i < incidentPolygon.Length; ++i)
            {
                ClipVertex vertex = incidentPolygon[i];
                // 计算顶点到参考平面的距离
                float distance = referencePlane.Distance(vertex.position);

                // 如果顶点在参考平面下方（或在平面上），则为有效接触点
                if (distance <= 0)
                {
                    ContactID id = default;
                    id.FeaturePair = vertex.featurePair; // 保存特征对信息（边/顶点标识）

                    if (flipNormal)
                    {
                        // 如果需要翻转法线，法线取反
                        res.Normal = -referencePlane.Normal;
                        // 同时交换特征对中边的索引，保持一致性
                        Swap(id.FeaturePair.InEdge1, id.FeaturePair.InEdge2);
                        Swap(id.FeaturePair.OutEdge1, id.FeaturePair.OutEdge2);
                    }
                    else
                    {
                        // 否则法线保持参考平面法线方向
                        res.Normal = referencePlane.Normal;                        
                    }
                    
                    // 将顶点投影到参考平面上，作为接触点位置
                    float3 position = referencePlane.ClosestPoint(vertex.position);

                    // 将接触点位置、距离和特征对添加到碰撞接触点列表中
                    res.Add(position, distance, id);
                }
            }

            // 注意：clippingPlanes 和 incidentPolygon 都是栈上分配，没调用 Dispose
            // 若使用 NativeList，需显式 Dispose 释放托管内存
        }
        /// <summary>
        /// 为指定面生成其所有边对应的裁剪平面（side planes），并写入到输出列表中。
        /// </summary>
        public static unsafe void GetFaceSidePlanes(ref NativeBuffer<ClipPlane> output, NativePlane facePlane, int faceIndex, RigidTransform transform, NativeHull hull)
        { 
            // 获取该面起始边（HalfEdge结构）
            NativeHalfEdge* start = hull.GetHalfEdgePtr(hull.GetFaces(faceIndex)->Edge);
            NativeHalfEdge* current = start;

            do
            {
                // 获取当前边的“对边”（另一面共享的边）
                NativeHalfEdge* twin = hull.GetHalfEdgePtr(current->Twin);

                // 取当前边的两个端点（并转换到目标空间）
                float3 P = math.transform(transform, hull.GetVertex(current->Origin));
                float3 Q = math.transform(transform, hull.GetVertex(twin->Origin));

                // 构造裁剪平面（边 × 面法线 = 垂直于边并指向外侧的平面）
                ClipPlane clipPlane = default;
                clipPlane.edgeId = twin->Twin; // 记录边的 ID
                clipPlane.plane.Normal = math.normalize(math.cross(Q - P, facePlane.Normal)); // 侧平面法线
                clipPlane.plane.Offset = math.dot(clipPlane.plane.Normal, P); // 侧平面偏移量（点法式）

                // 添加裁剪平面到输出列表中
                output.Add(clipPlane);

                // 移动到下一个边
                current = hull.GetHalfEdgePtr(current->Next);
            }
            while (current != start); // 遍历完整个环（一个面是一圈连通边）
        }
        private static void Swap<T>(T a, T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

        private static unsafe void CreateEdgeContact(ref NativeManifold res, EdgeQueryResult input,
            RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2)
        {
            ContactPoint cp = default;
            
            if (input.Index1 < 0 || input.Index2 < 0) // 若任何一个边索引无效则返回
                return;
            
            NativeHalfEdge* edge1 = h1.GetHalfEdgePtr(input.Index1);
            NativeHalfEdge* twin1= h1.GetHalfEdgePtr(edge1->Twin);
            
            float3 P1= math.transform(t1, h1.GetVertex(edge1->Origin)); // 边1起点
            float3 Q1= math.transform(t1, h1.GetVertex(twin1->Origin)); // 边1终点
            float3 PQ1= Q1 - P1; // 边1向量    
            
            NativeHalfEdge* edge2 = h2.GetHalfEdgePtr(input.Index2);
            NativeHalfEdge* twin2= h2.GetHalfEdgePtr(edge2->Twin);
            
            float3 P2= math.transform(t2, h2.GetVertex(edge2->Origin)); // 边2起点
            float3 Q2= math.transform(t2, h2.GetVertex(twin2->Origin)); // 边2终点
            float3 PQ2 = Q2 - P2;
            
            float3 normal = math.normalize(math.cross(PQ1, PQ2)); // 计算边缘间的法线
            
            // 计算两个物体位置向量之差
            float3 C2C1 = t2.pos - t1.pos;

            // 判断法线方向是否指向 hull2 -> hull1，如果点积 < 0，需要反转法线方向及相关特征对
            if (math.dot(normal, C2C1) < 0)
            {
                res.Normal=-normal;
                
                cp.Id.FeaturePair.InEdge1=input.Index2;
                cp.Id.FeaturePair.OutEdge1=input.Index2 + 1;
                
                cp.Id.FeaturePair.InEdge2=input.Index1;
                cp.Id.FeaturePair.OutEdge2=input.Index1 + 1;
            }
            else
            {
                res.Normal = normal;

                cp.Id.FeaturePair.InEdge1 = input.Index1;
                cp.Id.FeaturePair.OutEdge1 = input.Index1 + 1;

                cp.Id.FeaturePair.InEdge2 = input.Index2 + 1;
                cp.Id.FeaturePair.OutEdge2 = input.Index2;
            }
            
            

        }
    }
}