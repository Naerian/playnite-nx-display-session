using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.RestoreHost
{
    internal static class Program
    {
        private const string Protocol = "DM1";
        private const int HeartbeatKillSeconds = 30;
        private static readonly object Sync = new object();
        private static DisplaySnapshot armedSnapshot;
        private static string armedSessionId;
        private static DateTime lastHeartbeatUtc = DateTime.UtcNow;
        private static bool restoreApplied;
        private static volatile bool running = true;

        private static int Main(string[] args)
        {
            if (HasFlag(args, "--self-test"))
            {
                return RunSelfTest();
            }

            var pipeName = ReadArgument(args, "--pipe");
            var token = ReadArgument(args, "--token");
            if (string.IsNullOrWhiteSpace(pipeName) ||
                string.IsNullOrWhiteSpace(token) ||
                !int.TryParse(ReadArgument(args, "--parent"), out var parentProcessId))
            {
                Log("Missing --pipe, --token or --parent.");
                return 2;
            }

            Log("RestoreHost started. parent=" + parentProcessId + " pipe=" + pipeName);

            var server = new Thread(() => RunServer(pipeName, token))
            {
                IsBackground = true,
                Name = "DM RestoreHost Pipe"
            };
            server.Start();

            while (running)
            {
                if (IsParentGone(parentProcessId))
                {
                    Log("Parent process lost; applying restore lease.");
                    ApplyArmedRestore("parent-lost");
                    break;
                }

                TimeSpan elapsed;
                lock (Sync)
                {
                    elapsed = DateTime.UtcNow - lastHeartbeatUtc;
                }

                if (armedSnapshot != null && elapsed > TimeSpan.FromSeconds(HeartbeatKillSeconds))
                {
                    Log("Heartbeat timeout (" + HeartbeatKillSeconds + "s); applying restore lease.");
                    ApplyArmedRestore("heartbeat-timeout");
                    break;
                }

                Thread.Sleep(500);
            }

            Log("RestoreHost exiting. restoreApplied=" + restoreApplied);
            return restoreApplied ? 0 : 0;
        }

        private static int RunSelfTest()
        {
            try
            {
                var topology = new DisplayTopologyService();
                var snapshot = topology.CaptureSnapshot();
                var path = Path.Combine(Path.GetTempPath(), "dm-restore-selftest-" + Guid.NewGuid().ToString("N") + ".json");
                snapshot.SaveToFile(path);
                Log("Self-test captured snapshot: " + path + " paths=" + snapshot.PathCount);

                if (!topology.TryRestoreSnapshot(snapshot, out var error))
                {
                    Log("Self-test restore FAILED: " + error);
                    return 1;
                }

                Log("Self-test restore OK (idempotent reapply).");
                try { File.Delete(path); } catch { /* ignore */ }
                return 0;
            }
            catch (Exception ex)
            {
                Log("Self-test exception: " + ex);
                return 1;
            }
        }

        private static void RunServer(string pipeName, string token)
        {
            while (running)
            {
                try
                {
                    using (var server = CreatePipe(pipeName))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server, Encoding.UTF8, false, 64 * 1024, true))
                        {
                            var line = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(line) || line.Length > 256 * 1024)
                            {
                                continue;
                            }

                            Dispatch(line, token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    Thread.Sleep(250);
                }
            }
        }

        private static NamedPipeServerStream CreatePipe(string pipeName)
        {
            var identity = WindowsIdentity.GetCurrent();
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            return new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.None, 64 * 1024, 64 * 1024, security);
        }

        private static void Dispatch(string line, string token)
        {
            var parts = line.Split('|');
            if (parts.Length < 4 || parts[0] != Protocol || parts[1] != token)
            {
                Log("IPC rejected (protocol/token).");
                return;
            }

            var sessionId = parts[2];
            var command = parts[3];

            if (command == "HEARTBEAT")
            {
                lock (Sync)
                {
                    lastHeartbeatUtc = DateTime.UtcNow;
                }
                return;
            }

            if (command == "ARM" && parts.Length >= 5)
            {
                var snapshotPath = Decode(parts[4]);
                try
                {
                    var snapshot = DisplaySnapshot.LoadFromFile(snapshotPath);
                    lock (Sync)
                    {
                        armedSnapshot = snapshot;
                        armedSessionId = sessionId;
                        lastHeartbeatUtc = DateTime.UtcNow;
                        restoreApplied = false;
                    }
                    Log("Armed restore lease. session=" + sessionId + " file=" + snapshotPath +
                        " paths=" + (snapshot?.PathCount ?? 0));
                }
                catch (Exception ex)
                {
                    Log("ARM failed: " + ex.Message);
                }
                return;
            }

            if (command == "DISARM")
            {
                lock (Sync)
                {
                    armedSnapshot = null;
                    armedSessionId = null;
                    lastHeartbeatUtc = DateTime.UtcNow;
                }
                Log("Disarmed restore lease. session=" + sessionId);
                return;
            }

            if (command == "RESTORE")
            {
                ApplyArmedRestore("explicit-restore");
                return;
            }

            if (command == "SHUTDOWN")
            {
                Log("SHUTDOWN received.");
                // Graceful plugin unload: do not restore; plugin owns clean restore.
                lock (Sync)
                {
                    armedSnapshot = null;
                }
                running = false;
            }
        }

        private static void ApplyArmedRestore(string reason)
        {
            DisplaySnapshot snapshot;
            lock (Sync)
            {
                snapshot = armedSnapshot;
                armedSnapshot = null;
            }

            if (snapshot == null)
            {
                Log("No armed snapshot for reason=" + reason);
                running = false;
                return;
            }

            var topology = new DisplayTopologyService();
            if (topology.TryRestoreSnapshot(snapshot, out var error))
            {
                restoreApplied = true;
                Log("Restore applied (" + reason + ").");
            }
            else
            {
                Log("Restore FAILED (" + reason + "): " + error);
            }

            running = false;
        }

        private static bool IsParentGone(int parentProcessId)
        {
            try
            {
                using (var parent = Process.GetProcessById(parentProcessId))
                {
                    return parent.HasExited;
                }
            }
            catch
            {
                return true;
            }
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return value ?? string.Empty;
            }
        }

        private static string ReadArgument(string[] args, string name)
        {
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Log(string message)
        {
            var line = DateTime.UtcNow.ToString("o") + " " + message;
            Debug.WriteLine(line);
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "PlayniteDisplayManager-RestoreHost.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging is best-effort.
            }
        }
    }
}
