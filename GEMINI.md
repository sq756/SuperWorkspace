# SuperWorkspace - AI 辅助开发上下文与指令指南 (GEMINI.md)

## 🚀 项目概述 (Project Context)
**SuperWorkspace (赛博工作站)** 是一个深度融合 PC 与 Android 设备的极客级工作站系统。
主要通过 C# (WPF) 调度 `scrcpy`、`adb`、`Virtual Display Driver`、`VB-CABLE` 和 `UnityCapture`，实现跨设备的屏幕接管、虚拟副屏、音频路由、硬件监控与虚拟外设桥接。

## 🛠️ 技术栈 (Tech Stack)
- **UI 框架:** WPF (Windows Presentation Foundation) / .NET 8+
- **核心库:** NAudio (音频), SharpDX (DXGI 渲染), Concentus (Opus 编码), WebSockets
- **底层交互:** Windows P/Invoke (JobObjects, Shared Memory, DWM)
- **跨端引擎:** ADB, Scrcpy, WebCodecs (JS)

## ⚠️ 编码约束与纪律 (Strict Coding Rules)

### 1. 架构解耦原则 (Architecture)
- **禁止向 `MainWindow` 添加业务逻辑**：任何新的核心功能必须抽象为独立的服务类 (例如 `HardwareRadarService`, `AudioMatrixRouter`)，并使用部分类 (`partial`) 或依赖注入。
- **保护线程安全**：项目包含大量的后台轮询、WebSocket 回调和 UI 更新。涉及跨线程 UI 更新必须使用 `Dispatcher.InvokeAsync` (注意后台优先级)。对共享集合操作必须加锁或使用 `ConcurrentDictionary`。

### 2. 底层操作与安全 (Security & P/Invoke)
- **严禁暴力读写权限**：涉及内存映射 (`CreateFileMapping`) 必须收紧 SDDL 权限，防范提权漏洞。
- **规范进程管理**：必须使用 `JobObject` 绑定由本程序启动的子进程 (scrcpy, adb)，确保主程序退出时子进程不留残影。严禁在系统全局无差别执行 `Kill()`，需精确匹配 PID。
- **安全提权**：废除将批处理 (.bat) 写入 Temp 目录并执行的做法。如需管理员权限，请直接利用 `ProcessStartInfo` 的 `Verb="runas"` 或注册服务。

### 3. ADB 与跨端适配 (Cross-Device Compatibility)
- **防御性解析**：所有解析 ADB (如 `dumpsys`, `getprop`) 输出的代码必须使用极度保守的 `TryParse` 和防御性正则。绝不可默认字符串在特定位置（需兼顾 MIUI, HyperOS, ColorOS 等定制安卓系统）。
- **并发隔离**：所有针对 Android 设备的 ADB 命令必须携带 `-s {DeviceId}` 参数，以防多设备连接时产生命令串扰。

### 4. 极致性能要求 (Performance)
- **内存零分配**：在音频处理、视频帧抓取的 `while` 循环中，禁止使用 `new byte[]` 或 `new Bitmap`。必须使用 `ArrayPool<T>.Shared` 或预先分配的固定内存块。
- **低延迟保障**：网络发送 (WebSockets) 必须通过 SemaphoreSlim 进行排队防雪崩，但需引入严格的 Drop 策略，避免陈旧包堆积导致延迟升高。

### 5. 🎨 UI 设计规范与赛博理念 (Cyberpunk Design System)
我们的核心视觉主张是 **“深邃、呼吸感、极客掌控力”**。一切 UI 开发必须遵循：
- **色彩体系 (Palette)**：
  - 背景：避免死黑 (`#000000`)，使用带灰蓝基调的深色渐变 (`#0D0D12` 到 `#1A1A24`)。
  - 主题色 (Accent)：赛博蓝 `#00BFFF` (用于指引与焦点)，霓虹紫 `#A074E5` (用于核心激活态)，极客绿 `#00CA72` (用于成功或健康指标)，警告橙/红 `#D83B01` (用于异常断联或高温)。
- **排版与层级 (Layout & Typography)**：
  - **字体**：常规文本使用系统默认无衬线字体，所有涉及**数据、坐标、状态、控制台输出**的文本**必须**使用 `Consolas` 等等宽字体。
  - **字号与颜色**：主标题 (20-28px, 白色带粗体)，正文 (14-16px, 白色或 `#E0E0E0`)，注释与次要信息 (10-12px, `#888888` 或 `#AAAAAA`)。
  - **留白与圆角**：卡片和容器必须具备圆角 (`CornerRadius="8"` 或 `12`)，严禁元素贴边拥挤，保持 `15px` 到 `25px` 的呼吸感 Padding。
- **组件规范 (Components)**：
  - **图标 (Icons)**：优先使用极简的 Emoji (如 📱, 🖥️, ⚙️) 作为视觉锚点，或使用纯色 `<Path>` 矢量图。禁止使用复杂的彩色位图。
  - **下拉框 (ComboBox) / 列表**：必须使用自定义的 `CyberComboBox` 样式，下拉弹窗需采用深色背景 (`#1A1A24`) 和赛博紫边框，彻底消灭 Windows 原生的白色样式。
  - **幽灵滑块 (Ghost Slider)**：轨道背景极度弱化，只有当鼠标悬停或拖拽时，Thumb (圆点) 才瞬间浮现放大并伴随发光。
  - **深邃滚动条 (ScrollBar)**：隐藏原生的灰色方块滚动条，改为宽度不超过 6px 的半透明光带。
- **交互与动效 (Interactions)**：
  - **左键与右键**：左键单击为主要触发；**双击左键**通常用于展示高级详情 (如全屏图表)；**右键单击**必须唤出定制的深色 `ContextMenu` (如传感器切换菜单)，严禁出现系统默认白底菜单。
  - **微动效**：所有按钮具备悬停响应 (Scale = 1.05) 并伴随阴影发光 (DropShadowEffect)；长驻后台的功能必须配有“呼吸灯” (Opacity 循环动画)。
- **性能铁律**：严禁在 UI 线程 (Dispatcher) 中使用 `Thread.Sleep`。如果是高频操作（如拖拽滑块），必须使用 `Interlocked` 计数器进行微秒级防抖 (Debounce)，然后利用 `Task.Run` 将耗时指令丢给底层 ADB 引擎。

## 🤖 专属 Prompt 指令词 (AI Instructions)
当你在本项目上下文中生成代码时：
1. **不要打破现有功能**：改动必须向下兼容。
2. **注释风格**：保留极客风/赛博风的 Emoji 注释风格 (如 🌟, 🧨, 🛡️) 以区分核心逻辑、风险点和防御代码。
3. **提供完整的 Diff**：如果修改方法，请确保不遗漏原有的关键防御逻辑（例如：空检查、句柄释放等）。