using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class TrxResultParserTests
{
    [Fact]
    public void Parse_ReadsFailedTestDetailsFromTrx()
    {
        using var trx = new TempFile(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="SampleTests.Fails" outcome="Failed" duration="00:00:01.234">
                  <Output>
                    <StdOut>[GATE] Sample output</StdOut>
                    <ErrorInfo>
                      <Message>Expected true but found false</Message>
                      <StackTrace>at SampleTests.Fails() in D:\repo\SampleTests.cs:line 42</StackTrace>
                    </ErrorInfo>
                  </Output>
                </UnitTestResult>
              </Results>
            </TestRun>
            """);

        var parser = new TrxResultParser();
        var outcomes = parser.Parse(trx.Path, "SampleGate");

        var outcome = Assert.Single(outcomes);
        Assert.Equal("SampleGate", outcome.GateName);
        Assert.Equal("SampleTests.Fails", outcome.TestName);
        Assert.Equal(TestStatus.Failed, outcome.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), outcome.Duration);
        Assert.Equal("Expected true but found false", outcome.ErrorMessage);
        Assert.Equal(@"D:\repo\SampleTests.cs", outcome.FilePath);
        Assert.Equal(42, outcome.LineNumber);
        Assert.Contains("[GATE] Sample output", outcome.StdOut);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-{Guid.NewGuid():N}.trx");
            File.WriteAllText(Path, content);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
