using System;
using System.Linq;
using System.Reflection;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Pure, vendor-free coverage of <see cref="VciProbeObservationRunner"/> and
/// <see cref="VciProbeObservationOutcomeInfo"/> — VCI Workspace Phase 1 Task 3.3/3.4. Exercises the
/// runner with plain delegates only; no Siemens Openness, no live TIA Portal, no <c>Project</c>
/// reference. <c>Project.IsModified</c> is represented purely as a supplied <c>Func&lt;bool&gt;</c>
/// state-sampling delegate.
/// </summary>
public class VciProbeObservationRunnerTests
{
    [Fact]
    public void Run_NonNullReturn_ReturnsReturnedOutcome()
    {
        var outcome = VciProbeObservationRunner.Run(sampleState: () => false, read: () => "hello");

        Assert.Equal("returned", outcome.Outcome);
        Assert.Equal("hello", outcome.ReturnValue);
        Assert.Null(outcome.Exception);
        Assert.Null(outcome.NotObservableReason);
    }

    [Fact]
    public void Run_NullReturn_ReturnsReturnedNullOutcome()
    {
        var outcome = VciProbeObservationRunner.Run(sampleState: () => false, read: () => null);

        Assert.Equal("returned_null", outcome.Outcome);
        Assert.Null(outcome.ReturnValue);
        Assert.Null(outcome.Exception);
    }

    [Fact]
    public void NotObservable_ExplicitUnavailablePrerequisite_ReturnsNotObservableOutcomeWithReason()
    {
        var outcome = VciProbeObservationOutcomeInfo.NotObservable("secondary project was not supplied");

        Assert.Equal("not_observable", outcome.Outcome);
        Assert.Equal("secondary project was not supplied", outcome.NotObservableReason);
        Assert.Null(outcome.ReturnValue);
        Assert.Null(outcome.Exception);
        Assert.False(outcome.IsModifiedBefore);
        Assert.False(outcome.IsModifiedAfter);
    }

    [Fact]
    public void Run_ReadThrows_ReturnsThrewOutcomeWithTypeMessageHResultAndNoStackTraceField()
    {
        var expected = new InvalidOperationException("boom");

        var outcome = VciProbeObservationRunner.Run(sampleState: () => false, read: () => throw expected);

        Assert.Equal("threw", outcome.Outcome);
        Assert.NotNull(outcome.Exception);
        Assert.Equal(typeof(InvalidOperationException).FullName, outcome.Exception!.ExceptionTypeName);
        Assert.Equal("boom", outcome.Exception.Message);
        Assert.Equal(expected.HResult, outcome.Exception.HResult);

        // The normalized exception type structurally cannot carry a stack trace or Exception.Data.
        var exceptionInfoType = outcome.Exception.GetType();
        Assert.Null(exceptionInfoType.GetProperty("StackTrace"));
        Assert.Null(exceptionInfoType.GetProperty("Data"));
    }

    [Fact]
    public void Run_ExceptionWithInnerChain_NormalizesAtMostThreeInnerExceptions()
    {
        var e4 = new Exception("level4");
        var e3 = new Exception("level3", e4);
        var e2 = new Exception("level2", e3);
        var e1 = new Exception("level1", e2);
        var outer = new Exception("level0", e1);

        var outcome = VciProbeObservationRunner.Run(sampleState: () => false, read: () => throw outer);

        var node = outcome.Exception;
        Assert.NotNull(node);
        Assert.Equal("level0", node!.Message);
        node = node.InnerException;
        Assert.NotNull(node);
        Assert.Equal("level1", node!.Message);
        node = node.InnerException;
        Assert.NotNull(node);
        Assert.Equal("level2", node!.Message);
        node = node.InnerException;
        Assert.NotNull(node);
        Assert.Equal("level3", node!.Message);

        // A fourth inner exception ("level4") is beyond the three-inner-exception bound.
        Assert.Null(node.InnerException);
    }

