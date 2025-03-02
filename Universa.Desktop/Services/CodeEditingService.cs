using System;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class CodeEditingService : BaseLangChainService
    {
        public CodeEditingService(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI)
            : base(apiKey, model, provider)
        {
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            var prompt = BuildBasePrompt(content, request);
            return await ExecutePrompt(prompt);
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            return $@"You are a code editing assistant. Please help with the following code:

{content}

Request: {request}

Please provide specific suggestions and explain your reasoning.";
        }
    }
} 