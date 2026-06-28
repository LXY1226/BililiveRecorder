using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BililiveRecorder.Core;
using BililiveRecorder.Core.Api.Danmaku;
using BililiveRecorder.Core.Config.V3;
using BililiveRecorder.Core.Danmaku;
using BililiveRecorder.Core.Event;
using Serilog.Core;
using Xunit;

namespace BililiveRecorder.Core.UnitTests.Danmaku
{
    public class RawDanmakuWriterTests
    {
        [Fact]
        public async Task WriteAsync_WritesHeaderRecordsAndOriginalPacketsAsync()
        {
            var dir = CreateTempDirectory();
            try
            {
                var room = CreateRoom(dir);
                var writer = new RawDanmakuWriter(Logger.None);
                var receivedAt = new DateTimeOffset(2026, 6, 28, 12, 0, 1, TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
                var packet1 = CreatePacket(action: 3);
                var packet2 = CreatePacket(action: 5, body: new byte[] { 1, 2, 3 });

                await writer.WriteAsync(new RawDanmakuPacketReceivedEventArgs(receivedAt, 3, packet1), room);
                await writer.WriteAsync(new RawDanmakuPacketReceivedEventArgs(receivedAt.AddSeconds(1), 5, packet2), room);
                writer.Disable();

                var file = Assert.Single(Directory.GetFiles(dir, "*.raw_danmaku.bin", SearchOption.AllDirectories));
                using var stream = File.OpenRead(file);
                using var reader = new BinaryReader(stream);

                Assert.Equal(RawDanmakuWriter.FileHeader, reader.ReadBytes(RawDanmakuWriter.FileHeader.Length));
                Assert.Equal(RawDanmakuWriter.FileVersion, reader.ReadByte());

                Assert.Equal(receivedAt.ToUnixTimeMilliseconds(), reader.ReadInt64());
                Assert.Equal((uint)packet1.Length, reader.ReadUInt32());
                Assert.Equal(packet1, reader.ReadBytes(packet1.Length));

                Assert.Equal(receivedAt.AddSeconds(1).ToUnixTimeMilliseconds(), reader.ReadInt64());
                Assert.Equal((uint)packet2.Length, reader.ReadUInt32());
                Assert.Equal(packet2, reader.ReadBytes(packet2.Length));

                Assert.Equal(stream.Length, stream.Position);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task WriteAsync_SplitsFilesByLocalReceivedDateAsync()
        {
            var dir = CreateTempDirectory();
            try
            {
                var room = CreateRoom(dir);
                var writer = new RawDanmakuWriter(Logger.None);
                var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);

                await writer.WriteAsync(new RawDanmakuPacketReceivedEventArgs(new DateTimeOffset(2026, 6, 28, 23, 59, 59, offset), 3, CreatePacket(3)), room);
                await writer.WriteAsync(new RawDanmakuPacketReceivedEventArgs(new DateTimeOffset(2026, 6, 29, 0, 0, 0, offset), 3, CreatePacket(3)), room);
                writer.Disable();

                var fileNames = Directory.GetFiles(dir, "*.raw_danmaku.bin", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .OrderBy(x => x)
                    .ToArray();

                Assert.Equal(new[]
                {
                    "record-20260628-235959.raw_danmaku.bin",
                    "record-20260629-000000.raw_danmaku.bin",
                }, fileNames);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryParseCommand_ArchivesPopularityButSkipsHeartbeat()
        {
            var heartbeat = new ReadOnlySequence<byte>(CreatePacket(action: 2));
            var heartbeatPackets = new List<RawDanmakuPacketReceivedEventArgs>();

            Assert.True(DanmakuClient.TryParseCommand(ref heartbeat, _ => { }, heartbeatPackets.Add));
            Assert.Empty(heartbeatPackets);

            var popularityPacket = CreatePacket(action: 3);
            var popularity = new ReadOnlySequence<byte>(popularityPacket);
            var popularityPackets = new List<RawDanmakuPacketReceivedEventArgs>();

            Assert.True(DanmakuClient.TryParseCommand(ref popularity, _ => { }, popularityPackets.Add));
            var e = Assert.Single(popularityPackets);
            Assert.Equal(3, e.Action);
            Assert.Equal(popularityPacket, e.Packet);
        }

        private static TestRoom CreateRoom(string dir)
        {
            var globalConfig = new GlobalConfig
            {
                WorkDirectory = dir,
                FileNameRecordTemplate = @"{{ roomId }}-{{ name }}/{{ ""now"" | format_date: ""yyyyMMdd"" }}/record-{{ ""now"" | format_date: ""yyyyMMdd-HHmmss"" }}.flv",
                RecordDanmakuFlushInterval = 1,
            };
            var roomConfig = new RoomConfig
            {
                RoomId = 123,
                RecordDanmaku = true,
            };
            roomConfig.SetParent(globalConfig);

            return new TestRoom(roomConfig)
            {
                Name = "test",
                Title = "title",
            };
        }

        private static byte[] CreatePacket(int action, byte[]? body = null)
        {
            body ??= Array.Empty<byte>();
            var length = 16 + body.Length;
            var packet = new byte[length];
            WriteUInt32BigEndian(packet, 0, (uint)length);
            WriteUInt16BigEndian(packet, 4, 16);
            WriteUInt16BigEndian(packet, 6, 1);
            WriteUInt32BigEndian(packet, 8, (uint)action);
            WriteUInt32BigEndian(packet, 12, 1);
            Array.Copy(body, 0, packet, 16, body.Length);
            return packet;
        }

        private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static string CreateTempDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "BililiveRecorder.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

#pragma warning disable CS0067
        private sealed class TestRoom : IRoom
        {
            public TestRoom(RoomConfig roomConfig)
            {
                this.RoomConfig = roomConfig;
            }

            public Guid ObjectId { get; } = Guid.NewGuid();
            public RoomConfig RoomConfig { get; }
            public int ShortId { get; set; }
            public string Name { get; set; } = string.Empty;
            public long Uid { get; set; }
            public string Title { get; set; } = string.Empty;
            public string AreaNameParent { get; set; } = string.Empty;
            public string AreaNameChild { get; set; } = string.Empty;
            public Newtonsoft.Json.Linq.JObject? RawBilibiliApiJsonData { get; set; }
            public bool Recording { get; set; }
            public bool Streaming { get; set; }
            public bool DanmakuConnected { get; set; }
            public bool AutoRecordForThisSession { get; set; }
            public RoomStats Stats { get; } = new RoomStats();

            public event EventHandler<RecordSessionStartedEventArgs>? RecordSessionStarted;
            public event EventHandler<RecordSessionEndedEventArgs>? RecordSessionEnded;
            public event EventHandler<RecordFileOpeningEventArgs>? RecordFileOpening;
            public event EventHandler<RecordFileClosedEventArgs>? RecordFileClosed;
            public event EventHandler<RecordingStatsEventArgs>? RecordingStats;
            public event EventHandler<IOStatsEventArgs>? IOStats;
            public event PropertyChangedEventHandler? PropertyChanged;

            public void StartRecord() { }
            public void StopRecord() { }
            public void SplitOutput() { }
            public Task RefreshRoomInfoAsync() => Task.CompletedTask;
            public void MarkNextRecordShouldUseRawMode() { }
            public void Dispose() { }
        }
#pragma warning restore CS0067
    }
}
