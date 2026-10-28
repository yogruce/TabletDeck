using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
ConfigStore.EnsureExists(configPath);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

var currentCfg = ConfigStore.Load(configPath);
builder.WebHost.UseUrls(currentCfg.Urls);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

var tokens = new ConcurrentDictionary<string, DateTime>();
var activeClients = new ConcurrentDictionary<string, WebSocket>();

bool IsAuthorized(string? token)
{
    if (string.IsNullOrWhiteSpace(token)) return false;
    if (tokens.TryGetValue(token, out var exp) && exp > DateTime.UtcNow)
    {
        tokens[token] = DateTime.UtcNow.AddDays(30); // Sliding expiration
        return true;
    }
    return false;
}

string GetBearerOrQueryToken(HttpRequest req)
{
    var auth = req.Headers.Authorization.ToString();
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return auth.Substring(7).Trim();
    return req.Query["token"].ToString();
}

app.MapGet("/api/status", () =>
{
    var cfg = ConfigStore.Load(configPath);
    return Results.Json(new
    {
        online = true,
        name = Environment.MachineName,
        connectedClients = activeClients.Count,
        screenEnabled = cfg.ScreenCrop.Enabled
    });
});

app.MapPost("/api/pair", async (HttpRequest r) =>
{
    using var d = await JsonDocument.ParseAsync(r.Body);
    var cfg = ConfigStore.Load(configPath);
    var inputPin = d.RootElement.TryGetProperty("pin", out var pEl) ? pEl.GetString() : "";
    if (string.IsNullOrEmpty(inputPin) || inputPin != cfg.PairingPin)
        return Results.Unauthorized();

    var t = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    tokens[t] = DateTime.UtcNow.AddDays(30);
    AgentWindow.UpdateClientStatus(activeClients.Count, true);
    return Results.Json(new { paired = true, token = t, machine = Environment.MachineName });
});

app.MapGet("/api/config", (HttpRequest r) =>
{
    if (!IsAuthorized(GetBearerOrQueryToken(r))) return Results.Unauthorized();
    var cfg = ConfigStore.Load(configPath);
    return Results.Json(new
    {
        screenCrop = cfg.ScreenCrop,
        apps = cfg.Apps,
        monitors = Screen.AllScreens.Select((s, i) => new
        {
            index = i,
            name = s.DeviceName,
            primary = s.Primary,
            bounds = new { x = s.Bounds.X, y = s.Bounds.Y, w = s.Bounds.Width, h = s.Bounds.Height }
        }).ToArray()
    });
});

app.MapPost("/api/config", async (HttpRequest r) =>
{
    if (!IsAuthorized(GetBearerOrQueryToken(r))) return Results.Unauthorized();
    using var d = await JsonDocument.ParseAsync(r.Body);
    var root = d.RootElement;
    var cfg = ConfigStore.Load(configPath);

    if (root.TryGetProperty("screenCrop", out var sc))
    {
        if (sc.TryGetProperty("enabled", out var en)) cfg.ScreenCrop.Enabled = en.GetBoolean();
        if (sc.TryGetProperty("x", out var x)) cfg.ScreenCrop.X = x.GetInt32();
        if (sc.TryGetProperty("y", out var y)) cfg.ScreenCrop.Y = y.GetInt32();
        if (sc.TryGetProperty("width", out var w)) cfg.ScreenCrop.Width = Math.Max(50, w.GetInt32());
        if (sc.TryGetProperty("height", out var h)) cfg.ScreenCrop.Height = Math.Max(50, h.GetInt32());
    }

    if (root.TryGetProperty("apps", out var appsEl) && appsEl.ValueKind == JsonValueKind.Object)
    {
        foreach (var prop in appsEl.EnumerateObject())
        {
            cfg.Apps[prop.Name] = prop.Value.GetString() ?? "";
        }
    }

    ConfigStore.Save(configPath, cfg);
    AgentWindow.RefreshFromConfig();
    return Results.Json(new { ok = true, screenCrop = cfg.ScreenCrop, apps = cfg.Apps });
});

