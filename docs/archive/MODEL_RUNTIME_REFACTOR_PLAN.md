# 通用 2D 模型运行时改造方案

状态：本轮实施已完成，结果见 [实施报告](MODEL_RUNTIME_IMPLEMENTATION.md)。下文保留实施前的设计与验收约定，实际 API 以 addon README 为准。  
日期：2026-09-15。  
适用基线：当前工作区，包含上一轮 animation runtime 抽取及 `demo/SampleRig` 迁移结果。

## 1. 目标与完成标准

本次目标是补齐一个能实际运行、变形和绘制 2D 模型的运行时。现有动画播放器作为其中一个子系统保留。

**核心负责模型如何被运行；模型扩展负责这个角色具体怎样运动和变形。**

完成后，使用方应能通过代码：

1. 定义模型参数、网格、图层、纹理槽和遮罩关系。
2. 绑定动作和表情，创建多个相互独立的模型实例。
3. 使用内置的图层平移、旋转、缩放和透明度绑定，直接显示一个简单模型。
4. 编写自定义 CPU 变形器；需要 GPU 加速时，提供对应 shader 和绑定适配器。
5. 复用 addon 的节点、帧调度、网格提交、遮罩绘制和资源管理。

**最重要的验收场景：一个完全不引用 SampleRig 的新模型，不实现自己的 renderer，也能由 addon 完成显示、播放、变形、裁切、暂停与卸载。**

上一轮独立 consumer 自行创建 `Polygon2D`、自行接管输出，只证明了参数播放器可以扩展，不能作为上述目标的完成证据。

### 1.1 已确认约束

- 允许破坏性 API 改造。
- 主动作互斥，表情互斥；两者可并行，切换过程允许旧、新项短暂交叉淡化。
- 注视、口型等外部输入可以同时参与。
- 动作、表情、模型扩展绑定全部由代码定义。
- 保留曲线构建工具，不新增动作 JSON、解析器或文件加载流程。
- 现有头眼嘴、头发、呼吸、自动动作、角色物理和特定变形公式留在 sample。
- 保留现有两个 sample 的 CPU/GPU 渲染能力和视觉基线。

### 1.2 本次不做

- Live2D 文件兼容、MOC 执行器或完整 Cubism 功能复刻。
- 参数驱动网格变形的通用 JSON 语言、动作编辑器、GPU shader 自动生成。
- 多动作层动画图、自动 idle 状态机、网络输入协议。
- 动态拓扑、多级嵌套遮罩、遮罩图集、多种图形后端同时优化。
- 自动把任意 C# 变形函数编译成 GPU 程序。

这些限制不阻止本次提供完整的模型执行与渲染闭环。

## 2. 现状诊断：按责任拆分，而不是按文件移动

以下判断来自当前代码，路径均相对仓库根目录。

| 当前实现 | 可复用机制 | 必须留在 sample 的规则 | 本次处理 |
| --- | --- | --- | --- |
| `addons/anime25d/Core/AnimationRuntime.cs` | 参数混合、曲线采样、播放实例 | 无角色规则 | 保留为动画子系统，拆开采样和最终输出 |
| `demo/SampleRig/Core/Model/RigDefinition.cs` | 模型尺寸、图层列表、纹理引用 | Face/Neck/Eye anchors、Role/Side/Fade、旧格式解释 | 新建通用定义；原文件改为 sample 导入数据 |
| `Core/Model/PartState.cs` | 可见性、透明度、绘制顺序、实例顶点 | `StrandState[]`、角色 Depth、直接构建 RigMeshGeometry | 分为通用图层实例与 sample 私有状态 |
| `Core/Geometry/RigMeshGeometry.cs` | 静止顶点、UV、三角索引、矩形网格生成 | 根据 PhysicsMesh 选密度、头发权重、刘海权重烘焙 | 抽出 MeshDefinition/GridMeshBuilder；权重生成留 sample |
| `Core/Deformation/RigDeformer.cs` | 调用变形器、管理结果缓冲 | 头眼嘴、身体、头发公式 | 核心管理执行和缓冲；公式改为 CPU 变形器 |
| `Core/Deformation/LayerVisibility.cs` | 合成最终透明度和显隐 | 开闭眼/口过渡、备用闭眼选择 | 核心合成图层状态；sample 输出视觉权重 |
| `Core/Physics/PhysicsSolver.cs` | 有状态更新阶段、时间推进 | 头部驱动头发、呼吸驱动胸部、风力公式 | 纳入核心调度的 sample simulation |
| `Core/Physics/Spring.cs` | 数学弹簧积分 | StrandState、位移缩放约定 | 本次仍放 sample；核心提供物理扩展阶段即可 |
| `Rendering/RigRenderer.cs` | MeshInstance2D/ArrayMesh、更新、遮罩视口、资源清理 | Iris/EyeWhite/Side 判定、固定左右遮罩、sample shader 路径 | 拆出 addon renderer，使用显式渲染描述 |
| `Rendering/GpuPoseBuffer.cs` | 更新 GPU 数据的生命周期 | 固定 16 像素纹理和参数顺序、Breath 等附加槽 | 具体打包格式留 sample GPU 适配器 |
| `Rendering/GpuDeformationBinding.cs` | 初始化/每帧绑定/释放阶段 | 六根头发上限、纹理权重格式、anchor uniforms | 生命周期由 renderer 调度；内容留 sample |
| `Shaders/part.gdshader`、`eye_mask.gdshader` | 纹理采样、预乘透明度、遮罩读取 | `deform_vertex`、固定眼部语义 | 拆成通用材质契约和 sample 变形入口 |
| `AnimeRigNode.cs` | 加载、播放时钟、Advance、Refresh、Clear | 只接收 AnimeRigModel、直接创建 RigSimulation/RigRenderer | 建立通用模型节点，sample wrapper 只负责组装 |

