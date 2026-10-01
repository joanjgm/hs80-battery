using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Hs80Battery
{
    // Plugin side of the Stream Deck SDK: Stream Deck launches the exe with
    // -port/-pluginUUID/-registerEvent/-info, we connect back over a local WebSocket, register,
    // and then paint every key that hosts our action with setImage.
    sealed class StreamDeckPlugin : IDisposable
    {
        public event Action RefreshRequested;   // key pressed or system woke up
        public event Action Closed;             // Stream Deck went away; the process should exit

        readonly int port;
        readonly string pluginUuid, registerEvent;
        readonly ClientWebSocket ws = new ClientWebSocket();
        readonly BlockingCollection<string> outbox = new BlockingCollection<string>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly HashSet<string> keys = new HashSet<string>();   // contexts of visible actions
        readonly object gate = new object();
        string image;   // last data URI, sent to keys as they appear
        int closed;

        StreamDeckPlugin(int port, string pluginUuid, string registerEvent)
        {
            this.port = port;
            this.pluginUuid = pluginUuid;
            this.registerEvent = registerEvent;
        }

        // Null unless the exe was started by Stream Deck.
        public static StreamDeckPlugin FromArgs(string[] args)
        {
            int port = 0;
            string uuid = null, register = null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-port": int.TryParse(args[i + 1], out port); break;
                    case "-pluginuuid": uuid = args[i + 1]; break;
                    case "-registerevent": register = args[i + 1]; break;
                }
            }
            if (port <= 0 || uuid == null || register == null) return null;
            return new StreamDeckPlugin(port, uuid, register);
        }

        public void Start()
        {
            var t = new Thread(Run) { IsBackground = true, Name = "streamdeck" };
            t.Start();
        }

        public void SetImage(string dataUri)
        {
            List<string> targets;
            lock (gate)
            {
                image = dataUri;
                targets = new List<string>(keys);
            }
            foreach (var ctx in targets) SendImage(ctx, dataUri);
        }

        void Run()
        {
            try
            {
                ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None).Wait();
                var sender = new Thread(SendLoop) { IsBackground = true, Name = "streamdeck-send" };
                sender.Start();
                Send(new Dictionary<string, object> { { "event", registerEvent }, { "uuid", pluginUuid } });
                ReceiveLoop();
            }
            catch (Exception) { }
            Close();
        }

        void ReceiveLoop()
        {
            var buf = new byte[16 * 1024];
            var msg = new MemoryStream();
            while (ws.State == WebSocketState.Open)
            {
                var res = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                if (res.MessageType == WebSocketMessageType.Close) return;
                msg.Write(buf, 0, res.Count);
                if (!res.EndOfMessage) continue;
                string text = Encoding.UTF8.GetString(msg.ToArray());
                msg.SetLength(0);
                try { Handle(json.DeserializeObject(text) as Dictionary<string, object>); }
                catch (Exception) { }
            }
        }

        void Handle(Dictionary<string, object> m)
        {
            if (m == null) return;
            object ev, ctx;
            m.TryGetValue("event", out ev);
            m.TryGetValue("context", out ctx);
            string context = ctx as string;
            switch (ev as string)
            {
                case "willAppear":
                    if (context == null) return;
                    string img;
                    lock (gate)
                    {
                        keys.Add(context);
                        img = image;
                    }
                    if (img != null) SendImage(context, img);
                    break;
                case "willDisappear":
                    if (context == null) return;
                    lock (gate) keys.Remove(context);
                    break;
                case "keyDown":
                case "systemDidWakeUp":
                    var h = RefreshRequested;
                    if (h != null) h();
                    break;
            }
        }

        void SendImage(string context, string dataUri)
        {
            Send(new Dictionary<string, object>
            {
                { "event", "setImage" },
                { "context", context },
                { "payload", new Dictionary<string, object> { { "image", dataUri } } },
            });
        }

        void Send(Dictionary<string, object> m)
        {
            try { outbox.Add(json.Serialize(m)); }
            catch (InvalidOperationException) { }   // closing: the outbox no longer takes messages
        }

        // ClientWebSocket allows a single outstanding send, so all sends go through one thread.
        void SendLoop()
        {
            try
            {
                foreach (var text in outbox.GetConsumingEnumerable())
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                        CancellationToken.None).Wait();
                }
            }
            catch (Exception) { Close(); }
        }

        void Close()
        {
            if (Interlocked.Exchange(ref closed, 1) == 1) return;
            outbox.CompleteAdding();
            var h = Closed;
            if (h != null) h();
        }

        public void Dispose()
        {
            Close();
            try { ws.Abort(); } catch (Exception) { }
            ws.Dispose();
        }
    }
}