app.MapPost("/api/launch", async (HttpRequest r) =>
{
    if (!IsAuthorized(GetBearerOrQueryToken(r))) return Results.Unauthorized();
    using var d = await JsonDocument.ParseAsync(r.Body);
    var n = d.RootElement.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";

    // First try to bring to front if already running
    if (Keys.Select(n))
        return Results.Json(new { status = "focused", message = $"{n} öne getirildi." });

    var cfg = ConfigStore.Load(configPath);
    if (!cfg.Apps.TryGetValue(n, out var p) || string.IsNullOrWhiteSpace(p))
    {
        // Try auto-detecting common paths or running process
        var detected = AppDetector.FindExecutable(n);
        if (!string.IsNullOrEmpty(detected))
        {
            p = detected;
            cfg.Apps[n] = p;
            ConfigStore.Save(configPath, cfg);
        }
    }

    if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
        return Results.BadRequest(new { error = $"'{n}' için uygulama yolu ayarlanmamış veya bulunamadı. PC Eşlikçisi penceresinden veya Ayarlar'dan dosya yolunu seçin." });

    try
    {
        Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        return Results.Json(new { status = "launched", message = $"{n} başlatıldı." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Uygulama başlatılamadı: {ex.Message}" });
    }
});

app.MapGet("/api/screen", (HttpRequest r) =>
{
    if (!IsAuthorized(GetBearerOrQueryToken(r))) return Results.Unauthorized();
    var cfg = ConfigStore.Load(configPath);
    var c = cfg.ScreenCrop;
    if (!c.Enabled)
        return Results.Problem("Önizleme kapalı; PC penceresinden veya Ayarlar sekmesinden ekran önizlemeyi açabilirsiniz.", statusCode: 403);

    try
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var x = c.X;
        var y = c.Y;
        var w = Math.Clamp(c.Width, 64, Math.Max(64, bounds.Width * 2));
        var h = Math.Clamp(c.Height, 64, Math.Max(64, bounds.Height * 2));

        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        }
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder != null)
        {
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
            bmp.Save(ms, encoder, ep);
        }
        else
        {
            bmp.Save(ms, ImageFormat.Jpeg);
        }
        return Results.Bytes(ms.ToArray(), "image/jpeg");
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message, statusCode: 400);
    }
});

app.Map("/ws", async (HttpContext c) =>
{
    var t = GetBearerOrQueryToken(c.Request);
    if (!IsAuthorized(t))
    {
        c.Response.StatusCode = 401;
        return;
    }
    if (!c.WebSockets.IsWebSocketRequest)
    {
        c.Response.StatusCode = 400;
        return;
    }

    using var ws = await c.WebSockets.AcceptWebSocketAsync();
    var clientId = Guid.NewGuid().ToString("N");
    activeClients[clientId] = ws;
    AgentWindow.UpdateClientStatus(activeClients.Count, true);

    var buf = new byte[8192];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult m;
            do
            {
                m = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (m.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buf, 0, m.Count);
            } while (!m.EndOfMessage);

            if (m.MessageType == WebSocketMessageType.Close) break;

            try
            {
                using var d = JsonDocument.Parse(ms.ToArray());
                var root = d.RootElement;
                var kind = root.TryGetProperty("type", out var kEl) ? kEl.GetString() : "";

                switch (kind)
                {
                    case "ping":
                        var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                        await ws.SendAsync(pong, WebSocketMessageType.Text, true, CancellationToken.None);
                        break;

                    case "profile":
                        Keys.Select(root.GetProperty("name").GetString() ?? "");
                        break;

                    case "key":
                        Keys.Send(root.GetProperty("key").GetString() ?? "");
                        break;

                    case "text":
                        Keys.SendText(root.GetProperty("text").GetString() ?? "");
                        break;

                    case "pen":
                        var mode = root.TryGetProperty("mode", out var mEl) ? (mEl.GetString() ?? "absolute") : "absolute";
                        var button = root.TryGetProperty("button", out var bEl) ? (bEl.GetString() ?? "left") : "left";
                        var click = !root.TryGetProperty("click", out var cEl) || cEl.GetBoolean();
                        var sensitivity = root.TryGetProperty("sensitivity", out var sEl) ? sEl.GetDouble() : 1.0;

                        if (root.TryGetProperty("points", out var points))
                        {
                            foreach (var q in points.EnumerateArray())
                            {
                                var px = q.TryGetProperty("x", out var qx) ? qx.GetDouble() : 0;
                                var py = q.TryGetProperty("y", out var qy) ? qy.GetDouble() : 0;
                                var dx = q.TryGetProperty("dx", out var qdx) ? qdx.GetDouble() : 0;
                                var dy = q.TryGetProperty("dy", out var qdy) ? qdy.GetDouble() : 0;
                                Keys.Pen(px, py, dx, dy, "move", mode, button, click, sensitivity);
                            }
                        }
                        else
                        {
                            var px = root.TryGetProperty("x", out var rx) ? rx.GetDouble() : 0;
                            var py = root.TryGetProperty("y", out var ry) ? ry.GetDouble() : 0;
                            var dx = root.TryGetProperty("dx", out var rdx) ? rdx.GetDouble() : 0;
                            var dy = root.TryGetProperty("dy", out var rdy) ? rdy.GetDouble() : 0;
                            var phase = root.TryGetProperty("phase", out var phEl) ? (phEl.GetString() ?? "move") : "move";
                            Keys.Pen(px, py, dx, dy, phase, mode, button, click, sensitivity);
                        }
                        break;

                    case "mouse_click":
                        var btn = root.TryGetProperty("button", out var mbEl) ? (mbEl.GetString() ?? "left") : "left";
                        var act = root.TryGetProperty("action", out var maEl) ? (maEl.GetString() ?? "click") : "click";
                        Keys.MouseButton(btn, act);
                        break;

                    case "scroll":
                        var deltaY = root.TryGetProperty("deltaY", out var dyEl) ? dyEl.GetInt32() : 0;
                        var deltaX = root.TryGetProperty("deltaX", out var dxEl) ? dxEl.GetInt32() : 0;
                        Keys.Scroll(deltaX, deltaY);
                        break;
                }
            }
            catch (Exception ex)
            {
                RuntimeLog.Write(ex);
            }
        }
    }
    catch { }
    finally
    {
        activeClients.TryRemove(clientId, out _);
        AgentWindow.UpdateClientStatus(activeClients.Count, tokens.Count > 0);
    }
});