不能直接搬回 `RigRenderer`：虽然它的注释说没有 rigging rules，实际创建遮罩、裁切和绑定时都在检查角色枚举。

不能直接搬回 `PartState`：构造函数同时生成特定网格并分配头发弹簧，尚不是通用实例容器。

另一个需要修正的生命周期问题：当前 `AnimeRigNode.LoadModel` 在 renderer 初始化之前调用 `ClearModel`。如果后续 shader 或渲染资源初始化失败，旧角色已经被清理。新节点需要真正的候选实例构建与成功后替换。

## 3. 总体架构与依赖方向

```text
addons/anime25d/
  Core/
    Animation/       参数、曲线、主动作层、表情层
    Model/           ModelDefinition / ModelInstance / 图层状态
    Geometry/        MeshDefinition / GridMeshBuilder / Bounds2D
    Evaluation/      帧调度、行为工厂、simulation、图层求值、CPU 变形契约
  Godot/
    AnimeModelNode   模型生命周期与引擎时钟
    ModelView       定义 + 已加载纹理 + 渲染扩展的绑定
    Rendering/      通用图层网格、材质、遮罩、CPU/GPU 提交
    Shaders/        通用纹理/遮罩/图层变换部分

demo/SampleRig/
  Import/           当前 manifest/profile 读取及旧版本兼容
  Definition/       将 sample 数据编译成通用模型定义
  Animation/        代码动作、表情、可选自动控制器
  Behavior/         anchors、头眼嘴规则、头发权重、弹簧状态、CPU 公式
  Rendering/        SampleGpuFactory / 数据打包 / sample 变形 shader
  SampleActor       可选的薄封装，供 demo 面板访问 sample 配置
```

依赖约束：

- `Core` 不引用 Godot、sample、shader 文件路径或 IO。
- `Godot` 引用 `Core`；不引用 `Anime25D.Sample`，不检查角色参数名或图层角色。
- sample 同时引用 `Core` 和 `Godot`，把自己的语义翻译成通用数据及扩展实现。
- Godot 类型、Texture2D、ShaderMaterial、SubViewport 不进入 Core。
- 原有模型和纹理加载仍由 sample/应用提供；运行时接收已构建的定义和已加载资源。
- 插件不需要动态发现或扫描扩展；通过构造参数和工厂显式注入。

`AnimationModel` 继续表示参数和动画资源集合。新 `ModelDefinition` 包含它，避免把几何和 Godot 资源塞入现有动画类型。

## 4. 通用数据与所有权

### 4.1 不可变模型定义

建议引入以下类型，名称可以在落地时统一调整，责任不能合并回角色实现：

| 类型 | 必要内容 | 约束 |
| --- | --- | --- |
| `ModelDefinition` | Canvas、AnimationModel、Meshes、Layers、Masks、BehaviorFactory | 不包含纹理对象、播放时钟或物理状态 |
| `MeshDefinition` | RestPositions、UV、TriangleIndices | 构造时复制并校验；定义发布后只读 |
| `LayerDefinition` | 稳定 Id、MeshId、TextureSlot、初始顺序/透明度/变换、可选 MaskId | Id 与显示名称分开；不含 Role/Side/Fade |
| `MaskDefinition` | 稳定 Id、SourceLayerIds、覆盖模式和阈值 | sample 负责选择眼白等 source；核心只处理图层引用 |
| `Bounds2D` | 模型空间保守包围盒 | GPU 变形不能依赖 CPU 顶点回读来确定裁剪范围 |
| `GridMeshBuilder` | 显式矩形、行列数或单元大小生成网格 | 不根据头发/身体角色自动决定密度 |

