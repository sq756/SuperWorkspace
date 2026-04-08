using System;
using System.Collections.Concurrent;
using NAudio.Wave;

namespace SuperWorkspace
{
    // 🌟 携带绝对时间戳的微观波形切片
    public class SyncAudioFrame
    {
        public long TimestampMs { get; set; }
        public float[] PcmData { get; set; } = null!;
    }

    // ==========================================
    // 🌟 终极主宰节点：带有绝对时间门控的无锁并发 Jitter Buffer
    // ==========================================
    public class LocalAudioSyncNode : ISampleProvider
    {
        private readonly WaveFormat _waveFormat;
        private readonly ConcurrentQueue<SyncAudioFrame> _jitterBuffer = new ConcurrentQueue<SyncAudioFrame>();
        private float[] _leftover = Array.Empty<float>();
        private int _leftoverPos = 0;
        
        public WaveFormat WaveFormat => _waveFormat;
        
        // 🌟 核心战术指令：本地播放延迟。电竞模式=20ms，交响乐模式=1000ms
        public long TargetDelayMs { get; set; } = 20; 

        public LocalAudioSyncNode(int sampleRate, int channels)
        {
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        // 🌟 生产者：挂载在 AppLoopbackEngine 事件上 (无锁！0 阻塞！防死锁幽灵！)
        public void OnAudioDataAvailable(short[] buffer, int sampleCount, long timestampMs)
        {
            // 极速转换 Short -> Float，迎合 NAudio 本地播放
            float[] floatData = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++) floatData[i] = buffer[i] / 32768f;
            
            _jitterBuffer.Enqueue(new SyncAudioFrame { TimestampMs = timestampMs, PcmData = floatData });
        }

        // 🌟 消费者：Windows 底层 WasapiOut 狂暴拉取数据的唯一接口
        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;
            long currentTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            while (written < count)
            {
                if (_leftoverPos < _leftover.Length) {
                    int toCopy = Math.Min(count - written, _leftover.Length - _leftoverPos);
                    Array.Copy(_leftover, _leftoverPos, buffer, offset + written, toCopy);
                    written += toCopy;
                    _leftoverPos += toCopy;
                    continue;
                }

                if (_jitterBuffer.TryPeek(out var nextFrame))
                {
                    long targetPlayTime = nextFrame.TimestampMs + TargetDelayMs;
                    long timeDiff = targetPlayTime - currentTimeMs;

                    // 🌟 绝对时间门控 (Time-Gate)：时间还没到？死死扣住波形不放！
                    if (timeDiff > 10) break; // 提前退出，后面会自动填充静音包拖延时间！

                    if (_jitterBuffer.TryDequeue(out nextFrame))
                    {
                        // 🌟 晶振漂移防线 (NetEQ)：如果本地声卡播放太慢导致积压严重，无情丢弃最老的帧！
                        // 10 帧 = 100ms 积压。在本地电脑上通常几小时才会触发一次，人耳绝对无法察觉。
                        if (_jitterBuffer.Count > (TargetDelayMs / 10) + 10) continue;

                        _leftover = nextFrame.PcmData;
                        _leftoverPos = 0;
                    }
                }
                else {
                    break; // 缓冲区干涸
                }
            }

            // 🌟 终极防空洞：如果没拿到足够数据（因为时间未到，或者源头没声音），绝对不能返回 0！
            // 必须向物理扬声器填充纯粹的绝对静音，欺骗 Windows 保持声卡常亮不死锁！
            if (written < count) {
                Array.Clear(buffer, offset + written, count - written);
                written = count;
            }
            return written;
        }
    }
}