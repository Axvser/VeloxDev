using VeloxDev.AI;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.English, "HTTP request method enum used to drive output slot routing in EnumSelectorNodeViewModel.")]
public enum NetworkRequestMethod
{
    Get,

    Post,

    Put,

    Patch,

    Delete,
}
