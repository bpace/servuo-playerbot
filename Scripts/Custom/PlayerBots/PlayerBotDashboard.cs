using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Server.Mobiles;

namespace Server.CustomBots
{
    // LAN-only dashboard. HTTP work never touches world state directly:
    // requests queue actions and the ServUO timer applies them safely.
    public static class PlayerBotDashboard
    {
        private sealed class ActionRequest
        {
            public string Name;
            public int Value;
            public string Facet;
            public string EntityName;
            public string Kind;
            public string OtherName;
            public int X;
            public int Y;
            public int Z;
            public int Width;
            public int Height;
        }

        private static readonly ConcurrentQueue<ActionRequest> Actions = new ConcurrentQueue<ActionRequest>();
        private static readonly ConcurrentDictionary<string, byte[]> MapImages = new ConcurrentDictionary<string, byte[]>();
        private static readonly object SnapshotLock = new object();
        private static readonly Dictionary<Serial, BotMotionSample> BotMotion = new Dictionary<Serial, BotMotionSample>();
        private static string _snapshot = "{\"enabled\":false,\"target\":0,\"count\":0,\"bots\":[],\"events\":[]}";
        private static HttpListener _listener;
        private static string _bindAddress = "192.168.50.139";
        private static string _allowedAddress = "192.168.50.81";

