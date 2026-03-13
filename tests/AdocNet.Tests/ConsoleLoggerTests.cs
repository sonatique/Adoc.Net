using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class ConsoleLoggerTests
{
    [Test]
    public void LogSuccess_verbose_prints_ok_with_timing()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: true, quiet: false);

        logger.LogSuccess("input.adoc", "output.html", TimeSpan.FromMilliseconds(42));

        var output = stdout.ToString();
        Assert.That(output, Does.Contain("[OK]"));
        Assert.That(output, Does.Contain("input.adoc"));
        Assert.That(output, Does.Contain("output.html"));
        Assert.That(output, Does.Contain("42ms"));
    }

    [Test]
    public void LogSuccess_quiet_prints_nothing()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: false, quiet: true);

        logger.LogSuccess("input.adoc", "output.html", TimeSpan.FromMilliseconds(42));

        Assert.That(stdout.ToString(), Is.Empty);
    }

    [Test]
    public void LogSuccess_default_prints_nothing()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: false, quiet: false);

        logger.LogSuccess("input.adoc", "output.html", TimeSpan.FromMilliseconds(42));

        Assert.That(stdout.ToString(), Is.Empty);
    }

    [Test]
    public void LogFailure_verbose_prints_fail()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: true, quiet: false);

        logger.LogFailure("broken.adoc", "parse error");

        var output = stderr.ToString();
        Assert.That(output, Does.Contain("[FAIL]"));
        Assert.That(output, Does.Contain("broken.adoc"));
    }

    [Test]
    public void LogFailure_quiet_still_prints_to_stderr()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: false, quiet: true);

        logger.LogFailure("broken.adoc", "parse error");

        var output = stderr.ToString();
        Assert.That(output, Does.Contain("broken.adoc"));
    }

    [Test]
    public void LogSummary_prints_counts()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: false, quiet: false);

        logger.LogSummary(total: 10, succeeded: 8, failed: 2);

        var output = stdout.ToString();
        Assert.That(output, Does.Contain("8/10"));
        Assert.That(output, Does.Contain("2 failed"));
    }

    [Test]
    public void LogSummary_quiet_prints_nothing_when_no_failures()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var logger = new ConsoleLogger(stdout, stderr, verbose: false, quiet: true);

        logger.LogSummary(total: 5, succeeded: 5, failed: 0);

        Assert.That(stdout.ToString(), Is.Empty);
    }
}
