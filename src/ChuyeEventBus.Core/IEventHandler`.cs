﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChuyeEventBus.Core {
    public interface IEventHandler<TEnvent> : IEventHandler where TEnvent : IEvent {
        void Handle(TEnvent eventEntry);
    }
}