
- **功能**：管理碰撞接触点的集合
- **特性**：实现 IDisposable 接口，支持不安全代码
- **容量**：最大支持 24 个接触点（MaxPoints = 24）

**核心成员**：

- `Normal` ：碰撞法向量（从物体 A 指向物体 B），表示碰撞方向
- `_maxIndex` ：当前接触点索引计数器，跟踪当前接触点数量
- `_points` ：NativeBuffer 缓冲区，存储接触点数据

**主要方法**：

- `Add()` ：添加接触点（支持两种重载形式）
- `ToArray()` ：将缓冲区转换为数组
- `Dispose()` ：释放资源、
- `this[int i]`:类似数组，根据索引访问对象
- 构造函数：索引设为-1，将`Allocator.Persistent`数据放入数组

**ContactPoint 结构体** 定义单个接触点的完整信息：

- `Id` ：接触点唯一标识
- `Position` ：碰撞点位置（两个形状接触点的中点）
- `Distance` ：两形状之间的距离
- `Penetration` ：穿透深度向量

**ContactID 和 FeaturePair** 用于精确标识碰撞特征：

- `FeaturePair` ：记录两个碰撞形状的输入/输出边信息
- 通过边索引精确追踪碰撞发生的具体几何特征