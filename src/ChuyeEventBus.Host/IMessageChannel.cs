﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace ChuyeEventBus.Host {
    public interface IMessageChannel : ICloneable {
        void Startup();
        event Action<Message> MessageQueueReceived;
    }
}