静止顶点统一采用模型坐标，UV 使用纹理坐标；第一版不改变拓扑。通用图层变换在自定义顶点变形之后应用，shader 的遮罩坐标也使用变换后的模型坐标。

`TextureSlot` 只是逻辑槽位，纹理是否存在、尺寸和 GPU 资源是否可用，在 `ModelView` 创建时校验。不要把图片路径放进 Core。

原 `PartState.Depth` 参与 sample 的视差/变形公式，**不直接变成通用图层的“深度”**。绘制顺序使用 `DrawOrder`；sample 的 Depth 作为其行为配置保存。

### 4.2 可变模型实例

`ModelInstance` 至少拥有：

- 独立的 AnimationRuntime、BasePose、最终参数快照。
- 独立的行为实例和 simulation 状态。
- 每层的持久用户设置与每帧求值结果。
- CPU 变形使用的顶点缓冲及结果版本号。
- 当前帧序号、运行状态及错误状态。

同一个 `ModelDefinition` 可以被多个实例共享；弹簧、当前参数、写入顶点、GPU 动态纹理不能共享。

LayerFrame、ResolvedPose 和 CPU 顶点使用预分配的 front/back 缓冲。先在 back 求值，通过有限值等检查后再交换发布；扩展异常时保留上一份 front，实例进入 faulted 状态。这里保证已发布帧完整，不保证回滚任意扩展的私有状态。

Frame 的只读 view 只保证在下一次成功 Advance/Refresh 之前有效，renderer 必须同步消费；需要跨帧保存的诊断工具显式复制。GPU 路径仍可使用同样的帧元数据机制，但不强制逐帧创建或写入 CPU 顶点。

将图层状态分为两层：

1. `LayerOverrides`：用户持久修改，例如 Visible、Opacity、DrawOrder、局部 Transform。
2. `LayerFrame`：本帧行为计算出的 VisualOpacity、Transform、最终 Visible/Opacity、保守 Bounds。

每次求值从定义及 overrides 重建 frame，不从上一帧的最终值继续累加。建议公式：

```text
EffectiveOpacity = clamp(authoredOpacity × userOpacity × visualOpacity, 0, 1)
EffectiveVisible = userVisible && behaviorVisible && EffectiveOpacity >= renderThreshold
```

默认 userOpacity、visualOpacity 均为 1。定义中的初始透明度只乘一次。sample 原来的眼口渐隐结果写入 visualOpacity。

变换使用明确的二维仿射矩阵，按列向量约定 `FinalTransform = UserTransform × BehaviorTransform × AuthoredTransform`，默认单位矩阵；pivot 由绑定显式给出。sample 原有公式生成的顶点已处于模型空间，初期三种通用矩阵均设为单位矩阵，避免重复旋转。

图层的计算索引在加载时固定；排序只改变渲染顺序，不重排参数、网格或状态数组。同序图层按定义顺序稳定排序。Godot 支持的排序范围在绑定时检查，不能静默截断。

### 4.3 资源生命周期

| 对象 | 创建者/持有者 | 清理责任 |
| --- | --- | --- |
| 模型定义、静止网格、曲线 | 应用或 sample builder | 普通托管对象；不可变共享 |
| 外部 Texture2D、Shader 资源 | ModelView 的调用方 | renderer 只借用引用，不显式 Dispose 外部资源 |
| 模型实例、行为、弹簧 | ModelInstance | 卸载时 Dispose 行为实例 |
| 每实例 ArrayMesh、材质、遮罩节点 | addon renderer | renderer Dispose/退出树时释放 |
| sample 权重纹理、动态姿态纹理等 | sample GPU binding | binding Dispose；由 renderer 调用一次 |

第一版每实例持有 ArrayMesh，先保持正确隔离。静止几何共享优化后续再做，避免 CPU 路径写入共享网格。

## 5. 执行链与动画子系统调整

### 5.1 唯一帧调度入口

`ModelInstance.Advance(delta)` 成为模型的唯一时间推进入口：

```text
推进控制器状态
  → 从 BasePose 构建基础参数（可选控制器）
  → 主动作混合
  → 表情混合
  → 外部输入混合
  → 得到动画参数快照
  → simulation 按 delta 更新私有状态
  → simulation 从当前状态求出派生输出
  → 计算图层可见性/透明度/变换
  → CPU 变形或准备 GPU 变形输入
  → 发布完整 Frame
  → Godot renderer 提交 Frame
  → 派发本次播放通知
```

Core 可以无 renderer 运行并取得 Frame。Godot 节点需要在提交完成后派发应用回调，避免回调清理模型时 renderer 仍在使用旧实例。