        private sealed class BotMotionSample
        {
            public Point3D Location;
            public Map Map;
            public DateTime MovedAt;
        }
        private static int _port = 8081;
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            LoadSettings();
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://" + _bindAddress + ":" + _port + "/");
                _listener.Start();
                var thread = new Thread(Listen) { IsBackground = true, Name = "PlayerBots Dashboard" };
                thread.Start();
                PlayerBotService.RecordEvent("Dashboard listening at http://" + _bindAddress + ":" + _port + "/");
            }
            catch (Exception e)
            {
                PlayerBotService.RecordEvent("Dashboard failed to start: " + e.Message);
            }
        }

        private static void LoadSettings()
        {
            var path = Path.Combine(Core.BaseDirectory, "Config", "PlayerBotsDashboard.cfg");
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var pair = line.Split(new[] { '=' }, 2);
                        if (pair.Length != 2) continue;
                        var key = pair[0].Trim();
                        var value = pair[1].Trim();
                        if (key == "BindAddress" && value.Length > 0) _bindAddress = value;
                        else if (key == "Port" && int.TryParse(value, out var port) && port > 1024 && port < 65536) _port = port;
                        else if (key == "AllowedAddress" && IPAddress.TryParse(value, out var address)) _allowedAddress = address.ToString();
                    }
                }
                File.WriteAllText(path,
                    "# PlayerBots LAN dashboard. Browser tokens are intentionally not used.\n" +
                    "# Only this client IP may access the dashboard.\n" +
                    "BindAddress=" + _bindAddress + "\n" +
                    "Port=" + _port + "\n" +
                    "AllowedAddress=" + _allowedAddress + "\n");
            }
            catch (Exception e)
            {
                Console.WriteLine("[PlayerBots] Could not save dashboard config: " + e.Message);
            }
        }

        private static void Listen()
        {
            while (_listener != null && _listener.IsListening)
            {
                try { Handle(_listener.GetContext()); }
                catch (HttpListenerException) { return; }
                catch (Exception e) { Console.WriteLine("[PlayerBots] Dashboard request failed: " + e.Message); }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath;
            if (!Authorized(request))
            {
                Write(context.Response, 403, "application/json", "{\"error\":\"forbidden\"}");
                return;
            }
            if (path == "/")
            {
                Write(context.Response, 200, "text/html; charset=utf-8", Html);
                return;
            }
            if (path.StartsWith("/maps/") && path.EndsWith(".png"))
            {
                var facet = path.Substring(6, path.Length - 10);
                var image = GetMapImage(facet);
                if (image == null) Write(context.Response, 404, "application/json", "{\"error\":\"unknown map\"}");
                else WriteBytes(context.Response, 200, "image/png", image);
                return;
            }

            var values = ParseValues(request);
            if (path == "/api/state")
            {
                lock (SnapshotLock) Write(context.Response, 200, "application/json", _snapshot);
                return;
            }
            if (path == "/api/action" && request.HttpMethod == "POST")
            {
                string action;
                if (!values.TryGetValue("action", out action) || !IsAction(action))
                {
                    Write(context.Response, 400, "application/json", "{\"error\":\"invalid action\"}");
                    return;
                }
                int value;
                int.TryParse(values.ContainsKey("value") ? values["value"] : "0", out value);
                string facet;
                values.TryGetValue("facet", out facet);
                Actions.Enqueue(new ActionRequest { Name = action, Value = value, Facet = facet });
                Write(context.Response, 202, "application/json", "{\"accepted\":true}");
                return;
            }
            if (path == "/api/editor" && request.HttpMethod == "POST")
            {
                string action;
                if (!values.TryGetValue("action", out action) || (action != "waypoint" && action != "destination" && action != "zone" && action != "spawn" && action != "portal"))
                {
                    Write(context.Response, 400, "application/json", "{\"error\":\"invalid editor action\"}");
                    return;
                }
                var name = values.ContainsKey("name") ? values["name"] : "";
                var kind = values.ContainsKey("kind") ? values["kind"] : "";
                var other = values.ContainsKey("other") ? values["other"] : "";
                var facet = values.ContainsKey("facet") ? values["facet"] : "Felucca";
                int x, y, z;
                if (!Int32.TryParse(values.ContainsKey("x") ? values["x"] : "", out x) ||
                    !Int32.TryParse(values.ContainsKey("y") ? values["y"] : "", out y) ||
                    !Int32.TryParse(values.ContainsKey("z") ? values["z"] : "0", out z))
                {
                    Write(context.Response, 400, "application/json", "{\"error\":\"invalid coordinates\"}");
                    return;
                }
                int width, height, count;
                Int32.TryParse(values.ContainsKey("width") ? values["width"] : "0", out width);
                Int32.TryParse(values.ContainsKey("height") ? values["height"] : "0", out height);
                Int32.TryParse(values.ContainsKey("count") ? values["count"] : "1", out count);
                Actions.Enqueue(new ActionRequest { Name = "editor-" + action, Facet = facet, EntityName = name, Kind = kind, OtherName = other, X = x, Y = y, Z = z, Width = width, Height = height, Value = count });
                Write(context.Response, 202, "application/json", "{\"accepted\":true}");
                return;
            }
            Write(context.Response, 404, "application/json", "{\"error\":\"not found\"}");
        }

        private static bool Authorized(HttpListenerRequest request)
        {
            var remote = request.RemoteEndPoint == null ? "" : request.RemoteEndPoint.Address.ToString();
            return remote == _allowedAddress || remote == "127.0.0.1" || remote == "::1";
        }

        private static bool IsAction(string action)
        {
            return action == "enable" || action == "disable" || action == "population" || action == "spawn" || action == "remove" || action == "removefacet" || action == "reloadworld" || action == "audit" || action == "dungeonaudit" || action == "generatespawns" || action == "regeneratespawns" || action == "spawnroadpks";
        }

        private static Dictionary<string, string> ParseValues(HttpListenerRequest request)
        {
            var text = request.Url.Query.TrimStart('?');
            if (request.HttpMethod == "POST" && request.HasEntityBody)
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding)) text += "&" + reader.ReadToEnd();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in text.Split('&'))
            {
                var pieces = pair.Split(new[] { '=' }, 2);
                if (pieces.Length != 2) continue;
                values[Uri.UnescapeDataString(pieces[0])] = Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
            }
            return values;
        }

        private static void Write(HttpListenerResponse response, int status, string type, string body)
        {
            WriteBytes(response, status, type, Encoding.UTF8.GetBytes(body));
        }

        private static void WriteBytes(HttpListenerResponse response, int status, string type, byte[] data)
        {
            response.StatusCode = status;
            response.ContentType = type;
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = data.Length;
            response.OutputStream.Write(data, 0, data.Length);
            response.Close();
        }

        internal static void ProcessPendingActions()
        {
            ActionRequest request;
            while (Actions.TryDequeue(out request))
            {
                if (request.Name.StartsWith("editor-"))
                    PlayerBotService.ApplyEditorAction(request.Name, request.Facet, request.EntityName, request.Kind, request.OtherName, request.X, request.Y, request.Z, request.Width, request.Height, request.Value);
                else
                    PlayerBotService.ApplyDashboardAction(request.Name, request.Value, request.Facet);
            }
        }

        internal static void RefreshSnapshot()
        {
            var bots = PlayerBotService.FindBots();
            var players = new List<PlayerMobile>();
            foreach (Mobile mobile in World.Mobiles.Values)
            {
                var player = mobile as PlayerMobile;
                if (player != null && player.Player && !player.Deleted && !player.IsStaff() && player.NetState != null)
                    players.Add(player);
            }
            var now = DateTime.UtcNow;
            var liveSerials = new HashSet<Serial>();
            var json = new StringBuilder(512 + bots.Count * 160);
            json.Append("{\"enabled\":").Append(PlayerBotService.Enabled ? "true" : "false")
                .Append(",\"target\":").Append(PlayerBotService.TargetPopulation)
                .Append(",\"spawnFacet\":\"").Append(Escape(PlayerBotService.SpawnFacet)).Append("\"")
                .Append(",\"count\":").Append(bots.Count).Append(",\"facets\":{");
            for (var i = 0; i < PlayerBotService.FacetNames.Length; i++)
            {
                var facet = PlayerBotService.FacetNames[i];
                var count = 0;
                foreach (var bot in bots) if (bot.Map != null && bot.Map.Name == facet) count++;
                if (i > 0) json.Append(',');
                json.Append("\"").Append(facet).Append("\":{\"count\":").Append(count)
                    .Append(",\"target\":").Append(PlayerBotService.GetTarget(facet)).Append("}");
            }
            json.Append("},\"roles\":{");
            var roles = Enum.GetNames(typeof(PlayerBotRole));
            for (var i = 0; i < roles.Length; i++)
            {
                var count = 0;
                foreach (var bot in bots) if (bot.BotRole.ToString() == roles[i]) count++;
                if (i > 0) json.Append(',');
                json.Append("\"").Append(roles[i]).Append("\":").Append(count);
            }
            json.Append("},\"bots\":[");
            for (var i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                liveSerials.Add(bot.Serial);
                if (i > 0) json.Append(',');
                json.Append("{\"name\":\"").Append(Escape(bot.Name)).Append("\",\"role\":\"")
                    .Append(bot.BotRole).Append("\",\"map\":\"").Append(Escape(bot.Map == null ? "Internal" : bot.Map.Name))
                    .Append("\",\"x\":").Append(bot.X).Append(",\"y\":").Append(bot.Y)
                    .Append(",\"alive\":").Append(bot.Alive ? "true" : "false")
                    .Append(",\"stuck\":").Append(IsStuck(bot, now) ? "true" : "false")
                    .Append(",\"destination\":\"").Append(Escape(bot.DestinationName)).Append("\"}");
            }
            json.Append("],\"players\":[");
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (i > 0) json.Append(',');
                json.Append("{\"name\":\"").Append(Escape(player.Name)).Append("\",\"map\":\"")
                    .Append(Escape(player.Map == null ? "Internal" : player.Map.Name))
                    .Append("\",\"x\":").Append(player.X).Append(",\"y\":").Append(player.Y)
                    .Append(",\"alive\":").Append(player.Alive ? "true" : "false").Append("}");
            }
            json.Append("]");
            var staleSerials = new List<Serial>();
            foreach (var serial in BotMotion.Keys) if (!liveSerials.Contains(serial)) staleSerials.Add(serial);
            foreach (var serial in staleSerials) BotMotion.Remove(serial);
            json.Append(",\"events\":[");
            var events = PlayerBotService.GetEvents();
            for (var i = 0; i < events.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("\"").Append(Escape(events[i])).Append("\"");
            }
            json.Append("]");
            PlayerBotWorldData.AppendDashboardJson(json);
            json.Append("}");
            lock (SnapshotLock) _snapshot = json.ToString();
        }

        private static bool IsStuck(PlayerBot bot, DateTime now)
        {
            BotMotionSample sample;
            if (!BotMotion.TryGetValue(bot.Serial, out sample))
            {
                BotMotion[bot.Serial] = new BotMotionSample { Location = bot.Location, Map = bot.Map, MovedAt = now };
                return false;
            }
            if (sample.Location != bot.Location || sample.Map != bot.Map)
            {
                sample.Location = bot.Location;
                sample.Map = bot.Map;
                sample.MovedAt = now;
                return false;
            }
            var shouldBeMoving = PlayerBotService.Enabled && bot.Alive && bot.Map != null && bot.Map != Map.Internal
                && bot.Combatant == null && String.IsNullOrEmpty(bot.DungeonReturnName)
                && bot.NextAction <= now && bot.Destination != Point3D.Zero && !bot.InRange(bot.Destination, 2);
            return shouldBeMoving && now - sample.MovedAt >= TimeSpan.FromSeconds(45);
        }

        // These are radar-color terrain maps sampled at one pixel per eight UO
        // tiles. They are rendered from the shard's own client data, not copied
        // from an external image or bundled with the mod.
        private static byte[] GetMapImage(string facetName)
        {
            var map = PlayerBotService.GetMap(facetName);
            if (map == null || map == Map.Internal || !String.Equals(map.Name, facetName, StringComparison.OrdinalIgnoreCase)) return null;
            return MapImages.GetOrAdd(map.Name, ignored => RenderMap(map));
        }

        private static byte[] RenderMap(Map map)
        {
            // Four tiles per pixel keeps the browser map compact enough for
            // LAN use while making zoomed terrain materially clearer than
            // the old eight-tile radar view.
            const int scale = 4;
            var width = (map.Width + scale - 1) / scale;
            var height = (map.Height + scale - 1) / scale;
            var palette = LoadRadarPalette();
            var raw = new byte[height * (1 + width * 3)];
            var offset = 0;
            for (var y = 0; y < map.Height; y += scale)
            {
                raw[offset++] = 0;
                for (var x = 0; x < map.Width; x += scale)
                {
                    var id = map.Tiles.GetLandTile(x, y).ID & TileData.MaxLandValue;
                    var color = id < palette.Length ? palette[id] : 0;
                    raw[offset++] = (byte)(color >> 16);
                    raw[offset++] = (byte)(color >> 8);
                    raw[offset++] = (byte)color;
                }
            }
            return Png(width, height, raw);
        }

        private static int[] LoadRadarPalette()
        {
            var path = Path.Combine(Core.DataDirectories[0], "radarcol.mul");
            var source = File.ReadAllBytes(path);
            var palette = new int[source.Length / 2];
            for (var i = 0; i < palette.Length; i++)
            {
                var color = source[i * 2] | (source[i * 2 + 1] << 8);
                var red = (color >> 10) & 31;
                var green = (color >> 5) & 31;
                var blue = color & 31;
                palette[i] = (((red << 3) | (red >> 2)) << 16) | (((green << 3) | (green >> 2)) << 8) | ((blue << 3) | (blue >> 2));
            }
            return palette;
        }

        private static byte[] Png(int width, int height, byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var header = new byte[13];
                PutInt(header, 0, width); PutInt(header, 4, height); header[8] = 8; header[9] = 2;
                PngChunk(output, "IHDR", header);
                PngChunk(output, "IDAT", ZlibStored(raw));
                PngChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        private static byte[] ZlibStored(byte[] raw)
        {
            using (var output = new MemoryStream(raw.Length + raw.Length / 65535 * 5 + 8))
            {
                output.WriteByte(0x78); output.WriteByte(0x01);
                for (var offset = 0; offset < raw.Length;)
                {
                    var length = Math.Min(65535, raw.Length - offset);
                    output.WriteByte((byte)(offset + length == raw.Length ? 1 : 0));
                    output.WriteByte((byte)length); output.WriteByte((byte)(length >> 8));
                    var inverse = ~length;
                    output.WriteByte((byte)inverse); output.WriteByte((byte)(inverse >> 8));
                    output.Write(raw, offset, length); offset += length;
                }
                var adler = Adler32(raw);
                output.WriteByte((byte)(adler >> 24)); output.WriteByte((byte)(adler >> 16)); output.WriteByte((byte)(adler >> 8)); output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        private static void PngChunk(Stream stream, string type, byte[] data)
        {
            var bytes = Encoding.ASCII.GetBytes(type);
            var length = new byte[4]; PutInt(length, 0, data.Length); stream.Write(length, 0, 4); stream.Write(bytes, 0, 4); stream.Write(data, 0, data.Length);
            var crc = Crc32(bytes, data); PutInt(length, 0, (int)crc); stream.Write(length, 0, 4);
        }

        private static void PutInt(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
        }

        private static uint Crc32(byte[] first, byte[] second)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var value in first) crc = CrcStep(crc, value);
            foreach (var value in second) crc = CrcStep(crc, value);
            return ~crc;
        }

        private static uint CrcStep(uint crc, byte value)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
            return crc;
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

#if false
        private const string Html = @"<!doctype html><html><head><meta charset='utf-8'><title>AoS PlayerBots</title><style>
body{margin:0;background:#101419;color:#dce6ef;font:14px system-ui,sans-serif;display:grid;grid-template-columns:1fr 330px;min-height:100vh}main{padding:20px}aside{background:#191f27;padding:18px;border-left:1px solid #364150}h1{margin:0 0 4px;font-size:22px}small{color:#93a4b7}.bar{display:flex;gap:8px;align-items:center;margin:16px 0}.stat{background:#1d2733;border-radius:7px;padding:10px 14px}.on{color:#62d98b}.off{color:#f57f7f}button,input,select{background:#273545;color:#eef5fc;border:1px solid #4d6176;border-radius:5px;padding:8px}button{cursor:pointer}button.danger{background:#803c46}canvas{background:#162a36;border:1px solid #3d5367;max-width:100%;height:auto}.list{max-height:40vh;overflow:auto;background:#101419;padding:8px;font:12px ui-monospace,monospace}.event{padding:4px;border-bottom:1px solid #293544}.legend{color:#8fa3b8;margin:8px 0}.dot{color:#71e4ff}.dead{color:#ff6f6f}</style></head><body><main><h1>Age of Shadows PlayerBots</h1><small>LAN dashboard. Keep its token private.</small><div class='bar'><div class='stat'>State: <b id='state'></b></div><div class='stat'>Bots: <b id='count'>0</b></div><div class='stat'>Target: <b id='target'>0</b></div><select id='map'><option>Felucca</option><option>Trammel</option><option>Ilshenar</option><option>Malas</option></select></div><canvas id='world' width='960' height='640'></canvas><div class='legend'><span class='dot'>●</span> living bot &nbsp; <span class='dead'>●</span> dead bot. Coordinate view uses the selected facet, not EA map art.</div></main><aside><h2>Controls</h2><div class='bar'><button onclick='act("enable")'>Enable</button><button onclick='act("disable")'>Disable</button></div><div class='bar'><input id='population' type='number' min='0' max='250' value='0'><button onclick='act("population",population.value)'>Set target</button></div><div class='bar'><input id='spawn' type='number' min='1' max='50' value='5'><button onclick='act("spawn",spawn.value)'>Spawn now</button></div><button class='danger' onclick='if(confirm("Remove every PlayerBot?"))act("remove")'>Remove all bots</button><h2>Events</h2><div id='events' class='list'></div><h2>Bot census</h2><div id='bots' class='list'></div></aside><script>
let token=new URLSearchParams(location.search).get('token')||localStorage.playerBotsToken||prompt('PlayerBots dashboard token');localStorage.playerBotsToken=token;let state={bots:[]};
async function get(){let r=await fetch('/api/state?token='+encodeURIComponent(token));if(!r.ok)throw Error('Dashboard token rejected');state=await r.json();render()}async function act(action,value=0){await fetch('/api/action',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded','X-PlayerBots-Token':token},body:'action='+action+'&value='+value});setTimeout(get,300)}
function render(){let s=document.querySelector('#state');s.textContent=state.enabled?'ENABLED':'DISABLED';s.className=state.enabled?'on':'off';count.textContent=state.count;target.textContent=state.target;population.value=state.target;let selected=map.value,c=document.querySelector('#world'),x=c.getContext('2d');x.clearRect(0,0,c.width,c.height);x.strokeStyle='#35566a';for(let i=0;i<=12;i++){x.beginPath();x.moveTo(i*80,0);x.lineTo(i*80,640);x.stroke()}for(let i=0;i<=8;i++){x.beginPath();x.moveTo(0,i*80);x.lineTo(960,i*80);x.stroke()}state.bots.filter(b=>b.map===selected).forEach(b=>{x.fillStyle=b.alive?'#71e4ff':'#ff6f6f';x.beginPath();x.arc(b.x/6144*960,b.y/4096*640,4,0,7);x.fill()});events.innerHTML=state.events.slice().reverse().map(e=>'<div class=event>'+e+'</div>').join('')||'No events yet.';bots.innerHTML=state.bots.map(b=>'<div>'+b.name+' · '+b.role+' · '+b.map+' '+b.x+','+b.y+' → '+b.destination+'</div>').join('')||'No PlayerBots yet.'}
get().catch(e=>document.body.innerHTML='<main><h1>Dashboard unavailable</h1><p>'+e.message+'</p></main>');setInterval(()=>get().catch(()=>{}),1000);
</script></body></html>";
#endif
        private static string Html
        {
            get
            {
                try
                {
                    return File.ReadAllText(Path.Combine(Core.BaseDirectory, "Scripts", "Custom", "PlayerBots", "Dashboard.html"));
                }
                catch
                {
                    return "<h1>PlayerBots dashboard asset missing</h1>";
                }
            }
        }
    }
}
