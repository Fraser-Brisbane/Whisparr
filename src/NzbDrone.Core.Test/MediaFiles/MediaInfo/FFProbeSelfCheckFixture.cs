using System;
using FFMpegCore;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    public class FFProbeSelfCheckFixture : CoreTest<FFProbeSelfCheck>
    {
        private void GivenExitCode(int exitCode)
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(s => s.StartAndCapture(It.IsAny<string>(), "-version", null))
                  .Returns(new ProcessOutput { ExitCode = exitCode });
        }

        [Test]
        public void TestR3_healthy_when_version_exits_zero()
        {
            GivenExitCode(0);

            Subject.IsHealthy().Should().BeTrue();
        }

        [TestCase(1)]
        [TestCase(127)]
        public void TestR3_unhealthy_when_version_exits_non_zero(int exitCode)
        {
            GivenExitCode(exitCode);

            Subject.IsHealthy().Should().BeFalse();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR3_unhealthy_when_ffprobe_cannot_be_started()
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(s => s.StartAndCapture(It.IsAny<string>(), "-version", null))
                  .Throws(new InvalidOperationException("No such file or directory"));

            Subject.IsHealthy().Should().BeFalse();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR3_result_is_cached()
        {
            GivenExitCode(0);

            Subject.IsHealthy();
            Subject.IsHealthy();
            Subject.IsHealthy();

            Mocker.GetMock<IProcessProvider>()
                  .Verify(s => s.StartAndCapture(It.IsAny<string>(), "-version", null), Times.Once());
        }

        [Test]
        public void TestR3_failed_result_is_cached_too()
        {
            GivenExitCode(127);

            Subject.IsHealthy();
            Subject.IsHealthy();

            Mocker.GetMock<IProcessProvider>()
                  .Verify(s => s.StartAndCapture(It.IsAny<string>(), "-version", null), Times.Once());
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        [DiskAccessTest]
        public void TestR3_bundled_ffprobe_passes_the_self_check()
        {
            // Same binary location VideoFileInfoReader configures
            GlobalFFOptions.Configure(options => options.BinaryFolder = AppDomain.CurrentDomain.BaseDirectory);
            Mocker.SetConstant<IProcessProvider>(Mocker.Resolve<ProcessProvider>());

            Subject.IsHealthy().Should().BeTrue();
        }
    }
}
