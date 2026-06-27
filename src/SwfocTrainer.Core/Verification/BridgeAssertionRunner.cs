using SwfocTrainer.Core.Services;

namespace SwfocTrainer.Core.Verification;

/// <summary>
/// Executes a BridgeAssertion against a NamedPipeLuaBridgeClient.
/// Captures pre-state, sends the command, captures post-state, runs the predicate.
/// </summary>
public sealed class BridgeAssertionRunner
{
    private readonly NamedPipeLuaBridgeClient _bridge;

    public BridgeAssertionRunner(NamedPipeLuaBridgeClient bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<BridgeAssertionResult> RunAsync(BridgeAssertion assertion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        // Step 1: pre-state
        var pre = await _bridge.SendAsync(assertion.PreStateProbe, cancellationToken);
        if (!pre.Succeeded)
        {
            return new BridgeAssertionResult(false, string.Empty, string.Empty, null,
                $"PreStateProbe failed: {pre.ErrorMessage}");
        }

        // Step 2: command
        var cmd = await _bridge.SendAsync(assertion.LuaCommand, cancellationToken);
        if (!cmd.Succeeded)
        {
            return new BridgeAssertionResult(false, pre.Response, string.Empty, cmd.ErrorMessage,
                $"LuaCommand failed: {cmd.ErrorMessage}");
        }

        // Step 3: post-state
        var post = await _bridge.SendAsync(assertion.PostStateProbe, cancellationToken);
        if (!post.Succeeded)
        {
            return new BridgeAssertionResult(false, pre.Response, string.Empty, cmd.Response,
                $"PostStateProbe failed: {post.ErrorMessage}");
        }

        // Step 4: predicate
        bool passed;
        try
        {
            passed = assertion.ExpectDelta(pre.Response, post.Response);
        }
        catch (Exception ex)
        {
            return new BridgeAssertionResult(false, pre.Response, post.Response, cmd.Response,
                $"ExpectDelta predicate threw: {ex.Message}");
        }

        return new BridgeAssertionResult(
            Passed: passed,
            PreState: pre.Response,
            PostState: post.Response,
            CommandResponse: cmd.Response,
            FailureReason: passed ? null : $"Delta predicate returned false. pre='{pre.Response}' post='{post.Response}'");
    }
}