Discovery.Start();

var ui = new Thread(() =>
{
    try
    {
        RuntimeLog.Info("UI thread started");
        AgentWindow.Run(configPath, () => app.Lifetime.StopApplication());
        RuntimeLog.Info("UI closed");
    }
    catch (Exception ex)
    {
        RuntimeLog.Write(ex);
        MessageBox.Show(ex.ToString(), "Tablet Deck açılış hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
});
ui.SetApartmentState(ApartmentState.STA);
ui.IsBackground = true;
ui.Start();

try
{
    app.Run();
}
catch (Exception ex)
{
    RuntimeLog.Write(ex);
    MessageBox.Show(ex.ToString(), "Tablet Deck sunucu hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

// ==========================================
// CONFIGURATION MODELS & STORE
// ==========================================
public class AppConfig
{
    public string Urls { get; set; } = "http://0.0.0.0:47821";
    public string PairingPin { get; set; } = "482915";
    public ScreenCropConfig ScreenCrop { get; set; } = new();
    public Dictionary<string, string> Apps { get; set; } = new()
    {
        ["Photoshop"] = "",
        ["Premiere Pro"] = "",
        ["After Effects"] = "",
        ["Illustrator"] = "",
        ["Blender"] = "",
        ["Stardew Valley"] = "",
        ["ARK: Survival Evolved"] = "",
        ["Valheim"] = ""
    };
}

public class ScreenCropConfig
{
    public bool Enabled { get; set; } = true;
    public int X { get; set; } = 0;
    public int Y { get; set; } = 0;
    public int Width { get; set; } = 900;
    public int Height { get; set; } = 700;
}

static class ConfigStore
{
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void EnsureExists(string path)
    {
        lock (_lock)
        {
            if (!File.Exists(path))
            {
                Save(path, new AppConfig());
            }
        }
    }

    public static AppConfig Load(string path)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(path)) return new AppConfig();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json, _jsonOpts) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }

    public static void Save(string path, AppConfig cfg)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(cfg, _jsonOpts);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                RuntimeLog.Write(ex);
            }
        }
    }
}

static class AppDetector
{
    public static string? FindExecutable(string appName)
    {
        try
        {
            var procNames = Keys.Names(appName);
            foreach (var pn in procNames)
            {
                var procs = Process.GetProcessesByName(pn);
                foreach (var p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            var file = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(file) && File.Exists(file))
                                return file;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        return null;
    }
}

// ==========================================
// INPUT SIMULATION (KEYBOARD, MOUSE, PEN)
// ==========================================
static class Keys
{
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nint extra);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, int data, nint extra);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool IsIconic(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_HWHEEL = 0x01000;

    static readonly Dictionary<string, ushort> map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modifiers
        {"CTRL", 17}, {"CONTROL", 17}, {"SHIFT", 16}, {"ALT", 18}, {"WIN", 91}, {"LWIN", 91},
        // Navigation & Editing
        {"SPACE", 32}, {"TAB", 9}, {"ENTER", 13}, {"RETURN", 13}, {"ESC", 27}, {"ESCAPE", 27},
        {"BACKSPACE", 8}, {"BS", 8}, {"DELETE", 46}, {"DEL", 46}, {"INSERT", 45}, {"INS", 45},
        {"HOME", 36}, {"END", 35}, {"PGUP", 33}, {"PAGEUP", 33}, {"PGDN", 34}, {"PAGEDOWN", 34},
        {"UP", 38}, {"DOWN", 40}, {"LEFT", 37}, {"RIGHT", 39},
        {"CAPSLOCK", 20}, {"PRINTSCREEN", 44},
        // Letters A-Z
        {"A", 65}, {"B", 66}, {"C", 67}, {"D", 68}, {"E", 69}, {"F", 70}, {"G", 71}, {"H", 72},
        {"I", 73}, {"J", 74}, {"K", 75}, {"L", 76}, {"M", 77}, {"N", 78}, {"O", 79}, {"P", 80},
        {"Q", 81}, {"R", 82}, {"S", 83}, {"T", 84}, {"U", 85}, {"V", 86}, {"W", 87}, {"X", 88},
        {"Y", 89}, {"Z", 90},
        // Digits 0-9
        {"0", 48}, {"1", 49}, {"2", 50}, {"3", 51}, {"4", 52}, {"5", 53}, {"6", 54}, {"7", 55}, {"8", 56}, {"9", 57},
        // Function Keys F1-F12
        {"F1", 112}, {"F2", 113}, {"F3", 114}, {"F4", 115}, {"F5", 116}, {"F6", 117},
        {"F7", 118}, {"F8", 119}, {"F9", 120}, {"F10", 121}, {"F11", 122}, {"F12", 123},
        // Media & Volume
        {"VOLUP", 175}, {"VOLDOWN", 174}, {"MUTE", 173},
        {"MEDIAPLAY", 179}, {"MEDIANEXT", 176}, {"MEDIAPREV", 177}, {"MEDIASTOP", 178},
        // Common Symbols / Brackets (for brush size [ ], zoom + -, etc.)
        {"[", 219}, {"]", 221}, {"LBRACKET", 219}, {"RBRACKET", 221},
        {"+", 107}, {"-", 109}, {"PLUS", 187}, {"MINUS", 189}, {"=", 187},
        {",", 188}, {".", 190}, {"/", 191}, {";", 186}, {"'", 222}, {"\\", 220},
        // Numpad
        {"NUM0", 96}, {"NUM1", 97}, {"NUM2", 98}, {"NUM3", 99}, {"NUM4", 100},
        {"NUM5", 101}, {"NUM6", 102}, {"NUM7", 103}, {"NUM8", 104}, {"NUM9", 105},
        {"NUMPLUS", 107}, {"NUMMINUS", 109}, {"NUMMUL", 106}, {"NUMDIV", 111}, {"NUMDOT", 110}
    };

