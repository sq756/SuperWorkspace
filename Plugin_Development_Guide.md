# 🧩 Super Workspace 插件开发指南 (Developer & AI Guide)

欢迎来到 **Super Workspace** 生态！本软件原生支持通过底层的 `.NET 8` Reflection（反射）机制，动态加载第三方 `.dll` 插件。

无论你是人类开发者，还是 AI 编程助手（如 ChatGPT / Claude / Gemini），请严格遵循以下指南来构建插件。

---

## 🚀 1. 极速初始化项目

请在你的终端（如 VS Code Terminal）中执行以下命令，创建一个基础的 `.NET 8` 类库项目：

```bash
# 创建名为 MyFirstPlugin 的类库，指定框架为 net8.0-windows
dotnet new classlib -n MyFirstPlugin -f net8.0-windows
cd MyFirstPlugin
```

---

## ⚙️ 2. 配置 `.csproj` (极其关键)

打开 `MyFirstPlugin.csproj`，因为我们的主程序使用了 WPF，你**必须**在属性中开启 `<UseWPF>`，并且配置对主程序 `SuperWorkspace.dll` 的引用。

请将内容修改为如下结构：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <!-- 必须开启 WPF 支持，否则无法操作 UI -->
    <UseWPF>true</UseWPF> 
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- 🌟 极其重要：屏蔽引用自包含主程序时产生的底层 DLL 版本冲突警告 -->
    <NoWarn>MSB3277</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <!-- 必须引用主程序的 DLL，请根据你实际的安装路径或编译路径修改 HintPath -->
    <Reference Include="SuperWorkspace">
      <HintPath>C:\Program Files\SuperWorkspace\SuperWorkspace.dll</HintPath>
      <!-- 🌟 必须写 False：确保编译时不会把主程序的 DLL 逆向打包进你的插件里 -->
      <Private>False</Private> 
    </Reference>
  </ItemGroup>

</Project>
```

---

## 💻 3. 编写核心逻辑代码

在项目中新建或修改 `Class1.cs`。你的核心类**必须继承**主程序暴露的 `ISuperPlugin` 接口。

```csharp
using System;
using System.Windows;
using SuperWorkspace; // 引入主程序命名空间

namespace MyFirstPlugin
{
    public class MyAwesomePlugin : ISuperPlugin
    {
        // 1. 填写显示在插件市场的基本信息
        public string Name => "我的超级监控插件";
        public string Description => "这是利用 AI 辅助编写的第一个扩展，用于验证极客生态！";
        public string Author => "你的名字";
        public string Version => "1.0.0";

        // 保存主窗口的上下文
        private MainWindow? _appContext;

        // 2. 初始化：软件加载你的 DLL 时会调用此方法
        public void Initialize(MainWindow context)
        {
            _appContext = context;
        }

        // 3. 启动：软件启动流程完毕后触发
        public void Start()
        {
            // ⚠️ 极其重要：如果涉及 UI 操作，必须包裹在 Dispatcher.InvokeAsync 中！
            _appContext?.Dispatcher.InvokeAsync(() => {
                _appContext.ShowCyberMessage("🧩 插件上线", "成功拦截到主程序的生命周期！");
            });
        }

        // 4. 销毁：软件退出时调用，用于清理你开辟的线程和资源
        public void Stop()
        {
            // 清理后台 Timer 或释放资源
        }
    }
}
```

---

## 📦 4. 编译与安装分发

1. 在终端执行编译命令：
   ```bash
   dotnet build -c Release
   ```
2. 找到生成的 `.dll` 文件（通常在 `bin\Release\net8.0-windows\` 目录下）。
3. 将你的 `MyFirstPlugin.dll` 复制到 Super Workspace 软件根目录下的 **`Plugins`** 文件夹中。
4. **重启 Super Workspace**，在右上角 `➕ 探索更多生态` 面板中，即可看到并享受你的杰作！

---

## 🖼️ 5. 进阶：截获实时视频流 (YOLO / AI 视觉识别)

Super Workspace 在内核层抓取屏幕或手机摄像头画面时，为生态插件提供了一个极其强悍的 **零拷贝视频帧 Hook (`OnVideoFrameCaptured`)**。
你可以非常轻松地接入 YOLOv8、OpenCV 等计算机视觉库。

```csharp
public void Start()
{
    if (_appContext != null)
    {
        // 订阅视频流 Hook (根据刷新率，约每秒 30~120 帧)
        _appContext.OnVideoFrameCaptured += (bitmap) => 
        {
            // ⚠️ 极客警告：
            // 1. 此代码在后台高频独立线程运行，绝对不能直接操作 UI！
            // 2. bitmap 对象在下一帧会被主程序复用或释放。如果你需要进行耗时的异步 YOLO 推理，
            //    必须先克隆它：Bitmap frameForAI = (Bitmap)bitmap.Clone();
            
            // TODO: 把 bitmap 丢给你的 OpenCV 或 ML.NET 模型进行目标检测
        };
    }
}
```