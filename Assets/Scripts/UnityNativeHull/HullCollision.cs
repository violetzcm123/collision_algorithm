using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

namespace UnityNativeHull
{
    /// <summary>
    /// 表示面查询的结果。
    /// </summary>
    public struct FaceQueryResult
    {
        public int FaceIndex;
        public float Distance;
    }
    /// <summary>
    /// 表示面查询的结果。
    /// </summary>
    public struct EdgeQueryResult
    {
        public int Index1;  // 边1的起始顶点索引
        public int Index2;  // 边1的结束顶点索引
        public float Distance;
    }
    public struct CollisionInfo
    {
        public bool IsCollision;// 是否碰撞
        public FaceQueryResult Face1;// hull1 面查询结果
        public FaceQueryResult Face2;// hull2 面查询结果
        public EdgeQueryResult Edge;// 边查询结果
    }
    public class HullCollision
    {
        public static CollisionInfo GetDebugCollisionInfo(RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2)
        {
            CollisionInfo result = default;
            QueryFaceDistance(out result.Face1, t1, h1, t2, h2);
            QueryFaceDistance(out result.Face2, t2, h2, t1, h1);
            //主要是物体很有可能两个凸起的地方错开，实际没相交，单纯计算面就相交了。所以必须还检测边
            QueryEdgeDistance(out result.Edge, t1, h1, t2, h2);
            result.IsCollision = result.Face1.Distance < 0 && result.Face2.Distance < 0 && result.Edge.Distance < 0;
            return result;
        }
        public unsafe static void QueryFaceDistance(out FaceQueryResult result, RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2)
        {
            // 在第二个体的局部空间中进行计算
            RigidTransform transform = math.mul(math.inverse(t2), t1);
            //初始化距离
            result.Distance = -float.MaxValue;
            //初始化索引
            result.FaceIndex = -1;
            for (int i = 0; i < h1.FaceCount; i++)
            {
                //获取t2局部坐标系下的t1面平面
                NativePlane plane= transform * h1.GetPlane(i);
                //获取hull2在这个法线方向上的支撑点
                float3 support= h2.GetSupport(-plane.Normal);
                float distance = plane.Distance(support);
                if (distance>result.Distance)
                {
                    result.Distance= distance;
                    result.FaceIndex= i;
                }
            }
        }
        public unsafe static void QueryEdgeDistance(out EdgeQueryResult result, RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2)
        {
            // 在第二个体的局部空间中进行计算
            RigidTransform transform = math.mul(math.inverse(t2), t1);
            float3 C1 = transform.pos;
            result.Distance = -float.MaxValue;
            result.Index1 = -1;
            result.Index2 = -1;
            for (int i = 0; i < h1.EdgeCount; i+=2)
            {
                NativeHalfEdge* edge1 = h1.GetHalfEdgePtr(i);
                NativeHalfEdge* twin1 = h1.GetHalfEdgePtr(i + 1);
                
                //确保对偶关系
                Debug.Assert(edge1->Twin == i + 1 && twin1->Twin == i, "HalfEdge twin relationship is incorrect.");
                float3 A1 = h1.GetVertex(edge1->Origin);
                float3 B1 = h1.GetVertex(twin1->Origin);
                float3 AB = B1 - A1;

                float3 AB_N1 = h1.GetPlane(edge1->Face).Normal;
                float3 AB_N2 = h1.GetPlane(twin1->Face).Normal;
                for (int j = 0; j < h2.EdgeCount; j+=2)
                {
                    NativeHalfEdge* edge2 = h2.GetHalfEdgePtr(j);
                    NativeHalfEdge* twin2 = h2.GetHalfEdgePtr(j + 1);
                    
                    Debug.Assert(edge2->Twin == j + 1 && twin2->Twin == j, "HalfEdge twin relationship is incorrect.");
                    float3 D1 = h2.GetVertex(edge2->Origin);
                    float3 E1 = h2.GetVertex(twin2->Origin);
                    float3 DE = E1-D1;
                    
                    float3 DE_N1 = h2.GetPlane(edge2->Face).Normal;
                    float3 DE_N2 = h2.GetPlane(twin2->Face).Normal;

                    if (IsMinkowskiFace( AB_N1,AB_N2, -AB, -DE_N1, -DE_N2, -DE))
                    {
                        float distance = Project(A1,AB,D1,DE,C1);
                        if (distance > result.Distance)
                        {
                            
                        }
                    }
                }
            }
        }
        public static bool IsMinkowskiFace(float3 A,float3 B,float3 BxA,float3 C,float3 D,float3 DxC)
        {
            float BAC=math.dot(BxA, C);
            float BAD=math.dot(BxA, D);
            float DCA=math.dot(DxC, A);
            float DCB=math.dot(DxC, B);
            
            return (BAC*BAD<0)
                   &&(DCA*DCB<0)
                   &&(BAC*DCB>0);
        }
        
        /// <summary>
        /// 对两个边向量（分别属于两个物体）构造一个分离轴，
        /// 并将第二个物体相对于第一个物体在该轴上的投影距离作为分离度量返回。
        /// 如果两个边近似平行，则认为无法构造有效分离轴，返回一个极小值（表示忽略此轴）。
        /// </summary>
        /// <param name="P1">第一个物体的边的起点（世界坐标）</param>
        /// <param name="E1">第一个物体的边向量</param>
        /// <param name="P2">第二个物体的边的起点（世界坐标）</param>
        /// <param name="E2">第二个物体的边向量</param>
        /// <param name="C1">第一个物体的质心（用于决定分离轴方向）</param>
        /// <returns>
        /// 返回值为在构造出的分离轴（E1 × E2）方向上，第二个边相对于第一个边的投影距离：
        ///若为正，表示存在间隙；
        /// — 若为负，表示重叠（即发生碰撞）；
        /// — 若返回极小值（-float.MaxValue），表示两边近似平行，不适合作为分离轴。
        /// </returns>
        public static float Project(float3 P1,float3 E1,float3 P2,float3 E2,float3 C1)
        {
            float3 E1xE2= math.cross(E1, E2);

            float kTol = 0.005f;
            float len = math.length(E1xE2);
            
            if (len < kTol * math.sqrt(math.lengthsq(E1) * math.lengthsq(E2)))
            {
                return -float.MaxValue; // 极小值表示此分离轴无效
            }
            
            float3 N = (1/len) * E1xE2;
            
            if (math.dot(C1 - P1, N) < 0)
            {
                N = -N;
            }
            
            return math.dot(N, P2 - P1);
        }
    }
}