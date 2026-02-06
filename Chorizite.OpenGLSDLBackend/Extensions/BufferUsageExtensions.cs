using Chorizite.Core.Render.Enums;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chorizite.OpenGLSDLBackend.Extensions {
    internal static class BufferUsageExtensions {
        /// <summary>
        /// Converts a BufferUsage to a GL BufferUsageARB
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        public static GLEnum ToGL(this BufferUsage usage) {
            switch (usage) {
                case BufferUsage.Static:
                    return GLEnum.StaticDraw;
                case BufferUsage.Dynamic:
                    return GLEnum.DynamicDraw;
                default:
                    return GLEnum.StaticDraw;
            }
        }
    }
}