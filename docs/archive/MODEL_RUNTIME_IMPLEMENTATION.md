# 通用模型运行时实施报告

实施日期：2026-09-16。对应 [改造方案](MODEL_RUNTIME_REFACTOR_PLAN.md)。

## 结果

addon 已拥有从模型定义到实际显示的完整执行链。动画播放器保留为子系统；sample 提供角色规则和变形程序，不再拥有独立的 renderer 或生产帧调度器。

```text
代码定义的 ModelDefinition + 已加载纹理 + 可选 GPU 扩展
                       ↓
                 AnimeModelNode
                       ↓
                 ModelInstance
        基础参数 → 动作 → 表情 → 外部输入
                       ↓
       行为状态/派生参数 → 图层状态 → CPU/GPU 变形输入
                       ↓
             只读 ModelFrame（前后缓冲发布）
                       ↓
        addon ModelRenderer：网格、材质、遮罩、提交
```

独立 consumer 使用 addon 创建和管理所有图层绘制、网格、材质与遮罩。它只提供模型定义及一个简单的 CPU/GPU 横向变形扩展，不实现 renderer，也不创建 Polygon2D 绕过运行时。

## M0–M6 落地对应

| 阶段 | 实现与证据 |
| --- | --- |
| M0 基线 | `artifacts/model-runtime-baseline` 保存改造前数值与 GPU 报告；继续使用 pinned JavaScript oracle |
| M1 通用数据 | `Core/Geometry/MeshDefinition.cs`、`Core/Model/ModelDefinition.cs`；网格构造快照、只读 span、显式 layer/mask ID；sample 静态几何/权重只烘焙一次 |
| M2 模型实例 | `Core/Model/ModelInstance.cs`、`ModelBehavior.cs`；前后帧缓冲、只读 Frame、faulted 状态、Refresh 与 CPU snapshot；动画采样和事件派发已拆开 |
| M3 通用绘制 | `Godot/AnimeModelNode.cs`、`Godot/Rendering/ModelRenderer.cs`；BasicModelBehavior 和默认 shader 能直接显示简单模型 |
| M4 遮罩 | MaskDefinition 明确声明 source/target；任意命名、多组、并集、空源及隐藏源；默认 binary 阈值保持 sample 行为 |
| M5 GPU 扩展 | `IGodotDeformationFactory/Binding`；默认 GPU、显式 CPU/GPU 和 Auto 回退；sample GPU 打包留在 sample；通用颜色/mask pass 在 addon |
| M6 收口 | 删除 sample RigRenderer；AnimeRigNode 继承通用节点；SampleReferenceAdapter 只适配旧测试入口；独立 consumer 验证真实像素、失败加载与回调重载 |

## 主要文件

### Core

- [ModelDefinition](../addons/anime25d/Core/Model/ModelDefinition.cs)：画布、不可变图层、mask、行为工厂。
- [MeshDefinition / GridMeshBuilder](../addons/anime25d/Core/Geometry/MeshDefinition.cs)：通用静止网格和矩形网格工具。
- [ModelInstance](../addons/anime25d/Core/Model/ModelInstance.cs)：唯一模型求值入口、实例 overrides、只读帧和诊断快照。
- [ModelBehavior](../addons/anime25d/Core/Model/ModelBehavior.cs)：行为阶段契约与内置平移、旋转、缩放、透明度绑定。
- [AnimationRuntime](../addons/anime25d/Core/AnimationRuntime.cs)：保留主动作/表情、循环和切换；增加不推进时间的采样与安全事件派发。

### Godot

- [AnimeModelNode](../addons/anime25d/Godot/AnimeModelNode.cs)：候选实例加载、成功后替换、提交后回调、回调中的延迟 clear/load。
- [ModelView](../addons/anime25d/Godot/ModelView.cs)：已有纹理、模型定义和 GPU 扩展的组合。
- [ModelRenderer](../addons/anime25d/Godot/Rendering/ModelRenderer.cs)：统一管理 ArrayMesh、图层材质、遮罩视口、提交与资源清理。
- [公共 shader 契约](../addons/anime25d/Godot/Shaders/runtime.gdshaderinc)：模型坐标变换、透明度和遮罩参数；公共颜色/mask pass 由默认及 sample shader 复用。

### Sample

- [SampleModelBuilder](../demo/SampleRig/Core/SampleModelBuilder.cs)：把旧角色格式编译成通用数据，明确眼白/虹膜遮罩关系。
- [SampleBehavior](../demo/SampleRig/Core/SampleBehavior.cs)：角色自动控制、弹簧、眼口显隐和 CPU 变形。
- [SampleGpuFactory](../demo/SampleRig/Rendering/SampleGpuFactory.cs)：固定 sample shader ABI 与参数纹理绑定，不管理绘制节点。
- [SampleReferenceAdapter](../demo/SampleRig/Core/SampleReferenceAdapter.cs)：沿用原测试参数/preset/限步约定，实际调用同一 ModelInstance；没有旧求值引擎副本。