参数权威来源只有一份。simulation 若需要修改变形用参数，应从动画快照复制到 `ResolvedPose` 后再写入，不能把修改写回 AnimationRuntime 的 BasePose 或已混合参数，防止逐帧反馈。

### 5.2 对当前 AnimationRuntime 的具体改动

保留曲线、模型绑定、PlaybackHandle、主动作/表情层及其测试。拆分当前 `Advance` 的责任：

- 内部 `AdvancePlayback(delta)`：推进时间和播放状态，收集待派发事件。
- `EvaluatePose(destination)`：在当前播放时间采样与混合，不推进时间，不调用模型输出，不派发事件。
- `DrainEvents()`：交给 ModelInstance/节点在安全点派发。
- 独立动画消费者仍可使用便利 `Advance`，内部组合上述步骤。

控制器区分 `AdvanceState(delta)` 与 `Apply(pose)`。现有 `IPoseModifier.Apply(pose, delta)` 可以在迁移期适配，但最终不能靠“调用一次 Apply(0)”假定任意外部代码不修改状态。

`IAnimationOutput` 不再承担模型、物理、变形、渲染的总出口。作为纯参数消费者的便利扩展可以保留，但 `ModelInstance` 不通过它运行完整模型。

### 5.3 暂停与刷新

- `Advance(delta)`：推进时间、simulation 和动作；负数及非有限 delta 拒绝。
- `Refresh()`：用当前时间、当前状态和当前输入重新求值图层/顶点；不推进控制器、弹簧和播放时间，不派发播放事件。
- `Playing=false`：节点不自动 Advance；必要时可以 Refresh 并提交修改后的姿态。
- 动作命令修改播放器选择，Refresh 可显示当前时间的结果；生命周期通知仍由下一次 Advance 派发。
- `Clear()`：卸载实例和 renderer，清除旧实例待派发事件；节点级输入订阅可按明确契约保留。

弹簧刷新时不能再次积分；可以根据冻结的弹簧位置和新的参数输入重新计算派生位移。这要求将当前 `PhysicsSolver.Step` 的积分与输出计算分开。

通用时钟不截断 delta。sample 原本的 `MaximumDeltaSeconds` 属于其参考控制器/演示策略，不得回灌通用播放时钟。旧 oracle 的限步测试在 sample reference adapter 中保留。默认 demo 和新 API 的 motion 应使用真实传入时间。

长帧物理由 sample 自行子步积分。先保留其积分算法；对超大 delta 的策略显式配置并验证，不顺带修改算法追求和新时钟完全相同的旧表现。

## 6. 行为与变形扩展契约

不设计一个要求调用者“交回整套 runtime”的接口。扩展只参与核心已经规定的阶段。

以下为责任示意，省略只读 view/参数校验等辅助类型：

```csharp
public interface IModelBehaviorFactory
{
    IModelBehavior Create(ModelBindingContext binding);
}

public interface IModelBehavior : IDisposable
{
    void AdvanceSimulation(ReadOnlyPose animatedPose, double deltaSeconds);
    void ResolvePose(ReadOnlyPose animatedPose, ParameterSet resolvedPose);
    void EvaluateLayers(ReadOnlyPose resolvedPose, LayerFrameWriter layers);
    void DeformCpu(int layerIndex, ReadOnlyPose resolvedPose,
        ReadOnlySpan<float> restXY, Span<float> outputXY);
}
```

约束：

- 工厂保存不可变定义；每个模型实例创建一个独立行为对象。
- `ModelBindingContext` 提供参数 ID/图层 ID 到索引的绑定、只读网格和 sample 预处理数据的注入入口。
- 必需参数/图层缺失在 Create 阶段报错；可选参数使用显式 `TryBind`。
- 行为对象可以组合独立的 simulation、图层 evaluator、CPU deformer；第一版不引入运行时依赖图排序。
- 核心固定阶段顺序，sample 内部可以保留 ApplyFeatures → Head → Body → Hair → RotateBody 的原有顺序。
- 核心拥有 outputXY，负责重置/分配/提交；变形器不能改 UV/索引或分配 Godot 节点。
- 变形器每次从 restXY 计算，不能依赖上一帧写入顶点，避免漂移和 CPU/GPU 模式切换差异。
- CPU 与 GPU 的所有依赖状态必须在 simulation 阶段形成。调用 CPU 变形不能偷偷推进弹簧或随机数。
- sample 的 typed 状态留在 `SampleBehavior`，不把 anchors、六根头发或任意字符串 object 字典加入 Core。

### 6.1 核心必须提供可用的默认行为

内置 `BasicModelBehavior`，可用代码绑定：

- 参数到图层平移 X/Y。
- 参数到绕指定 pivot 的旋转和缩放。
- 参数到图层透明度。

