using System;
using Xunit;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools
{
    public class CommandHistoryTests
    {
        private class TestCommand : ICommand
        {
            public string Name => "Test";
            public bool Executed { get; private set; }
            public bool Undone { get; private set; }

            public void Execute() => Executed = true;
            public void Undo() => Undone = true;
        }

        [Fact]
        public void Execute_RespectsMaxHistoryDepth()
        {
            var history = new CommandHistory { MaxHistoryDepth = 3 };

            history.Execute(new TestCommand());
            history.Execute(new TestCommand());
            history.Execute(new TestCommand());

            Assert.Equal(3, history.History.Count());
            Assert.False(history.IsTruncated);

            history.Execute(new TestCommand());

            Assert.Equal(3, history.History.Count());
            Assert.True(history.IsTruncated);
        }

        [Fact]
        public void Clear_ResetsTruncated()
        {
            var history = new CommandHistory { MaxHistoryDepth = 1 };
            history.Execute(new TestCommand());
            history.Execute(new TestCommand());

            Assert.True(history.IsTruncated);

            history.Clear();

            Assert.False(history.IsTruncated);
            Assert.Empty(history.History);
        }

        [Fact]
        public void JumpTo_HandlesTruncatedRange()
        {
            var history = new CommandHistory { MaxHistoryDepth = 2 };
            var cmd1 = new TestCommand();
            var cmd2 = new TestCommand();
            var cmd3 = new TestCommand();

            history.Execute(cmd1);
            history.Execute(cmd2);
            history.Execute(cmd3); // cmd1 is dropped

            Assert.True(history.IsTruncated);
            Assert.Equal(2, history.History.Count());

            // Jump to Original Document (-1)
            history.JumpTo(-1);
            Assert.True(cmd2.Undone);
            Assert.True(cmd3.Undone);
            Assert.Equal(-1, history.CurrentIndex);
        }
    }
}
