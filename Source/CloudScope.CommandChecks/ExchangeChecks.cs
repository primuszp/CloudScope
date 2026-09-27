using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Loading;

internal static class ExchangeChecks
{
    public static async Task Run(Action<string, bool, string> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), "cloudscope-exchange-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string xyz = Path.Combine(dir, "input.xyz");
            File.WriteAllText(xyz, "100001.25 200000.5 3 255 0 0\n100003.25 200002.5 5 0 0 255\n");
            var cloud = XyzPointCloudLoader.Load(xyz);
            var dataset = cloud.ToDataset();
            dataset.ApplyFilter(new ZFilter(4, 6));
            foreach (var format in Enum.GetValues<PointCloudExportFormat>())
            {
                string output = Path.Combine(dir, format.ToString());
                using (var stream = File.Create(output)) PointCloudExporter.Write(stream, dataset, format, _ => 6);
                if (format is PointCloudExportFormat.PlyAscii or PointCloudExportFormat.PlyBinary)
                {
                    var result = PlyPointCloudLoader.Load(output);
                    check($"{format} export preserves world coordinates and source mapping",
                        result.LoadedCount == 1 && Math.Abs(result.CenterX - 100003.25) < 1e-6 && result.Points[0].B == 1 && result.Attributes.Class[0] == 6, "");
                }
                else if (format == PointCloudExportFormat.Pts)
                    check("PTS export reimports filtered sample", PtsPointCloudLoader.Load(output).LoadedCount == 1, "");
                else
                    check($"{format} export contains world coordinates", File.ReadAllText(output).Contains("100003.25"), "");
            }

            string ptx = Path.Combine(dir, "scan.ptx");
            const string scanHeader = "1\n2\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n0 1 0 0\n-1 0 0 0\n0 0 1 0\n10 20 30 1\n";
            File.WriteAllText(ptx, scanHeader + "1 2 3 1 255 0 0\n0 0 0 0 0 0 0\n");
            var transformed = PtxPointCloudLoader.Load(ptx);
            check("PTX row-vector rotation, translation and invalid point omission",
                transformed.LoadedCount == 1 && transformed.CenterX == 8 && transformed.CenterY == 21 && transformed.CenterZ == 33, "");
            File.WriteAllText(ptx, scanHeader + "1 2 3 1\n");
            bool rejected = false;
            try { PtxPointCloudLoader.Load(ptx); } catch (EndOfStreamException) { rejected = true; }
            check("PTX rejects truncated scans", rejected, "");

            string e57 = Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-scans.e57");
            var decoded = E57PointCloudLoader.Load(e57);
            check("Independent libE57 fixture: multiple scans, pose and invalid masks", decoded.LoadedCount == 4 &&
                Math.Abs(decoded.CenterX - 506.5) < 1e-4 && Math.Abs(decoded.CenterY - 1010.5) < 1e-4 && Math.Abs(decoded.CenterZ - 1520) < 1e-4,
                $"count={decoded.LoadedCount}; center={decoded.CenterX},{decoded.CenterY},{decoded.CenterZ}");
            string damaged = Path.Combine(dir, "damaged.e57");
            byte[] bytes = File.ReadAllBytes(e57); bytes[60] ^= 1; File.WriteAllBytes(damaged, bytes);
            rejected = false;
            try { E57PointCloudLoader.Load(damaged); } catch (InvalidDataException) { rejected = true; }
            check("E57 rejects checksum corruption", rejected, "");

            var points = (from y in Enumerable.Range(0, 5) from x in Enumerable.Range(0, 5) select new PointData { X = x, Y = y, Z = 0 }).ToArray();
            var attrs = new PointCloudAttributes(new byte[25], new ushort[25], new byte[25], new double[25], 0, 0);
            var plane = new PointCloudDataset(points, 25, 4, false, 1, attrs, 100, 200, 300);
            var mesh = SurfaceReconstruction.Build(plane, 8, 1.5f);
            check("Surface reconstruction produces nondegenerate plane triangles", mesh.Indices.Length >= 3 &&
                mesh.Indices.All(i => i >= 0 && i < 25) && Enumerable.Range(0, mesh.Indices.Length / 3).All(t =>
                    OpenTK.Mathematics.Vector3.Cross(mesh.Vertices[mesh.Indices[t * 3 + 1]] - mesh.Vertices[mesh.Indices[t * 3]],
                        mesh.Vertices[mesh.Indices[t * 3 + 2]] - mesh.Vertices[mesh.Indices[t * 3]]).Z > 0), "");
            var obj = new StringWriter(); MeshExporter.WriteObj(obj, mesh);
            check("OBJ exports world coordinates and one-based faces", obj.ToString().Contains("v 100 200 300") && obj.ToString().Contains("\nf "), "");

            int port;
            var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); socket.Start();
            port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            using var api = new LocalCommandServer(port, (command, token) => Task.FromResult<object>(new { command, ready = true }));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var unauthorized = await client.GetAsync("/health");
            check("Local API rejects missing token", unauthorized.StatusCode == HttpStatusCode.Unauthorized, "");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
            check("Local API authenticated health", (await client.GetAsync("/health")).IsSuccessStatusCode, "");
            var reply = await client.PostAsync("/exec", new StringContent("{\"command\":\"THIN 25\"}", Encoding.UTF8, "application/json"));
            check("Local API forwards commands without separate handlers", reply.IsSuccessStatusCode && (await reply.Content.ReadAsStringAsync()).Contains("THIN 25"), "");
        }
        catch (Exception ex) { check("Exchange integration checks", false, ex.ToString()); }
        finally { Directory.Delete(dir, true); }
    }
}