建议 `LayerParameterBinding` 包含参数 ID、图层 ID、目标属性、比例与偏移。绑定创建时校验；旋转明确使用弧度、位移使用模型像素。默认无绑定时，模型也能以静止几何显示。

这些是通用 2D 操作，不应全部要求 sample 重写。第一版不提供任意顶点关键形状插值系统。

## 7. CPU/GPU 后端：共享结果契约，具体公式可扩展

### 7.1 第一版的取舍

采用**每个模型实例选择一个几何后端**。支持：

- `Cpu`：调用行为的 DeformCpu，核心负责更新 ArrayMesh 顶点。
- `Gpu`：静止网格留在 GPU，具体程序绑定最终参数及行为状态；每帧不上传顶点。
- `Auto`：有匹配的 GPU 程序就使用 GPU，否则完整退回 CPU，并公开实际后端。

显式请求 Gpu 而模型没有匹配实现时，在加载阶段给出清楚错误；不能运行到一半悄悄跳过变形。

默认 BasicModelBehavior 的 CPU 顶点操作为复制 rest，图层变换由公共渲染阶段应用；默认 GPU 程序为同等的直通变形，因此不提供自定义 shader 的普通模型也能直接使用 addon 默认 GPU 渲染。

暂不支持一串任意 CPU/GPU 变形器交错执行。第一版 GPU 扩展为一个完整的模型变形程序；需要组合多个公式，由扩展自己的 CPU 实现与 shader 保证顺序一致。这样能保留当前 sample GPU 路径，又不会承诺尚不存在的 shader 编译系统。

### 7.2 GPU 扩展位置与接口

GPU 工厂位于 Godot 层，Core 无 ShaderMaterial 概念：

```csharp
public interface IGodotDeformationFactory
{
    IGodotDeformationBinding Create(ModelInstance instance, RenderBindingContext context);
}

public interface IGodotDeformationBinding : IDisposable
{
    void ConfigureLayer(int layerIndex, ShaderMaterial material, RenderPass pass);
    void UploadFrame(ModelFrame frame);
}
```

`RenderBindingContext` 提供 renderer 创建的材质及只读网格元数据。工厂在创建 binding 前还需提供声明式的程序描述（主绘制 Shader、mask Shader、支持的行为类型/版本和 bounds 策略），用于加载前验证和创建材质；上述示意接口省略了这一只读属性。GPU 扩展提供匹配的主绘制/遮罩 shader 资源；只创建并拥有自己需要的数据纹理等资源，不创建或接管 MeshInstance2D、遮罩视口或整个 renderer。

`SampleGpuFactory` 可以在 sample 内检查 `instance.Behavior` 是否为 `SampleBehavior`，然后读取该实例的弹簧等 typed 状态。类型匹配和访问完全封装在 sample，核心不做角色分支。绑定不匹配时加载失败。

当前 `GpuPoseBuffer` 和 `GpuDeformationBinding` 的具体打包继续复用：固定参数索引、16 像素 pose texture、权重纹理、六根头发上限都属于 sample GPU ABI，不是全体模型的 ABI。它们必须从同一帧的 ResolvedPose/行为状态更新。

### 7.3 通用 renderer 的职责

- 为每层创建 ArrayMesh、MeshInstance2D 和独立材质实例。
- 设置纹理、预乘透明度、最终显隐、顺序、通用图层变换和包围盒。
- 创建/更新遮罩资源和遮罩源绘制。
- CPU 模式提交当前有效的变形顶点；GPU 模式调用扩展上传输入。
- 把相同变形与图层变换用于颜色 pass 和 mask pass。
- 记录实际后端、顶点上传字节数和绑定诊断。
- 释放所有自己创建的 GPU/场景资源，并调用扩展的 Dispose。

提交阶段不能调用角色规则、积分或动画采样。所有绘制数据来自已经发布的 Frame。

CPU 检查通过 `EvaluateCpuGeometry()` 显式取得当前帧的参考顶点；GPU 模式下不把静止或陈旧缓冲伪装成变形结果。该操作不推进时间，结果带帧版本号。

必须区分三个空间/缓冲：`rest` → 自定义变形结果 `deformed` → 通用图层矩阵后的 `final model position`。CPU 提交的缓冲为 deformed，公共 shader 再应用图层矩阵；GPU shader 完成自定义变形后应用同一矩阵。诊断方法另行计算最终模型坐标供比较，不能把这个诊断结果再提交并重复应用矩阵。

### 7.4 Shader 契约与包围盒

