using System;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class RssChain : BaseLangChainService
    {
        private readonly string _content;

        public RssChain(string apiKey, string model, Models.AIProvider provider, string content)
            : base(apiKey, model, provider)
        {
            _content = content;
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            var prompt = BuildBasePrompt(content, request);
            return await ExecutePrompt(prompt);
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            return $@"You are an RSS feed assistant. Help analyze and summarize RSS feed content.

Feed Content:
{_content}

Request:
{request}

Please provide specific and helpful suggestions about the RSS feed content.";
        }
    }
} 