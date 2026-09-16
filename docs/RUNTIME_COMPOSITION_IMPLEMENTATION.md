# Runtime 组件化实施报告

2026-09-16。本次替换此前以一个 `IModelBehavior` 承接全部角色逻辑的扩展方式。

## 已实现

- `addons/anime25d/Core` → `Runtime`；命名空间改为 `Anime25D.Runtime`。
- 移除 runtime 的 `IModelBehavior`、`BasicModelBehavior`、`CreateBehavior` 和实例 `Behavior`。
- `ModelPlan` 声明按阶段排序的组件、固定长度派生通道、有序 deformer。重复注册、阶段倒序、缺少/重复通道生产者、读取未生产通道、无效作用图层均被拒绝。
- runtime 创建每个实例的组件状态、随机源、帧通道和变形缓冲；控制启停、输入、重置、求值、刷新、发布及释放。
- 提供正弦、平滑、时序包络、参数映射、弹簧、图层变换/透明度/选择、仿射和顶点位移组件。
- `AdvanceState` 与 `EvaluateOutput` 分离。Refresh 不积分、不取新随机数、不发送播放通知；CPU snapshot 消费已发布帧。
- GPU 支持依据模型的完整变形序列判断，不再检查 behavior 类型。内建 affine/vertex-offset 组合提供实际 GPU 实现。
- `FrameTextureBinding` 负责通用数值布局、参数索引解析、帧打包和上传；sample 仅声明布局和特殊 shader。
- `BackendFallbackReason` 解释 Auto 回退，显式 GPU 不支持时拒绝加载并保留现有模型。

## 生产 sample

正常 demo 使用 `AnimeModelNode`，不继承角色专用模型节点。

`SampleModelBuilder` 负责组装：

1. Idle 的正弦信号、随机/口型/眨眼策略、通用平滑、呼吸。
2. 角色特定弹簧目标表达式 → 通用 `SpringBank` → 发布的位移通道。
3. 特殊闭眼图层选择策略 → 通用透明度绑定。
4. 无状态 `SampleWarp` → 通用 CPU/GPU 渲染。

不再维护生产用 Target/Current/Frame 副本、PartState、MotionPipeline 或 PhysicsSolver。
角色参数、锚点、特殊头部/嘴部/头发公式及权重烘焙仍归 sample。

`SampleWarp` 保留原算法内部 double 运算及 Float32 舍入位置，没有为拆接口而拆开相互依赖的数学步骤。
CPU 和 GPU 从同一帧读取参数及弹簧输出；GPU 不再读取物理求解器或驱动器对象。

旧节点和旧算法移到 `tests/Compatibility`，由测试专用组件桥接，供数值/图像基线验证。
旧算法不会从 addon 被引用。Godot 回归入口移到 `tests/Godot`；兼容代码只在 Debug 构建中编译，Release / ExportDebug / ExportRelease 排除；演示测试入口使用专用 ANIME25D_TESTS 编译标记。

## 关键 API

```csharp
var plan = new ModelPlan(
    components: new[] {
        new ComponentDefinition("breath", ModelStage.BasePose,
            model => new SineDriver(model, new SineBinding("Breath", .5, 2, Offset: .5, Blend: BlendMode.Override)))
    },
    deformers: new[] {
        new AffineDeformer("shift", new[] { "panel" }, System.Numerics.Matrix3x2.CreateTranslation(5, 0))
    });
var definition = new ModelDefinition(animation, width, height, layers, plan: plan);
actor.Load(new ModelView(definition, textures));
actor.Instance!.SetComponentEnabled("breath", false);
actor.Instance.SetParameter("Breath", .4, immediate: true);
actor.Instance.Animation.PlayMotion("nod");
```

阶段为 BasePose → motion/expression → FinalPose → 外部 FinalizingPose → Derived → Layers → deformation。
详细契约、资源所有权和可运行示例见 `addons/anime25d/README.md`。

## 验证

- Debug、Release、ExportDebug、ExportRelease 构建均通过，0 warnings / errors；导出构建不编译旧兼容实现。
- 原始 JS 参考：167 cases、1,143,442 comparisons，位置最大误差 0。
- 新 sample 组件链与旧算法直接对照：186,232 次顶点比较，最大误差 0；包含自动运动、参数、物理和图层 alpha。
- 新生产链真实渲染：166 组 CPU/GPU 比较；平均通道误差最坏约 0.000447 / 255，误差超过 8 的通道占比为 0。
- 独立 consumer：22 项断言通过，只复制 addon，验证遮罩、原子加载、失败清理、回调重载、CPU fallback，以及正弦→弹簧→透明度和多 deformer 的实际 CPU/GPU 图像一致性。
- 193 项 architecture、47 项 animation、25 项 model、11 项 composition 检查通过；覆盖顺序、实例隔离、Refresh、初始化失败清理和非法输出。
- 原有 GPU 基线：218 组 CPU/GPU 比较、14 张渲染捕获通过；测试改用显式绘制，避免后台窗口等待自动绘制信号。

报告文件：`artifacts/composition-render.json`、`artifacts/consumer-test.json`、`artifacts/core-tests.json`。
原始基线通过不代替新生产链测试；新旧两类验证分别保留。

## 有意保留的边界

- 第一版不是通用依赖图：按固定阶段和显式列表执行，通道读写元数据用于校验；自定义组件仍须遵守不保留缓冲、不偷偷推进状态等契约。
- 自定义组件工厂负责返回独立状态；捕获共享可变对象会破坏隔离，runtime 不尝试深拷贝任意用户对象。
- 通用内建 GPU 程序每层最多 16 个 affine/vertex-offset 操作，最多 16,384 顶点；特殊完整序列可提供专用 GPU factory，其他情况模型整体回退 CPU。
- 不自动编译任意 C# 变形到 shader，不支持逐层混合 CPU/GPU、改变网格拓扑或嵌套变形坐标系。
- 弹簧沿用最大步长细分的数值语义，不同时引入固定步累积器；超出单次工作量上限明确失败，不默默丢时间。
- 组件异常保留前一发布帧并 fault 实例，不承诺回滚私有状态。
- `ResetInputs` 不重置时钟、播放或启停状态。当前随机/眨眼/口型策略保留在 sample；runtime 已提供信号、平滑、包络等可复用基础能力。
- 没有加入 motion JSON 或新的文件读取链。
