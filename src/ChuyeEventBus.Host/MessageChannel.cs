﻿using ChuyeEventBus.Core;
using NLog;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Messaging;
using System.Threading;
using System.Threading.Tasks;

namespace ChuyeEventBus.Host {
    public class MessageChannel : ISingleMessageChannel {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly MessageQueueReceiver _messageReceiver;
        protected readonly CancellationTokenSource _ctx;

        public event EventHandler<Message> MessageQueueReceived;

        public String FriendlyName { get; private set; }

        public MessageChannel(EventBehaviourAttribute eventBehaviour) {
            FriendlyName = eventBehaviour.Label.Split('\\').Last();
            _ctx = new CancellationTokenSource();
            _messageReceiver = new MessageQueueReceiver(MessageQueueUtil.ApplyQueue(eventBehaviour));
        }

        public async virtual Task ListenAsync() {
            while (!_ctx.IsCancellationRequested) {
                using (Message message = await _messageReceiver.ReceiveAsync()) {
                    if (message != null && MessageQueueReceived != null) {
                        MessageQueueReceived(this, message);
                    }
                }
            }
            _logger.Debug("MessageChannel: {0,-50}  stoped", FriendlyName);
        }

        public virtual void Stop() {
            if (!_ctx.IsCancellationRequested) {
                _logger.Debug("MessageChannel: {0,-50} suspend", FriendlyName);
                _ctx.Cancel();
            }
        }
    }
}
