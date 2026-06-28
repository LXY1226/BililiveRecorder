using System;

namespace BililiveRecorder.Core.Api.Danmaku
{
    internal sealed class RawDanmakuPacketReceivedEventArgs : EventArgs
    {
        public RawDanmakuPacketReceivedEventArgs(DateTimeOffset receivedAt, int action, byte[] packet)
        {
            this.ReceivedAt = receivedAt;
            this.Action = action;
            this.Packet = packet ?? throw new ArgumentNullException(nameof(packet));
        }

        public DateTimeOffset ReceivedAt { get; }

        public int Action { get; }

        public byte[] Packet { get; }
    }
}
