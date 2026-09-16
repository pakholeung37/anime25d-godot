# Runtime 能力组合重构方案

状态：已实施，实际接口、验证及第一版边界见 [实施报告](RUNTIME_COMPOSITION_IMPLEMENTATION.md)。2026-09-16。

本文修正上一轮以 `IModelBehavior` 为主要扩展入口的设计。本文保留设计过程中的命名和伪代码；当前可调用 API 以 addon README 和本次实施报告为准。

## 1. 目标与判断依据

目标不是让 demo 文件更少，而是让新模型可以复用执行机制和基本动画能力。模型作者应该提供数据、绑定和特殊算法，而不需要编写自己的参数系统、物理调度器或 GPU 数据传输系统。

本轮代码审查发现：

- `SampleBehavior` 同时组织自动运动、平滑、物理、图层、变形；实际上仍是第二个 runtime。
- `SampleBehavior` 维护 Target、Current、Frame、targetFrame、controllerFrame、deformationFrame，与 runtime 的 BasePose/Pose 重复，并在每帧按字符串转换。
- `PartState` 同时保存定义、几何、弹簧、用户覆盖和求值结果，混合了不同生命周期的数据。
- `SampleGpuFactory` 依靠 `behavior is SampleBehavior` 选择后端，GPU 上传又读取 behavior 内的弹簧状态；发布的 ModelFrame 并不足以表达渲染输入。
- `RigDeformer` 并非任意一组独立函数：它有明确的特征→头部→身体→头发→最终旋转顺序，并在最终旋转前有 Float32 舍入边界。草率拆分会改变结果。
- `LayerVisibility` 的大部分能力实际是参数映射、互补透明度、条件选择，但目前只能通过角色专用代码获得。

结论：保留动画播放器、网格/遮罩、模型实例和通用 renderer，替换 behavior 的组织职责，同时提供实际可用的内建组件。

## 2. 从本地 Cubism 学到什么

检查的源码：

- `Framework/src/Motion/CubismUpdateScheduler.cpp`：独立 updater 注册、排序、调用和释放。
- `Framework/src/Motion/ICubismUpdater.hpp`：眼睛、表情、注视、呼吸、物理、口型、pose 有各自更新顺序。
- `Framework/src/Model/CubismModel.cpp`：最终模型更新调用 `Core::csmUpdateModel`。

可以借鉴独立能力、共享模型参数、确定更新顺序。但可见 Framework 的 updater 并不是全部变形求值器：不能把它的接口数量当作我们应实现能力的上限，也不能声称已从这套源码推导出 Cubism Core 内部的 deformer 实现。

本项目不需要照搬 Cubism 的固定优先级或文件格式。现有可观察语义优先；动画定义继续使用 C#，不增加 JSON 或文件读取代码。

## 3. 命名与模块边界

将 `addons/anime25d/Core` 和命名空间 `Anime25D.Core` 改为 `Runtime` / `Anime25D.Runtime`。整个 addon 对外仍叫 Anime25D。

建议内部目录：

```text
Runtime/
  Animation/       Motion、expression、曲线、播放状态
  Parameters/      参数布局、基础输入、帧参数、绑定
  Model/           定义、编译执行计划、实例、帧、生命周期
  Drivers/         周期信号、平滑、包络、随机时序
  Physics/         弹簧与输出绑定
  Deformation/     变形序列、内建变形、CPU 契约
  Geometry/        网格和不可变顶点属性
Godot/
  Rendering/      贴图、mesh、mask、材质、提交
  Deformation/    GPU 实现、程序解析、通用数据上传
demo/
  SampleModel/    模型定义、预处理、特殊算法、shader
  Input/          鼠标、按钮等交互
tests/
  Compatibility/ 原始实现的参考适配
```

移动目录不等于完成重构。重命名安排为独立步骤，避免与算法迁移混在一个难以核对的 diff 中。

## 4. 定义、实例、帧：明确三种数据

### 4.1 定义

不可变、可共享：参数/动画、组件配置、作用图层、静态权重、曲线、弹簧配置。构建器是可变的；Build 后复制并冻结。模型加载时编译 ID 到索引、解析目标、校验引用，帧内不再查角色名称。

### 4.2 实例

独立拥有：播放句柄、驱动器计时/RNG、平滑值、弹簧位置/速度、用户输入及覆盖。各组件可拥有私有状态，但必须通过 runtime 创建和释放，禁止自行推进第二套总时钟。

