#pragma warning disable CS8600, CS8602, CS8604
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox; // 🌟 核心修复：明确声明使用 WPF 的下拉框

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 🌟 MVP 测试引擎：应用级音频路由核心
        // ==========================================
        private Dictionary<string, HttpListener> _audioServers = new Dictionary<string, HttpListener>();
        private Dictionary<string, List<WebSocket>> _audioWebSockets = new Dictionary<string, List<WebSocket>>();
        private CancellationTokenSource? _audioMatrixCts;
        private AppLoopbackCapture? _mvpCapture;
        private AppAudioStreamer? _mvpStreamer;
        private byte _mvpStreamMode = 0; // 0=Gaming, 1=Music
        private Dictionary<WebSocket, int> _wsPendingSends = new Dictionary<WebSocket, int>(); // 并发丢包控制池
        private Dictionary<WebSocket, SemaphoreSlim> _wsLocks = new Dictionary<WebSocket, SemaphoreSlim>(); // 🌟 严格串行发送锁
        private LocalAudioSyncNode? _localSyncNode; // 🌟 终极物理同步节点
        private WasapiOut? _localWasapiOut;         // 🌟 本地物理音频输出

        public class AudioDeviceInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string DisplayName => (Name.Contains("CABLE") ? "💎 推荐: " : "🔊 ") + Name;
        }

        // 🌟 核心调优：直接列出所有的物理/虚拟输出声卡，让用户精准选择黑洞！
        private List<AudioDeviceInfo> GetActiveAudioDevices()
        {
            var list = new List<AudioDeviceInfo>();
            try {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                    list.Add(new AudioDeviceInfo {
                        Id = endpoint.ID,
                        Name = endpoint.FriendlyName
                    });
                }
            } catch { }
            return list;
        }

        private void Btn_AudioRouting2_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null) {
                ShowCyberMessage("⚠️ 未选择设备", "请先在右侧选择一个设备！");
                return;
            }

            // 🌟 利用纯 C# 动态构建一个极其极客的 MVP 进程选择窗口 (不污染原 XAML)
            var mvpWindow = new Window {
                Title = "MVP 进程级音频雷达",
                Width = 550, Height = 640, // 🌟 再次拉高窗口，容纳本地监听下拉框！
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 36)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var stack = new StackPanel { Margin = new Thickness(25) };
            stack.Children.Add(new TextBlock { Text = "🎧 选择要截获的音频通道", Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) });
            stack.Children.Add(new TextBlock { Text = "请直接选择 [VB-Cable] 虚拟声卡通道。并在电脑右下角将音量也切换到该通道，即可实现电脑静音、平板发声的极致体验！", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) });

            var listBox = new ListBox { 
                Height = 220, 
                Background = new SolidColorBrush(Color.FromRgb(20,20,25)), 
                Foreground = new SolidColorBrush(Color.FromRgb(0, 202, 114)), 
                FontFamily = new FontFamily("Consolas"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60,60,65)),
                Margin = new Thickness(0,0,0,20) 
            };

            var devices = GetActiveAudioDevices();
            if (devices.Count == 0) listBox.Items.Add("未检测到任何输出声卡...");
            else {
                foreach (var d in devices) listBox.Items.Add(d);
                listBox.SelectedIndex = 0; // 🌟 体验升级：默认自动选中列表里的第一个设备
            }
            
            listBox.DisplayMemberPath = "DisplayName";
            stack.Children.Add(listBox);

            // 🌟 终极扩展：本地同步监听下拉框 (Local Audio Sync Node)
            stack.Children.Add(new TextBlock { Text = "🔊 本地同步监听音响 (可选)", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) });
            var comboLocal = new ComboBox { Height = 35, FontSize = 13, Style = (Style)FindResource("CyberComboBox"), Margin = new Thickness(0,0,0,20) };
            comboLocal.Items.Add(new AudioDeviceInfo { Id = "", Name = "❌ 无 (仅推送网络)" });
            foreach (var d in devices) {
                if (!d.Name.Contains("CABLE")) comboLocal.Items.Add(d); // 🧨 核心防御：绝对屏蔽虚拟黑洞自身，防止无限啸叫死循环！
            }
            comboLocal.SelectedIndex = 0;
            comboLocal.DisplayMemberPath = "DisplayName";
            stack.Children.Add(comboLocal);

            var btnMode = new Button { 
                Content = _mvpStreamMode == 0 ? "🎮 当前模式: 电竞 (20ms低延迟)" : "🎵 当前模式: 交响乐 (1秒抗丢包)", 
                Height = 35, FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 45)),
                Foreground = _mvpStreamMode == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0, 202, 114)),
                Margin = new Thickness(0, 0, 0, 20), Cursor = System.Windows.Input.Cursors.Hand
            };
            btnMode.Click += (s, ev) => {
                if (_mvpStreamMode == 0) {
                    _mvpStreamMode = 1;
                    btnMode.Content = "🎵 当前模式: 交响乐 (1秒抗丢包)";
                    btnMode.Foreground = new SolidColorBrush(Color.FromRgb(0, 202, 114));
                } else {
                    _mvpStreamMode = 0;
                    btnMode.Content = "🎮 当前模式: 电竞 (20ms低延迟)";
                    btnMode.Foreground = Brushes.White;
                }
                if (_mvpStreamer != null) _mvpStreamer.StreamMode = _mvpStreamMode;
                if (_localSyncNode != null) _localSyncNode.TargetDelayMs = _mvpStreamMode == 1 ? 1000 : 20; // 🌟 同步扭曲本地时间轴！
            };
            stack.Children.Add(btnMode);

            var btnStart = new Button { 
                Content = "🚀 锁定该通道并启动极速推流", 
                Height = 45, FontSize = 16, FontWeight = FontWeights.Bold,
                Style = (Style)FindResource("PrimaryCyberButtonStyle")
            };
            
            btnStart.Click += async (s, args) => {
                if (listBox.SelectedItem is AudioDeviceInfo target) {
                    mvpWindow.Close();
                    string localId = (comboLocal.SelectedItem as AudioDeviceInfo)?.Id ?? "";
                    await StartMvpAudioStreamAsync(target.Id, _selectedDevice.Id, localId);
                } else {
                    ShowCyberMessage("⚠️ 提示", "请先选择一个音频通道！");
                }
            };
            stack.Children.Add(btnStart);

            mvpWindow.Content = stack;
            mvpWindow.ShowDialog();
        }

        private async Task StartMvpAudioStreamAsync(string endpointId, string deviceId, string localEndpointId = "")
        {
            StopMvpAudioEngine(); // 清理并释放上一个实例

            StatusText.Text = $"状态: 🎧 正在锁定目标音频通道...";
            _audioMatrixCts = new CancellationTokenSource();
            int port = GetAvailablePort();

            // 🌟 核心调优：1. 必须优先启动内核捕获引擎！只有抓取成功了，才去唤醒手机网络。
            try {
                _mvpCapture = new AppLoopbackCapture();
                var (success, errorMsg) = await _mvpCapture.StartAsync(endpointId);
                if (!success) {
                    ShowCyberMessage("❌ 捕获失败", $"无法挂载该音频通道！\n\n诊断报告:\n{errorMsg}");
                    AppendLog($"AudioCapture Failed: {errorMsg.Replace("\n", " ")}");
                    StopMvpAudioEngine(); // 失败立刻阻断并清理
                    return;
                }
                _mvpStreamer = new AppAudioStreamer(_mvpCapture);

                // 🌟 核心核弹爆炸：挂载带有时空延时控制的本地物理发声节点！
                if (!string.IsNullOrEmpty(localEndpointId)) {
                    _localSyncNode = new LocalAudioSyncNode(48000, 2);
                    _localSyncNode.TargetDelayMs = _mvpStreamMode == 1 ? 1000 : 20;
                    _mvpCapture.DataAvailable += _localSyncNode.OnAudioDataAvailable; // 像挂炸弹一样并联挂载！

                    using var enumerator = new MMDeviceEnumerator();
                    MMDevice? localDev = null;
                    foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                        if (dev.ID == localEndpointId) { localDev = dev; break; }
                    }
                    if (localDev != null) {
                        _localWasapiOut = new WasapiOut(localDev, AudioClientShareMode.Shared, true, 20); // 底层 20ms 极低延迟握手
                        _localWasapiOut.Init(_localSyncNode);
                        _localWasapiOut.Play();
                    }
                }
            } catch (Exception ex) {
                ShowCyberMessage("❌ 引擎崩溃", ex.Message);
                StopMvpAudioEngine();
                return;
            }

            StatusText.Text = $"状态: 🎧 正在准备 WebCodecs 接收端...";

            // 2. 打通 ADB 逆向隧道
            await Task.Run(() => RunAdbCommand($"-s {deviceId} reverse --remove-all")); // 稳妥起见先清理
            await Task.Run(() => RunAdbCommand($"-s {deviceId} reverse tcp:{port} tcp:{port}"));

            // 3. 启动中枢 Web 服务器
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            _audioServers[deviceId] = listener;
            _audioWebSockets[deviceId] = new List<WebSocket>();

            _ = Task.Run(async () => {
                while (listener.IsListening && !_audioMatrixCts.Token.IsCancellationRequested) {
                    try {
                        var context = await listener.GetContextAsync();
                        if (context.Request.IsWebSocketRequest) {
                            var wsContext = await context.AcceptWebSocketAsync(null);
                            lock (_audioWebSockets) { _audioWebSockets[deviceId].Add(wsContext.WebSocket); }
                            
                            // 黑洞循环保持连接
                            _ = Task.Run(async () => {
                                var buf = new byte[1024];
                                while (wsContext.WebSocket.State == WebSocketState.Open) {
                                    try { 
                                        var result = await wsContext.WebSocket.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); 
                                        if (result.MessageType == WebSocketMessageType.Text) {
                                            string msg = Encoding.UTF8.GetString(buf, 0, result.Count);
                                            // 🌟 拦截同步请求，以微秒级物理速度反射 PC 绝对时间！
                                            if (msg.StartsWith("sync_req|")) {
                                                long t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                                byte[] resBytes = Encoding.UTF8.GetBytes($"sync_res|{msg.Substring(9)}|{t2}");
                                                
                                                SemaphoreSlim sendLock;
                                                lock (_wsLocks) { 
                                                    if (!_wsLocks.TryGetValue(wsContext.WebSocket, out sendLock!)) {
                                                        sendLock = new SemaphoreSlim(1, 1);
                                                        _wsLocks[wsContext.WebSocket] = sendLock;
                                                    }
                                                }
                                                await sendLock.WaitAsync();
                                                try { if (wsContext.WebSocket.State == WebSocketState.Open) await wsContext.WebSocket.SendAsync(new ArraySegment<byte>(resBytes), WebSocketMessageType.Text, true, CancellationToken.None); }
                                                finally { sendLock.Release(); }
                                            }
                                            else if (msg.StartsWith("sync_report|")) {
                                                // 🌟 接收平板的时空战报，点亮主程序的雷达大屏！
                                                string[] parts = msg.Split('|');
                                                if (parts.Length >= 3) _ = Dispatcher.InvokeAsync(() => { // 🌟 核心修复：使用抛弃符消除 CS4014 警告
                                                    if (Sync_RTT != null) Sync_RTT.Text = parts[1] + " ms"; 
                                                    if (Sync_Offset != null) Sync_Offset.Text = parts[2] + " ms";
                                                    if (Sync_Device != null) Sync_Device.Text = "📱 移动端接收节点"; 
                                                    if (Sync_Status != null) { Sync_Status.Text = "✅ PTP 锁定"; Sync_Status.Foreground = new SolidColorBrush(Color.FromRgb(0, 202, 114)); }
                                                });
                                            }
                                        }
                                    } catch { break; }
                                }
                                lock (_audioWebSockets) { if (_audioWebSockets.ContainsKey(deviceId)) _audioWebSockets[deviceId].Remove(wsContext.WebSocket); }
                                lock (_wsPendingSends) { _wsPendingSends.Remove(wsContext.WebSocket); }
                                lock (_wsLocks) { if (_wsLocks.TryGetValue(wsContext.WebSocket, out var s)) { s.Dispose(); _wsLocks.Remove(wsContext.WebSocket); } }
                            });
                        } else if (context.Request.Url?.AbsolutePath == "/") {
                            // 🌟 注入 WebCodecs HTML 客户端！
                            string html = @"<html><head>
                            <meta name='viewport' content='width=device-width, initial-scale=1.0, user-scalable=no, maximum-scale=1.0'>
                            <style>
                                body { background: #111; color: white; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100vh; font-family: sans-serif; margin:0; }
                                .btn { padding: 20px 40px; border-radius: 12px; font-size: 20px; font-weight: bold; background: #A074E5; color: white; cursor: pointer; border: none; box-shadow: 0 4px 15px rgba(160,116,229,0.5); transition: 0.2s; }
                                .btn:active { transform: scale(0.95); }
                            </style>
                            </head><body>
                            <div style='color:#00CA72; font-size:24px; font-weight:bold; margin-bottom:20px;'>🎧 极速音频接收端</div>
                            <div id='status' style='color:#888; font-size:12px; margin-bottom:30px; text-align:center;'>等待激活...</div>
                            <button id='btn-play' class='btn'>▶ 点击激活 WebCodecs 音频流</button>
                            <div id='debug-output' style='font-family:Consolas; font-size:10px; color:#555; text-align:left; max-width:80%; word-break:break-all; margin-top:20px; border-top:1px solid #333; padding-top:10px; display:none;'></div>
                            <script>
                                let audioCtx = null;
                                let ws = null;
                                let currentJitterMs = 20; // 🌟 动态 QoS 缓冲池
                                let stat = document.getElementById('status');
                                let debugOutput = document.getElementById('debug-output');
                                let packetCounter = 0;
                                let decodedCounter = 0;
                                let silentCounter = 0; // 🌟 连续静音帧计数器
                                let lastScheduledTime = 0; // 🌟 防重叠排队锁

                                function logDebug(msg) {
                                    debugOutput.style.display = 'block';
                                        debugOutput.innerHTML = `<div style='margin-bottom:4px;'>${msg}</div>` + debugOutput.innerHTML;
                                    if (debugOutput.childNodes.length > 6) debugOutput.removeChild(debugOutput.lastChild);
                                }

                                // 🌟 Zero-Sync 时空追踪引擎
                                class ZeroSyncEngine {
                                    constructor(ws) {
                                        this.ws = ws;
                                        this.rttHistory = [];
                                        this.clockOffset = 0;
                                        this.isSynced = false;
                                        this.syncCount = 0;
                                        this.pingInterval = setInterval(() => this.ping(), 50); // 前 2 秒高频探测
                                    }
                                    ping() {
                                        if (this.ws.readyState === WebSocket.OPEN) {
                                            this.ws.send('sync_req|' + performance.now().toFixed(3));
                                            this.syncCount++;
                                            if (this.syncCount === 40) {
                                                clearInterval(this.pingInterval);
                                                this.pingInterval = setInterval(() => this.ping(), 5000); // 降级心跳
                                            }
                                        }
                                    }
                                    handlePong(t1, t2) {
                                        let t3 = performance.now();
                                        let rtt = t3 - t1;
                                        let offset = t2 - ((t1 + t3) / 2);
                                        this.rttHistory.push({ rtt, offset });
                                        if (this.rttHistory.length > 20) this.rttHistory.shift();
                                        
                                        // 🌟 算法 1：幸运包过滤 (Minimum RTT Filter)
                                        let sorted = [...this.rttHistory].sort((a, b) => a.rtt - b.rtt);
                                        let lucky = sorted.slice(0, Math.min(5, sorted.length));
                                        let avgOffset = lucky.reduce((sum, p) => sum + p.offset, 0) / lucky.length;
                                        
                                        // 🌟 算法 2：EMA 平滑滤波
                                        if (!this.isSynced) { this.clockOffset = avgOffset; this.isSynced = true; }
                                        else { this.clockOffset = this.clockOffset * 0.8 + avgOffset * 0.2; }
                                        
                                        if (this.syncCount % 10 === 0) {
                                            logDebug(`⏱️ PTP同步 | RTT:${rtt.toFixed(1)}ms | 误差:${this.clockOffset.toFixed(1)}ms`);
                                            // 🌟 实时向 PC 汇报战果
                                            if (this.ws.readyState === WebSocket.OPEN) this.ws.send(`sync_report|${rtt.toFixed(1)}|${this.clockOffset.toFixed(1)}`);
                                        }
                                    }
                                }
                                let zeroSync = null;

                                document.getElementById('btn-play').onclick = async () => {
                                    document.getElementById('btn-play').style.display = 'none';
                                    stat.innerText = '正在初始化 AudioContext...';
                                    
                                    audioCtx = new (window.AudioContext || window.webkitAudioContext)({sampleRate: 48000});
                                    if (audioCtx.state === 'suspended') await audioCtx.resume();

                                    const decoder = new AudioDecoder({
                                        output: (audioData) => {
                                            try {
                                                const buffer = audioCtx.createBuffer(audioData.numberOfChannels, audioData.numberOfFrames, audioData.sampleRate);
                                                let isSilent = true;
                                                for (let i = 0; i < audioData.numberOfChannels; i++) {
                                                    // 🌟 核心修复：让底层自己汇报需要多大的“内存对齐碗”，绝不盲猜！
                                                    const options = { planeIndex: i, format: 'f32-planar' }; // 🌟 核心破局：强制剥离左右声道平面，彻底击穿安卓底层交错格式的报错！
                                                    const allocSize = audioData.allocationSize(options);
                                                    const f32 = new Float32Array(allocSize / 4);
                                                    audioData.copyTo(f32, options);
                                                    // 🌟 掐头去尾：只把真实的帧数送给喇叭
                                                    buffer.copyToChannel(f32.subarray(0, audioData.numberOfFrames), i);
                                                    if (isSilent) {
                                                        for(let j=0; j<audioData.numberOfFrames; j++) {
                                                            if (Math.abs(f32[j]) > 0.0001) { isSilent = false; break; }
                                                        }
                                                    }
                                                }
                                                
                                                if (isSilent) silentCounter++; else silentCounter = 0;
                                                decodedCounter++;

                                                // 🌟 终极保命断路器：如果 PTP 握手还没完成，绝不使用绝对时间！直接相对播放保命！
                                                if (!zeroSync || !zeroSync.isSynced) {
                                                    const source = audioCtx.createBufferSource(); source.buffer = buffer; source.connect(audioCtx.destination);
                                                    source.start(); audioData.close();
                                                    return;
                                                }

                                                // 🌟 终极物理同步算法：绝对时间映射！
                                                const pcCaptureTimeMs = audioData.timestamp / 1000.0;
                                                const currentPcTimeMs = performance.now() + (zeroSync ? zeroSync.clockOffset : 0);
                                                const timeUntilPlayMs = (pcCaptureTimeMs + currentJitterMs) - currentPcTimeMs;
                                                
                                                let targetTime = audioCtx.currentTime + (timeUntilPlayMs / 1000.0);

                                                // 🌟 物理级防踩踏排队机制 (严格保证波形不重叠)
                                                if (targetTime < lastScheduledTime) targetTime = lastScheduledTime;
                                                lastScheduledTime = targetTime + (audioData.duration / 1000000.0);

                                                const source = audioCtx.createBufferSource();
                                                source.buffer = buffer;
                                                source.connect(audioCtx.destination);
                                                
                                                const timeDiff = targetTime - audioCtx.currentTime;
                                                const jitterSec = currentJitterMs / 1000.0;
                                                
                                                // 🌟 终极防空洞：波形必须 100% 严丝合缝，绝对禁止调整 playbackRate 导致波形撕裂！
                                                // 如果严重偏离目标缓冲（网络爆发导致滞后，或时钟漂移导致超前），直接硬重置锚点，抹平差距。
                                                if (targetTime > audioCtx.currentTime + jitterSec + 0.150 || targetTime < audioCtx.currentTime) {
                                                    targetTime = audioCtx.currentTime + jitterSec;
                                                    lastScheduledTime = targetTime + (audioData.duration / 1000000.0);
                                                }
                                                source.start(targetTime); 

                                                if (decodedCounter % 50 === 0) logDebug(`🎵 解码[${decodedCounter}] | 积压:${(timeDiff*1000).toFixed(0)}ms`);
                                            } catch (err) {
                                                logDebug(`❌ JS 回调崩溃: ${err.message}`);
                                            } finally {
                                                audioData.close(); // 🌟 极其重要：无论是否报错，必须释放内存！否则解码器必定卡死罢工！
                                            }
                                        },
                                        error: (e) => { stat.innerText = '解码异常: ' + e; console.error(e); }
                                    });

                                    // 🌟 终极修复：使用绝对标准的 RFC 7845 OpusHead，带有 312 帧的 Pre-skip！
                                    const opusHead = new Uint8Array([
                                        0x4f, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64, // 'OpusHead'
                                        0x01, 0x02, 0x38, 0x01, 0x80, 0xBB, 0x00, 0x00, // Version, Channels, Pre-skip(312), SampleRate(48000)
                                        0x00, 0x00, 0x00                                // OutputGain, ChannelMapping
                                    ]);

                                    try {
                                        // 🌟 核心修复：把上一轮不小心删掉的配置语句加回来！
                                        decoder.configure({ codec: 'opus', sampleRate: 48000, numberOfChannels: 2, description: opusHead.buffer });
                                        stat.innerText = 'WebCodecs 解码器配置成功，准备迎战...';
                                    } catch (err) { logDebug('❌ 配置失败: ' + err); }

                                    // 🌟 深度探针：检查浏览器到底是不是真支持！
                                    AudioDecoder.isConfigSupported({ codec: 'opus', sampleRate: 48000, numberOfChannels: 2 }).then(res => {
                                        logDebug(`🔧 浏览器底层支持度: ${res.supported ? '✅真支持' : '❌伪支持'}`);
                                    });

                                    ws = new WebSocket('ws://' + location.host + '/ws');
                                    ws.binaryType = 'arraybuffer';
                                    ws.onopen = () => { zeroSync = new ZeroSyncEngine(ws); stat.innerText = '🚀 管道已接通！正在对齐时空...'; stat.style.color = '#A074E5'; };
                                    ws.onmessage = async (e) => {
                                        if (typeof e.data === 'string') {
                                            if (e.data.startsWith('sync_res|')) {
                                                let parts = e.data.split('|');
                                                if (zeroSync) zeroSync.handlePong(parseFloat(parts[1]), parseFloat(parts[2]));
                                            }
                                            return;
                                        }
                                        
                                        const buffer = e.data;
                                        const view = new DataView(buffer);
                                        const seq = view.getUint32(0, false);
                                        const pcTicks = Number(view.getBigInt64(4, false)); 
                                        const payloadSize = view.getUint16(12, false);
                                        const mode = view.getUint8(14); // 🌟 提取 QoS 多模态标志位
                                        const payload = new Uint8Array(buffer, 15, payloadSize);

                                        // 🌟 动态 QoS 模式切换
                                        let newJitter = (mode === 1) ? 1000 : 20;
                                        if (currentJitterMs !== newJitter) {
                                            currentJitterMs = newJitter;
                                            lastScheduledTime = 0; // 重置防踩踏锁
                                            logDebug(mode === 1 ? '🎵 已切换至交响乐模式 (1000ms 抗丢包)' : '🎮 已切换至电竞模式 (20ms 低延迟)');
                                        }

                                        packetCounter++;
                                        // 🌟 深度探针：监控解码器的内部胃口，看它是不是被包噎死了！
                                        if (packetCounter % 50 === 0) logDebug(`📦 收包[${packetCounter}] | 净荷:${payloadSize}B | 解码积压:${decoder.decodeQueueSize}帧`);

                                        // 🌟 防雪崩拦截：如果解码器没配置成功，绝不硬塞数据导致疯狂报错！
                                        if (decoder.state === 'unconfigured') return;
                                        try {
                                            decoder.decode(new EncodedAudioChunk({
                                                type: 'key',
                                                timestamp: pcTicks * 1000, // 🌟 恢复为源头打下的绝对 PC 时间！
                                                duration: 20000,
                                                data: payload
                                            }));
                                        } catch (err) {
                                            logDebug(`❌ 解码抛错: ${err.message}`);
                                        }
                                    };
                                };
                            </script></body></html>";
                            byte[] b = Encoding.UTF8.GetBytes(html);
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.OutputStream.WriteAsync(b, 0, b.Length);
                            context.Response.Close();
                        }
                    } catch { }
                }
            });

            // 4. 唤起平板浏览器
            await Task.Run(() => {
                RunAdbCommand($"-s {deviceId} shell am start -a android.intent.action.VIEW -d http://localhost:{port}");
            });
            
            // 给浏览器一点反应时间
            await Task.Delay(1000);

            // 5. 绑定 Streamer 的 WebSocket 投递总线
            _mvpStreamer.StreamMode = _mvpStreamMode;
            _mvpStreamer.OnPacketEncoded += (buffer, length) => {
                lock (_audioWebSockets) {
                    foreach (var wsList in _audioWebSockets.Values) {
                        for (int i = wsList.Count - 1; i >= 0; i--) {
                            var ws = wsList[i];
                            if (ws.State == WebSocketState.Open) {
                                EnqueueWebSocketSend(ws, buffer, length);
                            } else {
                                wsList.RemoveAt(i);
                                lock (_wsPendingSends) { _wsPendingSends.Remove(ws); }
                                lock (_wsLocks) { if (_wsLocks.TryGetValue(ws, out var s)) { s.Dispose(); _wsLocks.Remove(ws); } }
                            }
                        }
                    }
                }
            };

            StatusText.Text = $"状态: 🎧 指定音频通道的底层音轨正在高速串流中...";
            if (Card_Audio2 != null) Card_Audio2.Tag = "Active";
        }

        // ==========================================
        // 🌟 异步扇出队列 (Async Fan-out) 与 TCP 防雪崩丢包
        // ==========================================
        private void EnqueueWebSocketSend(WebSocket ws, byte[] buffer, int length)
        {
            SemaphoreSlim sendLock;
            lock (_wsLocks) {
                if (!_wsLocks.TryGetValue(ws, out sendLock)) {
                    sendLock = new SemaphoreSlim(1, 1);
                    _wsLocks[ws] = sendLock;
                }
            }

            lock (_wsPendingSends) {
                if (!_wsPendingSends.ContainsKey(ws)) _wsPendingSends[ws] = 0;
                int maxBacklog = _mvpStreamMode == 1 ? 50 : 2; // 🎵 交响乐容忍 1 秒(50包)积压，🎮 电竞容忍 40ms(2包) 积压
                if (_wsPendingSends[ws] > maxBacklog) return; // 🧨 丢包策略：直接物理丢弃，防止 TCP 拥塞
                _wsPendingSends[ws]++;
            }

            // 使用 ArrayPool 为异步任务克隆一份绝对安全的内存快照
            byte[] snapshot = ArrayPool<byte>.Shared.Rent(length);
            Buffer.BlockCopy(buffer, 0, snapshot, 0, length);

            _ = Task.Run(async () => {
                try {
                    await sendLock.WaitAsync(); // 🌟 核心防雪崩：保护 WebSocket 绝对串行，防止并发 SendAsync 导致瞬间被操作系统断开！
                    try {
                        if (ws.State == WebSocketState.Open) {
                            await ws.SendAsync(new ArraySegment<byte>(snapshot, 0, length), WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                    } finally {
                        sendLock.Release();
                    }
                } catch { }
                finally {
                    ArrayPool<byte>.Shared.Return(snapshot);
                    lock (_wsPendingSends) { if (_wsPendingSends.ContainsKey(ws)) _wsPendingSends[ws]--; }
                }
            });
        }

        public void StopMvpAudioEngine()
        {
            if (Card_Audio2 != null) Card_Audio2.Tag = "";
            Card_Audio.Tag = "";
            _audioMatrixCts?.Cancel();
            _mvpStreamer?.Dispose();
            _mvpStreamer = null;
            _mvpCapture?.Dispose();
            _mvpCapture = null;

            foreach (var server in _audioServers.Values) { try { server.Stop(); } catch { } }
            _audioServers.Clear();
            _audioWebSockets.Clear();
            
            lock (_wsLocks) {
                foreach (var s in _wsLocks.Values) { try { s.Dispose(); } catch {} }
                _wsLocks.Clear();
            }
        }
        
        // 🌟 兜底：让遗留在 MainWindow.xaml.cs 里的旧矩阵代码能正常编译
        public void StartAudioMatrixEngine() { }
        public void StopAudioMatrixEngine() { StopMvpAudioEngine(); }

        // ==========================================
        // 🌟 驱动部署：一键静默注入虚拟音频线 (VAC)
        // ==========================================
        private void Btn_InstallVACDriver_Click(object sender, RoutedEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ""; // 🌟 核心修复：使用空合并运算符消除 CS8600 警告
            string installerPath = "";
            string prjRoot = "";
            try { prjRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..")); } catch { }
            
            var searchDirs = new System.Collections.Generic.List<string> { baseDir };
            if (System.IO.Directory.Exists(prjRoot)) searchDirs.Add(prjRoot);

            foreach (var dir in searchDirs)
            {
                if (System.IO.Directory.Exists(dir))
                {
                    var files = System.IO.Directory.GetFiles(dir, "VBCABLE_Setup_x64.exe", System.IO.SearchOption.AllDirectories);
                    if (files.Length > 0) { installerPath = files[0]; break; }
                }
            }

            if (string.IsNullOrEmpty(installerPath))
            {
                ShowCyberMessage("⚠️ 文件缺失", "找不到底层驱动安装程序！\n请确保已将 VBCABLE_Setup_x64.exe 放入软件目录中。");
                return;
            }

            try {
                // 🌟 使用 -i 参数进行无 UI 静默安装！
                Process.Start(new ProcessStartInfo { FileName = installerPath, Arguments = "-i", Verb = "runas", UseShellExecute = true })?.WaitForExit();
                ShowCyberMessage("✅ 部署成功", "虚拟音频线 (VAC) 驱动已成功注入系统内核！\n\n请在电脑右下角喇叭图标中，将输出设备切换为 [CABLE Input]。");
            }
            catch (Exception ex) { ShowCyberMessage("❌ 权限不足", "驱动部署失败:\n" + ex.Message); }
        }

        // ==========================================
        // 🌟 无缝同步 (Zero-Sync) UI 触点
        // ==========================================
        private void Btn_ZeroSync_Click(object sender, RoutedEventArgs e) {
            ZeroSyncOverlay.Visibility = Visibility.Visible;
        }
        private void Btn_CloseZeroSync_Click(object sender, RoutedEventArgs e) {
            ZeroSyncOverlay.Visibility = Visibility.Collapsed;
        }
    }
}