## 几个有意明确的实现选择

1. **Core 与 Godot 分层。** Core 无 Godot 和 IO；Godot 层可以使用引擎 shader 资源。没有新增动作/模型文件解析器；现有 sample 导入继续留在 sample。
2. **只读发布帧。** 参数和图层视图只读，CPU 顶点以只读 span 提供。求值在 back buffer 完成后才发布；错误保留上一个完整 front，实例进入 faulted 状态，要求重建。
3. **刷新不推进状态。** 行为收到明确的 `advance` 标志；状态型外部输入可使用独立 `IPoseController.AdvanceState/Apply`。旧 IPoseModifier 作为无状态输入便利接口保留，refresh 的 delta 为 0。
4. **变形空间明确。** CPU 上传自定义变形后的模型坐标，通用 shader 再应用图层矩阵；GPU 采用相同步骤。CPU snapshot 额外应用矩阵得到最终坐标，不能再次上传。
5. **不可见层的 CPU 缓冲可以保留旧值。** 这延续 sample 原有行为并避免无效计算；诊断方法会对全部图层重新求值。GPU 路径不隐式运行 CPU 顶点求值。
6. **生产时钟与参考测试区分。** AnimeModelNode 使用实际 delta。SampleReferenceAdapter 的 Step 保留旧 delta 上限，仅用于旧数值输入约定；两者共用角色行为与变形实现。
7. **GPU 扩展提供完整变形程序。** 支持一个模型级程序；没有任意 CPU/GPU 算子图或自动生成 shader。通用 renderer 始终负责绘制和资源管理。
8. **已有纯动画使用方式保留。** AnimeAnimationNode/IAnimationOutput 可以服务纯参数消费者，但模型运行不依赖这个总出口。

## 验证结果

平台：macOS / Apple M4，Godot 4.7.2 Mono，.NET 10，Compatibility/OpenGL。

| 检查 | 结果 |
| --- | --- |
| 构建 | 0 warning，0 error |
| 原数值参考 | 167 场景，1,143,442 次比较；最大顶点误差 0，最大参数误差 2.22e-16 |
| 架构 | 190 项断言；包括 Core 无引擎/IO、addon 无 sample 引用、sample 无平行 renderer |
| 动画子系统 | 47 项断言 |
| 新模型 Core | 25 项断言；包括定义快照、Refresh、状态型输入、只读/完整帧、实例隔离与 fault |
| sample CPU/GPU | 218 次图像比较；最大平均通道差约 0.001182 / 255 |
| sample 渲染/资源 | 14 个捕获；iris-mask、实例隔离、重复加载/清理通过 |
| 独立 consumer | 18 项真实像素与生命周期断言；不包含任何 sample 文件 |
| shader 标识与文本检查 | `shader-bindings --check`、`git diff --check` 通过 |

独立 consumer 的具体覆盖：

- 内存纹理、通用网格和默认 GPU 图层变换能显示模型。
- 自定义 CPU 和 GPU 变形输出像素完全一致，并与等价 Basic 图层平移一致。
- 三组不含角色语义的遮罩、多 source 并集、源隐藏与空 mask。
- GPU 逐帧顶点上传为零，CPU 路径有实际顶点提交。
- Refresh/CPU snapshot 不推进时间，两个实例显示状态隔离。
- 不支持的 GPU 请求保留旧实例；扩展初始化中途失败调用 Dispose，旧模型仍有效。
- Auto 回退 CPU；完成回调中 clear/reload 安全，节点输入订阅在重载后继续生效。
- 卸载后画面为空，多次重新加载正常。

## 使用与复现

使用示例见 [addon README](../addons/anime25d/README.md)。

```sh
dotnet build --nologo
dotnet run --project tests/CoreTests.csproj -- .
node tools/shader-bindings.cjs --check
node tools/verify-consumer.cjs
godot --path . -- --backend-tests
godot --path . -- --render-tests
godot --path . -- --capture-demo
```

数值测试需要 `artifacts/reference.json`，缺失时先运行 `node tools/reference.cjs`。
consumer 工具接受 `GODOT_BIN`，使用真实图形后端进行像素验证，运行阶段不再使用 headless dummy renderer。

当前报告不覆盖其他平台、发布导出、嵌套/软遮罩、多后端混合变形图或长期资源压力测试。它们不属于本次方案的交付范围。
