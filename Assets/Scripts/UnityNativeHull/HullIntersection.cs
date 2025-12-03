using System.Numerics;
using Common;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityNativeHull
{
    public struct ClipVertex
    {
        public float3 position;
        public FeaturePair featurePair;
        public NativePlane plane;
        public float3 hull2local;
    };
    public struct ClipPlane
    {
        public Vector3 position;
        public NativePlane plane;
        public int edgeId;
    };
    public class HullIntersection
    {
        public static bool DrawNativeHullHullIntersection(RigidTransform t1, NativeHull hull1, RigidTransform t2,
            NativeHull hull2)
        {
            
            //对于hull1的每个面，计算其在hull2中的投影，找出所有的接触面
            for (int i = 0; i < hull1.FaceCount; i++)
            {
                //储存接触面
                NativeManifold tmp= new NativeManifold(Allocator.Temp);

                ClipFace(ref tmp, i, t1, hull1, t2, hull2);
            }
            return false;
        }
        private static void ClipFace(ref NativeManifold tmp, int faceIndex, RigidTransform t1, NativeHull hull1, RigidTransform t2, NativeHull hull2)
        {
            // 将 hull1 的第 i 个局部平面变换到世界空间中
            var plane = t1 * hull1.GetPlane(faceIndex);
            // 找出 hull2 中与该平面相对的面索引，
            var incidentFaceIndex = ComputeIncidentFaceIndex(plane, hull2, t2);
            
            // 执行实际裁剪操作
            ClipFaceAgainstAnother(ref tmp,faceIndex, t1,hull1,incidentFaceIndex,t2, hull2 );
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
                if (dot< minDot)
                {
                    minDot= dot;
                    minIndex= i;
                }
            }
            // 返回该面索引，它将作为“incident face”
            return minIndex;
        }
        // 将 hull2 的某个面（incidentFaceIndex）裁剪到 hull1 的 referenceFaceIndex 面所在的平面上
        private static void ClipFaceAgainstAnother(ref NativeManifold tmp,
            int referenceFaceIndex, RigidTransform t1, NativeHull hull1, 
            int incidentFaceIndex, RigidTransform t2, NativeHull hull2)
        {
            // 获取世界空间下的reference 面的平面
            NativePlane referencePlane = t1 * hull1.GetPlane(referenceFaceIndex);
            
            NativeBuffer<ClipPlane> clipPlanes = new NativeBuffer<ClipPlane>(hull1.FaceCount, Allocator.Temp);
            
            // 从 referenceFace 构建裁剪平面组，用于将 incident polygon 裁剪成相交区域
            GetClippingPlanes(ref clipPlanes, t1, hull1);
            
            NativeBuffer<ClipVertex> incidentPolygon = new NativeBuffer<ClipVertex>(hull2.VertexCount, Allocator.Temp);
        }
        // <summary>
        /// 获取所有变换后的裁剪面（clipping planes），用于将 incident 面裁剪到 reference 面的边界内。
        /// </summary>
        private static unsafe void GetClippingPlanes(ref NativeBuffer<ClipPlane> output,  RigidTransform transform, NativeHull hull)
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
            NativeFace * face = hull.GetFaces(faceIndex);
            NativePlane plane = hull.GetPlane(faceIndex);
            
            // 获取该面首条边
            NativeHalfEdge* startEdge = hull.GetHalfEdgePtr(face->Edge);
            NativeHalfEdge* currentEdge = startEdge;

            do
            {
                //获取对边
                NativeHalfEdge* twinEdge = hull.GetHalfEdgePtr(currentEdge->Twin);
                
                float3 vertexPosition = math.transform(t, hull.GetVertex(twinEdge->Origin));
                

            } while (currentEdge!= startEdge);
        }
    }
}