    public static string[] Names(string name) => name switch
    {
        "Photoshop" => new[] { "Photoshop" },
        "Premiere Pro" => new[] { "Adobe Premiere Pro", "Premiere Pro" },
        "After Effects" => new[] { "AfterFX" },
        "Illustrator" => new[] { "Illustrator" },
        "Blender" => new[] { "blender" },
        "Stardew Valley" => new[] { "Stardew Valley" },
        "ARK: Survival Evolved" => new[] { "ShooterGame", "ArkAscended" },
        "Valheim" => new[] { "valheim" },
        _ => new[] { name }
    };

    public static bool Select(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var n in Names(name))
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(n))
                {
                    using (p)
                    {
                        var h = p.MainWindowHandle;
                        if (h != IntPtr.Zero)
                        {
                            if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                            SetForegroundWindow(h);
                            return true;
                        }
                    }
                }
            }
            catch { }
        }
        return false;
    }

    public static void Send(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return;
        var codes = combo.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => map.TryGetValue(x.Trim(), out var k) ? k : (ushort)0)
            .Where(x => x != 0)
            .ToArray();

        if (codes.Length == 0) return;

        foreach (var k in codes)
            keybd_event((byte)k, 0, 0, 0);
        Thread.Sleep(12);
        foreach (var k in codes.Reverse())
            keybd_event((byte)k, 0, 2, 0);
    }

    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            SendKeys.SendWait(text);
        }
        catch { }
    }

    private static bool _penButtonDown = false;
    private static uint _activeButtonUpFlag = MOUSEEVENTF_LEFTUP;

    public static void Pen(double x, double y, double dx, double dy, string phase, string mode, string button, bool click, double sensitivity)
    {
        int screenW = Math.Max(1, GetSystemMetrics(0));
        int screenH = Math.Max(1, GetSystemMetrics(1));

        uint downFlag = button switch
        {
            "right" => MOUSEEVENTF_RIGHTDOWN,
            "middle" => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN
        };
        uint upFlag = button switch
        {
            "right" => MOUSEEVENTF_RIGHTUP,
            "middle" => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP
        };

        if (phase == "up")
        {
            if (_penButtonDown)
            {
                mouse_event(_activeButtonUpFlag, 0, 0, 0, 0);
                _penButtonDown = false;
            }
            return;
        }

        if (mode == "relative")
        {
            if (GetCursorPos(out var pt))
            {
                int nx = Math.Clamp((int)Math.Round(pt.X + dx * screenW * sensitivity), 0, screenW - 1);
                int ny = Math.Clamp((int)Math.Round(pt.Y + dy * screenH * sensitivity), 0, screenH - 1);
                SetCursorPos(nx, ny);
            }
        }
        else
        {
            int tx = (int)Math.Round(Math.Clamp(x, 0, 1) * (screenW - 1));
            int ty = (int)Math.Round(Math.Clamp(y, 0, 1) * (screenH - 1));
            SetCursorPos(tx, ty);
        }

        if (phase == "down" && click)
        {
            if (_penButtonDown)
            {
                mouse_event(_activeButtonUpFlag, 0, 0, 0, 0);
            }
            mouse_event(downFlag, 0, 0, 0, 0);
            _penButtonDown = true;
            _activeButtonUpFlag = upFlag;
        }
    }

    public static void MouseButton(string button, string action)
    {
        uint downFlag = button switch
        {
            "right" => MOUSEEVENTF_RIGHTDOWN,
            "middle" => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN
        };
        uint upFlag = button switch
        {
            "right" => MOUSEEVENTF_RIGHTUP,
            "middle" => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP
        };

        if (action == "down")
        {
            mouse_event(downFlag, 0, 0, 0, 0);
        }
        else if (action == "up")
        {
            mouse_event(upFlag, 0, 0, 0, 0);
        }
        else if (action == "dblclick")
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            mouse_event(upFlag, 0, 0, 0, 0);
            Thread.Sleep(40);
            mouse_event(downFlag, 0, 0, 0, 0);
            mouse_event(upFlag, 0, 0, 0, 0);
        }
        else
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            mouse_event(upFlag, 0, 0, 0, 0);
        }
    }

    public static void Scroll(int dx, int dy)
    {
        if (dy != 0)
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, dy, 0);
        if (dx != 0)
            mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, dx, 0);
    }
}