    [Fact]
    public void Run_SamplesStateBeforeAndAfterEveryInvocation_OnSuccess()
    {
        var calls = 0;
        bool SampleState()
        {
            calls++;
            return calls == 1 ? false : true;
        }

        var outcome = VciProbeObservationRunner.Run(SampleState, read: () => "value");

        Assert.Equal(2, calls);
        Assert.False(outcome.IsModifiedBefore);
        Assert.True(outcome.IsModifiedAfter);
    }

    [Fact]
    public void Run_SamplesStateBeforeAndInFinallyAfter_OnException()
    {
        var calls = 0;
        bool SampleState()
        {
            calls++;
            return calls == 1 ? false : true;
        }

        var outcome = VciProbeObservationRunner.Run(SampleState, read: () => throw new InvalidOperationException("x"));

        Assert.Equal(2, calls);
        Assert.Equal("threw", outcome.Outcome);
        Assert.False(outcome.IsModifiedBefore);
        Assert.True(outcome.IsModifiedAfter);
    }

    [Fact]
    public void Run_StateSamplingExceptionBeforeInvocation_PropagatesAsInfrastructureFailure_NotThrewOutcome()
    {
        var readWasCalled = false;
        bool SampleState() => throw new InvalidOperationException("state sampling boom");
        object? Read()
        {
            readWasCalled = true;
            return "value";
        }

        var ex = Assert.Throws<InvalidOperationException>(() => VciProbeObservationRunner.Run(SampleState, Read));

        Assert.Equal("state sampling boom", ex.Message);
        Assert.False(readWasCalled);
    }

    [Fact]
    public void Run_StateSamplingExceptionAfterSuccessfulRead_PropagatesAsInfrastructureFailure_NotThrewOutcome()
    {
        var calls = 0;
        bool SampleState()
        {
            calls++;
            if (calls == 1)
            {
                return false;
            }

            throw new InvalidOperationException("state sampling boom after read");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => VciProbeObservationRunner.Run(SampleState, read: () => "value"));

        Assert.Equal("state sampling boom after read", ex.Message);
    }

    [Fact]
    public void Run_StateSamplingExceptionAfterReadAlsoThrew_PropagatesSamplingFailure_NotConvertedToThrewOutcome()
    {
        var calls = 0;
        bool SampleState()
        {
            calls++;
            if (calls == 1)
            {
                return false;
            }

            throw new InvalidOperationException("sampling failed after read threw");
        }

        object? Read() => throw new ArgumentException("read failed");

        var ex = Assert.Throws<InvalidOperationException>(() => VciProbeObservationRunner.Run(SampleState, Read));

        Assert.Equal("sampling failed after read threw", ex.Message);
    }

    [Fact]
    public void Run_NullSampleState_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => VciProbeObservationRunner.Run(null!, read: () => "value"));
    }

    [Fact]
    public void Run_NullRead_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => VciProbeObservationRunner.Run(() => false, null!));
    }

    [Fact]
    public void Returned_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => VciProbeObservationOutcomeInfo.Returned(null!, before: false, after: false));
    }

    [Fact]
    public void Run_CanOnlyProduceReturnedReturnedNullOrThrew_NeverTimedOutOrProcessLost()
    {
        var outcomes = new[]
        {
            VciProbeObservationRunner.Run(() => false, () => "x"),
            VciProbeObservationRunner.Run(() => false, () => null),
            VciProbeObservationRunner.Run(() => false, () => throw new Exception("boom")),
        };

        Assert.All(outcomes, o => Assert.Contains(o.Outcome, new[] { "returned", "returned_null", "threw" }));
    }

    [Fact]
    public void ObservationOutcomeInfo_HasNoFactoryCapableOfProducingTimedOutOrProcessLost()
    {
        var factoryNames = typeof(VciProbeObservationOutcomeInfo)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("TimedOut", factoryNames);
        Assert.DoesNotContain("ProcessLost", factoryNames);
    }
}
