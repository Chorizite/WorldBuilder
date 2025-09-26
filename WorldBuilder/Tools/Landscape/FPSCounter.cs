using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace WorldBuilder.Tools.Landscape {
    public class FPSCounter {
        private double fps = 0.0;
        private double frameTime = 0.0; // Frame time in milliseconds
        private double frameCount = 0;
        private double timeAccumulator = 0.0;
        private static double UPDATE_INTERVAL = 0.1; // Update FPS every 0.1 seconds

        public void UpdateFPS(double deltaTime) {
            frameCount++;
            timeAccumulator += deltaTime;

            frameTime = deltaTime * 1000.0;

            if (timeAccumulator >= UPDATE_INTERVAL) {
                fps = frameCount / timeAccumulator;
                frameCount = 0;
                timeAccumulator = 0.0;
            }
        }

        public double getFPS() {
            return fps;
        }

        public double getFrameTime() {
            return frameTime;
        }

        public String getFPSString() {
            return $"FPS: {fps:N0}";
        }

        public String getFrameTimeString() {
            return $"Frame Time: {frameTime:N1}ms";
        }

        public String getCombinedString() {
            return $"FPS: {fps:N0} | Frame Time: {frameTime:N1}ms";
        }
    }
}