- addon 提供无角色语义的默认纹理 shader、mask shader 以及可复用公共 include。
- 公共材质参数保留命名空间，例如 `runtime_layer_alpha`、`runtime_layer_transform`、`runtime_mask_texture`。
- sample shader 保留 `sample_*` 或其现有专属输入；GPU binding 不可覆盖 runtime 保留字段。
- shader 入口完成自定义变形后，再执行相同的通用图层变换，生成供裁切使用的模型坐标。
- sample 可以使用预先编写好的完整 shader，引入公共 include；不在运行时拼接源码或自动生成 shader。
- 静态默认模型可由顶点计算 bounds；GPU 扩展必须声明足够大的保守 bounds。sample 初期沿用现有扩大范围，之后另做优化。

## 8. 遮罩通用化：必须保持现有视觉语义

sample builder 将“左虹膜由左眼白裁切”转换为显式的 `MaskDefinition` 和目标图层 MaskId。renderer 只看到图层引用。

第一版范围：

- 每个目标图层最多一个 mask；一个 mask 可包含多个 source，使用覆盖并集。
- 多个目标可以共享同一个 mask。
- source 不允许再使用其他 mask，禁止自身引用和循环。
- 按 mask 创建模型画布大小的 SubViewport；零 mask 的模型不创建遮罩视口。
- source 隐藏或不满足有效绘制阈值时，不参与 mask；没有有效 source 的 mask 保持空，目标被裁掉。
- 遮罩资源按引用关系构建，不能固定分配两个，也不能以 Left/Right 命名确定行为。

**不能在这次拆分中顺带改变 alpha 规则。** 当前 sample mask shader 对原始纹理 alpha 以 0.25 作阈值，目标 shader 以 0.5 判断遮罩；源层整体透明度只通过有效显隐门控，没有连续乘入 mask 覆盖。

因此第一版提供显式的 binary coverage 模式和 source/receiver 阈值配置；sample 填入上述值。不要默认改成软遮罩或连续 alpha 相乘。主绘制仍沿用预乘 alpha 管线。后续若增加 soft mask，作为独立可选择模式和单独验收。

## 9. Godot 节点、ModelView 与加载事务

### 9.1 对外入口

建议以 `AnimeModelNode` 作为模型节点，`AnimeAnimationNode` 只保留给纯参数播放器或在迁移结束时移除。

节点接收代码构建的 `ModelView`：

```text
ModelView
  Definition: ModelDefinition
  Textures: 只读 Texture2D 槽位集合
  Deformation: 可选 IGodotDeformationFactory
```

典型用法（目标 API 示意）：

```csharp
var view = SimpleBanner.Build(texture); // 定义网格、图层、参数、绑定和动作
var actor = new AnimeModelNode();
AddChild(actor);
actor.Load(view, GeometryBackend.Auto);
actor.Instance.Animation.PlayMotion("flutter");
actor.Instance.Animation.SetExpression("bright");
```

`SimpleBanner.Build` 不创建 ArrayMesh、ShaderMaterial、Polygon2D 或自己的 renderer。它只构造定义，使用 BasicModelBehavior 或提供一个具体变形函数。

现有 `AnimeRigModel` 资源保留在 sample，读取旧格式后交给 `SampleModelBuilder`。demo 可暂时保留同名薄节点封装，但必须委托通用节点；不能继续拥有另一套 `_Process → simulation → renderer` 循环。

### 9.2 加载与清理顺序

1. 校验定义、参数绑定、纹理、后端能力和 mask 引用。
2. 创建候选 ModelInstance/行为。
3. 创建候选 renderer、材质、mask 和 GPU binding；在不可见的候选宿主下完成必要初始化。
4. 求值初始静止帧，不自动启动动作；完成初始提交。
5. 全部成功后将候选实例挂到正式显示位置，再清理旧实例。
6. 任一步失败，释放候选拥有的全部资源，旧实例保持可用。

节点拥有的输入订阅跟随节点；绑定到旧实例的 modifier 必须卸载，随后重新绑定到新实例。实例级句柄/事件不跨模型重载复用。回调期间请求 reload/clear，在当前提交与事件派发的安全边界执行。

## 10. Sample 的最终形态

`SampleModelBuilder` 在加载时做一次转换和绑定：

- 解析现有 RigDefinition/Profile，构建通用 MeshDefinition/LayerDefinition。
- 烘焙头发/刘海权重，保存在 sample 的不可变预处理数据中。
- 把 Role/Side 翻译成具体图层索引、变形分组与 mask 引用。
- 把 sample 参数名绑定到通用参数索引。
- 返回通用定义、SampleBehaviorFactory 及 SampleGpuFactory 所需的组合结果。

`SampleBehavior` 保存每实例的弹簧、角色配置和变形逻辑，不保存 Godot 节点。

原 `RigSimulation` 最终不再充当生产路径的调度器。原测试依赖的 `SetPreset`、`AutomaticMotion`、限步和旧锁定行为，可以保留在明确命名的 `SampleReferenceAdapter` 中用于 oracle；正常 demo 的表情使用新表达层，不调用旧 preset 锁。

