using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;

namespace SuperWorkspace
{
    // ==========================================
    // 🌟 战术 A 核心引擎：全局系统音无损捕获 (MVP)
    // ==========================================
    public class AppLoopbackCapture : IDisposable
    {
        private WasapiLoopbackCapture? _capture;
        private BufferedWaveProvider? _rawBuffer;
        private ISampleProvider? _resampler;
        
        private volatile bool _isCapturing = false;

        // 🌟 终极架构：在源头打上绝对时间戳！所有下游节点（网络/本地）必须遵循此时间！
        public delegate void AudioDataAvailableHandler(short[] buffer, int sampleCount, long captureTimestampMs);
        public event AudioDataAvailableHandler? DataAvailable;

        public async Task<(bool, string)> StartAsync(string endpointId)
        {
            return await Task.Run(() => 
            {
                try 
                {
                    MMDevice? captureDevice = null;
                    using var enumerator = new MMDeviceEnumerator();
                    if (string.IsNullOrEmpty(endpointId)) {
                        captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    } else {
                        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                            if (dev.ID == endpointId) { captureDevice = dev; break; }
                        }
                    }
                    if (captureDevice == null) return (false, "找不到指定的音频硬件设备！");

                    // 1. 启动全局系统内录 (WASAPI Loopback)
                    _capture = new WasapiLoopbackCapture(captureDevice);
                    
                    // 2. 建立缓冲池，接收原始 PCM
                    _rawBuffer = new BufferedWaveProvider(_capture.WaveFormat) { 
                        BufferDuration = TimeSpan.FromMilliseconds(500), 
                        DiscardOnBufferOverflow = true 
                    };
                    
                    _capture.DataAvailable += (s, e) => {
                        if (_rawBuffer != null && e.BytesRecorded > 0)
                            _rawBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

                        // 🌟 终极事件驱动：只要声卡吐出真实数据，我们就立刻榨干重采样器，绝不多等一毫秒！
                        DrainResampler();
                    };

                    // 3. 建立强力重采样管线：强制转换为 48kHz, 双声道, IEEE Float
                    ISampleProvider provider = _rawBuffer.ToSampleProvider();
                    
                    if (provider.WaveFormat.SampleRate != 48000) {
                        provider = new WdlResamplingSampleProvider(provider, 48000);
                    }
                    if (provider.WaveFormat.Channels == 1) {
                        provider = new MonoToStereoSampleProvider(provider);
                    } else if (provider.WaveFormat.Channels > 2) {
                        var multiplexer = new MultiplexingSampleProvider(new[] { provider }, 2);
                        multiplexer.ConnectInputToOutput(0, 0); 
                        multiplexer.ConnectInputToOutput(1, 1); 
                        provider = multiplexer;
                    }
                    _resampler = provider;

                    _isCapturing = true;

                    _capture.StartRecording();
                    return (true, "OK");
                } 
                catch (Exception ex) { 
                    return (false, $"全局音频捕获异常:\n{ex.Message}"); 
                }
            });
        }

        private void DrainResampler()
        {
            if (_resampler == null || !_isCapturing) return;
            
            try {
                // 每次尝试提取 10ms 的浮点数据 (480帧 * 双声道)
                float[] floatBuf = ArrayPool<float>.Shared.Rent(960);
                
                // 🌟 流水线积木机：只要缓冲池里还有真实数据，我们就连续切割！
                while (_rawBuffer != null && _rawBuffer.BufferedDuration.TotalMilliseconds >= 10)
                {
                    int read = _resampler.Read(floatBuf, 0, 960);
                    if (read > 0) {
                        // 🌟 极速类型转换 (Float32 -> Int16)
                        short[] shortBuffer = ArrayPool<short>.Shared.Rent(read);
                        for (int i = 0; i < read; i++) {
                            float val = floatBuf[i];
                            if (val > 1.0f) val = 1.0f; else if (val < -1.0f) val = -1.0f;
                            shortBuffer[i] = (short)(val * 32767f);
                        }

                        // 🌟 宇宙大爆炸时刻：烙印绝对时间戳！
                        long captureTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        DataAvailable?.Invoke(shortBuffer, read, captureTime);
                    }
                    else {
                        break; // 榨干了就退出，等待下一次硬件事件
                    }
                }
                ArrayPool<float>.Shared.Return(floatBuf); // 🌟 完璧归赵，拒绝内存泄漏
            } catch { }
        }

        public void Dispose()
        {
            _isCapturing = false; 
            
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;
        }
    }
}