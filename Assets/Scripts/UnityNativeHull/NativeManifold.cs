using System;
using Common;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityNativeHull
{
    /// <summary>
    /// NativeManifold 结构体用于表示碰撞检测中的接触点信息。
    /// </summary>
    public unsafe struct NativeManifold: IDisposable
    {
        public const int MaxPoints = 24;
        public float3 Normal; // A -> B 之间的法向量
        private int _maxIndex;// 当前已添加的接触点索引
        private NativeBuffer _points;
        public int Length => _maxIndex + 1;
        // 将接触点缓冲区转换为数组形式
        public ContactPoint[] ToArray() => _points.ToArray<ContactPoint>(Length);
        // 获取指定索引的接触点
        public ContactPoint this[int i] => _points.GetItem<ContactPoint>(i);
        // 判断缓冲区是否已创建
        public bool IsCreated => _points.IsCreated;
        
        // 构造函数，初始化接触面缓冲区
        public NativeManifold(Allocator allocator)
        {
            _points = NativeBuffer.Create<ContactPoint>(MaxPoints, Allocator.Persistent);
            _maxIndex = -1;
            Normal = 0;
        }
        
        // 添加一个接触点，包含位置、距离和ID
        public void Add(float3 position, float distance, ContactID id)
        {
            Add(new ContactPoint
            {
                Id = id,
                Position = position,
                Distance = distance,
            });
        }
        // 添加一个接触点
        private void Add(ContactPoint cp)
        {
            if (_maxIndex >= MaxPoints)
                return;

            _points.SetItem(++_maxIndex, cp);
        }
        public void Dispose()
        {
            if (_points.IsCreated)
            {
                _points.Dispose();
            }
        }
    }
    //定义单个接触点的完整信息
    public struct ContactPoint
    {
        public ContactID Id;
        /// <summary>
        /// 形状第一次碰撞时的接触点位置。
        /// （即每个形状上两点之间的连线的中点）。
        /// </summary>
        public float3 Position;
        public float Distance;//两形状之间的距离
        public float Penetration;//穿透深度
    }
    public struct ContactID
    {
        // 接触点的ID，包含了一个FeaturePair和一个键值
        public FeaturePair FeaturePair;
    }
    /// <summary>
    /// 多边形裁剪后生成的新点,不再是原 hull 的顶点,
    /// 需要知道,这个点是由 hull 的哪条 edge 与哪个 clipping plane 相交产生的
    /// </summary>
    public struct FeaturePair
    {
        public int InEdge1;// 来自形状1输入边
        public int OutEdge1;// 来自形状1输出边
        public int InEdge2;// 来自形状2输入边
        public int OutEdge2;// 来自形状2输出边
    }
}