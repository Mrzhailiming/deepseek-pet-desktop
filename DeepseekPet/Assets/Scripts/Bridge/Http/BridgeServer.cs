using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 回环 HTTP 监听。只绑 127.0.0.1，要求 X-Pet-Token 口令，拒绝带 Origin 头的请求
    /// （浏览器里的网页跨站 POST 一定带 Origin；agent 的钩子和插件不会带）。
    ///
    /// 收到请求**先回 204 再处理**：Claude Code 的 http 钩子是同步等响应的，处理慢了会拖住 agent。
    /// 路由：
    ///   GET  /health            200 "ok"（不要口令，给安装器 / 调试用）
    ///   POST /hook/claude-code  Claude Code 钩子原始负载
    ///   POST /event             统一格式的事件（dsh 转发插件、测试窗口）
    /// </summary>
    public sealed class BridgeServer : IDisposable
    {
        public delegate void BodyHandler(string route, string body);

        readonly int _port;
        readonly string _token;
        readonly BodyHandler _onBody;
        readonly Action<string> _log;
        HttpListener _listener;
        Thread _thread;
        volatile bool _running;

        public BridgeServer(int port, string token, BodyHandler onBody, Action<string> log)
        {
            _port = port;
            _token = token;
            _onBody = onBody;
            _log = log ?? (_ => { });
        }

        public int Port => _port;

        /// <summary>开始监听。端口被占用等失败时抛 HttpListenerException，调用方决定换端口。</summary>
        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "pet-bridge-http" };
            _thread.Start();
        }

        void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                try
                {
                    Serve(ctx);
                }
                catch (Exception error)
                {
                    _log("bridge: 处理请求出错 " + error.Message);
                    TryClose(ctx.Response, 500);
                }
            }
        }

        void Serve(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath.TrimEnd('/');
            if (req.HttpMethod == "GET" && path == "/health")
            {
                Write(ctx.Response, 200, "ok");
                return;
            }
            if (!string.IsNullOrEmpty(req.Headers["Origin"]))
            {
                TryClose(ctx.Response, 403);
                return;
            }
            if (!TokenMatches(req.Headers["X-Pet-Token"]))
            {
                TryClose(ctx.Response, 401);
                return;
            }
            if (req.HttpMethod != "POST" || (path != "/hook/claude-code" && path != "/event"))
            {
                TryClose(ctx.Response, 404);
                return;
            }
            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
            TryClose(ctx.Response, 204);
            _onBody(path, body);
        }

        /// <summary>定长比较，避免按字节提前返回泄露口令前缀（本机场景其实无所谓，顺手做对）。</summary>
        bool TokenMatches(string given)
        {
            if (given == null || given.Length != _token.Length) return false;
            var diff = 0;
            for (var i = 0; i < given.Length; i++) diff |= given[i] ^ _token[i];
            return diff == 0;
        }

        static void Write(HttpListenerResponse res, int status, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            res.StatusCode = status;
            res.ContentType = "text/plain; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        static void TryClose(HttpListenerResponse res, int status)
        {
            try
            {
                res.StatusCode = status;
                res.ContentLength64 = 0;
                res.Close();
            }
            catch (Exception)
            {
                // 对端已经走了：无所谓。
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception)
            {
                // 关的时候出什么错都不要紧。
            }
            _listener = null;
        }
    }
}
