using System;
using System.Buffers;
using System.Net;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Concentus.Structs;
using Concentus; // 🌟 引入 OpusCodecFactory
using Concentus.Enums;

namespace SuperWorkspace
{
    // ==========================================
    // 🌟 全域对表：神圣的 UDP 包头结构
    // ==========================================
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AudioPacketHeader
    {
        public uint SequenceNumber; // 序列号，用于手机端抗抖动 (Jitter Buffer)
        public long Timestamp;      // 绝对时间戳 (100ns / Ticks)，用于全域相位对齐
        public ushort PayloadSize;  // Opus 压缩后的净荷大小
        public byte StreamMode;     // 🌟 新增：战术指令 QoS 标志位
    }

    // ==========================================
    // 🌟 极速发射管：20ms 切片 + Opus 编码 + UDP 投递
    // ==========================================
    public class AppAudioStreamer : IDisposable
    {
        private readonly AppLoopbackCapture _captureEngine;
        
        private readonly OpusEncoder _opusEncoder;
        
        // 🌟 核心参数：48kHz, 双声道, 20ms 一帧 = 960 Frames = 1920 Samples
        private const int FRAME_SIZE = 960; 
        private const int SAMPLES_PER_FRAME = FRAME_SIZE * 2; 
        private const int HEADER_SIZE = 15; // 🌟 包头扩展为 15 字节

        private short[] _frameBuffer = new short[SAMPLES_PER_FRAME];
        private int _frameBufferPos = 0;
        private long _currentFrameTimestamp = 0; // 🌟 记录当前 20ms 帧的首个碎片时间
        
        private uint _sequenceCounter = 0;
        private byte[] _packetBuffer = new byte[1500]; // 预分配发送池，避免装箱

        public byte StreamMode { get; set; } = 0;

        // 🌟 暴露出栈口：包头和 Opus 数据已就绪，交给外部的 WebSocket 管道发射！
        public event Action<byte[], int>? OnPacketEncoded;

        public AppAudioStreamer(AppLoopbackCapture captureEngine)
        {
            _captureEngine = captureEngine;
            
            // 初始化 Opus 编码器 (48kHz, Stereo, 专为低延迟流媒体优化)
#pragma warning disable CS0618 // 🌟 架构师指令：强行忽略类构造函数的过时警告，使用最底层的原生纯 C# 实现！
            _opusEncoder = new OpusEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
#pragma warning restore CS0618
            _opusEncoder.Bitrate = 128000; // 128 kbps 足够高保真

            // 🌟 严格挂载单线阻塞回调
            _captureEngine.DataAvailable += OnAudioDataAvailable;
        }

        private void OnAudioDataAvailable(short[] pcmData, int sampleCount, long captureTimestampMs)
        {
            try
            {
                int readPos = 0;
                while (readPos < sampleCount)
                {
                    int needed = SAMPLES_PER_FRAME - _frameBufferPos;
                    int available = sampleCount - readPos;
                    int toCopy = Math.Min(needed, available);

                    // 🌟 如果是这 20ms 积木的第一块，记录它的绝对发车时间！
                    if (_frameBufferPos == 0) _currentFrameTimestamp = captureTimestampMs;

                    // 填充 20ms 切片缓冲
                    Array.Copy(pcmData, readPos, _frameBuffer, _frameBufferPos, toCopy);
                    _frameBufferPos += toCopy;
                    readPos += toCopy;

                    // 🌟 凑齐 20ms 完整帧，立刻引爆编码器！
                    if (_frameBufferPos == SAMPLES_PER_FRAME)
                    {
                        EncodeAndFire();
                        _frameBufferPos = 0; // 重置切片位置
                    }
                }
            }
            finally
            {
                // 🛡️ 架构师契约：用完后必须将内存归还给 ArrayPool！
                ArrayPool<short>.Shared.Return(pcmData);
            }
        }

        private void EncodeAndFire()
        {
            // 1. Opus 极限压缩 (直接将编码数据写入缓冲区的偏移 14 处，预留给包头)
#pragma warning disable CS0618 // 🌟 架构师指令：强行忽略过时警告，使用最底层的稳定数组调用！
            int compressedSize = _opusEncoder.Encode(_frameBuffer, 0, FRAME_SIZE, _packetBuffer, HEADER_SIZE, _packetBuffer.Length - HEADER_SIZE);
#pragma warning restore CS0618

            // 2. 锻造神圣包头 (14 字节)
            _sequenceCounter++;
            // 🌟 核心修复：强制使用与 WebSocket PTP 同步握手完全一致的时间基准 (Unix毫秒)！
            // 彻底消灭因为 Ticks (100纳秒) 传入前端导致计算出“2000万年后播放”的荒谬 Bug！
            long timestampToSend = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); 
            
            BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(_packetBuffer, 0, 4), _sequenceCounter);
            BinaryPrimitives.WriteInt64BigEndian(new Span<byte>(_packetBuffer, 4, 8), timestampToSend);
            BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(_packetBuffer, 12, 2), (ushort)compressedSize);
            _packetBuffer[14] = StreamMode; // 🌟 写入战术指令 QoS 标志位

            // 3. 抛出给主程序的 WebSocket 进行光速投递
            int totalPacketSize = HEADER_SIZE + compressedSize;
            OnPacketEncoded?.Invoke(_packetBuffer, totalPacketSize);
        }

        public void Dispose() { _captureEngine.DataAvailable -= OnAudioDataAvailable; }
    }
}