### 4.3 帧

发布完整、只读的渲染输入：最终参数、派生数值通道、图层属性，以及 CPU 后端需要的顶点。

派生通道用于弹簧位移、计算后的形变系数等。它们不是默认可播放的动画参数：有明确类型/长度、唯一生产者和消费者。第一版支持标量及固定长度浮点数组，不提供任意 object 容器。

GPU 上传和 CPU 变形只消费同一版帧数据及不可变定义，不能访问驱动器或物理求解器的可变对象。帧视图沿用当前“下一次求值后失效”的约定，需要长期保留时显式复制。

## 5. 执行模型：固定阶段、阶段内显式顺序

不先开发通用节点图、自动拓扑调度或 ECS。模型作者选择阶段，并按列表明确顺序；Build 负责报错，不暗中重新排序。

```text
基础参数输入
  → 基础驱动器（自动运动、需要平滑的基础输入）
  → motion
  → expression
  → 最终输入驱动器（注视、口型及外部覆盖）
  → 派生计算与物理
  → 图层绑定
  → 变形
  → 发布帧 → 渲染提交 → 播放通知
```

- motion/expression 的混合、过渡仍由现有 AnimationRuntime 管理，不重复实现。
- 原始输入和最终覆盖是不同入口，名称必须明确；不再暴露多个含义相近的 Preparing/Finalizing 回调作为主要 API。
- 每个写参数的驱动器声明目标参数和混合方式；同阶段多个写者按列表顺序合成，不禁止合法的叠加。
- 派生通道单写者，读取必须发生在写入之后。第一版不允许隐式反馈环；上一帧反馈只能是显式的组件私有状态。
- 图层透明度相乘、显示状态做 AND、draw order 按显式替换规则、矩阵按声明顺序组合。用户覆盖最后应用。
- 组件 ID 用于外部启停/设置输入，与角色语义无关。启停、重置、销毁是明确命令；启停不隐式重置状态。
- 不将所有参数统一做末尾平滑。平滑只作用于绑定的信号，否则会修改 motion 曲线和 expression 淡化语义。

### 时间语义

驱动器与物理组件分离 `AdvanceState` 和 `EvaluateOutput`。Advance 推进状态一次；Refresh 只重算输出，不推进时间、不取新随机数、不积分、不发送播放通知。诊断 CPU snapshot 只消费已发布帧，不重新执行物理。

物理默认保留现有“按最大积分步长细分当前 delta”的求解语义，第一轮不同时改成固定步累积器。大 delta 设置明确的工作量上限并返回诊断/错误，不能默默丢弃时间。固定步与插值是后续独立功能。

失败维持当前策略：不发布半成品帧，实例进入 Faulted；不承诺任意自定义组件私有状态回滚。初始化失败释放已创建组件和 GPU 资源，不替换正在显示的实例。

## 6. 必须提供的内建能力

不是只有接口。第一轮交付以下实际实现，且 sample 必须使用它们：

| 能力 | 内建内容 | 模型提供 |
|---|---|---|
| 参数映射 | 线性、clamp、smoothstep、代码创建的分段曲线 | 输入/输出、区间、系数 |
| 周期信号 | 正弦周期、相位、幅度、偏置 | 绑定到哪些参数 |
| 平滑 | 按绑定保存状态的指数平滑 | 速率、初值、作用阶段 |
| 时序包络 | 延迟、上升、保持、下降、重复间隔 | 时长和随机间隔策略 |
| 弹簧 | 批量弹簧状态、细分积分、位置/相对目标位移输出 | 目标信号、刚度、阻尼、缩放 |
| 图层绑定 | 变换、透明度映射、可见性阈值、互补淡化、离散选择 | 图层 ID、参数、阈值 |
| 基础变形 | 仿射变换、预定义顶点位移混合 | 枢轴、权重、位移数组 |

每个内建算法要有角色无关的语义。不要为了减少 sample 行数，把依赖眼球形状、脸部锚点、头发分类的分支整体搬进 runtime。

自动眨眼可以由通用包络加 sample 策略实现。双眨概率、特殊闭眼图层选择、虹膜回弹是否成为可复用扩展，由实际复用决定；第一版允许它们留在小型 sample driver 中，但不能继续保留 MotionPipeline 总调度器。