sample 自动控制器由 demo 明确安装。默认通用模型加载后静止；sample demo 可以继续显式启用待机、眨眼、随机动作等，以保留演示效果。

核心不自动推断参数的解剖意义。model-defined `ClosedEyeVariant` 这样的离散量在 sample 中取整解释；本次不把它升级成核心“眼睛模式”。

## 11. 可分批执行的实施顺序

每一步独立保持构建通过；过渡 wrapper 有明确删除节点。避免一次把所有代码移动后才验证。

### M0：冻结基线与明确新验收

工作：

- 保存当前构建、数值参考、图像报告及 sample 自动行为的基线。
- 建立不含 SampleRig 的新 consumer 骨架，要求最终使用通用模型节点/renderer。
- 记录当前 shader alpha/mask、顶点坐标和 Float32 转换顺序。

产出：基线说明、新 consumer 场景设计。  
通过条件：现有测试能复现；新验收明确失败在缺少通用模型/renderer，而不是用自建 Polygon2D 绕过。

### M1：模型、网格、图层数据

工作：

- 新建 ModelDefinition、MeshDefinition、LayerDefinition、MaskDefinition、LayerOverrides/Frame。
- 抽取纯矩形网格生成；将 sample 的网格烘焙拆成通用网格 + sample 权重。
- SampleModelBuilder 将旧格式编译为通用定义，旧 renderer 暂经适配读取。

通过条件：旧 sample 静止顶点、UV、索引逐项一致；定义不能被外部数组修改；图层 ID/索引/纹理槽校验有测试；多实例 overrides 隔离。

### M2：ModelInstance 与行为执行链

工作：

- 引入行为工厂及实例，拆分 AnimationRuntime 的时间推进、采样、事件派发。
- 接通 BasePose → 动画 → 外部输入 → simulation → 图层 → CPU 变形。
- 实现 Advance、Refresh、显式 CPU 参考求值和帧版本。
- 把 SamplePhysics、LayerVisibility、RigDeformer 接入阶段，暂保留旧 Godot 外壳。

通过条件：CPU sample 数值参考保持；Refresh 不积分、不吞事件；变形不累计；主动作与表情独立；最终参数经过真实 sample deformer；帧只在完整求值后发布。

错误策略：扩展抛异常时，不发布部分完成的 Frame；实例标记为 faulted，下一次推进要求显式重建/重置。无需承诺任意扩展私有状态可事务回滚。

### M3：通用 CPU renderer 和默认行为

工作：

- 新建通用 ModelView/AnimeModelNode、图层 renderer、默认材质和 BasicModelBehavior。
- 接通 CPU 网格提交、显隐、稳定顺序、变换和生命周期。
- 将 sample 的绘制接到通用 renderer，mask 在 M4 前可以使用临时适配，不能当作最终完成。
- 新 consumer 用内存生成纹理及通用网格运行，不加载 sample 文件。

通过条件：新 consumer 不定义 renderer、不创建自己的绘制节点；可以显示静止模型、播放动作、改变透明度和图层顺序；两实例独立；clear/reload 释放资源。

### M4：通用遮罩

工作：

- 显式 MaskDefinition 驱动视口及 source draws。
- sample builder 生成原左右眼关系；统一 source/target 的变形后坐标。
- 固化 binary mask、source 隐藏和空 mask 的语义。

通过条件：sample 原 iris-mask 测试通过；新 consumer 使用无眼部命名的三组 mask；隐藏 source、多个 source 并集、空 mask、非单位图层变换均正确；自引用/嵌套 mask 加载时拒绝。

### M5：通用 GPU 执行与 sample 绑定

工作：

- 增加 GPU factory/binding 生命周期和后端能力选择。
- sample pose/weight 打包及变形 shader 保持在 sample；主颜色/mask 公共渲染规则归 addon。
- 接入保守 bounds、GPU 输入更新、CPU 显式检查。
- 独立 consumer 增加一个简单 GPU 变形扩展，与其 CPU 实现做对比。

通过条件：既有 CPU/GPU 对比保持阈值；sample GPU 模式逐帧顶点上传为零；新模型 GPU 不依赖 sample ABI；Auto 正确回退；显式 Gpu 不支持时加载失败并保留旧实例。

### M6：收口生产路径、demo 与测试

工作：

- demo 改用通用节点；旧 RigSimulation/RigRenderer 生产调度路径删除。
- 旧参考调用集中到 SampleReferenceAdapter，正常 sample 与参考测试都复用真实新行为实现。
- 补齐输入订阅重载、失败加载资源释放、播放回调 clear/reload 测试。
- 更新文档和 converter 的 sample 输出约定；不新增动作 IO。

