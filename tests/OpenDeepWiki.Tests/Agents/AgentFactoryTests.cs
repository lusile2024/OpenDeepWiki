using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using Xunit;

namespace OpenDeepWiki.Tests.Agents;

public class AgentFactoryTests
{
    [Fact]
    public void CreateSimpleChatClient_ShouldUseProcessEnvironmentFallback_WhenOptionsAreMissing()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("CHAT_API_KEY");
        var originalEndpoint = Environment.GetEnvironmentVariable("ENDPOINT");
        var originalRequestType = Environment.GetEnvironmentVariable("CHAT_REQUEST_TYPE");

        try
        {
            Environment.SetEnvironmentVariable("CHAT_API_KEY", "process-key");
            Environment.SetEnvironmentVariable("ENDPOINT", "https://example.com/v1");
            Environment.SetEnvironmentVariable("CHAT_REQUEST_TYPE", "OpenAI");

            var factory = new AgentFactory(Options.Create(new AiRequestOptions()));
            var agent = factory.CreateSimpleChatClient("test-model");

            Assert.NotNull(agent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHAT_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("ENDPOINT", originalEndpoint);
            Environment.SetEnvironmentVariable("CHAT_REQUEST_TYPE", originalRequestType);
        }
    }
}
