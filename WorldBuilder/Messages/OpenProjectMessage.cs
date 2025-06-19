using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Messages {
    internal class OpenProjectMessage : ValueChangedMessage<Project> {
        public OpenProjectMessage(Project value) : base(value) {
        }
    }
}