通过条件：第 12 节全部验收通过。仓库中只有一套生产模型调度和通用渲染生命周期，不能用“旧 renderer 还在实际绘制、新类只是包装”完成迁移。

## 12. 验收矩阵

| 场景 | 必须验证的结果 |
| --- | --- |
| 独立新模型，仅 addon | 自定义参数、图层、纹理、动作能实际显示；不实现自己的 renderer |
| 无行为/无动作模型 | 静止网格正确显示，加载不产生隐式运动 |
| BasicModelBehavior | 参数驱动平移、pivot 旋转、缩放、透明度；不会逐帧累积 |
| 自定义 CPU 变形 | 外部只实现公式；addon 分配顶点、调度和提交 |
| 自定义 GPU 变形 | 外部提供 shader/binding；addon 仍管理网格、mask、资源生命周期 |
| 主动作/表情/输入 | 单主动作和单表情各自替换；注视/口型按显式顺序合成 |
| 通用 mask | 多组、自定义名称、source 隐藏/并集/空源、变形坐标一致 |
| CPU 检查 | GPU 模式取当前帧 CPU 参考不会推进弹簧或动画；明确版本 |
| 暂停/Refresh | 输入及图层修改可以刷新，时间与物理状态不推进 |
| 多实例 | 定义可共享；pose、网格写入、弹簧、材质、动态纹理隔离 |
| 加载失败 | 原模型仍显示；候选资源完整清理；不遗留节点级订阅 |
| 回调中替换模型 | 不使用已释放 renderer/实例；不向新实例派发旧事件 |
| 旧 sample 数值 | 当前 167 场景约 114 万次比较维持；不能只留一套旧引擎跑 oracle |
| 旧 sample 图像 | 当前 218 次 CPU/GPU 比较、14 个渲染捕获及遮罩/清理检查维持 |
| 架构边界 | Core 无 Godot/IO/角色语义；Godot renderer 无 Sample/Role/眼部判断 |
| 核心性能 | 模型绑定与静态烘焙不进入逐帧循环；GPU 不意外执行 CPU 顶点或上传顶点 |

现有测试数只是当前基线，不能为了保持数量不变而拒绝必要的新用例。新增 consumer 的真实像素结果也要检查，不能只检查某个节点属性被赋值。

架构测试同时采用独立项目编译和源码/依赖约束。仅靠“没有 HeadYaw 这个单词”不足以证明核心完整，应把独立模型渲染场景作为主要证据。

## 13. 主要风险与具体控制

1. **抽象再次变成一个总出口。** IModelBehavior 只提供阶段算法；实例、图层、网格、mask、提交和清理留在 addon，并以 consumer 验收限制责任。
2. **CPU/GPU 看似通用，实际仍固定 sample 参数。** 不把 GpuPoseBuffer 搬回核心；用第二个独立 GPU 扩展验证接口不依赖其 ABI。
3. **sample 的数学顺序发生变化。** 保留原公式顺序和 Float32 边界；每个迁移阶段运行数值与 GPU 对比。
4. **遮罩透明度被无意修正。** 显式配置现有 binary 阈值；公共 shader 拆分时单独验证 source/receiver pass。
5. **同一状态被推进两次。** ModelInstance 是唯一调度器；renderer 只提交；CPU 检查和 Refresh 不推进 simulation。
6. **不可变定义名义共享，实际持有可写数组。** 构造复制、只读访问；运行缓冲只由实例持有。
7. **兼容壳长期变成第二套运行时。** M6 必须删除旧生产调度；reference adapter 只适配输入/断言，不能独立保留整套旧求值引擎。
8. **扩展数量和接口过度增长。** 先固定阶段和一个模型级变形程序；暂不实现通用依赖图、混合 CPU/GPU 图或自动 shader 组合。

## 14. 最终交付清单

- [ ] addon 拥有通用模型定义/实例、网格/图层/遮罩、行为调度和 Godot renderer。
- [ ] 保留现有代码曲线与动作/表情能力，作为模型子系统。
- [ ] 默认基础行为足以显示和驱动简单模型。
- [ ] sample 只提供导入、角色配置/控制器、CPU 公式及 GPU 适配，不拥有另一套 renderer。
- [ ] 新独立模型完成 CPU 与 GPU 接入，不借用 sample 代码。
- [ ] 当前 sample 的数值、GPU、输入和生命周期基线保持。
- [ ] 失败加载、暂停刷新、回调重载、资源共享/隔离均有验证。
- [ ] 没有新增动作文件格式与 IO；全部扩展由代码显式注册。

**下一步从 M0/M1 开始，先让通用数据和实例具有真实职责，再接 renderer；不再以目录搬迁或接口数量作为改造完成标志。**
