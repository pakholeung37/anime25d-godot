# Anime25D · Godot Runtime

Anime2.5DRig 的 Godot / C# runtime 复刻。核心 addon 为分层角色提供网格变形、伪 3D 转头、眼口差分、自动动作和头发物理。

首版目标是复现原项目的参数、公式、默认动作与视觉效果。没有 VN 专用行为，也不包含摄像头、麦克风、OBS 或 Web 支持。

## 运行

本机已验证：**Godot 4.7.2 Mono、.NET SDK 10.0.400、macOS / Apple M4、Compatibility renderer**。

```sh
cd /Users/pakholeung/web/anime25d-godot
dotnet build
/Applications/Godot_mono.app/Contents/MacOS/Godot --editor --path .
```

在 Godot 中按 F6/F5 运行 `demo/Demo.tscn`，或直接运行：

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

演示界面包含两个 sample、全部 35 个参数、原版 7 个表情、6 个自动动作开关、暂停、图层显示/不透明度/深度/顺序，以及两个独立实例展示。双击滑块恢复该参数默认值。参数面板显示目标值；实时动画平滑后的值位于 `Simulation.Frame`。

默认 Idle / Blink / Random / Talk / Physics 全开，Mouse 关闭，与原版普通浏览器模式一致。呼吸仍按照原版公式持续运行；关闭 Idle 不会单独停止呼吸，Pause 才冻结整个模拟。

## 项目结构

```text
addons/anime25d/       可单独复制的 C# addon
  Core/               不依赖 Godot 的资源数据、参数、网格、模拟和变形
  AnimeRigModel.cs     包含绑定信息与纹理引用的 Godot Resource
  AnimeRigNode.cs      角色节点、GPU 网格和眼部遮罩
  Shaders/            预乘 alpha 合成与眼部裁剪
demo/                 演示场景、GPU 验收入口、转换好的两个 sample
tools/                独立 Node.js 转换工具与原版对照工具
tests/                数值对照与独立消费者工程
docs/                 范围、架构、验收记录
```

## 单独使用 addon

将 `addons/anime25d` 复制到一个 **Godot C#** 工程的同名目录，编译即可使用 `AnimeRigNode` 和 `AnimeRigModel`。不需要启用 editor plugin，也不需要 Node.js、PSD 解析器或 demo 代码。

模型是 `.tres` 资源，内部包含版本化绑定信息和直接的 `Texture2D` 引用。将模型文件夹连同 `.png.import` 一起复制，保留预乘 alpha 导入设置。

```csharp
using Anime25D;

var actor = new AnimeRigNode
{
    Model = GD.Load<AnimeRigModel>("res://characters/example/model.tres"),
    Position = new Vector2(100, 40),
    Scale = Vector2.One * 0.5f
};
AddChild(actor);

actor.SetParameter("angleX", 0.6); // 使用原版指数平滑
actor.SetPreset("smile");
actor.Playing = false;            // 冻结模拟，仍允许调整静止姿态
actor.SetParameter("angleX", 0.2, immediate: true);
```

坐标原点位于 PSD 画布左上角，单位是原图像素。节点的 `Position`、`Scale`、`Rotation` 和 `Modulate` 由 Godot 控制。各实例独立拥有参数、弹簧、顶点和眼部遮罩；可共享模型纹理。

```csharp
// 手动推进，用于可复现的播放或测试。
actor.AutomaticProcessing = false;
actor.Advance(1.0 / 60.0);

// 运行中更换模型。
actor.LoadModel(otherModel);

// 自动动作与图层控制。
actor.Simulation!.Auto.Random = false;
actor.Simulation.SetBlinkEnabled(false);
actor.Simulation.Parts[0].Depth = 0.8;
actor.Simulation.Parts[0].DrawOrder = 10;
actor.RefreshPose();
```

`Advance` 明确推进一次，独立于 `Playing`；设置 `AutomaticProcessing=false` 可避免自动与手动重复推进。原版最大帧步长 `0.05s` 和弹簧 `1/120s` 子步保持不变。模拟时钟从实例的 0 开始，随机源可以注入；默认每个实例使用独立随机数发生器。

## 模型转换（在 runtime 外）

两个 sample 已转换并包含在 demo 中，运行演示不需要原始 PSD。

转换其他符合原版命名规则的 PSD，仅需要 Node.js，无需安装 npm 依赖：

```sh
node tools/convert.cjs /path/to/character.psd demo/models/my-character
```

转换器复用固定版本的原版 PSD 清理和自动绑定算法；优先从 PSD 所在目录读取 `eye_close.psd`、`mouth_close.psd`，缺失时使用原版内置素材。输出：

- `00.png` 等图层纹理及保留预乘 alpha 的 `.import` 设置。
- `model.tres`：runtime 实际加载的模型。
- `model.rig.json`：供检查和数值对照使用，runtime 不依赖这个单独文件。

重新转换会覆盖指定输出目录中同名的生成文件。请将手工素材保存在其他目录。

## 验证

```sh
# 原版 JS 生成参考结果 → 纯 C# 逐参数、逐顶点对照。
node tools/reference.cjs
dotnet run --project tests/CoreTests.csproj -- .

# 真正的 Godot GPU 渲染：14 个姿态、透明像素、裁剪、资源清理、多实例。
dotnet build
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --import
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . -- --render-tests

# 可选：使用 Chrome 的原版 WebGL 着色器和 stencil，与 Godot 截图比较。
npm ci --prefix tools
node tools/render-reference.cjs

# 在临时空白 Godot 工程中验证 addon，不包含 demo 和工具。
node tools/verify-consumer.cjs
```

对照报告和截图位于 `artifacts/`，不纳入版本控制。独立消费者测试可用环境变量 `GODOT_BIN` 指定 Godot 路径；浏览器比较使用本机 Chrome。

详细设计见 [架构说明](docs/ARCHITECTURE.md)，验收结果见 [验收记录](docs/VALIDATION.md)，范围见 [SCOPE](docs/SCOPE.md)。

## 当前边界

- 复现原版的小角度 2.5D 变形；不会生成隐藏面或真实侧脸。
- 眼部裁剪使用每个实例两个原图分辨率的 SubViewport 遮罩；还没有做多角色规模的性能优化或其他 renderer 的专项验证。
- Godot 与浏览器的抗锯齿、纹理采样和透明边缘可能有少量像素差异；数值几何有独立的严格对照。
- 没有完整绑定编辑器、设置 JSON UI、PNG 导出 UI、换装系统或动作时间线。演示参数调整暂不保存。
- 仅验证了本机桌面环境，其他桌面平台尚未实测。

## 来源和许可

原项目：[852wa/Anime2.5DRig](https://github.com/852wa/Anime2.5DRig)，参考提交 `7450341934a8ff77bf05b90d9f708786e3eb3996`。

派生代码保留原项目 MIT 许可及作者声明，见 [LICENSE](addons/anime25d/LICENSE)。原版快照位于 `tools/vendor/anime25d`，仅供转换与验收。

Sample 与差分图像的权利仍归各原作者；代码许可不涵盖图像的任意再分发授权。
