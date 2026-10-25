using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp {
    //The MCP server of the editor over HTTP (the streamable HTTP transport of MCP, with JSON responses only, no event
    //streams): POST http://127.0.0.1:<port>/mcp. Started by the setting mcpServerEnabled (off by default) on the port of
    //mcpServerPort; only local connections, and each request needs the token of mcpServerToken
    //(Authorization: Bearer <token>). A minimal HTTP/1.1 server on a TcpListener (HttpListener needs a URL reservation on
    //Windows for 127.0.0.1), the messages are handled by McpServer.
    public class McpHttp {
        public const int DefaultPort = 7810;
        const int MaxHeader = 64 * 1024;
        const int MaxBody = 64 * 1024 * 1024;

        public static McpHttp Current { get; private set; }
        //why the server could not start, null if it did (or isn't enabled)
        public static string LastError { get; private set; }

        public readonly int port;
        readonly string token;
        readonly McpServer server;
        readonly TcpListener listener;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();

        public string Url => "http://127.0.0.1:" + port + "/mcp";

        McpHttp(int port, string token, McpServer server) {
            this.port = port;
            this.token = token;
            this.server = server;
            listener = new TcpListener(IPAddress.Loopback, port);
        }

        //the token of the setting, created the first time
        public static string GetToken() {
            var token = SettingsManager.Get<string>("mcpServerToken", null);
            if (string.IsNullOrEmpty(token)) token = NewToken();
            return token;
        }

        public static string NewToken() {
            var token = System.Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            SettingsManager.Set("mcpServerToken", token);
            return token;
        }

        //starts or stops the server as the settings say (called at startup and when they change)
        public static void ApplySettings() {
            Stop();
            LastError = null;
            if (!SettingsManager.Get("mcpServerEnabled", false)) return;
            Current = Start(SettingsManager.Get("mcpServerPort", DefaultPort), GetToken());
        }

        //a server on the port (null if it can't listen, see LastError), not the one of the settings: stop it with Close
        public static McpHttp Start(int port, string token) {
            var http = new McpHttp(port, token, new McpServer(Api.ApiHost.Instance.Dispatcher));
            try {
                http.listener.Start();
            } catch (SocketException e) {
                LastError = e.Message;
                GameEngine.LogError("MCP: could not listen on port " + port + ": " + e.Message);
                return null;
            }
            _ = http.Accept();
            GameEngine.Print("MCP: listening on " + http.Url);
            return http;
        }

        public void Close() {
            cancel.Cancel();
            listener.Stop();
        }

        public static void Stop() {
            Current?.Close();
            Current = null;
        }

        async Task Accept() {
            while (!cancel.IsCancellationRequested) {
                TcpClient client;
                try {
                    client = await listener.AcceptTcpClientAsync(cancel.Token);
                } catch (System.Exception e) when (e is System.OperationCanceledException || e is System.ObjectDisposedException || e is SocketException) {
                    return;
                }
                _ = Task.Run(() => Serve(client));
            }
        }

        class Request {
            public string method;
            public string path;
            public readonly Dictionary<string, string> headers = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            public byte[] body;

            public string Header(string name) => headers.TryGetValue(name, out var v) ? v : null;
        }

        //the requests of a connection, until it is closed
        async Task Serve(TcpClient client) {
            using (client) {
                try {
                    var stream = client.GetStream();
                    var buffer = new MemoryStream();
                    while (!cancel.IsCancellationRequested) {
                        var request = await Read(stream, buffer);
                        if (request == null) return;
                        var keepAlive = !string.Equals(request.Header("Connection"), "close", System.StringComparison.OrdinalIgnoreCase);
                        await Respond(stream, request, keepAlive);
                        if (!keepAlive) return;
                    }
                } catch (System.Exception e) when (e is IOException || e is SocketException || e is System.ObjectDisposedException || e is System.OperationCanceledException) {
                    //the connection was closed
                }
            }
        }

        //the next request, null when the connection ends (buffer: the bytes read past the previous request)
        async Task<Request> Read(NetworkStream stream, MemoryStream buffer) {
            var chunk = new byte[16384];
            int headerEnd;
            while ((headerEnd = IndexOfHeaderEnd(buffer)) < 0) {
                if (buffer.Length > MaxHeader) return null;
                var n = await stream.ReadAsync(chunk, 0, chunk.Length, cancel.Token);
                if (n == 0) return null;
                buffer.Write(chunk, 0, n);
            }
            var data = buffer.ToArray();
            var lines = Encoding.ASCII.GetString(data, 0, headerEnd).Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 3) return null;
            var request = new Request { method = first[0], path = first[1].Split('?')[0] };
            for (int i = 1; i < lines.Length; i++) {
                var colon = lines[i].IndexOf(':');
                if (colon > 0) request.headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            if (!int.TryParse(request.Header("Content-Length") ?? "0", out var length) || length < 0 || length > MaxBody) return null;
            var start = headerEnd + 4;
            while (data.Length - start < length) {
                var n = await stream.ReadAsync(chunk, 0, chunk.Length, cancel.Token);
                if (n == 0) return null;
                buffer.Write(chunk, 0, n);
                data = buffer.ToArray();
            }
            request.body = new byte[length];
            System.Array.Copy(data, start, request.body, 0, length);
            //what follows belongs to the next request
            buffer.SetLength(0);
            buffer.Write(data, start + length, data.Length - start - length);
            return request;
        }

        static int IndexOfHeaderEnd(MemoryStream buffer) {
            var data = buffer.GetBuffer();
            for (int i = 0; i + 3 < buffer.Length; i++) {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
            }
            return -1;
        }

        //a browser page can't send requests to it (DNS rebinding): only local origins, or none
        static bool LocalOrigin(string origin) {
            if (string.IsNullOrEmpty(origin)) return true;
            if (!System.Uri.TryCreate(origin, System.UriKind.Absolute, out var uri)) return false;
            return uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]";
        }

        bool Authorized(Request request) {
            var auth = request.Header("Authorization");
            if (auth == null || !auth.StartsWith("Bearer ")) return false;
            var given = Encoding.UTF8.GetBytes(auth.Substring(7).Trim());
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(token));
        }

        async Task Respond(NetworkStream stream, Request request, bool keepAlive) {
            if (request.path != "/mcp") {
                await Write(stream, 404, "Not Found", keepAlive, "not found (the endpoint is /mcp)");
            } else if (!LocalOrigin(request.Header("Origin"))) {
                await Write(stream, 403, "Forbidden", keepAlive, "origin not allowed");
            } else if (!Authorized(request)) {
                await Write(stream, 401, "Unauthorized", keepAlive, "missing or wrong token (Authorization: Bearer <token>, see Settings > MCP)",
                    extraHeaders: "WWW-Authenticate: Bearer\r\n");
            } else if (request.method != "POST") {
                //no event stream (GET) and no sessions (DELETE)
                await Write(stream, 405, "Method Not Allowed", keepAlive, "only POST is supported", extraHeaders: "Allow: POST\r\n");
            } else {
                var response = await server.Handle(Encoding.UTF8.GetString(request.body));
                if (response == null) await Write(stream, 202, "Accepted", keepAlive, null);
                else await Write(stream, 200, "OK", keepAlive, response, "application/json");
            }
        }

        static async Task Write(NetworkStream stream, int status, string reason, bool keepAlive, string body, string contentType = "text/plain", string extraHeaders = "") {
            var bytes = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);
            var head = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                (body != null ? "Content-Type: " + contentType + "; charset=utf-8\r\n" : "") +
                "Content-Length: " + bytes.Length + "\r\n" +
                "Connection: " + (keepAlive ? "keep-alive" : "close") + "\r\n" +
                extraHeaders + "\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(headBytes, 0, headBytes.Length);
            if (bytes.Length > 0) await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }
    }
}
