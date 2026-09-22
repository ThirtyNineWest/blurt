using System.Text.Json;
using Blurt.Engine.STT;
using FluentAssertions;
using static Blurt.Engine.Tests.Stubs.TranscriberTestSupport;

namespace Blurt.Engine.Tests.STT;

public sealed class KeytermsWireTests
{
    [Test]
    public void ConfigCarriesTheKeyTermsAsTheKeytermsPromptList()
    {
        var config = SteeringConfig(keyterms: ["AssemblyAI", "LeMUR"]);

        Strings(config.GetProperty("keyterms_prompt")).Should().Equal("AssemblyAI", "LeMUR");
        Keys(config).Should().NotContain(["word_boost", "keyterms"]);
    }

    [Test]
    public void ConfigCarriesThePriorTextAsOneOrderedPrompt()
    {
        var config = SteeringConfig(prompt: "First one.\nthanks for");

        config.GetProperty("stt_prompt").GetString().Should().Be("First one.\nthanks for");
        Keys(config).Should().NotContain(["prompt", "conversation_context"]);
    }

    [Test]
    public void EachFieldEncodesAsTheJsonTypeItsContractNames()
    {
        var config = SteeringConfig(prompt: "thanks for", keyterms: ["Blurt"]);

        config.GetProperty("stt_prompt").ValueKind.Should().Be(JsonValueKind.String);
        config.GetProperty("keyterms_prompt").ValueKind.Should().Be(JsonValueKind.Array);
        Strings(config.GetProperty("keyterms_prompt")).Should().Equal("Blurt");
    }

    [Test]
    public void ContextAndKeyTermsRideTheSameRequest()
    {
        var config = SteeringConfig(prompt: "thanks for", keyterms: ["Blurt"]);

        config.GetProperty("stt_prompt").GetString().Should().Be("thanks for");
        Strings(config.GetProperty("keyterms_prompt")).Should().Equal("Blurt");
    }

    [Test]
    public void ConfigNeverSetsALanguage()
    {
        var config = SteeringConfig();

        Keys(config).Should().NotContain(["language_code", "language_codes"]);
        Keys(config).Should().Equal("channels", "llm_instruction", "sample_rate");
    }

    [Test]
    public void ConfigOmitsEachSteeringFieldWhenItHasNothingToSay()
    {
        var config = SteeringConfig();

        Keys(config).Should().NotContain(["keyterms_prompt", "stt_prompt"]);
    }

    [Test]
    public void TheCleanupInstructionEncodesUnderTheKeyTheRouteReadsItFrom()
    {
        var withInstruction = ParseConfig(AssemblyAITranscriber.MakeConfigData(16_000, "", [], "do the thing"));
        var without = ParseConfig(AssemblyAITranscriber.MakeConfigData(16_000, "", [], null));

        Keys(withInstruction).Should().Equal("channels", "llm_instruction", "sample_rate");
        withInstruction.GetProperty("llm_instruction").GetString().Should().Be("do the thing");
        Keys(without).Should().Equal("channels", "sample_rate");
    }

    [Test]
    public void ConfigLeadsWithTheAudioGeometry()
    {
        var names = SteeringConfig(prompt: "p", keyterms: ["k"]).EnumerateObject().Select(p => p.Name);

        names.Should().Equal("sample_rate", "channels", "stt_prompt", "keyterms_prompt", "llm_instruction");
    }

    private static JsonElement SteeringConfig(string prompt = "", string[]? keyterms = null) =>
        ParseConfig(AssemblyAITranscriber.MakeConfigData(16_000, prompt, keyterms ?? [], CleanupInstruction.Sendable()));

    private static string?[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString())];
}
