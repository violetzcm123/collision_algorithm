using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace UnityNativeHull
{
    public unsafe struct NativePlane
    {
        /// <summary>
        /// 平面的法向量，表示平面相对于 hull 原点的方向。
        /// </summary>
        public float3 Normal;

        /// <summary>
        /// 平面到 hull 原点的距离。
        /// </summary>
        public float Offset;

        //构造函数
        public NativePlane(float3 normal, float offset)
        {
            Normal = normal;
            Offset = offset;
        }

        //计算点到平面距离
        public float Distance(float3 point)
        {
            return dot(point, Normal) - Offset;
        }
        
        //点到平面最近的的点
        public float3 ClosestPoint(float3 point)
        {
            return point - Distance(point) * normalize(Normal);
        }

        public static NativePlane operator *(RigidTransform t, NativePlane p)
        {
            // 对平面的法向量进行旋转变换
            float3 normal = mul(t.rot,p.Normal);
            // 返回平面的位置和法向量经过刚体变换后的平面,新的偏移量 = 旧的偏移量 + 新法向量与刚体平移部分的点积
            return new NativePlane(normal, p.Offset+ dot(normal, t.pos));
        }
    }
}