// ==========================================
// UDP DISCOVERY
// ==========================================
static class Discovery
{
    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, 47820));

                while (true)
                {
                    var req = await listener.ReceiveAsync();
                    var msg = Encoding.UTF8.GetString(req.Buffer);
                    if (msg != "TABLET_DECK_DISCOVER") continue;

                    string host = "";
                    try
                    {
                        using var route = new UdpClient();
                        route.Connect(req.RemoteEndPoint.Address, req.RemoteEndPoint.Port);
                        host = ((IPEndPoint)route.Client.LocalEndPoint!).Address.ToString();
                    }
                    catch
                    {
                        host = AgentWindow.GetPreferredIp() ?? "127.0.0.1";
                    }

                    var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        host,
                        port = 47821,
                        name = Environment.MachineName
                    }));
                    await listener.SendAsync(response, response.Length, req.RemoteEndPoint);
                }
            }
            catch (Exception e)
            {
                RuntimeLog.Write(e);
            }
        });
    }
}

// ==========================================
// PC AGENT WINDOWS FORMS UI
// ==========================================
static class AgentWindow
{
    private static Form? _form;
    private static Label? _statusLabel;
    private static CheckBox? _chkScreen;
    private static NumericUpDown? _numX, _numY, _numW, _numH;
    private static ComboBox? _cmbApp;
    private static TextBox? _txtAppPath;
    private static string _configPath = "";

    public static string? GetPreferredIp()
    {
        return GetLocalIps().FirstOrDefault();
    }