“呼吸写入哪个参数”“弹簧目标如何由头部角度构成”属于模型绑定。头部空间投影、嘴部曲率、特殊头发权重烘焙属于 sample 特有算法。

## 7. Deformer 设计

### 注册与绑定

模型持有具名 deformer 定义，每个定义包含：实现、不可变配置、读取的参数/通道、作用图层、静态顶点属性。各图层保存有序 deformer 引用列表。注册表属于模型，不是全局静态 singleton。

自定义 deformer 的核心输入：

```text
restPositions      原始位置，只读，始终不变
inputPositions     前一操作的结果，只读
outputPositions    当前操作写入的缓冲区
frame              已发布或正在构建的同版只读数值输入
staticAttributes   已验证的权重等数据
```

第一版统一模型画布坐标；UV、拓扑不变。操作默认完整写出 output。runtime 管理交替缓冲区和图层遍历，自定义操作不得再循环推进其他组件。

这样可以准确区分“在原始位置上计算权重”和“继续变形前一步结果”，防止每个 deformer 都从 rest 开始覆盖上一步。

### 组合粒度与数值兼容

不要求每个数学步骤都是一个公开组件。现有 RigDeformer 的特征/头/身体/头发存在位置依赖和舍入约定，可以先作为一个无状态的 `SampleWarp` 复合操作迁移。它只读 frame 和静态属性，不持有弹簧、时钟、图层状态，也不拥有执行流程。

内建操作间第一版使用 float 顶点缓冲。SampleWarp 内保留现有 double 计算及 Float32 边界，以保持参考结果。后续拆分 SampleWarp 必须以误差验证为依据，不能为接口整齐而新增舍入边界。

区别在于：一个特殊几何算法允许较大；一个同时管理整个模型生命周期和状态的万能 behavior 不应继续存在。

## 8. GPU 是独立的执行后端问题

普通 Godot vertex shader 在一次绘制中执行，不能把任意 C# 函数列表直接搬进去。第一版不承诺任意 deformer 自动 GPU 化，也不为每个 deformer 增加一次全屏/网格绘制。

选择：CPU 支持任意合法序列；GPU 支持显式提供实现的完整序列。后端选择依据变形计划和数据布局，移除对 SampleBehavior 类型的检查。

- 内建仿射 + 顶点位移能力提供已验证 GPU 程序。
- SampleWarp 提供配对 CPU kernel 和 GPU shader，实现同一计划。
- Godot 层的 GPU program provider 声明支持的操作顺序、配置和静态属性布局；严格匹配，不能只看一个名称。
- runtime/Godot 层提供通用帧数值打包、静态属性纹理和材质绑定工具，sample 只提供 shader、布局描述及支持条件。
- GPU 只读取 ModelFrame 与定义，禁止强转某个行为对象读取私有状态。
- 某序列没有完整 GPU 实现：Auto 模式整个模型退回 CPU，显式 GPU 返回包含不支持操作的错误。本轮继续采用模型级后端，不混合每层 CPU/GPU。
- Color/Mask 共享同一变形入口和数据布局，只替换通用片元处理。

这是明确的第一版边界：增加一个任意自定义 CPU deformer 可能导致 CPU fallback。若未来需要任意组合的自动 GPU 编译，再单独设计有限操作集或 shader 编译器，不能隐含在此次重构中。

## 9. 期望模型作者看到的接口

以下是说明职责的伪代码，不是现有可调用 API：

```csharp
var model = new ModelBuilder(parameters, layers);
model.Animations.AddMotion("nod", nod);
model.Animations.AddExpression("happy", happy);

model.Drivers.Add("breath", SineDriver.Bind("Breath", period: 4));
model.Drivers.Add("blink", new SampleBlinkDriverDefinition(blinkConfig));
model.Physics.Add("hair", SpringBank.Define(hairTargets, springConfig));

model.LayerBindings.Add(OpacityMap.Bind("eye-open", "EyeOpen", eyeFade));
model.LayerBindings.Add(OpacityMap.Bind("eye-closed", "EyeOpen", eyeFade.Inverted()));

model.Deformers.Add("character-warp", new SampleWarpDefinition(anchors, weights));
model.BindDeformers(characterLayers, "character-warp");

actor.Load(new ModelView(model.Build(), textures, sampleGpuPrograms));
actor.Instance.Animation.PlayMotion("nod");
actor.Instance.Animation.SetExpression("happy");
actor.Instance.SetDriverEnabled("blink", false);
```

