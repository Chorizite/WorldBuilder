using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chorizite.OpenGLSDLBackend.Lib;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests {
    public class ObjectLandblockConcurrencyTests {
        [Fact]
        public async Task ObjectLandblock_Concurrency_Test() {
            var lb = new ObjectLandblock();
            for (int i = 0; i < 1000; i++) {
                lb.Instances.Add(new SceneryInstance());
            }

            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var readerTask = Task.Run(() => {
                while (!token.IsCancellationRequested) {
                    lock (lb.Lock) {
                        foreach (var inst in lb.Instances) {
                            // Simulate work
                            var x = inst.ObjectId;
                        }
                    }
                }
            });

            var writerTask = Task.Run(async () => {
                for (int i = 0; i < 100; i++) {
                    lock (lb.Lock) {
                        lb.Instances.Clear();
                        for (int j = 0; j < 1000; j++) {
                            lb.Instances.Add(new SceneryInstance());
                        }
                    }
                    await Task.Delay(1);
                }
                cts.Cancel();
            });

            await Task.WhenAll(readerTask, writerTask);
        }
    }
}
