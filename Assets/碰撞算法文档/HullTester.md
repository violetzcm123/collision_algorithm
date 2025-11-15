HullTester — 每帧调度与碰撞可视化
---

## 概览
`HullTester` 是场景级的控制器（编辑器/运行时都可用），负责：
- 检测场景中需要进行凸包（NativeHull）构建的对象；
- 每帧遍历物体对并执行碰撞检测（调用 `HullCollision` / `HullIntersection` / `HullOperations`）；
- 绘制调试信息与记录耗时日志；
- 管理凸包资源的创建与释放。
## 流程图
``` mermaid
flowchart TD
subgraph "🎯 HullTester — 每帧调度与碰撞可视化"
direction TB


A["Update"]
B["HandleTransformChanged()"]
C["HandleHullCollisions()"]
D["过滤激活节点 + 去重"]
E["检查 Hulls 是否存在新节点"]
F["重建凸包字典"]
G["EnsureDestroyed() 清理旧资源"]
H["CreateShape(Transform)"]
I["CreateHull() — 调用 HullFactory.CreateFromMesh"]
J["更新 SceneView.RepaintAll() 重绘场景"]
K["return 跳过重建"]
L["遍历每对 Transform"]
M["获取 HullA/HullB 与 TransformA/B"]
N["HullDrawingUtility.DrawDebugHull"]
O["DrawHullCollision()"]
P["HullCollision.GetDebugCollisionInfo"]
Q["HullIntersection.DrawNativeHullHullIntersection"]
R["HullOperations.TryGetContact.Invoke"]
S["记录接触计算耗时 Stopwatch"]
T["绘制碰撞状态 DebugDrawer.DrawSphere"]
U["return"]

L_new["发现新节点或数量不同"]
L_nochange["无变化：跳过重建"]

A --> B
A --> C

B --> D
D --> E
E --> L_new
L_new --> F
F --> G
G --> H
H --> I
I --> J

E --> L_nochange
L_nochange --> K

C --> L
L --> M
M --> N
N --> O

O --> P
P --> Q
P --> R
R --> S
S --> T
P --> U
click A "#Update" "跳转到 Update"
click B "#HandleTransformChanged" "跳转到 HandleTransformChanged"
end
```

## 函数详解

### Update
`Update()` 是每帧调用的主调度入口：

- 执行 `HandleTransformChanged()` 检查是否需要重建凸包字典（例如新增/删除物体或启用状态变化）；
    
- 执行 `HandleHullCollisions()` 遍历物体对并进行碰撞检测与调试绘制。
    

**要点**：

- 在编辑器模式下也会运行（标注 `[ExecuteInEditMode]`）。
    
- 保持 Update 轻量：仅做调度与条件判断，把耗时计算交给子函数。
    

---

### HandleTransformChanged()

`HandleTransformChanged()` 的职责：

- 过滤 `Transforms` 列表（只保留激活的、去重）；
    
- 比较当前 `Hulls` 字典（InstanceID -> TestShape）是否与 `Transforms` 列表一致；
    
- 若发现新节点或数量不同：
    
    - 调用 `EnsureDestroyed()` 清理旧资源；
        
    - 使用 `CreateShape()` 为每个 `Transform` 生成 `TestShape` 并存入 `Hulls`；
        
    - 调用 `SceneView.RepaintAll()` 刷新编辑器视图。
        

**实现要点**：

- 为避免频繁 GC，实际生产可加缓存与增量更新逻辑（此实现为了可读性而直接重建）。
    

---

### CreateShape(Transform)

`CreateShape` 将 `Transform` 映射为 `TestShape`：

- 会调用 `CreateHull(Transform)` 来构建 `NativeHull`；
    
- 返回 `TestShape { Id = transform.GetInstanceID(), Hull = nativeHull }`。
    

**注意**：`TestShape` 是一个轻量 struct，包含 Id 与 NativeHull，用于字典存储与比较。

---

### CreateHull() — 调用 [HullFactory.CreateFromMesh](./HullFactory.md)

若 `Transform` 携带 `MeshCollider`（或其它可处理的碰撞体）：

- `CreateHull()` 会调用 `HullFactory.CreateFromMesh(meshCollider.sharedMesh)`；
    
- `HullFactory` 会把 Mesh 三角形合并成面，生成半边结构（Vertices / Faces / Edges / Planes）并返回 `NativeHull`。
    

**扩展**：可在此处支持 Box/ Sphere 等基础体的快速构建逻辑，避免对简单碰撞体调用网格构建。

---

### HandleHullCollisions

`HandleHullCollisions()` 的职责：

- 遍历 `Transforms` 列表的每一对物体（i < j）；
    
- 跳过未变化的 Transform（通过 `Transform.hasChanged` 优化）；
    
- 获取对应的 `NativeHull` 与 `RigidTransform`（position + rotation）；
    
- 绘制每个凸包的调试信息（`HullDrawingUtility.DrawDebugHull`）；
    
- 调用 `DrawHullCollision()` 来获取碰撞信息与绘制接触。
    

---

### DrawHullCollision()

`DrawHullCollision(GameObject a, GameObject b, RigidTransform t1, NativeHull h1, RigidTransform t2, NativeHull h2)`：

- 调用 `HullCollision.GetDebugCollisionInfo(t1,h1,t2,h2)` 返回面/边查询信息和 `IsColliding` 状态；
    
- 若 `IsColliding`：
    
    - （可选）调用 `HullIntersection.DrawNativeHullHullIntersection(...)` 绘制相交多边形与接触信息；
        
    - 创建 `NativeManifold` 并调用 `HullIntersection.NativeHullHullContact` 或 `HullOperations.TryGetContact.Invoke`（Burst 优化）获得接触点集合；
        
    - 记录耗时（普通 vs Burst）并写入 Debug Log；
        
    - 在场景视图绘制接触球体/线段做可视化。
        

---

### EnsureDestroyed() 清理旧资源

- 遍历 `Hulls` 字典，调用每个 `NativeHull.Dispose()` 安全释放原生数组；
    
- 清空字典，避免内存泄漏。