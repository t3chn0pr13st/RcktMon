using RcktMon.Client.Core.Trading;
using Xunit;
namespace RcktMon.Client.Core.Tests;
public sealed class SubmissionJournalTests
{
    [Fact]
    public void Lost_reply_survives_restart_and_cannot_be_cleared_while_sending()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SubmissionJournal(path, TradeVenue.Finam);
            var second = new SubmissionJournal(path, TradeVenue.Finam);
            Assert.Null(first.Read());
            using (first.Begin("one"))
            {
                Assert.Throws<IOException>(() => second.Begin("two"));
                Assert.Throws<IOException>(() => second.AcknowledgeCheckedReference("one"));
            }
            Assert.Equal("one", second.Read()!.Reference);
            Assert.Throws<IOException>(() => second.Begin("two"));
            Assert.False(second.AcknowledgeCheckedReference("wrong"));
            Assert.True(second.AcknowledgeCheckedReference("one"));
            using (var next = first.Begin("new")) next.MarkDefinitive();
            Assert.Null(second.Read());
        }
        finally { Directory.Delete(path, true); }
    }
}
