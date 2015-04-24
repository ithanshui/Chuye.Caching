﻿using ChuyeEventBus.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChuyeEventBus.Host {
    public class EventHandlerFinder {
        private Boolean _initialized;
#pragma warning disable
        [ImportMany]
        private IEnumerable<IEventHandler> _handlers;
#pragma warning disable

        public String Folder { get; set; }

        public IEnumerable<IEventHandler> GetEventHandlers() {
            return GetEventHandlers(false);
        }

        public IEnumerable<IEventHandler> GetEventHandlers(Boolean rescan) {
            if (!_initialized || rescan) {
                var catalog = new AggregateCatalog();
                catalog.Catalogs.Add(new DirectoryCatalog(Folder));
                var container = new CompositionContainer(catalog);
                container.ComposeParts(this);
                _initialized = true;
            }
            return _handlers;
        }
    }
}
