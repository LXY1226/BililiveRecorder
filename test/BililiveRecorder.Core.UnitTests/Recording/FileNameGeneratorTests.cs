using System;
using System.IO;
using BililiveRecorder.Core.Config.V3;
using BililiveRecorder.Core.Templating;
using Xunit;

namespace BililiveRecorder.Core.UnitTests.Recording
{
    public class FileNameGeneratorTests
    {
        [Fact]
        public void CreateFilePath_UsesProvidedNow()
        {
            var config = new GlobalConfig
            {
                WorkDirectory = Path.GetTempPath(),
                FileNameRecordTemplate = @"record-{{ ""now"" | format_date: ""yyyyMMdd-HHmmss-fff"" }}.flv",
            };
            var generator = new FileNameGenerator(config, null);

            var output = generator.CreateFilePath(new FileNameTemplateContext(), new DateTimeOffset(2026, 6, 28, 12, 34, 56, 789, TimeSpan.Zero));

            Assert.Equal("record-20260628-123456-789.flv", output.RelativePath);
        }
    }
}
