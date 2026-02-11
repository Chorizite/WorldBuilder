using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Chorizite.OpenGLSDLBackend.Lib {

    internal static class EmbeddedResourceReader {
        internal static string GetEmbeddedResource(string filename) {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Chorizite.OpenGLSDLBackend." + filename;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not find embedded resource '{resourceName}'");
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}