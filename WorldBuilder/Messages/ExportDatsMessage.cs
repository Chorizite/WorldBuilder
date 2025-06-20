using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Messages {
    internal class ExportDatsMessage : ValueChangedMessage<Project> {
        public ExportDatsMessage(Project value) : base(value) {
        }
    }
}
