using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.AI;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.English, "LLM protocol standard")]
internal enum ModelProtocol
{
    OpenAI = 0,
    Ollama = 1,
    Azure = 2,
}