    public static string[] GetLocalIps()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => new
                    {
                        IP = a.Address.ToString(),
                        Gateway = n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    }))
                .Where(x => !x.IP.StartsWith("169.254.") && !x.IP.StartsWith("127."))
                .OrderByDescending(x => x.Gateway)
                .Select(x => x.IP)
                .Distinct()
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void UpdateClientStatus(int activeWsCount, bool anyPaired)
    {
        if (_form == null || _statusLabel == null || _form.IsDisposed) return;
        try
        {
            _form.BeginInvoke(() =>
            {
                if (activeWsCount > 0)
                {
                    _statusLabel.Text = $"● {activeWsCount} tablet aktif olarak bağlı";
                    _statusLabel.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else if (anyPaired)
                {
                    _statusLabel.Text = "● Eşleştirildi — tablet bağlantısı bekleniyor";
                    _statusLabel.ForeColor = Color.FromArgb(245, 158, 11);
                }
                else
                {
                    _statusLabel.Text = "● PC eşlikçisi çalışıyor — tablet bağlantısı bekleniyor";
                    _statusLabel.ForeColor = Color.FromArgb(59, 130, 246);
                }
            });
        }
        catch { }
    }

    public static void RefreshFromConfig()
    {
        if (_form == null || _form.IsDisposed || string.IsNullOrEmpty(_configPath)) return;
        try
        {
            _form.BeginInvoke(() =>
            {
                var cfg = ConfigStore.Load(_configPath);
                if (_chkScreen != null) _chkScreen.Checked = cfg.ScreenCrop.Enabled;
                if (_numX != null) _numX.Value = Math.Clamp(cfg.ScreenCrop.X, -10000, 10000);
                if (_numY != null) _numY.Value = Math.Clamp(cfg.ScreenCrop.Y, -10000, 10000);
                if (_numW != null) _numW.Value = Math.Clamp(cfg.ScreenCrop.Width, 50, 10000);
                if (_numH != null) _numH.Value = Math.Clamp(cfg.ScreenCrop.Height, 50, 10000);
                if (_cmbApp != null && _cmbApp.SelectedItem is string appName && _txtAppPath != null)
                {
                    _txtAppPath.Text = cfg.Apps.TryGetValue(appName, out var p) ? p : "";
                }
            });
        }
        catch { }
    }

    public static void Run(string configPath, Action stop)
    {
        _configPath = configPath;
        var cfg = ConfigStore.Load(configPath);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var bgDark = Color.FromArgb(18, 24, 34);
        var cardBg = Color.FromArgb(28, 37, 51);
        var textLight = Color.FromArgb(237, 242, 247);
        var textMuted = Color.FromArgb(160, 174, 192);
        var accent = Color.FromArgb(57, 132, 255);

        var form = new Form
        {
            Text = "Tablet Deck — PC Eşlikçisi",
            Width = 640,
            Height = 650,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            BackColor = bgDark,
            ForeColor = textLight,
            Font = new Font("Segoe UI", 9.5f)
        };
        _form = form;

        var title = new Label
        {
            Text = "TABLET DECK",
            Left = 24,
            Top = 16,
            Width = 340,
            Height = 32,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White
        };

        var pinBadge = new Label
        {
            Text = $"PIN: {cfg.PairingPin}",
            Left = 420,
            Top = 18,
            Width = 180,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 13, FontStyle.Bold),
            BackColor = Color.FromArgb(38, 54, 74),
            ForeColor = Color.FromArgb(147, 197, 253)
        };

        var status = new Label
        {
            Text = "● PC eşlikçisi çalışıyor — tablet bağlantısı bekleniyor",
            Left = 26,
            Top = 52,
            Width = 574,
            Height = 24,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(59, 130, 246)
        };
        _statusLabel = status;

        // Connection Section
        var connGroup = new Panel
        {
            Left = 24,
            Top = 82,
            Width = 576,
            Height = 165,
            BackColor = cardBg
        };

        var addrLabel = new Label
        {
            Text = "Bağlantı Adresleri (Aynı Wi-Fi üzerinde otomatik bulunur veya elle girilir):",
            Left = 14,
            Top = 10,
            Width = 548,
            Height = 20,
            ForeColor = textMuted
        };

        var addresses = new TextBox
        {
            Left = 14,
            Top = 34,
            Width = 548,
            Height = 72,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11),
            BackColor = Color.FromArgb(12, 17, 25),
            ForeColor = Color.FromArgb(226, 232, 240),
            BorderStyle = BorderStyle.FixedSingle
        };

        void RefreshAddresses()
        {
            var list = GetLocalIps().Select(ip => $"http://{ip}:47821").ToArray();
            addresses.Text = list.Length == 0
                ? "Wi-Fi/ağ bağlantısı bulunamadı."
                : string.Join(Environment.NewLine, list);
        }
        RefreshAddresses();

        Button MakeBtn(string text, int left, int top, int width, Color? bg = null)
        {
            return new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg ?? Color.FromArgb(45, 62, 85),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }

        var copyBtn = MakeBtn("Adresi Kopyala", 14, 116, 145, accent);
        copyBtn.Click += (s, e) =>
        {
            var first = addresses.Lines.FirstOrDefault(x => x.StartsWith("http://"));
            if (first != null) Clipboard.SetText(first);
        };

        var openBtn = MakeBtn("Web Panelini Aç", 168, 116, 155);
        openBtn.Click += (s, e) => Process.Start(new ProcessStartInfo("http://127.0.0.1:47821") { UseShellExecute = true });

        var refreshBtn = MakeBtn("Adresleri Yenile", 332, 116, 140);
        refreshBtn.Click += (s, e) => RefreshAddresses();

        var fwBtn = MakeBtn("Güvenlik Duvarı İznini Aç", 480, 116, 82);
        fwBtn.Text = "Port İzni";
        fwBtn.Click += (s, e) =>
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"TabletDeck\" dir=in action=allow protocol=TCP localport=47821")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi)?.WaitForExit(3000);
                var psiUdp = new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"TabletDeck UDP\" dir=in action=allow protocol=UDP localport=47820")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psiUdp)?.WaitForExit(3000);
                MessageBox.Show("TCP 47821 ve UDP 47820 portları için Windows Güvenlik Duvarı kuralları eklendi.", "Tablet Deck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show("Güvenlik duvarı izni eklenemedi (Yönetici izni gerekli).", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        connGroup.Controls.AddRange(new Control[] { addrLabel, addresses, copyBtn, openBtn, refreshBtn, fwBtn });

        // Screen Crop Configuration Group
        var cropGroup = new Panel
        {
            Left = 24,
            Top = 258,
            Width = 576,
            Height = 148,
            BackColor = cardBg
        };

        var cropTitle = new Label
        {
            Text = "Ekran / Envanter Önizleme ve Kırpma Alanı",
            Left = 14,
            Top = 10,
            Width = 380,
            Height = 22,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White
        };

        var chkScreen = new CheckBox
        {
            Text = "Ekran paylaşımını etkinleştir",
            Left = 14,
            Top = 36,
            Width = 250,
            Height = 24,
            Checked = cfg.ScreenCrop.Enabled,
            ForeColor = textLight
        };
        _chkScreen = chkScreen;

        NumericUpDown MakeNum(int left, int top, int val, int min, int max)
        {
            return new NumericUpDown
            {
                Left = left,
                Top = top,
                Width = 75,
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(val, min, max),
                BackColor = Color.FromArgb(12, 17, 25),
                ForeColor = Color.White
            };
        }

        var lblX = new Label { Text = "Sol (X):", Left = 14, Top = 70, Width = 55, ForeColor = textMuted };
        var numX = MakeNum(70, 67, cfg.ScreenCrop.X, -10000, 10000);
        var lblY = new Label { Text = "Üst (Y):", Left = 155, Top = 70, Width = 55, ForeColor = textMuted };
        var numY = MakeNum(210, 67, cfg.ScreenCrop.Y, -10000, 10000);
        var lblW = new Label { Text = "Genişlik:", Left = 295, Top = 70, Width = 62, ForeColor = textMuted };
        var numW = MakeNum(360, 67, cfg.ScreenCrop.Width, 50, 10000);
        var lblH = new Label { Text = "Yükseklik:", Left = 442, Top = 70, Width = 65, ForeColor = textMuted };
        var numH = MakeNum(508, 67, cfg.ScreenCrop.Height, 50, 10000);

        _numX = numX; _numY = numY; _numW = numW; _numH = numH;

        void SaveCrop()
        {
            var c = ConfigStore.Load(configPath);
            c.ScreenCrop.Enabled = chkScreen.Checked;
            c.ScreenCrop.X = (int)numX.Value;
            c.ScreenCrop.Y = (int)numY.Value;
            c.ScreenCrop.Width = (int)numW.Value;
            c.ScreenCrop.Height = (int)numH.Value;
            ConfigStore.Save(configPath, c);
        }

        chkScreen.CheckedChanged += (s, e) => SaveCrop();
        numX.ValueChanged += (s, e) => SaveCrop();
        numY.ValueChanged += (s, e) => SaveCrop();
        numW.ValueChanged += (s, e) => SaveCrop();
        numH.ValueChanged += (s, e) => SaveCrop();

        var fullScreenBtn = MakeBtn("Tam Ekran Yap", 14, 103, 150);
        fullScreenBtn.Click += (s, e) =>
        {
            var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            numX.Value = b.X;
            numY.Value = b.Y;
            numW.Value = b.Width;
            numH.Value = b.Height;
            chkScreen.Checked = true;
            SaveCrop();
        };

        var selectRegionBtn = MakeBtn("Ekrandan Bölge Seç...", 174, 103, 180, accent);
        selectRegionBtn.Click += (s, e) =>
        {
            var rect = RegionSelector.SelectRegion();
            if (rect.HasValue && rect.Value.Width >= 50 && rect.Value.Height >= 50)
            {
                numX.Value = rect.Value.X;
                numY.Value = rect.Value.Y;
                numW.Value = rect.Value.Width;
                numH.Value = rect.Value.Height;
                chkScreen.Checked = true;
                SaveCrop();
            }
        };

        cropGroup.Controls.AddRange(new Control[]
        {
            cropTitle, chkScreen, lblX, numX, lblY, numY, lblW, numW, lblH, numH, fullScreenBtn, selectRegionBtn
        });

        // App Paths Configuration Group
        var appGroup = new Panel
        {
            Left = 24,
            Top = 418,
            Width = 576,
            Height = 135,
            BackColor = cardBg
        };

        var appTitle = new Label
        {
            Text = "Uygulama Kısayol Yolları (Tabletten 'Programı Aç' düğmesi için)",
            Left = 14,
            Top = 10,
            Width = 540,
            Height = 22,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White
        };

        var cmbApp = new ComboBox
        {
            Left = 14,
            Top = 40,
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(12, 17, 25),
            ForeColor = Color.White
        };
        foreach (var k in cfg.Apps.Keys) cmbApp.Items.Add(k);
        _cmbApp = cmbApp;

        var txtAppPath = new TextBox
        {
            Left = 214,
            Top = 40,
            Width = 348,
            BackColor = Color.FromArgb(12, 17, 25),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtAppPath = txtAppPath;

        cmbApp.SelectedIndexChanged += (s, e) =>
        {
            if (cmbApp.SelectedItem is string key)
            {
                var c = ConfigStore.Load(configPath);
                txtAppPath.Text = c.Apps.TryGetValue(key, out var val) ? val : "";
            }
        };
        if (cmbApp.Items.Count > 0) cmbApp.SelectedIndex = 0;

        var browseBtn = MakeBtn("Dosya Seç (.exe)...", 14, 82, 170, accent);
        browseBtn.Click += (s, e) =>
        {
            if (cmbApp.SelectedItem is not string key) return;
            using var ofd = new OpenFileDialog
            {
                Title = $"{key} çalıştırılabilir dosyasını seçin",
                Filter = "Uygulamalar (*.exe)|*.exe|Tüm Dosyalar (*.*)|*.*"
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtAppPath.Text = ofd.FileName;
                var c = ConfigStore.Load(configPath);
                c.Apps[key] = ofd.FileName;
                ConfigStore.Save(configPath, c);
            }
        };

        var savePathBtn = MakeBtn("Yolu Kaydet", 194, 82, 140);
        savePathBtn.Click += (s, e) =>
        {
            if (cmbApp.SelectedItem is not string key) return;
            var c = ConfigStore.Load(configPath);
            c.Apps[key] = txtAppPath.Text.Trim();
            ConfigStore.Save(configPath, c);
            MessageBox.Show($"{key} uygulama yolu kaydedildi.", "Tablet Deck", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var detectBtn = MakeBtn("Açık Programdan Bul", 344, 82, 218);
        detectBtn.Click += (s, e) =>
        {
            if (cmbApp.SelectedItem is not string key) return;
            var found = AppDetector.FindExecutable(key);
            if (!string.IsNullOrEmpty(found))
            {
                txtAppPath.Text = found;
                var c = ConfigStore.Load(configPath);
                c.Apps[key] = found;
                ConfigStore.Save(configPath, c);
                MessageBox.Show($"Açık olan '{key}' bulundu ve kaydedildi:\n{found}", "Tablet Deck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Şu anda çalışan '{key}' işlemi bulunamadı. Programı açıp tekrar deneyin veya 'Dosya Seç' ile elle seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        appGroup.Controls.AddRange(new Control[] { appTitle, cmbApp, txtAppPath, browseBtn, savePathBtn, detectBtn });

        var footer = new Label
        {
            Text = "İpucu: Tablet ve PC aynı Wi-Fi ağında olmalıdır. Çizim alanı ve makrolar tablet arayüzünde sekmeler halinde sunulur.",
            Left = 24,
            Top = 566,
            Width = 576,
            Height = 36,
            ForeColor = textMuted
        };

        form.Controls.AddRange(new Control[] { title, pinBadge, status, connGroup, cropGroup, appGroup, footer });
        form.FormClosed += (s, e) => stop();
        Application.Run(form);
    }
}

// ==========================================
// INTERACTIVE SCREEN REGION SELECTOR
// ==========================================
static class RegionSelector
{
    private sealed class OverlayForm : Form
    {
        public OverlayForm()
        {
            DoubleBuffered = true;
        }
    }

    public static Rectangle? SelectRegion()
    {
        Rectangle? result = null;
        var vScreen = SystemInformation.VirtualScreen;

        using var overlay = new OverlayForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = vScreen,
            TopMost = true,
            BackColor = Color.Black,
            Opacity = 0.45,
            Cursor = Cursors.Cross,
            ShowInTaskbar = false
        };

        Point startPt = Point.Empty;
        Rectangle currentRect = Rectangle.Empty;
        bool dragging = false;

        overlay.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                overlay.DialogResult = DialogResult.Cancel;
                overlay.Close();
                return;
            }
            dragging = true;
            startPt = e.Location;
            currentRect = new Rectangle(startPt, Size.Empty);
        };

        overlay.MouseMove += (s, e) =>
        {
            if (!dragging) return;
            int x = Math.Min(startPt.X, e.X);
            int y = Math.Min(startPt.Y, e.Y);
            int w = Math.Abs(startPt.X - e.X);
            int h = Math.Abs(startPt.Y - e.Y);
            currentRect = new Rectangle(x, y, w, h);
            overlay.Invalidate();
        };

        overlay.MouseUp += (s, e) =>
        {
            if (!dragging) return;
            dragging = false;
            if (currentRect.Width >= 30 && currentRect.Height >= 30)
            {
                result = new Rectangle(
                    vScreen.X + currentRect.X,
                    vScreen.Y + currentRect.Y,
                    currentRect.Width,
                    currentRect.Height);
                overlay.DialogResult = DialogResult.OK;
            }
            else
            {
                overlay.DialogResult = DialogResult.Cancel;
            }
            overlay.Close();
        };

        overlay.KeyDown += (s, e) =>
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Escape)
            {
                overlay.DialogResult = DialogResult.Cancel;
                overlay.Close();
            }
        };

        overlay.Paint += (s, e) =>
        {
            using var font = new Font("Segoe UI", 14, FontStyle.Bold);
            e.Graphics.DrawString("Fareyle ekranda paylaşılacak alanı seçin (İptal için ESC veya Sağ Tık)", font, Brushes.White, 30, 30);

            if (currentRect.Width > 0 && currentRect.Height > 0)
            {
                using var pen = new Pen(Color.FromArgb(57, 132, 255), 3);
                using var fill = new SolidBrush(Color.FromArgb(60, 57, 132, 255));
                e.Graphics.FillRectangle(fill, currentRect);
                e.Graphics.DrawRectangle(pen, currentRect);
                e.Graphics.DrawString($"{currentRect.Width} x {currentRect.Height}", font, Brushes.Yellow, currentRect.Left + 6, currentRect.Top + 6);
            }
        };

        overlay.ShowDialog();
        return result;
    }
}

static class RuntimeLog
{
    public static void Info(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabletDeck");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), DateTime.Now + " " + message + Environment.NewLine);
        }
        catch { }
    }

    public static void Write(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabletDeck");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), DateTime.Now + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch { }
    }
}
