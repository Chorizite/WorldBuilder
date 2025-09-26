using Autofac;
using Chorizite.Core;
using Chorizite.Core.Backend;
using Chorizite.Core.Dats;
using Chorizite.Core.Input;
using Chorizite.Core.Render;
using Microsoft.Extensions.Logging;
using System;

namespace WorldBuilder {
    public class WorldBuilderBackend : IChoriziteBackend {

        public static IChoriziteBackend Create(IContainer container) {
            return new WorldBuilderBackend();
        }

        public override IRenderer Renderer => null;// WorldBuilderApp.Instance.Renderer ?? throw new InvalidOperationException();

        public override IInputManager Input { get; } = new NullInputManager();

        public override ChoriziteEnvironment Environment => ChoriziteEnvironment.Inspector;

        public override event EventHandler<LogMessageEventArgs> OnLogMessage;

        public override string? GetClipboardText() {
            throw new NotImplementedException();
        }

        public override void HandleLogMessage(LogMessageEventArgs evt) {
            
        }

        public override void Invoke(Action action) {
            throw new NotImplementedException();
        }

        public override void PlaySound(uint soundId) {
            throw new NotImplementedException();
        }

        public override void SetClipboardText(string text) {
            throw new NotImplementedException();
        }

        public override void SetCursorDid(uint did, int hotX = 0, int hotY = 0, bool makeDefault = false) {
            throw new NotImplementedException();
        }
    }
}