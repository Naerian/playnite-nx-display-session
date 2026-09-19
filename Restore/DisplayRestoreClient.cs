using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Playnite.SDK;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.Restore
{
    /// <summary>
    /// Starts the external RestoreHost and arms a display-topology lease.
    /// If Playnite dies or heartbeats stop, the host reapplies the snapshot.
    /// </summary>
    public sealed class DisplayRestoreClient : IDisposable
    {
        private const string Protocol = "DM1";
        private readonly ILogger logger;
        private readonly string pipeName;
        private readonly string token;
        private readonly BlockingCollection<string> queue = new BlockingCollection<string>();
        private readonly Thread worker;
        private readonly object hostSync = new object();
        private Process hostProcess;
        private bool disposed;
        private string lastSessionId = Guid.NewGuid().ToString("N");
        private DateTime lastHeartbeatUtc = DateTime.MinValue;
        private string armedSnapshotPath;

        public DisplayRestoreClient(ILogger sourceLogger)
        {
            logger = sourceLogger ?? LogManager.GetLogger();
            pipeName = "DM_" + Process.GetCurrentProcess().Id + "_" + Guid.NewGuid().ToString("N");
            token = Guid.NewGuid().ToString("N");
            worker = new Thread(ProcessQueue) { IsBackground = true, Name = "DM RestoreHost IPC" };
            worker.Start();
        }

        public bool IsHostRunning
        {
            get
            {
                try
                {
                    return hostProcess != null && !hostProcess.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsArmed => !string.IsNullOrWhiteSpace(armedSnapshotPath);

        public string Arm(DisplaySnapshot snapshot, string sessionId = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            EnsureHost();
            lastSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            var path = Path.Combine(Path.GetTempPath(),
                "PlayniteDisplayManager-snapshot-" + lastSessionId + ".json");
            snapshot.SaveToFile(path);
            armedSnapshotPath = path;
            Enqueue(string.Join("|", new[]
            {
                Protocol, token, lastSessionId, "ARM", Encode(path)
            }));
            Heartbeat();
            logger.Info("Display Manager restore lease armed: " + path);
            return path;
        }

        public void Heartbeat()
        {
            if (!IsHostRunning ||
                DateTime.UtcNow - lastHeartbeatUtc < TimeSpan.FromSeconds(2))
            {
                return;
            }

            lastHeartbeatUtc = DateTime.UtcNow;
            Enqueue(string.Join("|", new[] { Protocol, token, lastSessionId, "HEARTBEAT" }));
        }

        public void Disarm()
        {
            if (IsHostRunning)
            {
                Enqueue(string.Join("|", new[] { Protocol, token, lastSessionId, "DISARM" }));
            }

            CleanupSnapshotFile();
            armedSnapshotPath = null;
            logger.Info("Display Manager restore lease disarmed.");
        }

        public void RequestRestore()
        {
            if (!IsHostRunning)
            {
                return;
            }

            Enqueue(string.Join("|", new[] { Protocol, token, lastSessionId, "RESTORE" }));
        }

        private void EnsureHost()
        {
            lock (hostSync)
            {
                if (disposed || IsHostRunning)
                {
                    return;
                }

                if (hostProcess != null)
                {
                    hostProcess.Dispose();
                    hostProcess = null;
                }

                var directory = Path.GetDirectoryName(typeof(DisplayRestoreClient).Assembly.Location);
                var executable = Path.Combine(directory ?? string.Empty, "PlayniteDisplayManager.RestoreHost.exe");
                if (!File.Exists(executable))
                {
                    logger.Error("Display Manager RestoreHost was not found: " + executable);
                    return;
                }

                hostProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Format("--pipe {0} --token {1} --parent {2}",
                        pipeName, token, Process.GetCurrentProcess().Id),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                logger.Debug("Display Manager RestoreHost started.");
            }
        }

        private void Enqueue(string message)
        {
            if (disposed || queue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                queue.Add(message);
            }
            catch (InvalidOperationException)
            {
                logger.Warn("RestoreHost command ignored while shutting down.");
            }
        }

        private void ProcessQueue()
        {
            foreach (var message in queue.GetConsumingEnumerable())
            {
                for (var attempt = 0; attempt < 12 && !disposed; attempt++)
                {
                    try
                    {
                        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                        {
                            pipe.Connect(350);
                            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)))
                            {
                                writer.WriteLine(message);
                                writer.Flush();
                            }
                        }

                        break;
                    }
                    catch (TimeoutException)
                    {
                        Thread.Sleep(100);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(100);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Unexpected RestoreHost IPC failure.");
                        break;
                    }
                }
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private void CleanupSnapshotFile()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(armedSnapshotPath) && File.Exists(armedSnapshotPath))
                {
                    File.Delete(armedSnapshotPath);
                }
            }
            catch
            {
                // Temp cleanup is best-effort.
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (IsHostRunning)
            {
                Enqueue(string.Join("|", new[] { Protocol, token, lastSessionId, "SHUTDOWN" }));
            }

            queue.CompleteAdding();
            if (!worker.Join(2500))
            {
                try { worker.Interrupt(); } catch { /* ignore */ }
            }

            disposed = true;
            queue.Dispose();
            CleanupSnapshotFile();

            if (hostProcess != null)
            {
                try
                {
                    if (!hostProcess.HasExited && !hostProcess.WaitForExit(2500))
                    {
                        hostProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to stop Display Manager RestoreHost.");
                }

                hostProcess.Dispose();
            }
        }
    }
}
