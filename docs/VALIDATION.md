# 0.1 验收记录

日期：2026-09-15。参考源：`7450341934a8ff77bf05b90d9f708786e3eb3996`。

环境：macOS / Apple M4、Godot `4.7.2.stable.mono.official.ed1daf0bf`、.NET SDK `10.0.400`，Compatibility / OpenGL renderer。浏览器参照由本机 Chrome WebGL 实际绘制。

## 数值对照：通过

使用未修改的原版 `prepareLayers`、`fadeAlpha`、`deform`、`animate` 生成独立参考数据。

- 原版全部 **35 个参数**的默认值与上下限一致。
- **167 个用例，1,143,442 次数值比较**。
- 最大顶点坐标差异：**0 像素**。
- 最大参数差异：**2.220446049250313 × 10⁻¹⁶**。
- 覆盖两个实际 sample、全部参数边界、眼口混合、左右单眼、图层反序，以及 10 / 30 / 60 / 144 FPS 动画序列。
- 覆盖原版表情抑制自动眨眼/口型、鼠标输入、关闭头发物理，以及加入合成 `eye_close2` 图层后的长闭眼路径。
- 验证独立实例没有共享顶点或参数状态，变帧率下坐标保持有限，弹簧能够收敛。

数值测试阈值：顶点 0.00025 像素，参数及透明度通常 10⁻⁹。报告：`artifacts/core-tests.json`。

## 实际 GPU 渲染：通过

两个 sample 各 7 个姿态，共 **14 张** 1280 × 1280 透明确认图：neutral、turn、closed、wink、crossfade、animation60、reordered。

Godot 独立计算姿态并绘制；浏览器使用原版 shader、纹理上传和 stencil 绘制流程。比较时统一为预乘 RGBA，忽略完全透明像素中没有意义的 RGB。

| 指标 | 结果 |
|---|---:|
| 单张图最高平均通道误差（0–255） | 0.050634 以下 |
| 通道误差超过 12 的像素比例，单张最高 | 0.102% 以下 |
| 平均通道误差验收上限 | 0.25 |
| 误差超过 12 的像素比例验收上限 | 0.25% |

少量局部像素差异仍存在；未将不同绘制后端描述为像素完全一致。报告：`artifacts/image-comparison.json`，差异图：`artifacts/reference-images/diff-*.png`。

附加渲染断言通过：

- 每个姿态有可见角色，画布边角保持透明。
- 隐藏白目后，虹膜不再泄漏到没有眼部遮罩的区域。
- 图层反序仍可绘制独立的左右眼遮罩。
- 共享模型资源的角色实例不共享可变几何。
- 反复加载模型无报告的渲染错误，清空节点后画布恢复透明。

报告：`artifacts/godot/render-tests.json`。主 demo 已实际启动并检查截图：`artifacts/demo.png`。

## addon 独立性：通过

在临时空白项目中，仅复制 `addons/anime25d` 和 Sample A 转换结果，执行编译、Godot 导入、模型实例化及模拟。

该工程没有 demo、tools、隐式全局 using 或已启用的 editor plugin，编译和运行均成功。报告：`artifacts/consumer-test.json`，详细输出：`artifacts/consumer.log`。

## 尚未验证的范围

- 其他桌面平台和其他 Godot renderer。
- 大量角色同时显示的性能、打包导出和长时间内存压力。
- 用户自己的 layered character；当前以原项目两个 sample 为首版验收素材。

这些不影响已确认的首版样例复刻结果，也不应被当作已有兼容性保证。