API 成功标准：模型作者不需要继承模型节点，不需要创建自己的 Pose/Frame，不需要实现总 Update，也不需要编写顶点上传或物理调度循环。

## 10. 当前 sample 如何收敛

| 当前代码 | 处理 |
|---|---|
| SampleBehavior | 迁移后删除，不能改名后继续保留同样职责 |
| Target/Current/Frame 与参数枚举转换 | 统一到 runtime 输入、平滑状态、参数句柄；角色参数常量可保留 |
| MotionPipeline | 删除总调度，分解为内建 driver 与小型 sample driver |
| Spring | 通用求解移入 Runtime/Physics |
| PhysicsSolver | 目标计算转模型绑定；通用推进由 runtime 完成 |
| PartState | 静态数据进入定义，弹簧进入组件状态，用户覆盖和输出进入 runtime |
| LayerVisibility | 常用规则改为图层绑定；特殊选择保留小型纯函数或 selector |
| RigDeformer | 成为无状态 SampleWarp，自定义公式保留 |
| GpuPoseBuffer | 通用传输移到 Godot 层，角色通道表成为布局定义 |
| GpuDeformationBinding | 通用资源管理移到 Godot 层，sample 保留布局和静态数据 |
| SampleReferenceAdapter | 移入 tests/Compatibility，不作为生产节点依赖 |
| AnimeRigNode | 场景迁移至 AnimeModelNode + 模型工厂；必要导入便利函数不成为第二个 runtime |

旧 rig 数据读取仍可留在 demo 的 importer；本轮不增加动作文件 IO。支持旧数据不意味着生产运行链必须保留旧模拟器 API。

## 11. 实施顺序与停止条件

### M0：基线与设计验证

记录当前 build、core/reference、CPU/GPU、consumer、render 结果。先做一个不含角色语义的最小纵向样例：周期参数→弹簧→派生通道→透明度绑定→两个 deformer 顺序组合，两个实例共用定义。必须覆盖 GPU 支持序列与不支持序列 fallback。

该样例用于验证 proposed API 确实消除了外部调度；若仍需要一个万能 callback，先修设计，不继续迁移整个 sample。

### M1：模型计划与统一状态

实现定义编译、参数句柄、组件阶段、帧通道、实例生命周期和 Refresh 契约。暂保留旧 behavior 适配用于逐步迁移，禁止新功能依赖它。

### M2：实际组件

实现上述内建驱动器、弹簧、图层绑定。将 sample 的基础输入、平滑、周期信号、物理和图层切换迁入同一计划；逐项比较基线，保留随机消费顺序或明确记录差异。

### M3：变形与 GPU

实现 CPU 序列和缓冲管理；将 SampleWarp 改成仅消费帧数据。实现 GPU 完整计划匹配及通用打包；删除 GPU 对 SampleBehavior/PartState 的可变状态依赖。

### M4：生产链清理与命名

demo 使用通用节点，参考适配只留测试。删除旧 behavior 和重复参数/调度系统。完成 Core→Runtime 的路径/命名空间迁移及所有文档、资源引用更新。

### M5：验收

- 不依赖 demo 的 consumer 用内建能力创建有动画、弹簧、图层切换和变形的模型。
- 第二个实例无共享时间、随机状态、弹簧状态和覆盖；定义与静态权重可共享。
- 两个非交换 deformer 的顺序测试，验证 input/rest 不混淆。
- 重复 Refresh 不积累参数、不推进随机/物理；snapshot 不改变帧或时钟。
- GPU 帧上传只从帧和定义取值；支持计划 CPU/GPU 图像一致，fallback 有可检查原因。
- sample 原始参考与 CPU/GPU/render 回归维持；若必须改变语义，单独列差异，不能放宽全部误差阈值掩盖问题。
- 生产 demo 不再包含总调度器、另一套 pose/frame、弹簧积分循环或 GPU 上传管理。
- 自定义组件异常、加载失败、回调内清理/重载仍正确释放资源并保留已发布画面。

## 12. 本轮明确不做

任意图形编辑器、JSON 动画格式、新文件 IO、自动 C#→shader、任意变形图 GPU 编译、嵌套 deformer 坐标层级、通用约束求解、软/嵌套遮罩、运行中任意修改模型拓扑。

这些不影响本次核心目标：让 runtime 真正提供可组合、可使用的能力，让 sample 负责模型定义与特殊算法。
