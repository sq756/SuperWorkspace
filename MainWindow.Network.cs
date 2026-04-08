using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace SuperWorkspace
{
    // 🌟 利用 partial 关键字，这个文件里的代码会自动合并到你的主 MainWindow 里
    public partial class MainWindow
    {
        // 🌟 多路复用 WebSocket 处理器
        private async Task HandleWebSocketConnection(HttpListenerContext context, DisplaySession session)
        {
            if (!context.Request.IsWebSocketRequest) return;

            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
            session.Ws = webSocketContext.WebSocket;

            byte[] buffer = new byte[1024];
            while (session.Ws.State == WebSocketState.Open && !session.Cts!.IsCancellationRequested)
            {
                var result = await session.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close) break;

                ProcessBinaryTouchData(buffer, result.Count, session);
            }
        }

        private void ProcessBinaryTouchData(byte[] data, int length, DisplaySession session)
        {
            // 🌟 终极 11 字节电竞级协议解析：[Type(1b)][Action(1b)][ID(1b)][X(4b)][Y(4b)]
            if (length < 11) return; // 拦截残缺包

            byte type = data[0];
            byte action = data[1];
            byte id = data[2];
            float x = BitConverter.ToSingle(data, 3);
            float y = BitConverter.ToSingle(data, 7);

            InjectTouchFromNetwork(type, action, id, x, y, session);
        }
    }
}