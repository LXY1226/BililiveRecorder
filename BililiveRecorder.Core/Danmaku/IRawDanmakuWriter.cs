using System;
using System.Threading.Tasks;
using BililiveRecorder.Core.Api.Danmaku;

namespace BililiveRecorder.Core.Danmaku
{
    internal interface IRawDanmakuWriter : IDisposable
    {
        Task WriteAsync(RawDanmakuPacketReceivedEventArgs e, IRoom room);

        void Disable();
    }
}
