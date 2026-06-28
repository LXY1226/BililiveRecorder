using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core.Api.Danmaku;
using BililiveRecorder.Core.Templating;
using Serilog;

namespace BililiveRecorder.Core.Danmaku
{
    internal sealed class RawDanmakuWriter : IRawDanmakuWriter
    {
        internal static readonly byte[] FileHeader = Encoding.ASCII.GetBytes("BLRDMRAW");
        internal const byte FileVersion = 1;

        private readonly ILogger logger;
        private readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        private BinaryWriter? writer;
        private DateTime currentDate;
        private uint writeCount;
        private bool disposedValue;

        public RawDanmakuWriter(ILogger logger)
        {
            this.logger = logger?.ForContext<RawDanmakuWriter>() ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task WriteAsync(RawDanmakuPacketReceivedEventArgs e, IRoom room)
        {
            if (this.disposedValue)
                return;
            if (e is null)
                throw new ArgumentNullException(nameof(e));
            if (room is null)
                throw new ArgumentNullException(nameof(room));

            await this.semaphoreSlim.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!room.RoomConfig.RecordDanmaku)
                {
                    this.DisableCore();
                    return;
                }

                var receivedDate = e.ReceivedAt.LocalDateTime.Date;
                if (this.writer is null || this.currentDate != receivedDate)
                {
                    this.DisableCore();
                    this.OpenFile(room, e.ReceivedAt, receivedDate);
                }

                if (this.writer is null)
                    return;

                this.writer.Write(e.ReceivedAt.ToUnixTimeMilliseconds());
                this.writer.Write((uint)e.Packet.Length);
                this.writer.Write(e.Packet);

                if (this.writeCount++ >= room.RoomConfig.RecordDanmakuFlushInterval)
                {
                    this.writer.Flush();
                    this.writeCount = 0;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "写入原始弹幕数据时发生错误");
                this.DisableCore();
            }
            finally
            {
                this.semaphoreSlim.Release();
            }
        }

        public void Disable()
        {
            if (this.disposedValue)
                return;

            this.semaphoreSlim.Wait();
            try
            {
                this.DisableCore();
            }
            finally
            {
                this.semaphoreSlim.Release();
            }
        }

        private void OpenFile(IRoom room, DateTimeOffset receivedAt, DateTime receivedDate)
        {
            var generator = new FileNameGenerator(room.RoomConfig, this.logger);
            var output = generator.CreateFilePath(new FileNameTemplateContext
            {
                Name = FileNameGenerator.RemoveInvalidFileName(room.Name, ignore_slash: false),
                Title = FileNameGenerator.RemoveInvalidFileName(room.Title, ignore_slash: false),
                RoomId = room.RoomConfig.RoomId,
                ShortId = room.ShortId,
                Uid = room.Uid,
                AreaParent = FileNameGenerator.RemoveInvalidFileName(room.AreaNameParent, ignore_slash: false),
                AreaChild = FileNameGenerator.RemoveInvalidFileName(room.AreaNameChild, ignore_slash: false),
                PartIndex = 1,
                Qn = 0,
                Json = room.RawBilibiliApiJsonData,
            }, receivedAt, checkFileExists: false);

            if (output.FullPath is null)
                return;

            var path = Path.ChangeExtension(output.FullPath, "raw_danmaku.bin");

            try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch (Exception) { }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var shouldWriteHeader = stream.Length == 0;
            this.writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
            if (shouldWriteHeader)
            {
                this.writer.Write(FileHeader);
                this.writer.Write(FileVersion);
            }

            this.currentDate = receivedDate;
            this.writeCount = 0;
        }

        private void DisableCore()
        {
            try
            {
                this.writer?.Flush();
                this.writer?.Dispose();
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "关闭原始弹幕文件时发生错误");
            }
            finally
            {
                this.writer = null;
                this.currentDate = default;
                this.writeCount = 0;
            }
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.DisableCore();
                    this.semaphoreSlim.Dispose();
                }

                this.disposedValue = true;
            }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
