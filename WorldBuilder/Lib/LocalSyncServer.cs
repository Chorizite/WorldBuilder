using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Lib {
    internal class LocalSyncServer {
        private readonly CancellationToken _cancellationToken;

        public LocalSyncServer(CancellationToken cancellationToken) {
            _cancellationToken = cancellationToken;
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            Process process = new Process();
            try {
                process.StartInfo.FileName = "WorldBuilder.Server.exe";
                process.StartInfo.Arguments = "";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
                process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) {
                if (!process.HasExited) {
                    try {
                        process.Kill();
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"Failed to kill process: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) {
                if (!process.HasExited) {
                    try {
                        process.Kill();
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"Failed to kill process: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error running process: {ex}");
            }
            finally {
                if (!process.HasExited) {
                    try {
                        process.Kill();
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"Failed to kill process in finally: {ex.Message}");
                    }
                }

                try {
                    process.Close();
                    process.Dispose();
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error during process cleanup: {ex.Message}");
                }
            }
        }

        private void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine) {
            if (!string.IsNullOrEmpty(outLine.Data)) {
                Console.WriteLine(outLine.Data);
            }
        }
    }
}
